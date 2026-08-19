using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private readonly HashSet<Control> _digimonBookScrollWired = new();
        private bool _digimonBookRuntimeHooksInstalled;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InstallDigimonBookRuntimeHooks();
        }

        private void InstallDigimonBookRuntimeHooks()
        {
            if (_digimonBookRuntimeHooksInstalled) return;
            _digimonBookRuntimeHooksInstalled = true;

            editorTabs.ControlAdded += (_, args) =>
            {
                if (args.Control is TabPage page)
                    BeginInvoke(new Action(() => PrepareDigimonBookPage(page)));
            };
            editorTabs.SelectedIndexChanged += (_, _) =>
            {
                if (editorTabs.SelectedTab != null)
                    BeginInvoke(new Action(() => PrepareDigimonBookPage(editorTabs.SelectedTab)));
            };

            foreach (TabPage page in editorTabs.TabPages)
                PrepareDigimonBookPage(page);
        }

        private void PrepareDigimonBookPage(TabPage page)
        {
            if (page.IsDisposed || page.Tag is not DigimonBookTabState state) return;

            ReplaceDigimonBookImportButton(page);
            state.Content.AutoScroll = false;

            if (!_digimonBookScrollWired.Contains(state.Content))
            {
                _digimonBookScrollWired.Add(state.Content);
                state.Content.ControlAdded += (_, _) => BeginInvoke(new Action(() => FixDigimonBookScrollAndCards(state)));
                state.Content.Resize += (_, _) => BeginInvoke(new Action(() => FixDigimonBookScrollAndCards(state)));
            }

            FixDigimonBookScrollAndCards(state);
        }

        private void ReplaceDigimonBookImportButton(TabPage page)
        {
            Button? old = EnumerateCashShopControls(page)
                .OfType<Button>()
                .FirstOrDefault(x => x.Text.Equals("IMPORT DB", StringComparison.OrdinalIgnoreCase));
            if (old == null || old.Name == "DigimonBookImportLive" || old.Parent == null) return;

            Control host = old.Parent;
            var button = CreateEditorActionButton("IMPORT DB");
            button.Name = "DigimonBookImportLive";
            button.Location = old.Location;
            button.Size = old.Size;
            button.Anchor = old.Anchor;
            button.TabIndex = old.TabIndex;
            button.Click += async (_, _) => await RunDigimonBookImportAsync();

            host.Controls.Remove(old);
            old.Dispose();
            host.Controls.Add(button);
            button.BringToFront();
        }

        private void FixDigimonBookScrollAndCards(DigimonBookTabState state)
        {
            if (state.Content.IsDisposed) return;
            FlowLayoutPanel? list = state.Content.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (list == null || list.IsDisposed) return;

            list.Dock = DockStyle.Fill;
            list.AutoScroll = true;
            list.WrapContents = false;
            list.FlowDirection = FlowDirection.TopDown;
            list.TabStop = true;
            list.Padding = new Padding(0, 0, 10, 18);

            WireWheelFocus(list, list);
            foreach (Control child in list.Controls)
                WireWheelFocus(child, list);

            int usableWidth = Math.Max(705, list.ClientSize.Width - list.Padding.Horizontal - 18);
            int totalHeight = list.Padding.Top + list.Padding.Bottom;

            foreach (Panel card in list.Controls.OfType<Panel>())
            {
                card.Width = usableWidth;
                ExpandDigimonBookDescription(card);
                int needed = card.Controls.Count == 0 ? card.Height : card.Controls.Cast<Control>().Max(x => x.Bottom) + 14;
                if (needed > card.Height) card.Height = needed;

                foreach (Button b in card.Controls.OfType<Button>().Where(x => x.Text.StartsWith("EDIT", StringComparison.OrdinalIgnoreCase)))
                {
                    b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    b.Left = Math.Max(8, card.ClientSize.Width - b.Width - 18);
                }

                totalHeight += card.Height + card.Margin.Vertical;
            }

            list.AutoScrollMinSize = new Size(0, Math.Max(0, totalHeight));
        }

        private void ExpandDigimonBookDescription(Panel card)
        {
            Label? description = card.Controls.OfType<Label>()
                .Where(x => x.Width >= 400 && x.Top >= 50 && x.Top <= 80 && x.Text.Length > 45)
                .OrderBy(x => x.Top)
                .FirstOrDefault();
            if (description == null || Equals(description.Tag, "DigimonBookExpanded")) return;

            description.Tag = "DigimonBookExpanded";
            int oldBottom = description.Bottom;
            Size measured = TextRenderer.MeasureText(
                description.Text,
                description.Font,
                new Size(Math.Max(120, description.Width - 4), 1000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            int newHeight = Math.Max(description.Height, Math.Min(220, measured.Height + 6));
            int delta = newHeight - description.Height;
            if (delta <= 0) return;

            description.Height = newHeight;
            foreach (Control c in card.Controls.Cast<Control>().Where(x => !ReferenceEquals(x, description) && x.Top >= oldBottom + 1).ToArray())
                c.Top += delta;
            card.Height += delta;
        }

        private void WireWheelFocus(Control control, FlowLayoutPanel list)
        {
            if (_digimonBookScrollWired.Contains(control)) return;
            _digimonBookScrollWired.Add(control);
            control.MouseEnter += (_, _) =>
            {
                if (!list.IsDisposed && list.CanFocus) list.Focus();
            };
        }

        private async Task RunDigimonBookImportAsync()
        {
            string folder = Path.Combine(AppPaths.Xml, "Digimon_Book");
            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Digimon Book Import DB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show("Configure and test the SQL Server connection in SETTINGS first.", "Digimon Book Import DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "Import the current Digimon_Book XML into all three database tables?\r\n\r\n" +
                "• Asset.DeckBookInfo\r\n• Asset.DeckBuff\r\n• Asset.DeckBuffOption\r\n\r\n" +
                "The operation runs in one SQL transaction and writes BEFORE_*.csv snapshots to Logs first.\r\n" +
                "Groups no longer present in DeckOption.xml will be removed from DeckBuff/DeckBuffOption.",
                "Digimon Book Import DB", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            var page = CreateDarkTab("Digimon Book DB Import");
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var title = new Label { Text = "Digimon Book → Database", ForeColor = CText, Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold), Location = new Point(8, 8), AutoSize = true };
            var subtitle = new Label { Text = "Transactional import • automatic BEFORE snapshots • rollback on failure", ForeColor = CMuted, Location = new Point(10, 40), AutoSize = true };
            var status = new Label { Text = "Preparing...", ForeColor = CMuted, Location = new Point(10, 68), Size = new Size(850, 24), AutoEllipsis = true };
            var log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 440, BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 8.5F) };
            root.Controls.AddRange(new Control[] { title, subtitle, status, log });
            page.Controls.Add(root);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            using var cts = new CancellationTokenSource();
            EventHandler disposed = (_, _) => { if (!cts.IsCancellationRequested) cts.Cancel(); };
            page.Disposed += disposed;
            var progress = new Progress<string>(line =>
            {
                if (page.IsDisposed) return;
                status.Text = line;
                log.AppendText(line + Environment.NewLine);
            });

            try
            {
                var service = new DigimonBookDatabaseImportService();
                DigimonBookDatabaseImportSummary summary = await service.ImportAsync(connection, folder, progress, cts.Token);
                if (page.IsDisposed) return;

                status.Text = $"DONE • DeckBookInfo {summary.BookInfoRows:N0} • DeckBuff {summary.DeckBuffRows:N0} • DeckBuffOption {summary.DeckBuffOptionRows:N0}";
                status.ForeColor = Color.FromArgb(120, 220, 145);
                MessageBox.Show(
                    "Digimon Book import completed successfully.\r\n\r\n" +
                    $"DeckBookInfo: {summary.BookInfoRows:N0} rows\r\n" +
                    $"DeckBuff: {summary.DeckBuffRows:N0} rows\r\n" +
                    $"DeckBuffOption: {summary.DeckBuffOptionRows:N0} rows\r\n\r\n" +
                    "A BEFORE snapshot of all three tables was saved in the import log folder.",
                    "Digimon Book Import DB", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                if (!page.IsDisposed) { status.Text = "Cancelled — transaction rolled back."; status.ForeColor = Color.FromArgb(255, 190, 90); }
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "FAILED — transaction rolled back.";
                    status.ForeColor = Color.FromArgb(255, 100, 110);
                    log.AppendText(Environment.NewLine + ex + Environment.NewLine);
                    ShowEditorError("Digimon Book Import DB", ex);
                }
            }
            finally
            {
                if (!page.IsDisposed) page.Disposed -= disposed;
            }
        }
    }
}
