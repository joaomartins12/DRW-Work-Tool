using DRW_Work_Tool.Core;
using System;
using System.Diagnostics;
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
        private readonly System.Windows.Forms.Timer _dmBaseLevelPolishV2Timer = CreateDMBaseLevelPolishV2Timer();

        private static System.Windows.Forms.Timer CreateDMBaseLevelPolishV2Timer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 350 };
            timer.Tick += (_, _) =>
            {
                foreach (Form1 form in Application.OpenForms.OfType<Form1>().ToArray())
                {
                    if (!form.IsDisposed && form.IsHandleCreated)
                        form.ApplyDMBaseLevelPolishV2();
                }
            };
            timer.Start();
            return timer;
        }

        private void ApplyDMBaseLevelPolishV2()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            TabPage? page = editorTabs.SelectedTab;
            if (page?.Tag is not DMBaseVisualState state || !DMBaseIsLevelCurveFile(state.FileName))
                return;

            Panel? root = page.Controls.OfType<Panel>().FirstOrDefault(x => x.Dock == DockStyle.Fill);
            Panel? header = root?.Controls.OfType<Panel>().FirstOrDefault(x => x.Dock == DockStyle.Top);
            if (header == null)
                return;

            header.Height = Math.Max(160, header.Height);

            Button? newTemplate = header.Controls.OfType<Button>().FirstOrDefault(x => x.Text.Equals("NEW TEMPLATE", StringComparison.OrdinalIgnoreCase));
            Button? xmlInfo = header.Controls.OfType<Button>().FirstOrDefault(x => x.Text.Equals("XML INFO", StringComparison.OrdinalIgnoreCase));
            Label? title = header.Controls.OfType<Label>().OrderByDescending(x => x.Font.Size).FirstOrDefault();
            Label? subtitle = header.Controls.OfType<Label>().Where(x => !ReferenceEquals(x, title)).OrderBy(x => x.Top).FirstOrDefault();

            int right = Math.Max(300, header.ClientSize.Width - 4);
            if (newTemplate != null)
            {
                newTemplate.Size = new Size(118, 34);
                newTemplate.Location = new Point(Math.Max(300, right - newTemplate.Width), 4);
                newTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                newTemplate.BringToFront();
                right = newTemplate.Left - 10;
            }
            if (xmlInfo != null)
            {
                xmlInfo.Size = new Size(118, 34);
                xmlInfo.Location = new Point(Math.Max(170, right - xmlInfo.Width), 4);
                xmlInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                xmlInfo.BringToFront();
                right = xmlInfo.Left - 12;
            }

            if (title != null)
            {
                title.Width = Math.Max(180, right - title.Left);
                title.AutoEllipsis = true;
            }
            if (subtitle != null)
            {
                subtitle.Width = Math.Max(220, header.ClientSize.Width - subtitle.Left - 12);
                subtitle.AutoEllipsis = true;
            }

            if (state.FileName.StartsWith("TamerBase", StringComparison.OrdinalIgnoreCase))
                ReplaceTamerImportButton(header, state);
        }

        private void ReplaceTamerImportButton(Panel header, DMBaseVisualState state)
        {
            Button? current = header.Controls["DMBaseImportLevelDb"] as Button;
            if (current?.Tag as string == "tamer-import-v2")
                return;

            Point location = current?.Location ?? new Point(250, 112);
            Size size = current?.Size ?? new Size(108, 32);
            current?.Dispose();

            Button import = CreateEditorActionButton("IMPORT DB");
            import.Name = "DMBaseImportLevelDb";
            import.Tag = "tamer-import-v2";
            import.Size = size;
            import.Location = location;
            import.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            import.Click += async (_, _) => await RunDMBaseTamerImportAsync(state);
            header.Controls.Add(import);
            import.BringToFront();
        }

        private async Task RunDMBaseTamerImportAsync(DMBaseVisualState state)
        {
            if (state.Page.IsDisposed)
                return;

            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Tamer Level Import DB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(this, "Configure and test the SQL connection in SETTINGS first.", "Tamer Level Import DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                "Import the Tamer level curve into [dmo].[Asset].[CharacterLevelStatus]?\r\n\r\n" +
                "Confirmed from the supplied diagnostic:\r\n" +
                "• all existing DB Type values are preserved\r\n" +
                "• the canonical XML curve is detected against the current DB\r\n" +
                "• AT/CT/DE/DS/EV/HP/HT/MS map directly\r\n" +
                "• ExpValue = XML Exp / 100\r\n" +
                "• the whole table is replaced inside one SQL transaction\r\n" +
                "• a BEFORE snapshot and import plan are written to Logs first\r\n\r\n" +
                "Continue?",
                "Tamer Level Import DB",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            var page = CreateDarkTab("Tamer Level DB Import");
            page.Name = "dmbase-tamer-db-import:" + Guid.NewGuid().ToString("N");
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var title = new Label
            {
                Text = "TamerBase XML → CharacterLevelStatus",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Location = new Point(8, 8), AutoSize = true
            };
            var subtitle = new Label
            {
                Text = "Transactional import • existing Type set preserved • XML curve auto-validated against current DB",
                ForeColor = CMuted, Location = new Point(10, 40), AutoSize = true
            };
            var status = new Label
            {
                Text = "Preparing...", ForeColor = CMuted,
                Location = new Point(10, 70), Size = new Size(900, 24), AutoEllipsis = true
            };
            var log = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom, Height = 430,
                BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 8.5F)
            };
            root.Controls.AddRange(new Control[] { title, subtitle, status, log });
            page.Controls.Add(root);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            var cts = new CancellationTokenSource();
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
                var service = new DMBaseCharacterLevelImportService();
                DMBaseCharacterLevelImportSummary summary = await service.ImportAsync(connection, state.XmlPath, progress, cts.Token);
                if (page.IsDisposed) return;

                status.Text = $"DONE • Curve {summary.CanonicalCurveKey} • {summary.ExistingTypes} Types • {summary.LevelsPerType} levels • {summary.InsertedRows:N0} rows";
                status.ForeColor = Color.FromArgb(115, 225, 145);
                log.AppendText(Environment.NewLine + $"Canonical match: {summary.CanonicalMatchPercent:F4}%" + Environment.NewLine);
                log.AppendText("Output: " + summary.OutputFolder + Environment.NewLine);

                DialogResult open = MessageBox.Show(
                    this,
                    "CharacterLevelStatus import completed successfully.\r\n\r\n" +
                    $"Canonical CurveKey: {summary.CanonicalCurveKey}\r\n" +
                    $"Existing Types preserved: {summary.ExistingTypes}\r\n" +
                    $"Levels per Type: {summary.LevelsPerType}\r\n" +
                    $"Rows inserted: {summary.InsertedRows:N0}\r\n\r\n" +
                    "Open the backup/import log folder?",
                    "Tamer Level Import DB",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (open == DialogResult.Yes && Directory.Exists(summary.OutputFolder))
                    Process.Start(new ProcessStartInfo { FileName = summary.OutputFolder, UseShellExecute = true });
            }
            catch (OperationCanceledException)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "Cancelled — transaction rolled back.";
                    status.ForeColor = Color.FromArgb(255, 190, 90);
                }
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "FAILED — transaction rolled back.";
                    status.ForeColor = Color.FromArgb(255, 100, 110);
                    log.AppendText(Environment.NewLine + ex + Environment.NewLine);
                    ShowEditorError("Tamer Level Import DB", ex);
                }
            }
            finally
            {
                if (!page.IsDisposed)
                    page.Disposed -= disposed;
                cts.Dispose();
            }
        }
    }
}
