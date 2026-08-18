using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private sealed class MonsterBrowseState
        {
            public required MonsterEditorService Service { get; set; }
            public required MonsterReferenceCatalog Catalog { get; set; }
            public required TextBox Search { get; init; }
            public required Label CountLabel { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
            public required string XmlPath { get; init; }
            public required Button PreviousButton { get; init; }
            public required Button NextButton { get; init; }
            public required Label PageLabel { get; init; }
            public int PageIndex { get; set; }
            public const int PageSize = 24;
        }

        private sealed class MonsterEditState
        {
            public required MonsterEditorService Service { get; init; }
            public required TabPage Page { get; init; }
            public required XElement Working { get; init; }
            public required Dictionary<XElement, Control> Editors { get; init; }
            public required PictureBox Preview { get; init; }
            public required Label TitleLabel { get; init; }
            public required RichTextBox XmlPreview { get; init; }
            public bool Dirty { get; set; }
            public Action? RefreshBrowser { get; init; }
        }

        private sealed class MonsterSkillBrowseState
        {
            public required MonsterSkillEditorService Service { get; set; }
            public required MonsterReferenceCatalog Monsters { get; set; }
            public required BuffMiniCatalog? Buffs { get; init; }
            public required TextBox Search { get; init; }
            public required ComboBox UseTermFilter { get; init; }
            public required Label CountLabel { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
            public required string XmlPath { get; init; }
            public required Button PreviousButton { get; init; }
            public required Button NextButton { get; init; }
            public required Label PageLabel { get; init; }
            public int PageIndex { get; set; }
            public const int PageSize = 24;
        }

        private sealed class MonsterSkillEditState
        {
            public required MonsterSkillEditorService Service { get; init; }
            public required MonsterReferenceCatalog Monsters { get; init; }
            public required BuffMiniCatalog? Buffs { get; init; }
            public required MonsterSkillTermsEditorService? Terms { get; init; }
            public required XElement Working { get; init; }
            public required Dictionary<string, Control> Editors { get; init; }
            public required PictureBox Preview { get; init; }
            public required Label MonsterLabel { get; init; }
            public required Label UseTermLabel { get; init; }
            public required Label MechanicsHintLabel { get; init; }
            public required Label RangeInfoLabel { get; init; }
            public required Label Factor1Label { get; init; }
            public required Label Factor2Label { get; init; }
            public required Label Factor3Label { get; init; }
            public required RichTextBox XmlPreview { get; init; }
            public bool Dirty { get; set; }
            public Action? RefreshBrowser { get; init; }
        }

        private sealed class MonsterSkillTermsBrowseState
        {
            public required MonsterSkillTermsEditorService Service { get; set; }
            public required TextBox Search { get; init; }
            public required Label CountLabel { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
            public required string XmlPath { get; init; }
        }

        private async void OpenMonsterBrowser(string xmlPath)
        {
            string fullPath = Path.GetFullPath(xmlPath);
            var page = CreateDarkTab("Monster.xml");
            page.Name = fullPath;

            var loading = new EditorLoadingView(
                "Loading Monster.xml",
                "Preparing monster database, visual index and the first page.");
            loading.Dock = DockStyle.Fill;
            page.Controls.Add(loading);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            UpdateEditorEmptyState();
            UpdateEditorTabChrome();

            MonsterEditorService service;
            try
            {
                service = await EditorPreloadService.GetMonsterEditorAsync(fullPath);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                    loading.SetError("Monster.xml could not be loaded", ex.Message);
                return;
            }

            if (page.IsDisposed)
                return;

            // Build the first usable frame while the loading view still hides
            // the editor.  This prevents WinForms from painting thousands of
            // half-created controls.
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Visible = false
            };

            var header = CreateBrowserHeader(
                "Monster.xml",
                "Visual Monster Editor",
                out TextBox search,
                out Label countLabel);

            search.PlaceholderText =
                "Pesquisar MonsterID, ModelDigimon, Name ou Comment...";

            var newButton = CreateEditorActionButton("NEW MONSTER");
            newButton.Size = new Size(132, 34);
            header.Controls.Add(newButton);
            PositionHeaderActions(header, newButton);

            var previous = CreateEditorActionButton("◀ PREVIOUS");
            previous.Size = new Size(112, 30);

            var pageLabel = new Label
            {
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 8.5F),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(90, 30)
            };

            var next = CreateEditorActionButton("NEXT ▶");
            next.Size = new Size(112, 30);

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(18, 6, 0, 4)
            };
            nav.Controls.Add(previous);
            nav.Controls.Add(pageLabel);
            nav.Controls.Add(next);

            var resultsHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(18, 12, 18, 12)
            };

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = CEditor,
                Padding = new Padding(0, 0, 34, 22)
            };
            DarkUi.ApplyDarkScrollBar(results);
            resultsHost.Controls.Add(results);

            content.Controls.Add(resultsHost);
            content.Controls.Add(nav);
            content.Controls.Add(header);
            page.Controls.Add(content);

            var timer = new System.Windows.Forms.Timer { Interval = 220 };
            var state = new MonsterBrowseState
            {
                Service = service,
                Catalog = new MonsterReferenceCatalog(service),
                Search = search,
                CountLabel = countLabel,
                Results = results,
                SearchTimer = timer,
                XmlPath = fullPath,
                PreviousButton = previous,
                NextButton = next,
                PageLabel = pageLabel,
                PageIndex = 0
            };
            page.Tag = state;

            int lastMonsterResultsWidth = results.ClientSize.Width;
            results.Resize +=
                (_, _) =>
                {
                    if (Math.Abs(
                            results.ClientSize.Width -
                            lastMonsterResultsWidth) < 8)
                    {
                        return;
                    }

                    lastMonsterResultsWidth =
                        results.ClientSize.Width;

                    RenderMonsterResults(
                        state);
                };

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                state.PageIndex = 0;
                RenderMonsterResults(state);
            };

            search.TextChanged += (_, _) =>
            {
                timer.Stop();
                timer.Start();
            };

            previous.Click += (_, _) =>
            {
                if (state.PageIndex <= 0)
                    return;

                state.PageIndex--;
                RenderMonsterResults(state);
                ResetEditorVerticalScroll(state.Results);
            };

            next.Click += (_, _) =>
            {
                int total = state.Service.Search(state.Search.Text).Count;
                int pages = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        total /
                        (double)MonsterBrowseState.PageSize));

                if (state.PageIndex >= pages - 1)
                    return;

                state.PageIndex++;
                RenderMonsterResults(state);
                ResetEditorVerticalScroll(state.Results);
            };

            newButton.Click += async (_, _) =>
            {
                XElement created = state.Service.CreateNewMonster();
                uint createdId = UIntValue(created, "MonsterID");
                state.Service.Save();
                await RefreshMonsterBrowserAsync(state);

                XElement? reloaded =
                    state.Service.Root.Elements("Monster")
                        .FirstOrDefault(
                            x => UIntValue(x, "MonsterID") == createdId);

                if (reloaded != null)
                    OpenMonsterEditor(state, reloaded);
            };

            try
            {
                RenderMonsterResults(state);
                await Task.Yield();

                if (page.IsDisposed)
                    return;

                page.Controls.Remove(loading);
                loading.Dispose();
                content.Visible = true;
                content.BringToFront();
            }
            catch (Exception ex)
            {
                content.Visible = false;
                loading.BringToFront();
                loading.SetError(
                    "Monster editor could not render",
                    ex.Message);
            }
        }

        private async Task RefreshMonsterBrowserAsync(
            MonsterBrowseState state)
        {
            try
            {
                state.Service =
                    await EditorPreloadService.GetMonsterEditorAsync(
                        state.XmlPath);

                state.Catalog =
                    new MonsterReferenceCatalog(
                        state.Service);

                RenderMonsterResults(state);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Monster Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private async void RefreshMonsterBrowser(
            MonsterBrowseState state)
        {
            await RefreshMonsterBrowserAsync(state);
        }

        private void RenderMonsterResults(MonsterBrowseState state)
        {
            IReadOnlyList<MonsterRecord> filtered =
                state.Service.Search(
                    state.Search.Text);

            int pages = Math.Max(
                1,
                (int)Math.Ceiling(
                    filtered.Count /
                    (double)MonsterBrowseState.PageSize));

            state.PageIndex =
                Math.Clamp(
                    state.PageIndex,
                    0,
                    pages - 1);

            state.CountLabel.Text =
                $"{filtered.Count} monsters";

            state.PageLabel.Text =
                $"{state.PageIndex + 1} / {pages}";

            state.PreviousButton.Enabled =
                state.PageIndex > 0;

            state.NextButton.Enabled =
                state.PageIndex < pages - 1;

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (MonsterRecord record in
                     filtered
                         .Skip(
                             state.PageIndex *
                             MonsterBrowseState.PageSize)
                         .Take(
                             MonsterBrowseState.PageSize))
            {
                state.Results.Controls.Add(
                    CreateMonsterCard(
                        state,
                        record));
            }

            if (filtered.Count == 0)
            {
                state.Results.Controls.Add(
                    CreateInfoLabel(
                        "Nenhum monster corresponde ao filtro atual."));
            }

            state.Results.ResumeLayout(
                true);
        }

        private static void ResetEditorVerticalScroll(
            ScrollableControl control)
        {
            try
            {
                control.AutoScrollPosition =
                    new Point(
                        0,
                        0);

                control.VerticalScroll.Value =
                    control.VerticalScroll.Minimum;

                control.PerformLayout();
                control.Invalidate();
            }
            catch
            {
                // Scroll reset is a presentation convenience only.
            }
        }

        private Control CreateMonsterCard(MonsterBrowseState state, MonsterRecord record)
        {
            int availableWidth =
                Math.Max(
                    420,
                    state.Results.ClientSize.Width -
                    state.Results.Padding.Horizontal -
                    SystemInformation.VerticalScrollBarWidth -
                    22);

            var card = new Panel
            {
                Width = availableWidth,
                Height = 108,
                BackColor = Color.FromArgb(27, 27, 27),
                Margin = new Padding(0, 0, 0, 10)
            };
            card.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            var iconBox = new PictureBox
            {
                Location = new Point(16, 16),
                Size = new Size(72, 72),
                BackColor = Color.FromArgb(18, 18, 18),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = MonsterAssetResolver.TryGetPreloadedMonsterDigimonIcon(record.ModelDigimon)
            };

            var name = new Label
            {
                Text = record.DisplayName,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(104, 14),
                Size = new Size(300, 25),
                AutoEllipsis = true
            };

            string stats = $"ID {record.MonsterId}  •  ModelDigimon {record.ModelDigimon}  •  Lv {record.Level}  •  HP {record.HP:n0}  •  AT {record.AT:n0}  •  DE {record.DE:n0}  •  HT {record.HT:n0}";
            var meta = new Label
            {
                Text = stats,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                Location = new Point(104, 42),
                Size = new Size(300, 20),
                AutoEllipsis = true
            };

            string details = $"Move {record.MS}/{record.WS}  •  Speed AS {record.AS} / AR {record.AR} / CT {record.CT} / EV {record.EV}  •  {TrimSummary(record.Comment, 90)}";
            var detail = new Label
            {
                Text = details,
                ForeColor = Color.FromArgb(125, 210, 145),
                Font = new Font("Segoe UI", 8.2F),
                AutoSize = false,
                Location = new Point(104, 66),
                Size = new Size(300, 18),
                AutoEllipsis = true
            };

            var edit = CreateEditorActionButton("EDIT");
            edit.Size = new Size(104, 34);
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            edit.Click += (_, _) => OpenMonsterEditor(state, record.Node);

            var remove = CreateEditorActionButton("REMOVE");
            remove.Size = new Size(104, 34);
            remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            remove.ForeColor = Color.FromArgb(255, 120, 120);

            void PositionMonsterCard()
            {
                const int rightPadding = 14;
                const int buttonGap = 8;
                const int textGap = 18;

                remove.Location =
                    new Point(
                        Math.Max(
                            104,
                            card.ClientSize.Width -
                            remove.Width -
                            rightPadding),
                        16);

                edit.Location =
                    new Point(
                        Math.Max(
                            104,
                            remove.Left -
                            edit.Width -
                            buttonGap),
                        16);

                int textRight =
                    Math.Max(
                        150,
                        edit.Left -
                        textGap);

                int textWidth =
                    Math.Max(
                        80,
                        textRight -
                        name.Left);

                name.Width = textWidth;
                meta.Width = textWidth;
                detail.Width = textWidth;
            }

            card.Resize +=
                (_, _) =>
                    PositionMonsterCard();

            PositionMonsterCard();
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show(
                        $"Remover monster {record.DisplayName} ({record.MonsterId})?",
                        "Confirmar",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                state.Service.Delete(record.Node);
                state.Service.Save();
                RefreshMonsterBrowser(state);
            };

            card.Controls.Add(iconBox);
            card.Controls.Add(name);
            card.Controls.Add(meta);
            card.Controls.Add(detail);
            card.Controls.Add(edit);
            card.Controls.Add(remove);
            return card;
        }

        private void OpenMonsterEditor(MonsterBrowseState browse, XElement monsterNode)
        {
            uint id = UIntValue(monsterNode, "MonsterID");
            string tabKey = Path.GetFullPath(browse.XmlPath) + "#Monster#" + id;
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Name, tabKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page = CreateDarkTab($"Monster {id}");
            page.Name = tabKey;
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            var editorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            editorLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F));

            editorLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 285F));

            editorLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));

            var left = new Panel { Dock = DockStyle.Fill, BackColor = CEditor };
            var topBar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(25, 25, 25) };
            var save = CreateEditorActionButton("SAVE");
            save.Size = new Size(110, 34);
            save.Location = new Point(16, 12);
            var close = CreateEditorActionButton("CLOSE");
            close.Size = new Size(110, 34);
            close.Location = new Point(136, 12);

            var formHost = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, AutoScroll = true, Padding = new Padding(16, 12, 16, 16) };
            DarkUi.ApplyDarkScrollBar(formHost);
            var form = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = CEditor
            };
            formHost.Controls.Add(form);

            void ResizeMonsterEditorForm()
            {
                int usableWidth =
                    Math.Max(
                        360,
                        formHost.ClientSize.Width -
                        formHost.Padding.Horizontal -
                        SystemInformation.VerticalScrollBarWidth -
                        24);

                form.Width =
                    usableWidth;

                ApplyResponsiveMonsterEditorLayout(
                    form,
                    usableWidth);
            }

            formHost.Resize +=
                (_, _) => ResizeMonsterEditorForm();

            ResizeMonsterEditorForm();

            var title = new Label
            {
                Text = "Monster Editor",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(270, 17)
            };
            topBar.Controls.Add(save);
            topBar.Controls.Add(close);
            topBar.Controls.Add(title);
            left.Controls.Add(formHost);
            left.Controls.Add(topBar);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 19, 19), Padding = new Padding(12) };
            var xmlTitle = new Label
            {
                Text = "LIVE XML PREVIEW",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 26
            };
            var xml = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 14, 14),
                ForeColor = Color.FromArgb(210, 210, 210),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.2F),
                ReadOnly = true
            };
            right.Controls.Add(xml);
            right.Controls.Add(xmlTitle);

            editorLayout.Controls.Add(left, 0, 0);
            editorLayout.Controls.Add(right, 1, 0);
            page.Controls.Add(editorLayout);

            void UpdateMonsterEditorColumns()
            {
                int available = Math.Max(1, editorLayout.ClientSize.Width);

                editorLayout.ColumnStyles[1].Width =
                    available < 760
                        ? Math.Max(220F, available * 0.29F)
                        : 285F;
            }

            editorLayout.Resize +=
                (_, _) => UpdateMonsterEditorColumns();

            UpdateMonsterEditorColumns();

            var previewCard = new Panel
            {
                Width = 520,
                Height = 112,
                BackColor = Color.FromArgb(26, 26, 26),
                Margin = new Padding(0, 0, 0, 10)
            };
            previewCard.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawRectangle(p, 0, 0, previewCard.Width - 1, previewCard.Height - 1);
            };
            var preview = new PictureBox
            {
                Location = new Point(14, 14),
                Size = new Size(84, 84),
                BackColor = Color.FromArgb(16, 16, 16),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            var previewTitle = new Label
            {
                Text = string.Empty,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(112, 18),
                Size = new Size(540, 24),
                AutoEllipsis = true
            };
            var previewSubtitle = new Label
            {
                Text = "Monster visual + battle overview",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                Location = new Point(114, 47),
                Size = new Size(540, 20),
                AutoEllipsis = true
            };
            previewCard.Controls.Add(preview);
            previewCard.Controls.Add(previewTitle);
            previewCard.Controls.Add(previewSubtitle);
            form.Controls.Add(previewCard);

            var editors = new Dictionary<XElement, Control>();
            var state = new MonsterEditState
            {
                Service = browse.Service,
                Page = page,
                Working = monsterNode,
                Editors = editors,
                Preview = preview,
                TitleLabel = previewTitle,
                XmlPreview = xml,
                RefreshBrowser = () => RefreshMonsterBrowser(browse)
            };

            AddMonsterIdentitySection(form, state);
            AddMonsterStatsSection(form, state);
            AddMonsterMovementSection(form, state);
            AddMonsterExtraSection(form, state);

            ApplyResponsiveMonsterEditorLayout(
                form,
                form.Width);

            void RefreshPreview()
            {
                uint model = UIntValue(monsterNode, "ModelDigimon");
                uint monsterId = UIntValue(monsterNode, "MonsterID");
                string nameText = monsterNode.Element("Name")?.Value ?? string.Empty;
                string comment = monsterNode.Element("Comment")?.Value ?? string.Empty;
                string level = monsterNode.Element("Level")?.Value ?? "0";
                preview.Image = MonsterAssetResolver.TryLoadMonsterDigimonIcon(model);
                previewTitle.Text = string.IsNullOrWhiteSpace(nameText) ? $"Monster {monsterId}" : nameText;
                previewSubtitle.Text = $"ID {monsterId}  •  ModelDigimon {model}  •  Level {level}  •  {TrimSummary(comment, 70)}";
                xml.Text = monsterNode.ToString();
            }

            foreach (Control control in state.Editors.Values)
            {
                if (control is TextBox tb)
                    tb.TextChanged += (_, _) => { state.Dirty = true; RefreshPreview(); };
            }
            RefreshPreview();

            save.Click += (_, _) =>
            {
                if (!ValidateMonsterIdentityBeforeSave(
                        state,
                        out string identityError))
                {
                    MessageBox.Show(
                        identityError,
                        "Monster Editor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    return;
                }

                state.Service.Save();
                state.Dirty = false;
                state.RefreshBrowser?.Invoke();
                RefreshPreview();

                MessageBox.Show(
                    "Monster.xml guardado com sucesso.",
                    "Monster Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };

            close.Click += (_, _) =>
            {
                editorTabs.TabPages.Remove(page);
                page.Dispose();
            };
        }

        private async void OpenMonsterSkillBrowser(string xmlPath)
        {
            string fullPath = Path.GetFullPath(xmlPath);
            var page = CreateDarkTab("MonstersSkill.xml");
            page.Name = fullPath;

            var loading = new EditorLoadingView(
                "Loading MonstersSkill.xml",
                "Preparing monster skill mechanics, monster references and buff/debuff catalogs.");
            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            UpdateEditorEmptyState();
            UpdateEditorTabChrome();

            MonsterSkillEditorService service;
            MonsterReferenceCatalog monsterCatalog;
            BuffMiniCatalog? buffCatalog;
            MonsterSkillTermsEditorService? termsService;
            try
            {
                service = await EditorPreloadService.GetMonsterSkillEditorAsync(fullPath);
                monsterCatalog = LoadMonsterCatalogSafe();
                buffCatalog = BuffMiniCatalog.TryLoadDefault();
                termsService = TryLoadMonsterSkillTermsNear(fullPath);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                    loading.SetError("MonstersSkill.xml could not be loaded", ex.Message);
                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();
            page.Controls.Clear();

            var header = CreateBrowserHeader("MonstersSkill.xml", "Visual Monster Skill Editor", out TextBox search, out Label countLabel);
            search.PlaceholderText = "Pesquisar Skill_IDX, MonsterID, UseTerms ou descrição...";

            var filterLabel = new Label
            {
                Text = "UseTerms",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = true,
                Location = new Point(510, 75)
            };
            var filter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = CText,
                Font = new Font("Segoe UI", 9F),
                Size = new Size(190, 26),
                Location = new Point(570, 72)
            };
            filter.Items.Add(new ComboOption(-1, "All UseTerms"));
            foreach (UseTermInfo info in MonsterUseTermCatalog.All)
                filter.Items.Add(new ComboOption(info.Value, $"{info.Value} - {info.Name}"));
            filter.SelectedIndex = 0;

            var newButton = CreateEditorActionButton("NEW SKILL");
            newButton.Size = new Size(120, 34);
            newButton.Location = new Point(780, 68);

            header.Controls.Add(filterLabel);
            header.Controls.Add(filter);
            header.Controls.Add(newButton);
            PositionHeaderActions(header, newButton);

            var previous = CreateEditorActionButton("◀ PREVIOUS");
            previous.Size = new Size(112, 30);

            var pageLabel = new Label
            {
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 8.5F),
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false,
                Size = new Size(90, 30)
            };

            var next = CreateEditorActionButton("NEXT ▶");
            next.Size = new Size(112, 30);

            var nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(18, 6, 0, 4)
            };
            nav.Controls.Add(previous);
            nav.Controls.Add(pageLabel);
            nav.Controls.Add(next);

            var resultsHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(18, 12, 12, 12)
            };
            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = CEditor,
                Padding = new Padding(0, 0, 34, 22)
            };
            DarkUi.ApplyDarkScrollBar(results);
            resultsHost.Controls.Add(results);

            var timer = new System.Windows.Forms.Timer { Interval = 220 };
            var state = new MonsterSkillBrowseState
            {
                Service = service,
                Monsters = monsterCatalog,
                Buffs = buffCatalog,
                Search = search,
                UseTermFilter = filter,
                CountLabel = countLabel,
                Results = results,
                SearchTimer = timer,
                XmlPath = fullPath,
                PreviousButton = previous,
                NextButton = next,
                PageLabel = pageLabel,
                PageIndex = 0
            };
            page.Tag = state;

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                RenderMonsterSkillResults(state);
            };
            search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };
            filter.SelectedIndexChanged += (_, _) =>
            {
                state.PageIndex = 0;
                RenderMonsterSkillResults(state);
            };

            previous.Click += (_, _) =>
            {
                if (state.PageIndex <= 0)
                    return;

                state.PageIndex--;
                RenderMonsterSkillResults(state);
                ResetEditorVerticalScroll(state.Results);
            };

            next.Click += (_, _) =>
            {
                int? selectedUseTerm = null;
                if (state.UseTermFilter.SelectedItem is ComboOption selected && selected.Value >= 0)
                    selectedUseTerm = selected.Value;

                int total = state.Service.Search(state.Search.Text, selectedUseTerm).Count;
                int pages = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        total /
                        (double)MonsterSkillBrowseState.PageSize));

                if (state.PageIndex >= pages - 1)
                    return;

                state.PageIndex++;
                RenderMonsterSkillResults(state);
                ResetEditorVerticalScroll(state.Results);
            };

            newButton.Click += (_, _) =>
            {
                XElement created = state.Service.CreateNewSkill();
                uint createdId = UIntValue(created, "Skill_IDX");
                state.Service.Save();
                RefreshMonsterSkillBrowser(state);
                XElement? reloaded = state.Service.Root.Elements("MonsterSkill").FirstOrDefault(x => UIntValue(x, "Skill_IDX") == createdId);
                if (reloaded != null)
                    OpenMonsterSkillEditor(state, reloaded, termsService);
            };

            page.Controls.Add(resultsHost);
            page.Controls.Add(nav);
            page.Controls.Add(header);
            RenderMonsterSkillResults(state);
            page.ResumeLayout();
        }

        private async void RefreshMonsterSkillBrowser(
            MonsterSkillBrowseState state)
        {
            try
            {
                state.Service =
                    await EditorPreloadService.GetMonsterSkillEditorAsync(
                        state.XmlPath);

                state.Monsters =
                    LoadMonsterCatalogSafe();

                RenderMonsterSkillResults(
                    state);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Monster Skill Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RenderMonsterSkillResults(
            MonsterSkillBrowseState state)
        {
            int? filterValue = null;
            if (state.UseTermFilter.SelectedItem is ComboOption opt &&
                opt.Value >= 0)
            {
                filterValue = opt.Value;
            }

            IReadOnlyList<MonsterSkillRecord> filtered =
                state.Service.Search(
                    state.Search.Text,
                    filterValue);

            int pages = Math.Max(
                1,
                (int)Math.Ceiling(
                    filtered.Count /
                    (double)MonsterSkillBrowseState.PageSize));

            state.PageIndex =
                Math.Clamp(
                    state.PageIndex,
                    0,
                    pages - 1);

            state.CountLabel.Text =
                $"{filtered.Count} monster skills";

            state.PageLabel.Text =
                $"{state.PageIndex + 1} / {pages}";

            state.PreviousButton.Enabled =
                state.PageIndex > 0;

            state.NextButton.Enabled =
                state.PageIndex < pages - 1;

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (MonsterSkillRecord record in
                     filtered
                         .Skip(
                             state.PageIndex *
                             MonsterSkillBrowseState.PageSize)
                         .Take(
                             MonsterSkillBrowseState.PageSize))
            {
                state.Results.Controls.Add(
                    CreateMonsterSkillCard(
                        state,
                        record));
            }

            if (filtered.Count == 0)
            {
                state.Results.Controls.Add(
                    CreateInfoLabel(
                        "Nenhuma monster skill corresponde ao filtro atual."));
            }

            state.Results.ResumeLayout(
                true);
        }

        private Control CreateMonsterSkillCard(MonsterSkillBrowseState state, MonsterSkillRecord record)
        {
            MonsterRecord? monster = state.Monsters.Find(record.MonsterId);
            UseTermInfo useTerm = MonsterUseTermCatalog.Get(record.UseTerms);

            int availableWidth =
                Math.Max(
                    440,
                    state.Results.ClientSize.Width -
                    state.Results.Padding.Horizontal -
                    SystemInformation.VerticalScrollBarWidth -
                    22);

            var card = new Panel
            {
                Width = availableWidth,
                Height = 116,
                BackColor = Color.FromArgb(27, 27, 27),
                Margin = new Padding(0, 0, 0, 10)
            };
            card.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            var icon = new PictureBox
            {
                Location = new Point(16, 16),
                Size = new Size(72, 72),
                BackColor = Color.FromArgb(16, 16, 16),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = MonsterAssetResolver.TryGetPreloadedMonsterDigimonIcon(monster?.ModelDigimon ?? 0)
            };

            var name = new Label
            {
                Text = monster?.DisplayName ?? $"Monster {record.MonsterId}",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(104, 14),
                Size = new Size(480, 24),
                AutoEllipsis = true
            };
            var meta = new Label
            {
                Text = $"Skill_IDX {record.SkillIndex}  •  MonsterID {record.MonsterId}  •  UseTerms {record.UseTerms} ({useTerm.Name})  •  SkillType {record.SkillType}",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = false,
                Location = new Point(104, 42),
                Size = new Size(620, 19),
                AutoEllipsis = true
            };
            var detail = new Label
            {
                Text = $"{useTerm.Description}  •  Cool {record.CoolTime} ms  •  Cast {record.CastTime} ms  •  Factors [{record.EffectFactor1}, {record.EffectFactor2}, {record.EffectFactor3}]",
                ForeColor = useTerm.Implemented ? Color.FromArgb(115, 225, 145) : Color.FromArgb(244, 190, 102),
                Font = new Font("Segoe UI", 8.2F),
                AutoSize = false,
                Location = new Point(104, 66),
                Size = new Size(620, 32),
                AutoEllipsis = true
            };

            var edit = CreateEditorActionButton("EDIT");
            edit.Size = new Size(110, 34);
            edit.Location = new Point(card.Width - 250, 16);
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            edit.Click += (_, _) => OpenMonsterSkillEditor(state, record.Node, TryLoadMonsterSkillTermsNear(state.XmlPath));

            var remove = CreateEditorActionButton("REMOVE");
            remove.Size = new Size(110, 34);
            remove.Location = new Point(card.Width - 128, 16);
            remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            remove.ForeColor = Color.FromArgb(255, 120, 120);
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show(
                        $"Remover monster skill {record.SkillIndex} de MonsterID {record.MonsterId}?",
                        "Confirmar",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }

                state.Service.Delete(record.Node);
                state.Service.Save();
                RefreshMonsterSkillBrowser(state);
            };

            card.Controls.Add(icon);
            card.Controls.Add(name);
            card.Controls.Add(meta);
            card.Controls.Add(detail);
            card.Controls.Add(edit);
            card.Controls.Add(remove);
            return card;
        }

        private void OpenMonsterSkillEditor(MonsterSkillBrowseState browse, XElement skillNode, MonsterSkillTermsEditorService? terms)
        {
            uint skillIndex = UIntValue(skillNode, "Skill_IDX");
            string tabKey = Path.GetFullPath(browse.XmlPath) + "#MonsterSkill#" + skillIndex;
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => string.Equals(x.Name, tabKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page = CreateDarkTab($"MonsterSkill {skillIndex}");
            page.Name = tabKey;
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            var editorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            editorLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100F));

            editorLayout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 350F));

            editorLayout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100F));

            var left = new Panel { Dock = DockStyle.Fill, BackColor = CEditor };
            var topBar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Color.FromArgb(25, 25, 25) };
            var save = CreateEditorActionButton("SAVE");
            save.Size = new Size(110, 34);
            save.Location = new Point(16, 12);
            var close = CreateEditorActionButton("CLOSE");
            close.Size = new Size(110, 34);
            close.Location = new Point(136, 12);
            var topTitle = new Label
            {
                Text = "Monster Skill Mechanics Editor",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(270, 17)
            };
            topBar.Controls.Add(save);
            topBar.Controls.Add(close);
            topBar.Controls.Add(topTitle);

            var scroll = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, AutoScroll = true, Padding = new Padding(16, 12, 16, 16) };
            DarkUi.ApplyDarkScrollBar(scroll);
            var form = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = CEditor
            };
            scroll.Controls.Add(form);

            void ResizeMonsterSkillEditorForm()
            {
                form.Width =
                    Math.Max(
                        440,
                        scroll.ClientSize.Width -
                        scroll.Padding.Horizontal -
                        SystemInformation.VerticalScrollBarWidth -
                        20);
            }

            scroll.Resize +=
                (_, _) => ResizeMonsterSkillEditorForm();

            ResizeMonsterSkillEditorForm();

            left.Controls.Add(scroll);
            left.Controls.Add(topBar);

            var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(19, 19, 19), Padding = new Padding(12) };
            var xmlTitle = new Label
            {
                Text = "LIVE XML PREVIEW",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 26
            };
            var xml = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 14, 14),
                ForeColor = Color.FromArgb(210, 210, 210),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.2F),
                ReadOnly = true
            };
            right.Controls.Add(xml);
            right.Controls.Add(xmlTitle);
            editorLayout.Controls.Add(left, 0, 0);
            editorLayout.Controls.Add(right, 1, 0);
            page.Controls.Add(editorLayout);

            void UpdateMonsterSkillEditorColumns()
            {
                int available = Math.Max(1, editorLayout.ClientSize.Width);

                editorLayout.ColumnStyles[1].Width =
                    available < 800
                        ? Math.Max(230F, available * 0.33F)
                        : 350F;
            }

            editorLayout.Resize +=
                (_, _) => UpdateMonsterSkillEditorColumns();

            UpdateMonsterSkillEditorColumns();

            var headerCard = new Panel
            {
                Width = 520,
                Height = 138,
                BackColor = Color.FromArgb(26, 26, 26),
                Margin = new Padding(0, 0, 0, 10)
            };
            headerCard.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawRectangle(p, 0, 0, headerCard.Width - 1, headerCard.Height - 1);
            };
            var preview = new PictureBox
            {
                Location = new Point(14, 14),
                Size = new Size(84, 84),
                BackColor = Color.FromArgb(16, 16, 16),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            var monsterLabel = new Label
            {
                Text = string.Empty,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(112, 18),
                Size = new Size(520, 24),
                AutoEllipsis = true
            };
            var useTermLabel = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(120, 220, 140),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = false,
                Location = new Point(114, 46),
                Size = new Size(610, 20),
                AutoEllipsis = true
            };
            var mechanicsHint = new Label
            {
                Text = string.Empty,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.3F),
                AutoSize = false,
                Location = new Point(114, 70),
                Size = new Size(610, 44),
                AutoEllipsis = true
            };
            headerCard.Controls.Add(preview);
            headerCard.Controls.Add(monsterLabel);
            headerCard.Controls.Add(useTermLabel);
            headerCard.Controls.Add(mechanicsHint);
            form.Controls.Add(headerCard);

            var rangeCard = new Panel
            {
                Width = 760,
                Height = 58,
                BackColor = Color.FromArgb(25, 25, 25),
                Margin = new Padding(0, 0, 0, 10)
            };
            rangeCard.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawRectangle(p, 0, 0, rangeCard.Width - 1, rangeCard.Height - 1);
            };
            var rangeInfo = new Label
            {
                Text = string.Empty,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.6F),
                Location = new Point(14, 18),
                Size = new Size(730, 20),
                AutoEllipsis = true
            };
            rangeCard.Controls.Add(rangeInfo);
            form.Controls.Add(rangeCard);

            var editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);
            var state = new MonsterSkillEditState
            {
                Service = browse.Service,
                Monsters = browse.Monsters,
                Buffs = browse.Buffs,
                Terms = terms,
                Working = skillNode,
                Editors = editors,
                Preview = preview,
                MonsterLabel = monsterLabel,
                UseTermLabel = useTermLabel,
                MechanicsHintLabel = mechanicsHint,
                RangeInfoLabel = rangeInfo,
                Factor1Label = new Label(),
                Factor2Label = new Label(),
                Factor3Label = new Label(),
                XmlPreview = xml,
                RefreshBrowser = () => RefreshMonsterSkillBrowser(browse)
            };

            AddMonsterSkillCoreSection(form, state);
            AddMonsterSkillCombatSection(form, state);
            AddMonsterSkillEffectsSection(form, state);
            AddMonsterSkillFactorSection(form, state, "Eff_Factor", "Eff_Fact_Val", "Factor 1", state.Factor1Label);
            AddMonsterSkillFactorSection(form, state, "Eff_Factor2", "Eff_Fact_Val2", "Factor 2", state.Factor2Label);
            AddMonsterSkillFactorSection(form, state, "Eff_Factor3", "Eff_Fact_Val3", "Factor 3", state.Factor3Label);
            AddMonsterSkillExtraSection(form, state);

            void RefreshView()
            {
                uint monsterId = UIntValue(skillNode, "MonsterID");
                MonsterRecord? monster = state.Monsters.Find(monsterId);
                preview.Image = MonsterAssetResolver.TryLoadMonsterDigimonIcon(monster?.ModelDigimon ?? 0);
                monsterLabel.Text = monster == null
                    ? $"MonsterID {monsterId}"
                    : $"{monster.DisplayName}  •  MonsterID {monsterId}  •  ModelDigimon {monster.ModelDigimon}";

                int useTermValue = IntValue(skillNode, "UseTerms");
                UseTermInfo info = MonsterUseTermCatalog.Get(useTermValue);
                useTermLabel.Text = $"UseTerms {useTermValue} - {info.Name}  •  {(info.Implemented ? "Implemented" : "Not implemented in server note")}  •  {info.Description}";
                mechanicsHint.Text = BuildUseTermMechanicsHint(info, skillNode);

                int rangeIdx = IntValue(skillNode, "RangeIDX");
                MonsterSkillTermRecord? term = state.Terms?.Records.FirstOrDefault(x => x.Idx == rangeIdx);
                rangeInfo.Text = term == null
                    ? $"RangeIDX {rangeIdx} — no MonstersSkillTerms.xml reference found."
                    : $"RangeIDX {rangeIdx}  •  Direction {term.Direction}  •  Range {term.Range}  •  TargetingType {term.TargetingType}  •  RefCode {term.RefCode}";

                state.Factor1Label.Text = BuildFactorSummary(state, IntValue(skillNode, "Eff_Factor"), "Eff_Factor");
                state.Factor2Label.Text = BuildFactorSummary(state, IntValue(skillNode, "Eff_Factor2"), "Eff_Factor2");
                state.Factor3Label.Text = BuildFactorSummary(state, IntValue(skillNode, "Eff_Factor3"), "Eff_Factor3");
                xml.Text = skillNode.ToString();
            }

            foreach (Control control in state.Editors.Values)
            {
                switch (control)
                {
                    case TextBox tb:
                        tb.TextChanged += (_, _) => { state.Dirty = true; RefreshView(); };
                        break;
                    case ComboBox cb:
                        cb.SelectedIndexChanged += (_, _) => { state.Dirty = true; RefreshView(); };
                        break;
                }
            }
            RefreshView();

            save.Click += (_, _) =>
            {
                state.Service.Save();
                state.Dirty = false;
                state.RefreshBrowser?.Invoke();
                RefreshView();
                MessageBox.Show("MonstersSkill.xml guardado com sucesso.", "Monster Skill Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            close.Click += (_, _) =>
            {
                editorTabs.TabPages.Remove(page);
                page.Dispose();
            };
        }

        private async void OpenMonsterSkillTermsBrowser(string xmlPath)
        {
            string fullPath = Path.GetFullPath(xmlPath);
            var page = CreateDarkTab("MonstersSkillTerms.xml");
            page.Name = fullPath;

            var loading = new EditorLoadingView(
                "Loading MonstersSkillTerms.xml",
                "Preparing term ranges, targeting types and editor search indexes.");
            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            UpdateEditorEmptyState();
            UpdateEditorTabChrome();

            MonsterSkillTermsEditorService service;
            try
            {
                service = (await EditorPreloadService.GetMonsterSkillTermsAsync(fullPath))
                    ?? throw new FileNotFoundException("MonstersSkillTerms.xml was not found.", fullPath);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                    loading.SetError("MonstersSkillTerms.xml could not be loaded", ex.Message);
                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();
            page.Controls.Clear();
            var header = CreateBrowserHeader("MonstersSkillTerms.xml", "Monster Skill Targeting/Range Editor", out TextBox search, out Label countLabel);
            search.PlaceholderText = "Pesquisar IDX, Range, TargetingType ou RefCode...";

            var resultsHost = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18, 12, 12, 12) };
            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = CEditor,
                Padding = new Padding(0, 0, 8, 0)
            };
            DarkUi.ApplyDarkScrollBar(results);
            resultsHost.Controls.Add(results);
            var timer = new System.Windows.Forms.Timer { Interval = 220 };
            var state = new MonsterSkillTermsBrowseState
            {
                Service = service,
                Search = search,
                CountLabel = countLabel,
                Results = results,
                SearchTimer = timer,
                XmlPath = fullPath
            };
            page.Tag = state;
            timer.Tick += (_, _) => { timer.Stop(); RenderMonsterSkillTermsResults(state); };
            search.TextChanged += (_, _) => { timer.Stop(); timer.Start(); };

            page.Controls.Add(resultsHost);
            page.Controls.Add(header);
            RenderMonsterSkillTermsResults(state);
            page.ResumeLayout();
        }

        private void RenderMonsterSkillTermsResults(MonsterSkillTermsBrowseState state)
        {
            IReadOnlyList<MonsterSkillTermRecord> filtered = state.Service.Search(state.Search.Text);
            state.CountLabel.Text = $"{filtered.Count} skill terms";
            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (MonsterSkillTermRecord term in filtered)
            {
                var card = new Panel
                {
                    Width = 900,
                    Height = 70,
                    BackColor = Color.FromArgb(27, 27, 27),
                    Margin = new Padding(0, 0, 0, 8)
                };
                card.Paint += (_, e) =>
                {
                    using var p = new Pen(Color.FromArgb(58, 58, 58));
                    e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
                };
                var lbl1 = new Label { Text = $"IDX {term.Idx}", ForeColor = CText, Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold), Location = new Point(14, 10), Size = new Size(140, 22) };
                var lbl2 = new Label { Text = $"Direction {term.Direction}  •  Range {term.Range}  •  TargetingType {term.TargetingType}  •  RefCode {term.RefCode}", ForeColor = CMuted, Font = new Font("Segoe UI", 8.5F), Location = new Point(14, 36), Size = new Size(560, 18) };
                var xmlButton = CreateEditorActionButton("VIEW XML BLOCK");
                xmlButton.Size = new Size(130, 34);
                xmlButton.Location = new Point(card.Width - 150, 17);
                xmlButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                xmlButton.Click += (_, _) => ShowReadonlyXmlDialog($"MonstersSkillTerm {term.Idx}", term.Node.ToString());
                card.Controls.Add(lbl1);
                card.Controls.Add(lbl2);
                card.Controls.Add(xmlButton);
                state.Results.Controls.Add(card);
            }
            if (filtered.Count == 0)
                state.Results.Controls.Add(CreateInfoLabel("Nenhum term corresponde ao filtro atual."));
            state.Results.ResumeLayout();
        }

        private static void ApplyResponsiveMonsterEditorLayout(
            FlowLayoutPanel form,
            int availableWidth)
        {
            int contentWidth =
                Math.Max(
                    340,
                    availableWidth -
                    4);

            form.SuspendLayout();

            foreach (Control topLevel in
                     form.Controls.Cast<Control>())
            {
                if (topLevel is Panel previewCard &&
                    topLevel is not FlowLayoutPanel)
                {
                    // The first top-level Panel is the monster preview card.
                    previewCard.Width =
                        contentWidth;

                    foreach (Label label in
                             previewCard.Controls.OfType<Label>())
                    {
                        label.Width =
                            Math.Max(
                                100,
                                previewCard.ClientSize.Width -
                                label.Left -
                                18);
                    }

                    continue;
                }

                if (topLevel is not FlowLayoutPanel section)
                    continue;

                section.SuspendLayout();

                section.Width =
                    contentWidth;

                int innerWidth =
                    Math.Max(
                        280,
                        section.ClientSize.Width -
                        section.Padding.Horizontal -
                        6);

                // Heading/subtitle labels always span the full section.
                foreach (Label label in
                         section.Controls.OfType<Label>())
                {
                    label.Width =
                        innerWidth;
                }

                List<Panel> fields =
                    section.Controls
                        .OfType<Panel>()
                        .ToList();

                // Monster editor uses two columns only when there is genuinely
                // enough room. On normal editor widths it switches to one full
                // width field per row, eliminating clipping and horizontal scroll.
                bool twoColumns =
                    innerWidth >= 650;

                int fieldWidth =
                    twoColumns
                        ? Math.Max(
                            260,
                            (innerWidth - 12) / 2)
                        : innerWidth;

                foreach (Panel field in fields)
                {
                    field.Width =
                        fieldWidth;

                    if (string.Equals(
                            field.Tag as string,
                            "MonsterSpecialField",
                            StringComparison.Ordinal))
                    {
                        // Smart ID / Model cards have buttons and status
                        // labels; their own Resize handler positions children.
                        field.PerformLayout();
                        continue;
                    }

                    foreach (Label label in
                             field.Controls.OfType<Label>())
                    {
                        label.Width =
                            Math.Max(
                                80,
                                field.ClientSize.Width -
                                label.Left -
                                14);
                    }

                    foreach (TextBox box in
                             field.Controls.OfType<TextBox>())
                    {
                        box.Width =
                            Math.Max(
                                80,
                                field.ClientSize.Width -
                                box.Left -
                                14);
                    }
                }

                section.ResumeLayout(
                    true);
            }

            form.ResumeLayout(
                true);
        }

        private void AddMonsterIdentitySection(
            FlowLayoutPanel host,
            MonsterEditState state)
        {
            var section =
                CreateEditorSection(
                    "IDENTITY",
                    "Main identifiers, Digimon model reference and display text.");

            section.Controls.Add(
                CreateMonsterIdValidationCard(
                    state));

            section.Controls.Add(
                CreateMonsterModelSelectionCard(
                    state));

            AddBoundTextField(
                section,
                state.Editors,
                state.Working,
                "Name",
                "Monster Name",
                320);

            AddBoundTextField(
                section,
                state.Editors,
                state.Working,
                "Comment",
                "Comment",
                320);

            AddBoundTextField(
                section,
                state.Editors,
                state.Working,
                "Title",
                "Title",
                320);

            AddBoundTextField(
                section,
                state.Editors,
                state.Working,
                "Level",
                "Level");

            AddBoundTextField(
                section,
                state.Editors,
                state.Working,
                "Battle",
                "Battle Type");

            host.Controls.Add(
                section);
        }

        private Panel CreateMonsterIdValidationCard(
            MonsterEditState state)
        {
            XElement monsterIdElement =
                EnsureElement(
                    state.Working,
                    "MonsterID");

            var card =
                CreateFieldHost(
                    "Monster ID",
                    "Must be a unique positive ID in Monster.xml.",
                    420,
                    116);

            card.Tag =
                "MonsterSpecialField";

            var idBox =
                new TextBox
                {
                    Text = monsterIdElement.Value,
                    BackColor = Color.FromArgb(16, 16, 16),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(14, 50),
                    Height = 24
                };

            var status =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI Semibold", 7.8F),
                    Location = new Point(14, 79),
                    Height = 22,
                    AutoEllipsis = true
                };

            var useSuggested =
                CreateEditorActionButton(
                    "USE SUGGESTED");

            useSuggested.Size =
                new Size(
                    112,
                    27);

            useSuggested.Visible =
                false;

            void Relayout()
            {
                int right =
                    card.ClientSize.Width -
                    14;

                if (useSuggested.Visible)
                {
                    useSuggested.Location =
                        new Point(
                            Math.Max(
                                150,
                                right -
                                useSuggested.Width),
                            75);

                    status.Width =
                        Math.Max(
                            80,
                            useSuggested.Left -
                            status.Left -
                            8);
                }
                else
                {
                    status.Width =
                        Math.Max(
                            80,
                            right -
                            status.Left);
                }

                idBox.Width =
                    Math.Max(
                        100,
                        right -
                        idBox.Left);
            }

            uint SuggestedId()
            {
                HashSet<uint> used =
                    state.Service.Root
                        .Elements("Monster")
                        .Select(
                            node =>
                                UIntValue(
                                    node,
                                    "MonsterID"))
                        .Where(
                            id =>
                                id != 0)
                        .ToHashSet();

                uint candidate =
                    used.Count == 0
                        ? 1u
                        : used.Max() + 1u;

                while (candidate != uint.MaxValue &&
                       used.Contains(candidate))
                {
                    candidate++;
                }

                return candidate;
            }

            void Validate()
            {
                monsterIdElement.Value =
                    idBox.Text.Trim();

                if (!uint.TryParse(
                        idBox.Text.Trim(),
                        out uint parsed) ||
                    parsed == 0)
                {
                    uint suggested =
                        SuggestedId();

                    status.Text =
                        $"INVALID ID  •  Suggested: {suggested}";

                    status.ForeColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);

                    useSuggested.Text =
                        $"USE {suggested}";

                    useSuggested.Tag =
                        suggested;

                    useSuggested.Visible =
                        true;

                    Relayout();
                    return;
                }

                bool duplicate =
                    state.Service.Root
                        .Elements("Monster")
                        .Any(
                            node =>
                                !ReferenceEquals(
                                    node,
                                    state.Working) &&
                                UIntValue(
                                    node,
                                    "MonsterID") ==
                                parsed);

                if (duplicate)
                {
                    uint suggested =
                        SuggestedId();

                    status.Text =
                        $"ID ALREADY USED  •  Suggested: {suggested}";

                    status.ForeColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);

                    useSuggested.Text =
                        $"USE {suggested}";

                    useSuggested.Tag =
                        suggested;

                    useSuggested.Visible =
                        true;
                }
                else
                {
                    status.Text =
                        $"VALID ID  •  {parsed} is available";

                    status.ForeColor =
                        Color.FromArgb(
                            125,
                            220,
                            140);

                    useSuggested.Visible =
                        false;
                }

                Relayout();
            }

            idBox.TextChanged +=
                (_, _) =>
                {
                    state.Dirty = true;
                    Validate();
                };

            useSuggested.Click +=
                (_, _) =>
                {
                    if (useSuggested.Tag is uint suggested)
                    {
                        idBox.Text =
                            suggested.ToString();
                    }
                };

            card.Resize +=
                (_, _) =>
                    Relayout();

            card.Controls.Add(
                idBox);

            card.Controls.Add(
                status);

            card.Controls.Add(
                useSuggested);

            state.Editors[monsterIdElement] =
                idBox;

            Relayout();
            Validate();

            return card;
        }

        private Panel CreateMonsterModelSelectionCard(
            MonsterEditState state)
        {
            XElement modelElement =
                EnsureElement(
                    state.Working,
                    "ModelDigimon");

            var card =
                CreateFieldHost(
                    "Model Digimon ID",
                    "Only Model.xml entries whose KFM path belongs to Data\\Digimon are selectable.",
                    420,
                    132);

            card.Tag =
                "MonsterSpecialField";

            var modelBox =
                new TextBox
                {
                    Text = modelElement.Value,
                    BackColor = Color.FromArgb(16, 16, 16),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(14, 50),
                    Height = 24
                };

            var selectModel =
                CreateEditorActionButton(
                    "SELECT MODEL");

            selectModel.Size =
                new Size(
                    116,
                    28);

            var modelInfo =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.5F),
                    Location = new Point(14, 82),
                    Height = 38,
                    AutoEllipsis = true
                };

            void Relayout()
            {
                int right =
                    card.ClientSize.Width -
                    14;

                selectModel.Location =
                    new Point(
                        Math.Max(
                            150,
                            right -
                            selectModel.Width),
                        47);

                modelBox.Width =
                    Math.Max(
                        90,
                        selectModel.Left -
                        modelBox.Left -
                        10);

                modelInfo.Width =
                    Math.Max(
                        100,
                        right -
                        modelInfo.Left);
            }

            async Task ValidateModelAsync()
            {
                modelElement.Value =
                    modelBox.Text.Trim();

                await RefreshSelectedModelInfoAsync(
                    modelBox,
                    modelInfo);

                if (!modelInfo.IsDisposed)
                {
                    // RefreshSelectedModelInfoAsync already uses green for
                    // valid Data\Digimon models and warning colors otherwise.
                    modelInfo.Invalidate();
                }
            }

            var validationTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 220
                };

            validationTimer.Tick +=
                async (_, _) =>
                {
                    validationTimer.Stop();
                    await ValidateModelAsync();
                };

            modelBox.TextChanged +=
                (_, _) =>
                {
                    state.Dirty = true;
                    modelElement.Value =
                        modelBox.Text.Trim();

                    validationTimer.Stop();
                    validationTimer.Start();
                };

            selectModel.Click +=
                async (_, _) =>
                {
                    await OpenDigimonModelPickerAsync(
                        state.Page,
                        modelBox);

                    if (!modelBox.IsDisposed)
                        await ValidateModelAsync();
                };

            card.Resize +=
                (_, _) =>
                    Relayout();

            card.Controls.Add(
                modelBox);

            card.Controls.Add(
                selectModel);

            card.Controls.Add(
                modelInfo);

            state.Editors[modelElement] =
                modelBox;

            Relayout();

            _ =
                ValidateModelAsync();

            return card;
        }

        private bool ValidateMonsterIdentityBeforeSave(
            MonsterEditState state,
            out string error)
        {
            error =
                string.Empty;

            if (!uint.TryParse(
                    state.Working.Element("MonsterID")?.Value,
                    out uint monsterId) ||
                monsterId == 0)
            {
                error =
                    "Monster ID must be a positive numeric value.";

                return false;
            }

            bool duplicate =
                state.Service.Root
                    .Elements("Monster")
                    .Any(
                        node =>
                            !ReferenceEquals(
                                node,
                                state.Working) &&
                            UIntValue(
                                node,
                                "MonsterID") ==
                            monsterId);

            if (duplicate)
            {
                error =
                    $"Monster ID {monsterId} already exists in Monster.xml.";

                return false;
            }

            return true;
        }

        private void AddMonsterStatsSection(FlowLayoutPanel host, MonsterEditState state)
        {
            var section = CreateEditorSection("CORE STATS", "Battle values used by the monster in Monster.xml.");
            foreach (string field in new[] { "HP", "DS", "AT", "DE", "HT", "CT", "EV" })
                AddBoundTextField(section, state.Editors, state.Working, field, FriendlyFieldName(field), 150);
            host.Controls.Add(section);
        }

        private void AddMonsterMovementSection(FlowLayoutPanel host, MonsterEditState state)
        {
            var section = CreateEditorSection("MOVEMENT / COMBAT SPEED", "Movement, attack timing and behavior range values.");
            foreach (string field in new[] { "MS", "WS", "AS", "AR", "Sight", "HuntRange", "Scale" })
                AddBoundTextField(section, state.Editors, state.Working, field, FriendlyFieldName(field), 150);
            host.Controls.Add(section);
        }

        private void AddMonsterExtraSection(FlowLayoutPanel host, MonsterEditState state)
        {
            var section = CreateEditorSection("EXTRA / ICONS / EXP", "Auxiliary fields preserved from Monster.xml.");
            foreach (string field in new[] { "Class", "Icon1", "Icon2", "Icon3", "Icon4", "Icon5", "Icon6", "ExpMin", "ExpMax", "EXP", "Unknown2", "Unknown3", "Unknown" })
                AddBoundTextField(section, state.Editors, state.Working, field, FriendlyFieldName(field), 150);
            host.Controls.Add(section);
        }

        private void AddMonsterSkillCoreSection(FlowLayoutPanel host, MonsterSkillEditState state)
        {
            var section = CreateEditorSection("CORE", "Main identity, owner monster and mechanic type.");
            AddBoundTextField(section, state.Editors, state.Working, "Skill_IDX", "Skill IDX", 160);

            var monsterCard = CreateFieldHost("Monster ID", "Owner monster that can cast this skill.", 330, 94);
            var monsterText = CreateBoundTextBox(state.Working, "MonsterID");
            monsterText.Width = 110;
            monsterText.Location = new Point(14, 52);
            state.Editors["MonsterID"] = monsterText;
            monsterCard.Controls.Add(monsterText);
            var selectMonster = CreateEditorActionButton("SELECT MONSTER");
            selectMonster.Size = new Size(130, 28);
            selectMonster.Location = new Point(132, 48);
            selectMonster.Click += (_, _) =>
            {
                MonsterRecord? selected = ShowMonsterReferencePicker(state.Monsters, UIntValue(state.Working, "MonsterID"));
                if (selected == null)
                    return;
                monsterText.Text = selected.MonsterId.ToString();
            };
            monsterCard.Controls.Add(selectMonster);
            section.Controls.Add(monsterCard);

            var useTermCard = CreateFieldHost("UseTerms", "Behavior/mechanics type. Includes server implementation notes.", 330, 94);
            var useTermCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = CText,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(14, 49),
                Size = new Size(294, 28)
            };
            HashSet<int> values = new(MonsterUseTermCatalog.All.Select(x => x.Value)) { IntValue(state.Working, "UseTerms"), 0 };
            foreach (int value in values.OrderBy(x => x))
            {
                UseTermInfo info = MonsterUseTermCatalog.Get(value);
                useTermCombo.Items.Add(new ComboOption(value, $"{value} - {info.Name}"));
            }
            for (int i = 0; i < useTermCombo.Items.Count; i++)
            {
                if (useTermCombo.Items[i] is ComboOption item && item.Value == IntValue(state.Working, "UseTerms"))
                {
                    useTermCombo.SelectedIndex = i;
                    break;
                }
            }
            useTermCombo.SelectedIndexChanged += (_, _) =>
            {
                if (useTermCombo.SelectedItem is ComboOption chosen)
                    SetElementValue(state.Working, "UseTerms", chosen.Value.ToString());
            };
            state.Editors["UseTerms"] = useTermCombo;
            useTermCard.Controls.Add(useTermCombo);
            section.Controls.Add(useTermCard);
            host.Controls.Add(section);
        }

        private void AddMonsterSkillCombatSection(FlowLayoutPanel host, MonsterSkillEditState state)
        {
            var section = CreateEditorSection("TIMING / TARGETING", "Cast timings, target counters and the linked RangeIDX term profile.");
            foreach (string field in new[] { "CoolTime", "CastTime", "CastCheck", "Target_Cnt", "Target_MinCnt", "Target_MaxCnt", "RangeIDX" })
                AddBoundTextField(section, state.Editors, state.Working, field, FriendlyFieldName(field), 150);
            host.Controls.Add(section);
        }

        private void AddMonsterSkillEffectsSection(FlowLayoutPanel host, MonsterSkillEditState state)
        {
            var section = CreateEditorSection("ANIMATION / EFFECT POWER", "Animation profile, skill type and core min/max values.");
            foreach (string field in new[] { "Skill_Type", "Eff_Val_Min", "Eff_Val_Max", "unk2", "SequenceID", "Ani_Delay", "Valocity", "Accel" })
                AddBoundTextField(section, state.Editors, state.Working, field, FriendlyFieldName(field), 150);
            host.Controls.Add(section);
        }

        private void AddMonsterSkillFactorSection(FlowLayoutPanel host, MonsterSkillEditState state, string factorField, string valueField, string title, Label summaryLabel)
        {
            var section = CreateEditorSection(title, "Helper pickers for summoned mobs or Buff/Debuff references. Raw values are still fully editable.");

            var factorCard = CreateFieldHost($"{title} raw reference", "Raw XML field plus helper buttons.", 420, 116);
            var factorText = CreateBoundTextBox(state.Working, factorField);
            factorText.Width = 110;
            factorText.Location = new Point(14, 52);
            state.Editors[factorField] = factorText;
            factorCard.Controls.Add(factorText);

            var selectMob = CreateEditorActionButton("SELECT MOB");
            selectMob.Size = new Size(118, 28);
            selectMob.Location = new Point(132, 48);
            selectMob.Click += (_, _) =>
            {
                MonsterRecord? selected = ShowMonsterReferencePicker(state.Monsters, (uint)Math.Max(0, IntValue(state.Working, factorField)));
                if (selected == null)
                    return;
                factorText.Text = selected.MonsterId.ToString();
            };
            factorCard.Controls.Add(selectMob);

            var selectBuff = CreateEditorActionButton("SELECT BUFF/DEBUFF");
            selectBuff.Size = new Size(154, 28);
            selectBuff.Location = new Point(256, 48);
            selectBuff.Click += (_, _) =>
            {
                if (state.Buffs == null)
                {
                    MessageBox.Show("Buff.xml não foi encontrado no workspace atual.", "Buff Picker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                BuffMiniRecord? selected = ShowBuffReferencePicker(state.Buffs, (uint)Math.Max(0, IntValue(state.Working, factorField)));
                if (selected == null)
                    return;
                factorText.Text = selected.Id.ToString();
            };
            factorCard.Controls.Add(selectBuff);

            summaryLabel.ForeColor = Color.FromArgb(125, 210, 145);
            summaryLabel.Font = new Font("Segoe UI", 8.3F);
            summaryLabel.AutoSize = false;
            summaryLabel.Location = new Point(14, 82);
            summaryLabel.Size = new Size(392, 24);
            summaryLabel.AutoEllipsis = true;
            factorCard.Controls.Add(summaryLabel);
            section.Controls.Add(factorCard);

            AddBoundTextField(section, state.Editors, state.Working, valueField, $"{title} value / timer", 180);
            host.Controls.Add(section);
        }

        private void AddMonsterSkillExtraSection(FlowLayoutPanel host, MonsterSkillEditState state)
        {
            var section = CreateEditorSection("EXTRA", "Talk, activation and warning effect fields.");
            foreach (string field in new[] { "TalkID", "Activetype", "NoticeTime", "NoticeEffname", "unk" })
                AddBoundTextField(section, state.Editors, state.Working, field, FriendlyFieldName(field), field == "NoticeEffname" ? 320 : 160);
            host.Controls.Add(section);
        }

        private FlowLayoutPanel CreateEditorSection(string title, string subtitle)
        {
            var section = new FlowLayoutPanel
            {
                Width = 760,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.FromArgb(23, 23, 23),
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(14, 12, 14, 14)
            };
            section.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(56, 56, 56));
                e.Graphics.DrawRectangle(p, 0, 0, section.Width - 1, section.Height - 1);
            };

            var titleLabel = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
                AutoSize = false,
                Width = 720,
                Height = 22,
                Margin = new Padding(2, 0, 2, 0)
            };
            var subtitleLabel = new Label
            {
                Text = subtitle,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.2F),
                AutoSize = false,
                Width = 720,
                Height = 18,
                Margin = new Padding(2, 0, 2, 8)
            };
            section.Controls.Add(titleLabel);
            section.Controls.Add(subtitleLabel);
            return section;
        }

        private static Panel CreateFieldHost(string label, string subtitle, int width, int height)
        {
            var host = new Panel
            {
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(18, 18, 18),
                Margin = new Padding(0, 0, 10, 10)
            };
            var title = new Label
            {
                Text = label,
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                Location = new Point(14, 10),
                Size = new Size(width - 26, 20),
                AutoEllipsis = true
            };
            var sub = new Label
            {
                Text = subtitle,
                ForeColor = Color.FromArgb(145, 145, 145),
                Font = new Font("Segoe UI", 7.7F),
                Location = new Point(14, 28),
                Size = new Size(width - 26, 18),
                AutoEllipsis = true
            };
            host.Controls.Add(title);
            host.Controls.Add(sub);
            return host;
        }

        private void AddBoundTextField(FlowLayoutPanel section, Dictionary<XElement, Control> editors, XElement node, string tag, string label, int width = 200)
        {
            XElement element = EnsureElement(node, tag);
            string shownLabel = string.IsNullOrWhiteSpace(label) ? tag : label;
            var host = CreateFieldHost(shownLabel, tag, width + 28, 90);
            var box = new TextBox
            {
                Text = element.Value,
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(14, 50),
                Size = new Size(width, 24)
            };
            box.TextChanged += (_, _) => element.Value = box.Text;
            host.Controls.Add(box);
            section.Controls.Add(host);
            editors[element] = box;
        }


        private void AddBoundTextField(FlowLayoutPanel section, Dictionary<string, Control> editors, XElement node, string tag, string label, int width = 200)
        {
            XElement element = EnsureElement(node, tag);
            string shownLabel = string.IsNullOrWhiteSpace(label) ? tag : label;
            var host = CreateFieldHost(shownLabel, tag, width + 28, 90);
            var box = new TextBox
            {
                Text = element.Value,
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(14, 50),
                Size = new Size(width, 24)
            };
            box.TextChanged += (_, _) => element.Value = box.Text;
            host.Controls.Add(box);
            section.Controls.Add(host);
            editors[tag] = box;
        }

        private TextBox CreateBoundTextBox(XElement node, string tag)
        {
            XElement element = EnsureElement(node, tag);
            var box = new TextBox
            {
                Text = element.Value,
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F)
            };
            box.TextChanged += (_, _) => element.Value = box.Text;
            return box;
        }

        private Panel CreateBrowserHeader(string titleText, string subtitleText, out TextBox search, out Label countLabel)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 124,
                BackColor = Color.FromArgb(27, 27, 27)
            };
            header.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            var title = new Label
            {
                Text = titleText,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                Location = new Point(20, 12),
                Size = new Size(320, 30)
            };
            var subtitle = new Label
            {
                Text = subtitleText,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(22, 40),
                Size = new Size(340, 20)
            };
            var searchPanel = new Panel
            {
                Location = new Point(20, 68),
                Size = new Size(470, 34),
                BackColor = Color.FromArgb(12, 12, 12)
            };
            searchPanel.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(74, 74, 74));
                e.Graphics.DrawRectangle(p, 0, 0, searchPanel.Width - 1, searchPanel.Height - 1);
            };
            search = new TextBox
            {
                Location = new Point(9, 6),
                Size = new Size(452, 22),
                BackColor = Color.FromArgb(12, 12, 12),
                ForeColor = CText,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10F)
            };
            searchPanel.Controls.Add(search);
            countLabel = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 8.3F),
                AutoSize = false,
                Location = new Point(20, 103),
                Size = new Size(250, 16)
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(searchPanel);
            header.Controls.Add(countLabel);
            return header;
        }

        private void PositionHeaderActions(Panel header, Button rightmostButton)
        {
            void Apply()
            {
                rightmostButton.Left = Math.Max(20, header.ClientSize.Width - rightmostButton.Width - 20);
            }
            header.Resize += (_, _) => Apply();
            Apply();
        }

        private MonsterReferenceCatalog LoadMonsterCatalogSafe()
        {
            string[] candidates =
            {
                Path.Combine(AppPaths.Xml, "Monster", "Monster.xml"),
                Path.Combine(AppPaths.Xml, "Monsters", "Monster.xml"),
                Path.Combine(AppContext.BaseDirectory, "Monster.xml")
            };

            string? path = candidates.FirstOrDefault(File.Exists);
            if (path == null)
                throw new FileNotFoundException("Monster.xml was not found in the workspace.");

            MonsterEditorService service =
                EditorPreloadService.TryGetMonsterEditor()
                ?? EditorPreloadService.GetMonsterEditorAsync(path).GetAwaiter().GetResult();

            return new MonsterReferenceCatalog(service);
        }

        private static MonsterSkillTermsEditorService? TryLoadMonsterSkillTermsNear(string skillPath)
        {
            string[] candidates =
            {
                Path.Combine(Path.GetDirectoryName(skillPath) ?? string.Empty, "MonstersSkillTerms.xml"),
                Path.Combine(AppPaths.Xml, "MonstersSkill", "MonstersSkillTerms.xml"),
                Path.Combine(AppPaths.Xml, "MonstersSkillTerms", "MonstersSkillTerms.xml"),
                Path.Combine(AppContext.BaseDirectory, "MonstersSkillTerms.xml")
            };
            MonsterSkillTermsEditorService? cached =
                EditorPreloadService.TryGetMonsterSkillTerms();

            if (cached != null)
                return cached;

            string? hit = candidates.FirstOrDefault(File.Exists);
            return hit == null
                ? null
                : EditorPreloadService.GetMonsterSkillTermsAsync(hit).GetAwaiter().GetResult();
        }

        private string BuildFactorSummary(MonsterSkillEditState state, int value, string fieldName)
        {
            if (value <= 0)
                return $"{fieldName}: no linked mob/buff reference.";

            MonsterRecord? monster = state.Monsters.Find((uint)value);
            if (monster != null)
                return $"{fieldName}: Monster {monster.MonsterId} — {monster.DisplayName}";

            BuffMiniRecord? buff = state.Buffs?.Find((uint)value);
            if (buff != null)
                return $"{fieldName}: Buff {buff.Id} — {buff.DisplayName}";

            return $"{fieldName}: raw value {value} (no monster/buff match found).";
        }

        private static string BuildUseTermMechanicsHint(UseTermInfo info, XElement node)
        {
            int factor1 = IntValue(node, "Eff_Factor");
            int factor2 = IntValue(node, "Eff_Factor2");
            int factor3 = IntValue(node, "Eff_Factor3");
            int value1 = IntValue(node, "Eff_Fact_Val");
            int value2 = IntValue(node, "Eff_Fact_Val2");
            int value3 = IntValue(node, "Eff_Fact_Val3");

            string animation = string.IsNullOrWhiteSpace(info.Animation)
                ? string.Empty
                : $" Animation: {info.Animation}.";

            return info.Value switch
            {
                13 => $"Summon-type mechanic. Typical expectation: Eff_Factor is spawned MonsterID, Eff_Val_Min/Max control spawn amount, and Eff_Fact_Val often acts as timer/interval.{animation}",
                14 => $"Growth mechanic. Factor slots usually carry stacked stat/buff parameters. Current factors: [{factor1}, {factor2}, {factor3}] and factor values [{value1}, {value2}, {value3}].{animation}",
                18 => $"Attack Seed / ground zone mechanic. Skill leaves a timed area effect. Value fields usually define duration or tick behavior.{animation}",
                19 => $"Berserk mechanic. Usually boosts monster stats or enrages behavior. Buff-like references can be selected with the Buff/Debuff picker.{animation}",
                21 or 22 or 23 or 24 => $"Buff-oriented mechanic. Use the Buff/Debuff picker if the factor should reference Buff.xml. Value fields can be used as duration, stack count or range-dependent tuning.{animation}",
                15 or 16 or 17 or 20 or 25 or 26 => $"Advanced targeting / summon / dispersion mechanic. Use the helper pickers for monster or buff references as needed, but raw values remain editable for unknown/engine-specific behavior.{animation}",
                _ => $"General monster skill mechanic. Current factors: [{factor1}, {factor2}, {factor3}] values [{value1}, {value2}, {value3}].{animation}"
            };
        }

        private MonsterRecord? ShowMonsterReferencePicker(MonsterReferenceCatalog catalog, uint selectedId)
        {
            MonsterRecord? selected = catalog.Find(selectedId);
            using var dialog = new Form
            {
                Text = "Select Monster",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(820, 620),
                BackColor = CEditor,
                ForeColor = CText,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(26, 26, 26) };
            var search = new TextBox
            {
                PlaceholderText = "Search MonsterID, ModelDigimon or Name...",
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(14, 15),
                Size = new Size(420, 24)
            };
            var count = new Label { ForeColor = CMuted, Font = new Font("Segoe UI", 8.3F), Location = new Point(450, 18), Size = new Size(200, 20) };
            header.Controls.Add(search);
            header.Controls.Add(count);

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = CEditor,
                Padding = new Padding(12)
            };
            DarkUi.ApplyDarkScrollBar(results);

            void Render()
            {
                IReadOnlyList<MonsterRecord> items = catalog.Search(search.Text);
                count.Text = $"{items.Count} monsters";
                results.SuspendLayout();
                results.Controls.Clear();
                foreach (MonsterRecord record in items.Take(400))
                {
                    var card = new Panel { Width = 760, Height = 78, BackColor = Color.FromArgb(27, 27, 27), Margin = new Padding(0, 0, 0, 8) };
                    card.Paint += (_, e) => { using var p = new Pen(Color.FromArgb(58, 58, 58)); e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
                    var icon = new PictureBox { Location = new Point(10, 10), Size = new Size(56, 56), BackColor = Color.FromArgb(16, 16, 16), SizeMode = PictureBoxSizeMode.Zoom, Image = MonsterAssetResolver.TryLoadMonsterDigimonIcon(record.ModelDigimon) };
                    var name = new Label { Text = record.DisplayName, ForeColor = CText, Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold), Location = new Point(78, 12), Size = new Size(470, 22), AutoEllipsis = true };
                    var meta = new Label { Text = $"MonsterID {record.MonsterId}  •  ModelDigimon {record.ModelDigimon}  •  Lv {record.Level}", ForeColor = CMuted, Font = new Font("Segoe UI", 8.2F), Location = new Point(78, 36), Size = new Size(420, 18), AutoEllipsis = true };
                    var button = CreateEditorActionButton("SELECT");
                    button.Size = new Size(100, 32);
                    button.Location = new Point(640, 22);
                    button.Click += (_, _) => { selected = record; dialog.DialogResult = DialogResult.OK; dialog.Close(); };
                    card.Controls.Add(icon);
                    card.Controls.Add(name);
                    card.Controls.Add(meta);
                    card.Controls.Add(button);
                    results.Controls.Add(card);
                }
                if (items.Count == 0)
                    results.Controls.Add(CreateInfoLabel("No monsters found."));
                results.ResumeLayout();
            }

            search.TextChanged += (_, _) => Render();
            dialog.Controls.Add(results);
            dialog.Controls.Add(header);
            Render();
            return dialog.ShowDialog(this) == DialogResult.OK ? selected : null;
        }

        private BuffMiniRecord? ShowBuffReferencePicker(BuffMiniCatalog catalog, uint selectedId)
        {
            BuffMiniRecord? selected = catalog.Find(selectedId);
            using var dialog = new Form
            {
                Text = "Select Buff / Debuff",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(820, 620),
                BackColor = CEditor,
                ForeColor = CText,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var header = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.FromArgb(26, 26, 26) };
            var search = new TextBox
            {
                PlaceholderText = "Search Buff ID, icon or name...",
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(14, 15),
                Size = new Size(420, 24)
            };
            var count = new Label { ForeColor = CMuted, Font = new Font("Segoe UI", 8.3F), Location = new Point(450, 18), Size = new Size(200, 20) };
            header.Controls.Add(search);
            header.Controls.Add(count);

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = CEditor,
                Padding = new Padding(12)
            };
            DarkUi.ApplyDarkScrollBar(results);

            void Render()
            {
                IReadOnlyList<BuffMiniRecord> items = catalog.Search(search.Text);
                count.Text = $"{items.Count} buffs";
                results.SuspendLayout();
                results.Controls.Clear();
                foreach (BuffMiniRecord record in items.Take(400))
                {
                    var card = new Panel { Width = 760, Height = 78, BackColor = Color.FromArgb(27, 27, 27), Margin = new Padding(0, 0, 0, 8) };
                    card.Paint += (_, e) => { using var p = new Pen(Color.FromArgb(58, 58, 58)); e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
                    var icon = new PictureBox { Location = new Point(10, 10), Size = new Size(56, 56), BackColor = Color.FromArgb(16, 16, 16), SizeMode = PictureBoxSizeMode.Zoom, Image = MonsterAssetResolver.TryLoadBuffIcon(record.IconId) };
                    var name = new Label { Text = record.DisplayName, ForeColor = CText, Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold), Location = new Point(78, 12), Size = new Size(470, 22), AutoEllipsis = true };
                    var meta = new Label { Text = $"BuffID {record.Id}  •  Icon {record.IconId}  •  {TrimSummary(record.Comment, 72)}", ForeColor = CMuted, Font = new Font("Segoe UI", 8.2F), Location = new Point(78, 36), Size = new Size(510, 18), AutoEllipsis = true };
                    var button = CreateEditorActionButton("SELECT");
                    button.Size = new Size(100, 32);
                    button.Location = new Point(640, 22);
                    button.Click += (_, _) => { selected = record; dialog.DialogResult = DialogResult.OK; dialog.Close(); };
                    card.Controls.Add(icon);
                    card.Controls.Add(name);
                    card.Controls.Add(meta);
                    card.Controls.Add(button);
                    results.Controls.Add(card);
                }
                if (items.Count == 0)
                    results.Controls.Add(CreateInfoLabel("No buffs found."));
                results.ResumeLayout();
            }

            search.TextChanged += (_, _) => Render();
            dialog.Controls.Add(results);
            dialog.Controls.Add(header);
            Render();
            return dialog.ShowDialog(this) == DialogResult.OK ? selected : null;
        }

        private void ShowReadonlyXmlDialog(string title, string xml)
        {
            using var dialog = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(720, 560),
                BackColor = CEditor,
                ForeColor = CText,
                FormBorderStyle = FormBorderStyle.SizableToolWindow
            };
            var box = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(14, 14, 14),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.4F),
                ReadOnly = true,
                Text = xml
            };
            dialog.Controls.Add(box);
            dialog.ShowDialog(this);
        }

        private sealed class ComboOption
        {
            public ComboOption(int value, string text)
            {
                Value = value;
                Text = text;
            }

            public int Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }

        private static XElement EnsureElement(XElement node, string name)
        {
            XElement? existing = node.Elements(name).FirstOrDefault();
            if (existing != null)
                return existing;
            existing = new XElement(name, string.Empty);
            node.Add(existing);
            return existing;
        }

        private static void SetElementValue(XElement node, string name, string value)
        {
            EnsureElement(node, name).Value = value;
        }

        private static int IntValue(XElement node, string name)
        {
            return int.TryParse(node.Element(name)?.Value, out int value) ? value : 0;
        }

        private static uint UIntValue(XElement node, string name)
        {
            return uint.TryParse(node.Element(name)?.Value, out uint value) ? value : 0;
        }
    }
}
