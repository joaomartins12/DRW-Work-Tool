using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private void ApplyDigimonBookStableLayout(TabPage page, DigimonBookTabState state)
        {
            if (page.IsDisposed || state.Content.IsDisposed) return;

            Panel? root = state.Content.Parent as Panel;
            if (root == null || root.IsDisposed) return;

            Panel? header = root.Controls.OfType<Panel>()
                .FirstOrDefault(x => !ReferenceEquals(x, state.Content) && x.Controls.OfType<Label>().Any(l => l.Text.StartsWith("Digimon Book", StringComparison.OrdinalIgnoreCase)));
            if (header == null) return;

            void Layout()
            {
                if (root.IsDisposed || header.IsDisposed || state.Content.IsDisposed) return;

                int left = 0;
                int top = header.Bottom + 10;
                int width = Math.Max(120, root.ClientSize.Width);
                int height = Math.Max(80, root.ClientSize.Height - top);

                state.Content.Dock = DockStyle.None;
                state.Content.Bounds = new Rectangle(left, top, width, height);
                state.Content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

                if (state.Content.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is FlowLayoutPanel list)
                {
                    list.Dock = DockStyle.Fill;
                    list.Padding = new Padding(8, 18, 18, 36);
                    list.Margin = Padding.Empty;
                    list.WrapContents = false;
                    list.FlowDirection = FlowDirection.TopDown;
                    list.AutoScroll = true;
                    list.TabStop = false;

                    int usableWidth = Math.Max(660, list.ClientSize.Width - list.Padding.Horizontal - 20);
                    foreach (Panel card in list.Controls.OfType<Panel>())
                    {
                        card.Width = usableWidth;
                        foreach (Button b in card.Controls.OfType<Button>().Where(x => x.Text.StartsWith("EDIT", StringComparison.OrdinalIgnoreCase)))
                            b.Left = Math.Max(12, card.ClientSize.Width - b.Width - 18);
                    }

                    list.PerformLayout();
                }
            }

            Layout();
            root.Resize -= DigimonBookRootResizeProxy;
            root.Resize += DigimonBookRootResizeProxy;
            root.Tag = new DigimonBookLayoutTag { Page = page, State = state, Layout = Layout };

            BeginInvoke(new Action(() =>
            {
                if (page.IsDisposed) return;
                Layout();
                ResetDigimonBookScroll(state);
                EnhanceBookInfoCards(page, state);
            }));
        }

        private sealed class DigimonBookLayoutTag
        {
            public required TabPage Page { get; init; }
            public required DigimonBookTabState State { get; init; }
            public required Action Layout { get; init; }
        }

        private void DigimonBookRootResizeProxy(object? sender, EventArgs e)
        {
            if (sender is Panel root && root.Tag is DigimonBookLayoutTag tag && !tag.Page.IsDisposed)
            {
                tag.Layout();
                ResetDigimonBookScroll(tag.State);
            }
        }

        private static void ResetDigimonBookScroll(DigimonBookTabState state)
        {
            FlowLayoutPanel? list = state.Content.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (list == null || list.IsDisposed) return;

            list.SuspendLayout();
            try
            {
                list.AutoScrollPosition = Point.Empty;
                if (list.VerticalScroll.Visible)
                {
                    list.VerticalScroll.Value = list.VerticalScroll.Minimum;
                    list.PerformLayout();
                }
            }
            catch
            {
                list.AutoScrollPosition = Point.Empty;
            }
            finally
            {
                list.ResumeLayout(true);
            }
        }

        private void EnhanceBookInfoCards(TabPage page, DigimonBookTabState state)
        {
            if (!state.FileName.Equals("BookInfo.xml", StringComparison.OrdinalIgnoreCase)) return;
            FlowLayoutPanel? list = state.Content.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (list == null) return;

            foreach (Panel card in list.Controls.OfType<Panel>())
            {
                Label? idLabel = card.Controls.OfType<Label>().FirstOrDefault(x => x.Text.StartsWith("Option ID ", StringComparison.OrdinalIgnoreCase));
                if (idLabel == null) continue;

                string first = idLabel.Text.Split('•')[0].Trim();
                string rawId = first.Replace("Option ID", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                if (!uint.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint optionId)) continue;

                PictureBox? icon = card.Controls.OfType<PictureBox>().FirstOrDefault();
                if (icon != null && icon.Name != "DigimonBookBookInfoIconPicker")
                {
                    icon.Name = "DigimonBookBookInfoIconPicker";
                    icon.Cursor = Cursors.Hand;
                    editorToolTip.SetToolTip(icon, "Select BookInfo icon from sicon01-sicon07");
                    icon.Click += async (_, _) => await SelectBookInfoIconAsync(state, optionId);
                }

                Button? oldEdit = card.Controls.OfType<Button>().FirstOrDefault(x => x.Text.Equals("EDIT", StringComparison.OrdinalIgnoreCase));
                if (oldEdit == null || oldEdit.Name == "DigimonBookBookInfoEditV2") continue;

                oldEdit.Visible = false;
                var edit = CreateEditorActionButton("EDIT");
                edit.Name = "DigimonBookBookInfoEditV2";
                edit.Size = oldEdit.Size;
                edit.Location = oldEdit.Location;
                edit.Anchor = oldEdit.Anchor;
                edit.Click += async (_, _) => await OpenBookInfoEditorV2Async(state, optionId);
                card.Controls.Add(edit);
                edit.BringToFront();
            }
        }

        private async Task SelectBookInfoIconAsync(DigimonBookTabState state, uint optionId)
        {
            XDocument doc = XDocument.Load(state.XmlPath, LoadOptions.PreserveWhitespace);
            XElement? row = doc.Root?.Elements("BookInfo").FirstOrDefault(x => U(x, "s_dwOptID") == optionId);
            if (row == null) return;

            uint current = U(row, "s_nIcon");
            uint? selected = await OpenSkillAtlasIconBrowserAsync(current, "Select BookInfo Icon");
            if (!selected.HasValue) return;

            BookSet(row, "s_nIcon", selected.Value.ToString(CultureInfo.InvariantCulture));
            File.Copy(state.XmlPath, state.XmlPath + ".editor.bak", true);
            doc.Save(state.XmlPath);
            BuildDigimonBookCards(state);

            TabPage? page = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => ReferenceEquals(x.Tag, state));
            if (page != null)
            {
                ApplyDigimonBookStableLayout(page, state);
                EnhanceBookInfoCards(page, state);
            }
        }

        private async Task OpenBookInfoEditorV2Async(DigimonBookTabState state, uint optionId)
        {
            XDocument source = XDocument.Load(state.XmlPath, LoadOptions.PreserveWhitespace);
            XElement? original = source.Root?.Elements("BookInfo").FirstOrDefault(x => U(x, "s_dwOptID") == optionId);
            if (original == null) return;
            XElement working = new XElement(original);

            using var form = CreateDarkDialog("Edit BookInfo.xml", 700, 560);

            form.Controls.Add(L("Option ID", 20, 18, 180, 20, true));
            var idBox = new TextBox { Text = T(working, "s_dwOptID"), Location = new Point(20, 42), Size = new Size(640, 28), BackColor = Color.Black, ForeColor = CText };
            form.Controls.Add(idBox);

            form.Controls.Add(L("Name", 20, 82, 180, 20, true));
            var nameBox = new TextBox { Text = T(working, "s_szOptName"), Location = new Point(20, 106), Size = new Size(640, 28), BackColor = Color.Black, ForeColor = CText };
            form.Controls.Add(nameBox);

            form.Controls.Add(L("Icon", 20, 148, 180, 20, true));
            uint iconId = U(working, "s_nIcon");
            var preview = new PictureBox { Location = new Point(20, 176), Size = new Size(72, 72), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Cursor = Cursors.Hand };
            preview.Image = ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Skill");
            form.Controls.Add(preview);

            var iconBox = new TextBox { Text = iconId.ToString(CultureInfo.InvariantCulture), Location = new Point(108, 176), Size = new Size(300, 28), BackColor = Color.Black, ForeColor = CText, ReadOnly = true };
            form.Controls.Add(iconBox);

            var selectIcon = CreateEditorActionButton("SELECT ICON");
            selectIcon.Location = new Point(420, 176);
            selectIcon.Size = new Size(128, 30);
            form.Controls.Add(selectIcon);

            var atlasHint = L("sicon01-sicon07 • click preview or SELECT ICON", 108, 211, 430, 22, false, Color.FromArgb(120, 220, 145));
            form.Controls.Add(atlasHint);

            async Task PickIconAsync()
            {
                uint current = uint.TryParse(iconBox.Text, out uint parsed) ? parsed : 0;
                uint? selected = await OpenSkillAtlasIconBrowserAsync(current, "Select BookInfo Icon");
                if (!selected.HasValue || form.IsDisposed) return;

                iconBox.Text = selected.Value.ToString(CultureInfo.InvariantCulture);
                Image? old = preview.Image;
                preview.Image = ImageDatabasePreview.TryLoadInterfaceIcon(selected.Value, "Skill");
                old?.Dispose();
            }

            selectIcon.Click += async (_, _) => await PickIconAsync();
            preview.Click += async (_, _) => await PickIconAsync();

            form.Controls.Add(L("Description", 20, 266, 180, 20, true));
            var explainBox = new TextBox
            {
                Text = T(working, "s_szOptExplain"),
                Location = new Point(20, 290),
                Size = new Size(640, 130),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.Black,
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle
            };
            form.Controls.Add(explainBox);

            var save = CreateEditorActionButton("SAVE");
            save.Size = new Size(120, 34);
            save.Location = new Point(540, 470);
            save.Click += (_, _) =>
            {
                BookSet(working, "s_dwOptID", idBox.Text.Trim());
                BookSet(working, "s_szOptName", nameBox.Text);
                BookSet(working, "s_nIcon", iconBox.Text.Trim());
                BookSet(working, "s_szOptExplain", explainBox.Text);

                XDocument doc = XDocument.Load(state.XmlPath, LoadOptions.PreserveWhitespace);
                XElement? target = doc.Root?.Elements("BookInfo").FirstOrDefault(x => U(x, "s_dwOptID") == optionId);
                if (target == null) return;
                target.ReplaceWith(working);
                File.Copy(state.XmlPath, state.XmlPath + ".editor.bak", true);
                doc.Save(state.XmlPath);

                BuildDigimonBookCards(state);
                TabPage? page = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => ReferenceEquals(x.Tag, state));
                if (page != null) ApplyDigimonBookStableLayout(page, state);

                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(save);

            form.ShowDialog(this);
        }
    }
}
