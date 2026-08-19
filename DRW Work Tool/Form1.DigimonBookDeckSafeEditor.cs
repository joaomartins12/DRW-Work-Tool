using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private void EnhanceDigimonBookSafeDeckButtons(TabPage sourcePage, DigimonBookTabState state)
        {
            if (!state.FileName.Equals("DeckComposition.xml", StringComparison.OrdinalIgnoreCase))
                return;

            FlowLayoutPanel? list = state.Content.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            if (list == null || list.IsDisposed) return;

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

                Button? safe = card.Controls.OfType<Button>()
                    .FirstOrDefault(x => x.Name == "DigimonBookDeckEditSafe");

                Button? template = card.Controls.OfType<Button>()
                    .FirstOrDefault(x =>
                        x.Text.Equals("EDIT DECK", StringComparison.OrdinalIgnoreCase) &&
                        x.Name != "DigimonBookDeckEditSafe");

                foreach (Button old in card.Controls.OfType<Button>()
                             .Where(x =>
                                 x.Text.Equals("EDIT DECK", StringComparison.OrdinalIgnoreCase) &&
                                 x.Name != "DigimonBookDeckEditSafe")
                             .ToArray())
                {
                    old.Visible = false;
                }

                if (safe != null)
                {
                    safe.Size = template?.Size ?? safe.Size;
                    safe.Location = template?.Location ?? new Point(Math.Max(12, card.ClientSize.Width - safe.Width - 18), 18);
                    safe.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    safe.Visible = true;
                    safe.Enabled = true;
                    safe.BringToFront();
                    continue;
                }

                var edit = CreateEditorActionButton("EDIT DECK");
                edit.Name = "DigimonBookDeckEditSafe";
                edit.Size = template?.Size ?? new Size(110, 32);
                edit.Location = template?.Location ?? new Point(Math.Max(12, card.ClientSize.Width - 128), 18);
                edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                edit.Visible = true;
                edit.Enabled = true;
                edit.Click += (_, _) => OpenDeckCompositionEditorTabSafe(state, groupId);
                card.Controls.Add(edit);
                edit.BringToFront();
            }
        }

        private void OpenDeckCompositionEditorTabSafe(DigimonBookTabState sourceState, uint groupId)
        {
            string key = sourceState.XmlPath + "#deck-safe:" + groupId.ToString(CultureInfo.InvariantCulture);
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

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(16)
            };
            page.Controls.Add(root);

            // Fixed three-row layout keeps the header, editor body and SAVE action
            // independent. The SplitContainer can never cover the footer.
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                ColumnCount = 1,
                RowCount = 3,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.Controls.Add(shell);

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Margin = Padding.Empty
            };
            var title = L($"Deck Composition — Group {groupId}", 0, 2, 520, 28, true);
            title.Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold);
            header.Controls.Add(title);
            header.Controls.Add(L(
                "Select Digimon from Digimon_List.xml • preview resolves ModelID → Model.xml → Data\\Digimon.",
                0, 31, 760, 22, false, Color.FromArgb(120, 220, 145)));
            shell.Controls.Add(header, 0, 0);

            var body = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor = CEditor,
                FixedPanel = FixedPanel.None,
                IsSplitterFixed = false,
                Margin = Padding.Empty
            };
            shell.Controls.Add(body, 0, 1);

            void ApplySafeSplitter()
            {
                if (body.IsDisposed) return;

                int width = body.ClientSize.Width;
                if (width <= 0) return;

                int desiredRight = Math.Max(260, Math.Min(360, width / 3));
                int desiredLeft = width - desiredRight;
                int minLeft = Math.Min(360, Math.Max(120, width / 3));
                int minRight = Math.Min(240, Math.Max(100, width / 4));

                if (minLeft + minRight + body.SplitterWidth >= width)
                {
                    minLeft = Math.Max(80, (width - body.SplitterWidth) / 2 - 20);
                    minRight = Math.Max(80, width - body.SplitterWidth - minLeft - 20);
                }

                body.Panel1MinSize = Math.Max(0, minLeft);
                body.Panel2MinSize = Math.Max(0, minRight);

                int minimum = body.Panel1MinSize;
                int maximum = Math.Max(minimum, width - body.Panel2MinSize - body.SplitterWidth);
                int target = Math.Max(minimum, Math.Min(desiredLeft, maximum));

                if (maximum >= minimum)
                    body.SplitterDistance = target;
            }
            body.SizeChanged += (_, _) => ApplySafeSplitter();

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
                members.Rows.Add(
                    T(m, "s_dwBaseDigimonID"),
                    T(m, "s_szBaseDigimonName"),
                    T(m, "s_nEvolslot"),
                    T(m, "s_dwDestDigimonID"),
                    T(m, "s_szDestDigimonName"));
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
            var addMember = CreateEditorActionButton("ADD MEMBER");
            addMember.Size = new Size(110, 30);
            var removeMember = CreateEditorActionButton("REMOVE");
            removeMember.Size = new Size(92, 30);
            memberButtons.Controls.AddRange(new Control[] { addMember, removeMember });
            body.Panel1.Controls.Add(memberButtons);
            memberButtons.BringToFront();

            var right = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(10, 0, 0, 0)
            };
            body.Panel2.Controls.Add(right);
            right.Controls.Add(L("DIGIMON SELECTOR", 10, 4, 260, 24, true));

            var search = BookTabTextBox(string.Empty, 10, 32, 250);
            search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(search);

            var results = new DataGridView
            {
                Location = new Point(10, 70),
                Size = new Size(300, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
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
                Size = new Size(82, 82),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            right.Controls.Add(preview);

            var selectedLabel = L("Select a Digimon.", 104, 0, 210, 70, false, CMuted);
            selectedLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(selectedLabel);

            var setBase = CreateEditorActionButton("SET BASE");
            setBase.Size = new Size(100, 30);
            var setDest = CreateEditorActionButton("SET DEST");
            setDest.Size = new Size(100, 30);
            setBase.Anchor = setDest.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            right.Controls.AddRange(new Control[] { setBase, setDest });

            void LayoutRightBottom()
            {
                int bottom = right.ClientSize.Height - 10;
                setBase.Location = new Point(10, Math.Max(330, bottom - 30));
                setDest.Location = new Point(120, Math.Max(330, bottom - 30));
                preview.Location = new Point(10, Math.Max(236, setBase.Top - 94));
                selectedLabel.Location = new Point(104, preview.Top);
                results.Height = Math.Max(140, preview.Top - results.Top - 10);
                results.Width = Math.Max(180, right.ClientSize.Width - 20);
                search.Width = Math.Max(160, right.ClientSize.Width - 20);
            }
            right.Resize += (_, _) => LayoutRightBottom();

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
                selectedDigimon = results.SelectedRows.Count > 0
                    ? results.SelectedRows[0].Tag as DigimonBookDigimonEntry
                    : null;

                Image? old = preview.Image;
                preview.Image = null;
                old?.Dispose();
                preview.Image = selectedDigimon == null
                    ? null
                    : DigimonBookDigimonCatalog.TryLoadIcon(selectedDigimon.Id);

                selectedLabel.Text = selectedDigimon == null
                    ? "Select a Digimon."
                    : $"{selectedDigimon.Id} — {selectedDigimon.Name}\r\nModelID {selectedDigimon.ModelId}";
            }

            search.TextChanged += (_, _) =>
            {
                FillResults();
                RefreshSelected();
            };
            results.SelectionChanged += (_, _) => RefreshSelected();

            DataGridViewRow EnsureMemberRow()
            {
                if (members.SelectedRows.Count > 0)
                    return members.SelectedRows[0];

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

            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Margin = Padding.Empty,
                Padding = new Padding(0, 8, 0, 0)
            };
            shell.Controls.Add(footer, 0, 2);

            var save = CreateEditorActionButton("SAVE DECK");
            save.Name = "DigimonBookDeckSave";
            save.Size = new Size(130, 34);
            save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            footer.Controls.Add(save);

            void LayoutSave()
            {
                save.Location = new Point(
                    Math.Max(0, footer.ClientSize.Width - save.Width),
                    8);
                save.Visible = true;
                save.Enabled = true;
                save.BringToFront();
            }
            footer.Resize += (_, _) => LayoutSave();

            save.Click += (_, _) =>
            {
                try
                {
                    XDocument latest = XDocument.Load(sourceState.XmlPath, LoadOptions.PreserveWhitespace);
                    XElement? target = latest.Root?.Elements("DeckComposition")
                        .FirstOrDefault(x => U(x, "s_nGroupIdx") == groupId);
                    if (target == null)
                    {
                        MessageBox.Show("The deck group no longer exists in DeckComposition.xml.", "Save Deck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    target.Elements("DeckDigimon").Remove();
                    foreach (DataGridViewRow row in members.Rows)
                    {
                        if (row.IsNewRow) continue;

                        var node = new XElement("DeckDigimon",
                            new XElement("s_dwBaseDigimonID", Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture) ?? "0"),
                            new XElement("s_szBaseDigimonName", Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty),
                            new XElement("s_nEvolslot", Convert.ToString(row.Cells[2].Value, CultureInfo.InvariantCulture) ?? "0"),
                            new XElement("s_dwDestDigimonID", Convert.ToString(row.Cells[3].Value, CultureInfo.InvariantCulture) ?? "0"),
                            new XElement("s_szDestDigimonName", Convert.ToString(row.Cells[4].Value, CultureInfo.InvariantCulture) ?? string.Empty));
                        target.Add(node);
                    }

                    System.IO.File.Copy(sourceState.XmlPath, sourceState.XmlPath + ".editor.bak", true);
                    latest.Save(sourceState.XmlPath);
                    BuildDigimonBookCards(sourceState);

                    TabPage? sourcePage = editorTabs.TabPages.Cast<TabPage>()
                        .FirstOrDefault(x => ReferenceEquals(x.Tag, sourceState));
                    if (sourcePage != null)
                    {
                        ApplyDigimonBookStableLayout(sourcePage, sourceState);
                        EnhanceDigimonBookInternalEditors(sourcePage, sourceState);
                        EnhanceDigimonBookSafeDeckButtons(sourcePage, sourceState);
                    }

                    MessageBox.Show("Deck saved successfully.", "Save Deck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    editorTabs.SelectedTab = sourcePage ?? page;
                }
                catch (Exception ex)
                {
                    ShowEditorError("Save Deck", ex);
                }
            };

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            BeginInvoke(new Action(() =>
            {
                if (page.IsDisposed) return;
                ApplySafeSplitter();
                LayoutRightBottom();
                LayoutSave();
                FillResults();
                if (results.Rows.Count > 0)
                {
                    results.Rows[0].Selected = true;
                    RefreshSelected();
                }
            }));
        }
    }
}
