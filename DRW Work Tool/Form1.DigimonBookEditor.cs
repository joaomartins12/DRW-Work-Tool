using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
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
        private static readonly string[] DigimonBookFiles =
        {
            "BookInfo.xml",
            "DeckComposition.xml",
            "DeckOption.xml",
            "EncyclopediaException.xml"
        };

        private sealed class DigimonBookTabState
        {
            public required string XmlPath { get; init; }
            public required string FileName { get; init; }
            public required Panel Content { get; init; }
        }

        private sealed class BookInfoOptionChoice
        {
            public int Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public string Display => $"{Id} — {Name}";
        }

        private void EnsureDigimonBookOpenButtons(TabPage page)
        {
            foreach (Button oldOpen in EnumerateCashShopControls(page)
                         .OfType<Button>()
                         .Where(x => x.Text.Equals("OPEN", StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                if (oldOpen.Parent == null || oldOpen.Name.StartsWith("DigimonBookOpen_", StringComparison.Ordinal))
                    continue;

                string? fileName = oldOpen.Parent.Controls
                    .OfType<Label>()
                    .Select(x => Path.GetFileName(x.Text.Trim()))
                    .FirstOrDefault(x => DigimonBookFiles.Contains(x, StringComparer.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                string path = Path.Combine(AppPaths.Xml, "Digimon_Book", fileName);
                Control host = oldOpen.Parent;

                var open = CreateEditorActionButton("OPEN");
                open.Name = "DigimonBookOpen_" + Path.GetFileNameWithoutExtension(fileName);
                open.Location = oldOpen.Location;
                open.Size = oldOpen.Size;
                open.Anchor = oldOpen.Anchor;
                open.TabIndex = oldOpen.TabIndex;
                open.Click += (_, _) => OpenDigimonBookVisualEditor(path);

                host.Controls.Remove(oldOpen);
                oldOpen.Dispose();
                host.Controls.Add(open);
                open.BringToFront();
            }
        }

        private async void OpenDigimonBookVisualEditor(string xmlPath)
        {
            string full = Path.GetFullPath(xmlPath);
            string fileName = Path.GetFileName(full);

            TabPage? existing = editorTabs.TabPages.Cast<TabPage>()
                .FirstOrDefault(x => string.Equals(x.Name, full, StringComparison.OrdinalIgnoreCase) && x.Tag is DigimonBookTabState);
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            if (!File.Exists(full))
            {
                MessageBox.Show("XML not found:\r\n\r\n" + full, "Digimon Book Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            InstallDigimonBookRuntimeHooks();

            var page = CreateDarkTab(fileName);
            page.Name = full;

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(16)
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = CEditor,
                Padding = new Padding(0, 0, 0, 8)
            };

            var title = new Label
            {
                Text = "Digimon Book — " + Path.GetFileNameWithoutExtension(fileName),
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Location = new Point(4, 5),
                Height = 28,
                AutoEllipsis = true
            };

            var subtitle = new Label
            {
                Text = fileName == "DeckComposition.xml"
                    ? "Deck title/description linked from DeckOption.xml • Digimon displayed 7 per row"
                    : "Visual editor • Digimon Book cross-references preserved",
                ForeColor = CMuted,
                Location = new Point(6, 38),
                Height = 40,
                AutoEllipsis = true
            };

            var compare = CreateEditorActionButton("COMPARE DB");
            compare.Name = "DigimonBookCompareDb";
            compare.Size = new Size(116, 32);
            compare.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            compare.Click += async (_, _) => await RunDigimonBookCompareAsync();

            var import = CreateEditorActionButton("IMPORT DB");
            import.Name = "DigimonBookImportPlaceholder";
            import.Size = new Size(108, 32);
            import.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            import.Click += (_, _) => MessageBox.Show(
                "The Digimon Book importer is initialized when this editor is prepared.\r\n\r\n" +
                "If this message remains visible, close and reopen this Digimon Book tab.",
                "Digimon Book Import",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            void LayoutHeader()
            {
                int right = Math.Max(260, header.ClientSize.Width - 4);
                import.Location = new Point(Math.Max(8, right - import.Width), 12);
                compare.Location = new Point(Math.Max(8, import.Left - compare.Width - 8), 12);

                int textRight = Math.Max(180, compare.Left - 16);
                title.Width = Math.Max(160, textRight - title.Left);
                subtitle.Width = Math.Max(160, textRight - subtitle.Left);
            }

            header.Resize += (_, _) => LayoutHeader();
            header.Controls.AddRange(new Control[] { title, subtitle, compare, import });

            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                AutoScroll = false,
                Padding = new Padding(0, 8, 8, 8),
                Visible = false
            };

            root.Controls.Add(content);
            root.Controls.Add(header);
            page.Controls.Add(root);

            var state = new DigimonBookTabState
            {
                XmlPath = full,
                FileName = fileName,
                Content = content
            };
            page.Tag = state;

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            LayoutHeader();

            var loading = new EditorLoadingView(
                "Loading " + fileName,
                "Reading XML data, resolving linked BookInfo/DeckOption records and preparing every card/icon preview...")
            {
                Dock = DockStyle.Fill
            };
            root.Controls.Add(loading);
            loading.BringToFront();

            // Give WinForms a real paint cycle before the synchronous UI card build starts.
            await Task.Delay(70);

            try
            {
                BuildDigimonBookCards(state);
                PrepareDigimonBookPage(page);
                await Task.Yield();

                if (!page.IsDisposed)
                {
                    content.Visible = true;
                    content.BringToFront();
                    header.BringToFront();
                }
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    content.Controls.Clear();
                    content.Controls.Add(CreateInfoLabel(ex.Message));
                    content.Visible = true;
                    content.BringToFront();
                    header.BringToFront();
                }
            }
            finally
            {
                if (!loading.IsDisposed)
                    loading.Dispose();
            }
        }

        private void BuildDigimonBookCards(DigimonBookTabState state)
        {
            state.Content.SuspendLayout();
            try
            {
                foreach (Control c in state.Content.Controls.Cast<Control>().ToArray())
                    c.Dispose();
                state.Content.Controls.Clear();

                var list = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = CEditor,
                    Padding = new Padding(0, 0, 10, 24),
                    Margin = Padding.Empty,
                    TabStop = true
                };
                DarkUi.ApplyDarkScrollBar(list);
                state.Content.Controls.Add(list);

                XDocument doc = XDocument.Load(state.XmlPath, LoadOptions.PreserveWhitespace);
                switch (state.FileName.ToLowerInvariant())
                {
                    case "bookinfo.xml":
                        foreach (XElement row in doc.Root?.Elements("BookInfo") ?? Enumerable.Empty<XElement>())
                            list.Controls.Add(CreateBookInfoCard(state, row));
                        break;

                    case "deckcomposition.xml":
                        BuildDeckCompositionCards(state, doc, list);
                        break;

                    case "deckoption.xml":
                        foreach (XElement row in doc.Root?.Elements("DeckOption") ?? Enumerable.Empty<XElement>())
                            list.Controls.Add(CreateDeckOptionCard(state, row));
                        break;

                    case "encyclopediaexception.xml":
                        foreach (XElement row in doc.Root?.Elements("EncyclopediaException") ?? Enumerable.Empty<XElement>())
                            list.Controls.Add(CreateEncyclopediaCard(state, row));
                        break;
                }
            }
            finally
            {
                state.Content.ResumeLayout(true);
            }
        }

        private Control CreateBookInfoCard(DigimonBookTabState state, XElement row)
        {
            uint optionId = U(row, "s_dwOptID");
            uint iconId = U(row, "s_nIcon");
            string name = T(row, "s_szOptName");
            string explain = T(row, "s_szOptExplain");

            const int width = 760;
            int descriptionHeight = MeasureBookTextHeight(explain, 500, 36, 110);
            int height = Math.Max(122, 72 + descriptionHeight);
            var card = DeckCard(width, height);

            var icon = new PictureBox
            {
                Location = new Point(14, 17),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            icon.Image = ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Skill");
            card.Controls.Add(icon);

            card.Controls.Add(L(name, 92, 11, 510, 26, true));
            card.Controls.Add(L($"Option ID {optionId} • Icon ID {iconId}", 92, 39, 510, 20, false, Color.FromArgb(120, 220, 145)));
            card.Controls.Add(BookDescription(explain, 92, 64, 500, descriptionHeight));

            Button edit = CreateEditorActionButton("EDIT");
            edit.Size = new Size(104, 32);
            edit.Location = new Point(width - edit.Width - 18, 38);
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            edit.Click += (_, _) => EditSimpleRecord(state, row, new[] { "s_dwOptID", "s_szOptName", "s_nIcon", "s_szOptExplain" });
            card.Controls.Add(edit);
            return card;
        }

        private void BuildDeckCompositionCards(DigimonBookTabState state, XDocument doc, FlowLayoutPanel list)
        {
            string optionPath = Path.Combine(Path.GetDirectoryName(state.XmlPath)!, "DeckOption.xml");
            Dictionary<uint, XElement> optionByGroup = File.Exists(optionPath)
                ? (XDocument.Load(optionPath).Root?.Elements("DeckOption") ?? Enumerable.Empty<XElement>())
                    .GroupBy(x => U(x, "s_nGroupIdx"))
                    .ToDictionary(g => g.Key, g => g.First())
                : new Dictionary<uint, XElement>();

            foreach (XElement group in doc.Root?.Elements("DeckComposition") ?? Enumerable.Empty<XElement>())
            {
                uint groupId = U(group, "s_nGroupIdx");
                optionByGroup.TryGetValue(groupId, out XElement? option);
                string title = option == null ? T(group, "s_szGroupName") : T(option, "s_szGroupName");
                string explain = option == null ? string.Empty : T(option, "s_szExplain");
                List<XElement> members = group.Elements("DeckDigimon").ToList();

                const int width = 760;
                int descriptionHeight = MeasureBookTextHeight(explain, 570, 38, 130);
                int iconsTop = 68 + descriptionHeight + 14;
                int rows = Math.Max(1, (members.Count + 6) / 7);
                int height = iconsTop + rows * 106 + 14;
                var card = DeckCard(width, height);

                card.Controls.Add(L(string.IsNullOrWhiteSpace(title) ? $"Deck {groupId}" : title, 16, 10, 580, 27, true));
                card.Controls.Add(L($"Group {groupId} • {members.Count} Digimon", 16, 38, 580, 20, false, Color.FromArgb(120, 220, 145)));
                card.Controls.Add(BookDescription(explain, 16, 62, 570, descriptionHeight));

                Button edit = CreateEditorActionButton("EDIT DECK");
                edit.Size = new Size(112, 32);
                edit.Location = new Point(width - edit.Width - 18, 20);
                edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                edit.Click += (_, _) => EditDeckComposition(state, group);
                card.Controls.Add(edit);

                for (int i = 0; i < members.Count; i++)
                {
                    XElement member = members[i];
                    int col = i % 7;
                    int row = i / 7;
                    int x = 15 + col * 103;
                    int y = iconsTop + row * 106;
                    uint baseId = U(member, "s_dwBaseDigimonID");
                    uint destId = U(member, "s_dwDestDigimonID");
                    string destName = T(member, "s_szDestDigimonName");
                    string baseName = T(member, "s_szBaseDigimonName");

                    var pb = new PictureBox
                    {
                        Location = new Point(x + 18, y),
                        Size = new Size(58, 58),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Black
                    };
                    pb.Image = TryLoadDeckDigimonIcon(destId, baseId);

                    var nm = L(string.IsNullOrWhiteSpace(destName) ? baseName : destName, x, y + 62, 96, 39, false);
                    nm.TextAlign = ContentAlignment.TopCenter;
                    nm.Font = new Font("Segoe UI", 7.2F);
                    nm.AutoEllipsis = false;

                    card.Controls.Add(pb);
                    card.Controls.Add(nm);
                }

                list.Controls.Add(card);
            }
        }

        private Bitmap? TryLoadDeckDigimonIcon(uint destId, uint baseId)
        {
            Bitmap? image = destId == 0 ? null : ImageDatabasePreview.TryLoadInterfaceIcon(destId, "Skill");
            image ??= baseId == 0 ? null : ImageDatabasePreview.TryLoadInterfaceIcon(baseId, "Skill");
            image ??= DigimonEvoIconResolver.TryLoad(destId, destId);
            image ??= DigimonEvoIconResolver.TryLoad(baseId, baseId);
            return image;
        }

        private Control CreateDeckOptionCard(DigimonBookTabState state, XElement row)
        {
            uint group = U(row, "s_nGroupIdx");
            string name = T(row, "s_szGroupName");
            string explain = T(row, "s_szExplain");
            Dictionary<int, string> optionNames = LoadBookInfoOptionChoices(state.XmlPath)
                .ToDictionary(x => x.Id, x => x.Name);

            const int width = 760;
            int descriptionHeight = MeasureBookTextHeight(explain, 570, 38, 130);
            int rowsTop = 68 + descriptionHeight + 14;
            int height = rowsTop + 3 * 22 + 16;
            var card = DeckCard(width, height);

            card.Controls.Add(L(name, 16, 10, 580, 26, true));
            card.Controls.Add(L($"Group {group}", 16, 38, 580, 20, false, Color.FromArgb(120, 220, 145)));
            card.Controls.Add(BookDescription(explain, 16, 62, 570, descriptionHeight));

            Button edit = CreateEditorActionButton("EDIT OPTIONS");
            edit.Size = new Size(118, 32);
            edit.Location = new Point(width - edit.Width - 18, 20);
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            edit.Click += (_, _) => EditDeckOption(state, row);
            card.Controls.Add(edit);

            int[] cond = A(row, "s_nCondition", "condition");
            int[] at = A(row, "s_nAT_Type", "atType");
            int[] opt = A(row, "s_nOption", "option");
            int[] val = A(row, "s_nVal", "value");
            int[] prob = A(row, "s_nProb", "prob");
            int[] time = A(row, "s_nTime", "time");

            for (int i = 0; i < 3; i++)
            {
                int optionId = At(opt, i);
                string optionText = optionNames.TryGetValue(optionId, out string? optionName)
                    ? $"{optionId} ({optionName})"
                    : optionId.ToString(CultureInfo.InvariantCulture);

                string text =
                    $"Slot {i + 1}   Condition {At(cond, i)}   AT {At(at, i)}   Option {optionText}   " +
                    $"Value {At(val, i)}   Prob {At(prob, i)}   Time {At(time, i)}";

                card.Controls.Add(L(text, 20, rowsTop + i * 22, width - 40, 20, false,
                    i == 0 ? Color.FromArgb(245, 200, 75) : CMuted));
            }

            return card;
        }

        private Control CreateEncyclopediaCard(DigimonBookTabState state, XElement row)
        {
            uint id = U(row, "s_dwDigimonID");
            string name = T(row, "s_szName");
            const int width = 760;
            var card = DeckCard(width, 94);

            var pb = new PictureBox
            {
                Location = new Point(14, 14),
                Size = new Size(58, 58),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            pb.Image = ImageDatabasePreview.TryLoadInterfaceIcon(id, "Skill") ?? DigimonEvoIconResolver.TryLoad(id, id);
            card.Controls.Add(pb);
            card.Controls.Add(L(string.IsNullOrWhiteSpace(name) ? $"Digimon {id}" : name, 88, 17, 500, 26, true));
            card.Controls.Add(L($"Digimon ID {id}", 88, 46, 500, 20, false, Color.FromArgb(120, 220, 145)));

            Button edit = CreateEditorActionButton("EDIT");
            edit.Size = new Size(104, 32);
            edit.Location = new Point(width - edit.Width - 18, 30);
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            edit.Click += (_, _) => EditSimpleRecord(state, row, new[] { "s_dwDigimonID", "s_szName" });
            card.Controls.Add(edit);
            return card;
        }

        private void EditSimpleRecord(DigimonBookTabState state, XElement original, string[] fields)
        {
            XElement working = new XElement(original);
            using var form = CreateDarkDialog("Edit " + state.FileName, 620, Math.Min(620, 110 + fields.Length * 86));
            int y = 18;

            foreach (string field in fields)
            {
                XElement node = working.Element(field) ?? new XElement(field);
                if (node.Parent == null)
                    working.Add(node);

                form.Controls.Add(L(field, 18, y, 550, 19, true));
                bool multi = field.Contains("Explain", StringComparison.OrdinalIgnoreCase);
                var box = new TextBox
                {
                    Text = node.Value,
                    Location = new Point(18, y + 23),
                    Size = new Size(565, multi ? 58 : 28),
                    Multiline = multi,
                    ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None,
                    BackColor = Color.Black,
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle
                };
                box.TextChanged += (_, _) => node.Value = box.Text;
                form.Controls.Add(box);
                y += multi ? 104 : 69;
            }

            Button save = CreateEditorActionButton("SAVE");
            save.Size = new Size(110, 34);
            save.Location = new Point(473, form.ClientSize.Height - 50);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            save.Click += (_, _) =>
            {
                ReplaceRecordAndSave(state, original, working);
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(save);
            form.ShowDialog(this);
        }

        private void EditDeckComposition(DigimonBookTabState state, XElement original)
        {
            XElement working = new XElement(original);
            using var form = CreateDarkDialog("Edit Deck Composition", 860, 560);
            form.Controls.Add(L($"Group {U(working, "s_nGroupIdx")}", 16, 12, 500, 26, true));

            var grid = new DataGridView
            {
                Location = new Point(16, 50),
                Size = new Size(810, 420),
                BackgroundColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.Black,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            foreach (string h in new[] { "BaseDigimonID", "BaseName", "EvolSlot", "DestDigimonID", "DestName" })
                grid.Columns.Add(h, h);

            foreach (XElement m in working.Elements("DeckDigimon"))
                grid.Rows.Add(T(m, "s_dwBaseDigimonID"), T(m, "s_szBaseDigimonName"), T(m, "s_nEvolslot"), T(m, "s_dwDestDigimonID"), T(m, "s_szDestDigimonName"));

            form.Controls.Add(grid);
            Button save = CreateEditorActionButton("SAVE");
            save.Location = new Point(716, 488);
            save.Size = new Size(110, 34);
            save.Click += (_, _) =>
            {
                working.Elements("DeckDigimon").Remove();
                int count = 0;
                foreach (DataGridViewRow r in grid.Rows)
                {
                    if (r.IsNewRow)
                        continue;

                    string baseId = Convert.ToString(r.Cells[0].Value, CultureInfo.InvariantCulture) ?? "0";
                    if (!uint.TryParse(baseId, out uint parsed) || parsed == 0)
                        continue;

                    working.Add(new XElement("DeckDigimon",
                        new XElement("s_dwBaseDigimonID", parsed),
                        new XElement("s_szBaseDigimonName", Convert.ToString(r.Cells[1].Value) ?? ""),
                        new XElement("s_nEvolslot", Convert.ToString(r.Cells[2].Value) ?? "0"),
                        new XElement("s_dwDestDigimonID", Convert.ToString(r.Cells[3].Value) ?? "0"),
                        new XElement("s_szDestDigimonName", Convert.ToString(r.Cells[4].Value) ?? "")));
                    count++;
                }

                BookSet(working, "s_nVal", count.ToString(CultureInfo.InvariantCulture));
                ReplaceRecordAndSave(state, original, working);
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(save);
            form.ShowDialog(this);
        }

        private void EditDeckOption(DigimonBookTabState state, XElement original)
        {
            XElement working = new XElement(original);
            using var form = CreateDarkDialog("Edit Deck Options", 900, 650);

            form.Controls.Add(L("Group ID", 16, 12, 120, 20, true));
            var group = new TextBox
            {
                Text = T(working, "s_nGroupIdx"),
                Location = new Point(16, 35),
                Size = new Size(140, 28),
                BackColor = Color.Black,
                ForeColor = CText
            };
            form.Controls.Add(group);

            form.Controls.Add(L("Title", 170, 12, 500, 20, true));
            var name = new TextBox
            {
                Text = T(working, "s_szGroupName"),
                Location = new Point(170, 35),
                Size = new Size(690, 28),
                BackColor = Color.Black,
                ForeColor = CText
            };
            form.Controls.Add(name);

            form.Controls.Add(L("Description", 16, 72, 300, 20, true));
            var explain = new TextBox
            {
                Text = T(working, "s_szExplain"),
                Location = new Point(16, 95),
                Size = new Size(844, 100),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.Black,
                ForeColor = CText
            };
            form.Controls.Add(explain);

            var hint = L(
                "Option uses BookInfo.xml Option IDs. Select the named option from the dropdown for each slot.",
                16, 202, 844, 24, false, Color.FromArgb(120, 220, 145));
            form.Controls.Add(hint);

            var grid = new DataGridView
            {
                Location = new Point(16, 232),
                Size = new Size(844, 300),
                BackgroundColor = Color.FromArgb(22, 22, 22),
                ForeColor = Color.Black,
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Slot", ReadOnly = true, FillWeight = 45 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Condition", HeaderText = "Condition", FillWeight = 75 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AT Type", HeaderText = "AT Type", FillWeight = 70 });

            List<BookInfoOptionChoice> choices = LoadBookInfoOptionChoices(state.XmlPath);
            var optionColumn = new DataGridViewComboBoxColumn
            {
                Name = "Option",
                HeaderText = "Option (BookInfo.xml)",
                DisplayMember = nameof(BookInfoOptionChoice.Display),
                ValueMember = nameof(BookInfoOptionChoice.Id),
                DataSource = choices,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
                FillWeight = 190
            };
            grid.Columns.Add(optionColumn);
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Prob", HeaderText = "Prob", FillWeight = 65 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "Time", FillWeight = 65 });

            int[] cond = A(working, "s_nCondition", "condition");
            int[] at = A(working, "s_nAT_Type", "atType");
            int[] opt = A(working, "s_nOption", "option");
            int[] val = A(working, "s_nVal", "value");
            int[] prob = A(working, "s_nProb", "prob");
            int[] time = A(working, "s_nTime", "time");

            for (int i = 0; i < 3; i++)
            {
                int optionValue = At(opt, i);
                if (!choices.Any(x => x.Id == optionValue))
                {
                    choices.Add(new BookInfoOptionChoice
                    {
                        Id = optionValue,
                        Name = "Unknown / preserved XML value"
                    });
                    optionColumn.DataSource = null;
                    optionColumn.DataSource = choices.OrderBy(x => x.Id).ToList();
                    optionColumn.DisplayMember = nameof(BookInfoOptionChoice.Display);
                    optionColumn.ValueMember = nameof(BookInfoOptionChoice.Id);
                }

                grid.Rows.Add(i + 1, At(cond, i), At(at, i), optionValue, At(val, i), At(prob, i), At(time, i));
            }

            form.Controls.Add(grid);

            Button save = CreateEditorActionButton("SAVE");
            save.Location = new Point(750, 558);
            save.Size = new Size(110, 34);
            save.Click += (_, _) =>
            {
                BookSet(working, "s_nGroupIdx", group.Text);
                BookSet(working, "s_szGroupName", name.Text);
                BookSet(working, "s_szExplain", explain.Text);
                SetArray(working, "s_nCondition", "condition", grid, 1);
                SetArray(working, "s_nAT_Type", "atType", grid, 2);
                SetArray(working, "s_nOption", "option", grid, 3);
                SetArray(working, "s_nVal", "value", grid, 4);
                SetArray(working, "s_nProb", "prob", grid, 5);
                SetArray(working, "s_nTime", "time", grid, 6);
                ReplaceRecordAndSave(state, original, working);
                form.DialogResult = DialogResult.OK;
                form.Close();
            };
            form.Controls.Add(save);
            form.ShowDialog(this);
        }

        private List<BookInfoOptionChoice> LoadBookInfoOptionChoices(string sourceXmlPath)
        {
            string folder = Path.GetDirectoryName(sourceXmlPath) ?? Path.Combine(AppPaths.Xml, "Digimon_Book");
            string bookInfoPath = Path.Combine(folder, "BookInfo.xml");
            var result = new List<BookInfoOptionChoice>();

            if (File.Exists(bookInfoPath))
            {
                XDocument bookInfo = XDocument.Load(bookInfoPath, LoadOptions.PreserveWhitespace);
                foreach (XElement row in bookInfo.Root?.Elements("BookInfo") ?? Enumerable.Empty<XElement>())
                {
                    int id = (int)U(row, "s_dwOptID");
                    string optionName = T(row, "s_szOptName");
                    if (result.All(x => x.Id != id))
                    {
                        result.Add(new BookInfoOptionChoice
                        {
                            Id = id,
                            Name = string.IsNullOrWhiteSpace(optionName) ? "Option " + id : optionName
                        });
                    }
                }
            }

            if (result.All(x => x.Id != 0))
                result.Insert(0, new BookInfoOptionChoice { Id = 0, Name = "None" });

            return result.OrderBy(x => x.Id).ToList();
        }

        private Form CreateDarkDialog(string text, int width, int height)
        {
            return new Form
            {
                Text = text,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(width, height),
                BackColor = CEditor,
                ForeColor = CText,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
        }

        private void ReplaceRecordAndSave(DigimonBookTabState state, XElement original, XElement working)
        {
            XDocument doc = XDocument.Load(state.XmlPath, LoadOptions.PreserveWhitespace);
            string keyName = state.FileName switch
            {
                "BookInfo.xml" => "s_dwOptID",
                "DeckComposition.xml" => "s_nGroupIdx",
                "DeckOption.xml" => "s_nGroupIdx",
                _ => "s_dwDigimonID"
            };
            string key = T(original, keyName);
            XElement? target = doc.Root?.Elements(original.Name).FirstOrDefault(x => T(x, keyName) == key);
            if (target == null)
                throw new InvalidDataException("Could not locate the original record in " + state.FileName + ".");

            target.ReplaceWith(working);
            string backup = state.XmlPath + ".editor.bak";
            File.Copy(state.XmlPath, backup, true);
            doc.Save(state.XmlPath);
            BuildDigimonBookCards(state);

            TabPage? page = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => ReferenceEquals(x.Tag, state));
            if (page != null)
                PrepareDigimonBookPage(page);
        }

        private static void SetArray(XElement row, string parent, string child, DataGridView grid, int column)
        {
            XElement p = row.Element(parent) ?? new XElement(parent);
            if (p.Parent == null)
                row.Add(p);
            p.RemoveNodes();

            for (int i = 0; i < 3; i++)
            {
                object? raw = grid.Rows[i].Cells[column].Value;
                string value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "0";
                p.Add(new XElement(child, value));
            }
        }

        private static Panel DeckCard(int width, int height)
        {
            var p = new Panel
            {
                Width = width,
                Height = height,
                MinimumSize = new Size(680, height),
                BackColor = Color.FromArgb(30, 30, 34),
                Margin = new Padding(0, 0, 0, 10)
            };
            p.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(62, 62, 68));
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private Label BookDescription(string text, int x, int y, int width, int height)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8F),
                AutoEllipsis = false
            };
        }

        private int MeasureBookTextHeight(string text, int width, int minimum, int maximum)
        {
            if (string.IsNullOrWhiteSpace(text))
                return minimum;

            Size measured = TextRenderer.MeasureText(
                text,
                new Font("Segoe UI", 8F),
                new Size(Math.Max(80, width), maximum),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

            return Math.Max(minimum, Math.Min(maximum, measured.Height + 6));
        }

        private Label L(string text, int x, int y, int w, int h, bool bold, Color? color = null)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = color ?? (bold ? CText : CMuted),
                Font = new Font("Segoe UI" + (bold ? " Semibold" : ""), bold ? 9.2F : 8F, bold ? FontStyle.Bold : FontStyle.Regular),
                AutoEllipsis = true
            };
        }

        private static uint U(XElement e, string n) =>
            uint.TryParse(e.Element(n)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0;

        private static string T(XElement e, string n) => e.Element(n)?.Value ?? string.Empty;

        private static int[] A(XElement e, string p, string c) =>
            (e.Element(p)?.Elements(c) ?? Enumerable.Empty<XElement>())
            .Select(x => int.TryParse(x.Value, out int v) ? v : 0)
            .ToArray();

        private static int At(int[] a, int i) => i < a.Length ? a[i] : 0;

        private static void BookSet(XElement e, string n, string v)
        {
            XElement? x = e.Element(n);
            if (x == null)
                e.Add(new XElement(n, v));
            else
                x.Value = v;
        }
    }
}
