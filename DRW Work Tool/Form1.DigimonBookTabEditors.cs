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
        private void EnhanceDigimonBookInternalEditors(TabPage sourcePage, DigimonBookTabState state)
        {
            FlowLayoutPanel? list = state.Content.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (list == null || list.IsDisposed) return;

            if (state.FileName.Equals("BookInfo.xml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Panel card in list.Controls.OfType<Panel>())
                {
                    Label? idLabel = card.Controls.OfType<Label>()
                        .FirstOrDefault(x => x.Text.StartsWith("Option ID ", StringComparison.OrdinalIgnoreCase));
                    if (idLabel == null) continue;

                    string raw = idLabel.Text.Split('•')[0]
                        .Replace("Option ID", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Trim();
                    if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint optionId))
                        continue;

                    Button? legacy = card.Controls.OfType<Button>()
                        .FirstOrDefault(x => x.Text.Equals("EDIT", StringComparison.OrdinalIgnoreCase));
                    if (legacy == null) continue;

                    Button? live = card.Controls.OfType<Button>()
                        .FirstOrDefault(x => x.Name == "DigimonBookBookInfoEditTab");
                    if (live != null) continue;

                    legacy.Visible = false;
                    var edit = CreateEditorActionButton("EDIT");
                    edit.Name = "DigimonBookBookInfoEditTab";
                    edit.Size = legacy.Size;
                    edit.Location = legacy.Location;
                    edit.Anchor = legacy.Anchor;
                    edit.Click += (_, _) => OpenBookInfoEditorTab(state, optionId);
                    card.Controls.Add(edit);
                    edit.BringToFront();
                }
            }
            else if (state.FileName.Equals("DeckComposition.xml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Panel card in list.Controls.OfType<Panel>())
                {
                    Label? groupLabel = card.Controls.OfType<Label>()
                        .FirstOrDefault(x => x.Text.StartsWith("Group ", StringComparison.OrdinalIgnoreCase));
                    if (groupLabel == null) continue;

                    string raw = groupLabel.Text.Split('•')[0]
                        .Replace("Group", string.Empty, StringComparison.OrdinalIgnoreCase)
                        .Trim();
                    if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint groupId))
                        continue;

                    Button? legacy = card.Controls.OfType<Button>()
                        .FirstOrDefault(x => x.Text.Equals("EDIT DECK", StringComparison.OrdinalIgnoreCase));
                    if (legacy == null) continue;

                    if (card.Controls.OfType<Button>().Any(x => x.Name == "DigimonBookDeckEditTab"))
                        continue;

                    legacy.Visible = false;
                    var edit = CreateEditorActionButton("EDIT DECK");
                    edit.Name = "DigimonBookDeckEditTab";
                    edit.Size = legacy.Size;
                    edit.Location = legacy.Location;
                    edit.Anchor = legacy.Anchor;
                    edit.Click += (_, _) => OpenDeckCompositionEditorTab(state, groupId);
                    card.Controls.Add(edit);
                    edit.BringToFront();
                }
            }
        }

        private void OpenBookInfoEditorTab(DigimonBookTabState sourceState, uint optionId)
        {
            string key = sourceState.XmlPath + "#bookinfo:" + optionId.ToString(CultureInfo.InvariantCulture);
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>()
                .FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            XDocument doc = XDocument.Load(sourceState.XmlPath, LoadOptions.PreserveWhitespace);
            XElement? original = doc.Root?.Elements("BookInfo")
                .FirstOrDefault(x => U(x, "s_dwOptID") == optionId);
            if (original == null)
            {
                MessageBox.Show($"BookInfo Option {optionId} was not found.", "BookInfo Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            XElement working = new XElement(original);
            var page = CreateDarkTab("BookInfo " + optionId);
            page.Name = key;

            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(20) };
            page.Controls.Add(root);

            var title = L($"BookInfo Option {optionId}", 0, 0, 560, 30, true);
            title.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            var subtitle = L("Edit inside the Work Tool • icon picker uses sicon01-sicon07", 0, 32, 650, 22, false, Color.FromArgb(120, 220, 145));
            root.Controls.AddRange(new Control[] { title, subtitle });

            root.Controls.Add(L("Option ID", 0, 72, 160, 20, true));
            var idBox = BookTabTextBox(T(working, "s_dwOptID"), 0, 96, 320);
            root.Controls.Add(idBox);

            root.Controls.Add(L("Name", 0, 140, 160, 20, true));
            var nameBox = BookTabTextBox(T(working, "s_szOptName"), 0, 164, 620);
            root.Controls.Add(nameBox);

            root.Controls.Add(L("Icon", 0, 208, 160, 20, true));
            uint iconId = U(working, "s_nIcon");
            var preview = new PictureBox
            {
                Location = new Point(0, 236),
                Size = new Size(88, 88),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Cursor = Cursors.Hand
            };
            preview.Image = ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Skill");
            root.Controls.Add(preview);

            var iconBox = BookTabTextBox(iconId.ToString(CultureInfo.InvariantCulture), 108, 236, 280);
            iconBox.ReadOnly = true;
            root.Controls.Add(iconBox);

            var selectIcon = CreateEditorActionButton("SELECT ICON");
            selectIcon.Location = new Point(404, 236);
            selectIcon.Size = new Size(132, 30);
            root.Controls.Add(selectIcon);
            root.Controls.Add(L("Browse sicon01 → sicon07 and confirm a mapped slot.", 108, 276, 500, 24, false, CMuted));

            root.Controls.Add(L("Description", 0, 346, 180, 20, true));
            var explainBox = BookTabTextBox(T(working, "s_szOptExplain"), 0, 370, 690);
            explainBox.Multiline = true;
            explainBox.ScrollBars = ScrollBars.Vertical;
            explainBox.Height = 120;
            root.Controls.Add(explainBox);

            async Task PickAsync()
            {
                uint current = uint.TryParse(iconBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0;
                uint? selected = await OpenSkillAtlasIconBrowserAsync(current, "Select BookInfo Icon");
                if (!selected.HasValue || page.IsDisposed) return;

                iconBox.Text = selected.Value.ToString(CultureInfo.InvariantCulture);
                Image? old = preview.Image;
                preview.Image = ImageDatabasePreview.TryLoadInterfaceIcon(selected.Value, "Skill");
                old?.Dispose();
                editorTabs.SelectedTab = page;
            }

            selectIcon.Click += async (_, _) => await PickAsync();
            preview.Click += async (_, _) => await PickAsync();

            var save = CreateEditorActionButton("SAVE");
            save.Size = new Size(120, 34);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            save.Location = new Point(Math.Max(0, root.ClientSize.Width - 140), Math.Max(0, root.ClientSize.Height - 54));
            root.Resize += (_, _) => save.Location = new Point(Math.Max(0, root.ClientSize.Width - 140), Math.Max(0, root.ClientSize.Height - 54));
            save.Click += (_, _) =>
            {
                XDocument latest = XDocument.Load(sourceState.XmlPath, LoadOptions.PreserveWhitespace);
                XElement? target = latest.Root?.Elements("BookInfo")
                    .FirstOrDefault(x => U(x, "s_dwOptID") == optionId);
                if (target == null) return;

                BookSet(working, "s_dwOptID", idBox.Text.Trim());
                BookSet(working, "s_szOptName", nameBox.Text);
                BookSet(working, "s_nIcon", iconBox.Text.Trim());
                BookSet(working, "s_szOptExplain", explainBox.Text);
                target.ReplaceWith(working);
                File.Copy(sourceState.XmlPath, sourceState.XmlPath + ".editor.bak", true);
                latest.Save(sourceState.XmlPath);

                BuildDigimonBookCards(sourceState);
                TabPage? sourcePage = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => ReferenceEquals(x.Tag, sourceState));
                if (sourcePage != null)
                {
                    ApplyDigimonBookStableLayout(sourcePage, sourceState);
                    EnhanceDigimonBookInternalEditors(sourcePage, sourceState);
                }
                editorTabs.SelectedTab = sourcePage ?? page;
            };
            root.Controls.Add(save);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
        }

        private void OpenDeckCompositionEditorTab(DigimonBookTabState sourceState, uint groupId)
        {
            string key = sourceState.XmlPath + "#deck:" + groupId.ToString(CultureInfo.InvariantCulture);
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>()
                .FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            XDocument source = XDocument.Load(sourceState.XmlPath, LoadOptions.PreserveWhitespace);
            XElement? original = source.Root?.Elements("DeckComposition")
                .FirstOrDefault(x => U(x, "s_nGroupIdx") == groupId);
            if (original == null) return;

            var page = CreateDarkTab("Deck " + groupId);
            page.Name = key;
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(16) };
            page.Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = CEditor };
            var title = L($"Deck Composition — Group {groupId}", 0, 2, 520, 28, true);
            title.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            header.Controls.Add(title);
            header.Controls.Add(L("Select Digimon from Digimon_List.xml; previews follow ModelID → Model.xml → Data\\Digimon folder.", 0, 31, 760, 22, false, Color.FromArgb(120, 220, 145)));
            root.Controls.Add(header);

            var body = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 585,
                BackColor = CEditor,
                Panel1MinSize = 420,
                Panel2MinSize = 280
            };
            root.Controls.Add(body);
            body.BringToFront();

            var members = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.Black,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            foreach (string h in new[] { "BaseDigimonID", "BaseName", "EvolSlot", "DestDigimonID", "DestName" })
                members.Columns.Add(h, h);
            foreach (XElement m in original.Elements("DeckDigimon"))
                members.Rows.Add(T(m, "s_dwBaseDigimonID"), T(m, "s_szBaseDigimonName"), T(m, "s_nEvolslot"), T(m, "s_dwDestDigimonID"), T(m, "s_szDestDigimonName"));
            body.Panel1.Controls.Add(members);

            var memberButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(0, 5, 0, 0)
            };
            var addMember = CreateEditorActionButton("ADD MEMBER"); addMember.Size = new Size(110, 30);
            var removeMember = CreateEditorActionButton("REMOVE"); removeMember.Size = new Size(92, 30);
            memberButtons.Controls.AddRange(new Control[] { addMember, removeMember });
            body.Panel1.Controls.Add(memberButtons);
            memberButtons.BringToFront();

            var right = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(10, 0, 0, 0) };
            body.Panel2.Controls.Add(right);
            right.Controls.Add(L("DIGIMON SELECTOR", 10, 4, 260, 24, true));
            var search = BookTabTextBox(string.Empty, 10, 32, 250);
            right.Controls.Add(search);

            var results = new DataGridView
            {
                Location = new Point(10, 70),
                Size = new Size(330, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackgroundColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.Black,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            results.Columns.Add("ID", "ID");
            results.Columns.Add("ModelID", "ModelID");
            results.Columns.Add("Name", "Name");
            right.Controls.Add(results);

            var preview = new PictureBox
            {
                Location = new Point(10, 334),
                Size = new Size(88, 88),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            right.Controls.Add(preview);
            var selectedLabel = L("Select a Digimon.", 110, 334, 230, 70, false, CMuted);
            right.Controls.Add(selectedLabel);

            var setBase = CreateEditorActionButton("SET BASE"); setBase.Location = new Point(10, 436); setBase.Size = new Size(100, 30);
            var setDest = CreateEditorActionButton("SET DEST"); setDest.Location = new Point(120, 436); setDest.Size = new Size(100, 30);
            right.Controls.AddRange(new Control[] { setBase, setDest });

            DigimonBookDigimonEntry? selectedDigimon = null;

            void FillResults()
            {
                results.Rows.Clear();
                foreach (DigimonBookDigimonEntry dig in DigimonBookDigimonCatalog.Search(search.Text, 300))
                {
                    int index = results.Rows.Add(dig.Id, dig.ModelId, dig.Name);
                    results.Rows[index].Tag = dig;
                }
            }

            void RefreshSelected()
            {
                selectedDigimon = results.SelectedRows.Count > 0 ? results.SelectedRows[0].Tag as DigimonBookDigimonEntry : null;
                Image? old = preview.Image;
                preview.Image = selectedDigimon == null ? null : DigimonBookDigimonCatalog.TryLoadIcon(selectedDigimon.Id);
                old?.Dispose();
                selectedLabel.Text = selectedDigimon == null
                    ? "Select a Digimon."
                    : $"{selectedDigimon.Id} — {selectedDigimon.Name}\r\nModelID {selectedDigimon.ModelId}";
            }

            search.TextChanged += (_, _) => { FillResults(); RefreshSelected(); };
            results.SelectionChanged += (_, _) => RefreshSelected();
            FillResults();
            if (results.Rows.Count > 0) results.Rows[0].Selected = true;

            DataGridViewRow EnsureMemberRow()
            {
                if (members.SelectedRows.Count > 0) return members.SelectedRows[0];
                int index = members.Rows.Add("0", "", "0", "0", "");
                members.Rows[index].Selected = true;
                return members.Rows[index];
            }

            setBase.Click += (_, _) =>
            {
                if (selectedDigimon == null) return;
                DataGridViewRow row = EnsureMemberRow();
                row.Cells[0].Value = selectedDigimon.Id;
                row.Cells[1].Value = selectedDigimon.Name;
            };
            setDest.Click += (_, _) =>
            {
                if (selectedDigimon == null) return;
                DataGridViewRow row = EnsureMemberRow();
                row.Cells[3].Value = selectedDigimon.Id;
                row.Cells[4].Value = selectedDigimon.Name;
            };
            results.CellDoubleClick += (_, _) => setDest.PerformClick();
            addMember.Click += (_, _) =>
            {
                int index = members.Rows.Add("0", "", "0", "0", "");
                members.ClearSelection();
                members.Rows[index].Selected = true;
            };
            removeMember.Click += (_, _) =>
            {
                if (members.SelectedRows.Count > 0)
                    members.Rows.Remove(members.SelectedRows[0]);
            };

            var save = CreateEditorActionButton("SAVE DECK");
            save.Size = new Size(120, 32);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            save.Location = new Point(Math.Max(0, root.ClientSize.Width - 140), Math.Max(0, root.ClientSize.Height - 48));
            root.Resize += (_, _) => save.Location = new Point(Math.Max(0, root.ClientSize.Width - 140), Math.Max(0, root.ClientSize.Height - 48));
            root.Controls.Add(save);
            save.BringToFront();

            save.Click += (_, _) =>
            {
                XDocument latest = XDocument.Load(sourceState.XmlPath, LoadOptions.PreserveWhitespace);
                XElement? target = latest.Root?.Elements("DeckComposition")
                    .FirstOrDefault(x => U(x, "s_nGroupIdx") == groupId);
                if (target == null) return;

                target.Elements("DeckDigimon").Remove();
                int count = 0;
                foreach (DataGridViewRow row in members.Rows)
                {
                    if (!uint.TryParse(Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture), out uint baseId) || baseId == 0)
                        continue;

                    string baseName = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
                    string evolSlot = Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? "0";
                    string destId = Convert.ToString(row.Cells[3].Value, CultureInfo.InvariantCulture) ?? "0";
                    string destName = Convert.ToString(row.Cells[4].Value) ?? string.Empty;

                    target.Add(new XElement("DeckDigimon",
                        new XElement("s_dwBaseDigimonID", baseId),
                        new XElement("s_szBaseDigimonName", baseName),
                        new XElement("s_nEvolslot", evolSlot),
                        new XElement("s_dwDestDigimonID", destId),
                        new XElement("s_szDestDigimonName", destName)));
                    count++;
                }

                BookSet(target, "s_nVal", count.ToString(CultureInfo.InvariantCulture));
                File.Copy(sourceState.XmlPath, sourceState.XmlPath + ".editor.bak", true);
                latest.Save(sourceState.XmlPath);
                BuildDigimonBookCards(sourceState);

                TabPage? sourcePage = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => ReferenceEquals(x.Tag, sourceState));
                if (sourcePage != null)
                {
                    ApplyDigimonBookStableLayout(sourcePage, sourceState);
                    EnhanceDigimonBookInternalEditors(sourcePage, sourceState);
                    editorTabs.SelectedTab = sourcePage;
                }
            };

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
        }

        private TextBox BookTabTextBox(string text, int x, int y, int width)
        {
            return new TextBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 28),
                BackColor = Color.Black,
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle
            };
        }
    }
}
