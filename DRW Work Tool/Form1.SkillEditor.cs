using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using DRW_Work_Tool.Core;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private const int SkillCardsPerPage = 24;

        private sealed class SkillBrowseState
        {
            public TabPage Page = null!;
            public SkillEditorService Service = null!;
            public FlowLayoutPanel Results = null!;
            public TextBox Search = null!;
            public DarkComboBox SkillType = null!;
            public DarkComboBox Target = null!;
            public DarkComboBox Attribute = null!;
            public Label Count = null!;
            public Label PageInfo = null!;
            public Button Previous = null!;
            public Button Next = null!;
            public Button FilterButton = null!;
            public Panel FilterPanel = null!;
            public bool FiltersVisible;
            public int PageIndex;
            public IReadOnlyList<SkillEditorRecord> Filtered =
                Array.Empty<SkillEditorRecord>();
        }

        private sealed class SkillEditState
        {
            public TabPage Page = null!;
            public SkillEditorService Service = null!;
            public XElement Original = null!;
            public XElement Working = null!;
            public bool IsNew;
            public bool Dirty;
            public PictureBox Icon = null!;
            public Label IconIdLabel = null!;
            public Label IdStatus = null!;
            public TextBox Id = null!;
            public TextBox Name = null!;
            public TextBox Comment = null!;
            public Panel Body = null!;
            public readonly Dictionary<string, TextBox> Fields =
                new(StringComparer.Ordinal);
            public readonly List<TextBox> EffectFields = new();
            public readonly Dictionary<XElement, Label> BuffStatusLabels =
                new();
        }

        private async void OpenSkillBrowser(string xmlPath)
        {
            string fullPath =
                System.IO.Path.GetFullPath(xmlPath);

            var page =
                CreateDarkTab("Skill.xml");

            page.Name = fullPath;

            var loading =
                new EditorLoadingView(
                    "Loading Skill Database",
                    "Preparing Skill.xml, search indexes and skill icon references.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            UpdateEditorEmptyState();
            UpdateEditorTabChrome();

            try
            {
                SkillEditorService service =
                    await EditorPreloadService
                        .GetSkillEditorAsync(fullPath);

                if (page.IsDisposed)
                    return;

                BuildSkillBrowser(
                    page,
                    service,
                    loading);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "Skill.xml could not be loaded",
                        ex.Message);
                }
            }
        }

        private void BuildSkillBrowser(
            TabPage page,
            SkillEditorService service,
            Control loading)
        {
            var root =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18, 18, 18),
                    Padding = new Padding(14, 12, 14, 16)
                };

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 156,
                    BackColor = Color.FromArgb(22, 22, 22)
                };

            var title =
                new Label
                {
                    Text = "Skill Database",
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        14F,
                        FontStyle.Bold),
                    Location = new Point(14, 12),
                    AutoSize = true
                };

            var subtitle =
                new Label
                {
                    Text =
                        $"Skill.xml  •  {service.Count:N0} skills  •  " +
                        "3 SkillApply effects per skill",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.4F),
                    Location = new Point(15, 42),
                    AutoSize = true
                };

            var add =
                CreateEditorActionButton("NEW SKILL");

            add.Size = new Size(150, 34);
            add.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var search =
                new TextBox
                {
                    BackColor = Color.FromArgb(10, 10, 10),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 9F),
                    PlaceholderText =
                        "Search Skill ID, name, description or icon ID...",
                    Location = new Point(14, 72),
                    Height = 28,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            var filterButton =
                CreateEditorActionButton("FILTERS");

            filterButton.Size = new Size(112, 30);
            filterButton.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var filterPanel =
                new Panel
                {
                    BackColor = Color.FromArgb(25, 25, 25),
                    Height = 46,
                    Visible = false,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            var skillType =
                CreateSkillFilterCombo(
                    "All Skill Types",
                    service.SkillTypes);

            var target =
                CreateSkillFilterCombo(
                    "All Targets",
                    service.Targets);

            var attribute =
                CreateSkillFilterCombo(
                    "All Attributes",
                    service.Attributes);

            skillType.Location = new Point(8, 8);
            target.Location = new Point(158, 8);
            attribute.Location = new Point(308, 8);

            var resetFilters =
                CreateEditorActionButton("RESET");

            resetFilters.Location = new Point(458, 8);
            resetFilters.Size = new Size(92, 28);

            filterPanel.Controls.Add(skillType);
            filterPanel.Controls.Add(target);
            filterPanel.Controls.Add(attribute);
            filterPanel.Controls.Add(resetFilters);

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.4F),
                    Location = new Point(15, 116),
                    AutoSize = true
                };

            var previous =
                CreateEditorActionButton("◀ PREVIOUS");

            previous.Size = new Size(112, 30);
            previous.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var pageInfo =
                new Label
                {
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        8.6F),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size = new Size(70, 30),
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Right
                };

            var next =
                CreateEditorActionButton("NEXT ▶");

            next.Size = new Size(112, 30);
            next.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            bool stateForLayoutFiltersVisible = false;

            void LayoutHeader()
            {
                add.Left =
                    Math.Max(
                        550,
                        header.ClientSize.Width -
                        add.Width -
                        14);

                add.Top = 12;

                filterButton.Left =
                    header.ClientSize.Width -
                    filterButton.Width -
                    14;

                filterButton.Top = 70;

                search.Width =
                    Math.Max(
                        260,
                        filterButton.Left -
                        search.Left -
                        10);

                filterPanel.Left = 14;
                filterPanel.Top = 106;
                filterPanel.Width =
                    Math.Max(
                        420,
                        header.ClientSize.Width - 28);

                next.Left =
                    header.ClientSize.Width -
                    next.Width -
                    14;

                pageInfo.Left =
                    next.Left -
                    pageInfo.Width -
                    8;

                previous.Left =
                    pageInfo.Left -
                    previous.Width -
                    8;

                int pagerTop =
                    stateForLayoutFiltersVisible
                        ? 164
                        : 116;

                previous.Top =
                    pageInfo.Top =
                    next.Top =
                    pagerTop;

                count.Top =
                    stateForLayoutFiltersVisible
                        ? 170
                        : 122;
            }

            header.Resize +=
                (_, _) => LayoutHeader();

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(add);
            header.Controls.Add(search);
            header.Controls.Add(filterButton);
            header.Controls.Add(filterPanel);
            header.Controls.Add(count);
            header.Controls.Add(previous);
            header.Controls.Add(pageInfo);
            header.Controls.Add(next);

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    WrapContents = false,
                    FlowDirection = FlowDirection.TopDown,
                    BackColor = Color.FromArgb(18, 18, 18),
                    Padding = new Padding(6, 12, 18, 28)
                };

            DarkUi.ApplyDarkScrollBar(results);

            var state =
                new SkillBrowseState
                {
                    Page = page,
                    Service = service,
                    Results = results,
                    Search = search,
                    SkillType = skillType,
                    Target = target,
                    Attribute = attribute,
                    Count = count,
                    PageInfo = pageInfo,
                    Previous = previous,
                    Next = next,
                    FilterButton = filterButton,
                    FilterPanel = filterPanel
                };

            page.Tag = state;

            var debounce =
                new System.Windows.Forms.Timer
                {
                    Interval = 170
                };

            debounce.Tick +=
                (_, _) =>
                {
                    debounce.Stop();
                    state.PageIndex = 0;
                    RefreshSkillBrowser(state);
                };

            search.TextChanged +=
                (_, _) =>
                {
                    debounce.Stop();
                    debounce.Start();
                };

            skillType.SelectedIndexChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshSkillBrowser(state);
                };

            target.SelectedIndexChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshSkillBrowser(state);
                };

            attribute.SelectedIndexChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshSkillBrowser(state);
                };

            filterButton.Click +=
                (_, _) =>
                {
                    state.FiltersVisible =
                        !state.FiltersVisible;

                    stateForLayoutFiltersVisible =
                        state.FiltersVisible;

                    filterPanel.Visible =
                        state.FiltersVisible;

                    filterButton.Text =
                        state.FiltersVisible
                            ? "HIDE FILTERS"
                            : "FILTERS";

                    header.Height =
                        state.FiltersVisible
                            ? 204
                            : 156;

                    LayoutHeader();
                };

            resetFilters.Click +=
                (_, _) =>
                {
                    skillType.SelectedIndex = 0;
                    target.SelectedIndex = 0;
                    attribute.SelectedIndex = 0;
                    state.PageIndex = 0;
                    RefreshSkillBrowser(state);
                };

            previous.Click +=
                (_, _) =>
                {
                    if (state.PageIndex <= 0)
                        return;

                    state.PageIndex--;
                    RefreshSkillBrowser(state);
                };

            next.Click +=
                (_, _) =>
                {
                    int pages =
                        Math.Max(
                            1,
                            (state.Filtered.Count +
                             SkillCardsPerPage - 1) /
                            SkillCardsPerPage);

                    if (state.PageIndex + 1 >= pages)
                        return;

                    state.PageIndex++;
                    RefreshSkillBrowser(state);
                };

            add.Click +=
                (_, _) =>
                {
                    XElement created =
                        service.CreateDefaultSkill();

                    OpenSkillEditTab(
                        service,
                        created,
                        isNew: true);
                };

            root.Controls.Add(results);
            root.Controls.Add(header);

            page.Controls.Add(root);
            root.BringToFront();

            loading.Dispose();

            LayoutHeader();
            RefreshSkillBrowser(state);
        }

        private DarkComboBox CreateSkillFilterCombo(
            string allText,
            IReadOnlyList<int> values)
        {
            var combo =
                new DarkComboBox
                {
                    Size = new Size(140, 28)
                };

            combo.Items.Add(allText);

            foreach (int value in values)
                combo.Items.Add(
                    value.ToString(
                        CultureInfo.InvariantCulture));

            combo.SelectedIndex = 0;
            return combo;
        }

        private static int? SelectedSkillFilter(
            DarkComboBox combo)
        {
            if (combo.SelectedIndex <= 0)
                return null;

            return int.TryParse(
                combo.SelectedItem?.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                    ? value
                    : null;
        }

        private void RefreshSkillBrowser(
            SkillBrowseState state)
        {
            if (state.Page.IsDisposed)
                return;

            state.Filtered =
                state.Service.Search(
                    state.Search.Text,
                    SelectedSkillFilter(
                        state.SkillType),
                    SelectedSkillFilter(
                        state.Target),
                    SelectedSkillFilter(
                        state.Attribute));

            int pages =
                Math.Max(
                    1,
                    (state.Filtered.Count +
                     SkillCardsPerPage - 1) /
                    SkillCardsPerPage);

            state.PageIndex =
                Math.Max(
                    0,
                    Math.Min(
                        state.PageIndex,
                        pages - 1));

            state.Count.Text =
                $"Total: {state.Service.Count:N0}   •   " +
                $"Results: {state.Filtered.Count:N0}";

            state.PageInfo.Text =
                $"{state.PageIndex + 1} / {pages}";

            state.Previous.Enabled =
                state.PageIndex > 0;

            state.Next.Enabled =
                state.PageIndex + 1 < pages;

            IEnumerable<SkillEditorRecord> pageRecords =
                state.Filtered
                    .Skip(
                        state.PageIndex *
                        SkillCardsPerPage)
                    .Take(SkillCardsPerPage);

            state.Results.SuspendLayout();

            foreach (Control control
                     in state.Results.Controls
                         .Cast<Control>()
                         .ToArray())
            {
                state.Results.Controls.Remove(control);
                control.Dispose();
            }

            foreach (SkillEditorRecord record in pageRecords)
            {
                state.Results.Controls.Add(
                    CreateSkillBrowserCard(
                        state,
                        record));
            }

            state.Results.ResumeLayout(true);

            ResetSkillScrollToTop(
                state.Results);
        }

        private Panel CreateSkillBrowserCard(
            SkillBrowseState state,
            SkillEditorRecord record)
        {
            int width =
                Math.Max(
                    680,
                    state.Results.ClientSize.Width -
                    38);

            var card =
                new Panel
                {
                    Width = width,
                    Height = 112,
                    BackColor = Color.FromArgb(29, 29, 29),
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(2, 0, 2, 10)
                };

            var icon =
                new PictureBox
                {
                    Location = new Point(14, 14),
                    Size = new Size(72, 72),
                    BackColor = Color.Black,
                    SizeMode = PictureBoxSizeMode.Zoom
                };

            _ = LoadSkillIconIntoAsync(
                state.Service,
                record.IconId,
                icon);

            var name =
                new Label
                {
                    Text = record.DisplayName,
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        10.5F,
                        FontStyle.Bold),
                    Location = new Point(100, 13),
                    Size = new Size(310, 22),
                    AutoEllipsis = true
                };

            var id =
                new Label
                {
                    Text =
                        $"ID {record.Id}  •  Icon {record.IconId}",
                    ForeColor = Color.FromArgb(125, 220, 140),
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(100, 38),
                    Size = new Size(310, 20)
                };

            var comment =
                new Label
                {
                    Text =
                        string.IsNullOrWhiteSpace(record.Comment)
                            ? "No description."
                            : record.Comment,
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.8F),
                    Location = new Point(100, 61),
                    Size = new Size(360, 36),
                    AutoEllipsis = true
                };

            var stats =
                new Label
                {
                    Text =
                        $"Type {record.SkillType}   |   Target {record.Target}   |   " +
                        $"AT Type {record.AttackType}\r\n" +
                        $"Max Lv {record.MaxLevel}   |   DS {record.UseDs}   |   " +
                        $"Cooldown {FormatSkillTime(record.Cooldown)}",
                    ForeColor = CText,
                    Font = new Font("Consolas", 7.5F),
                    Location = new Point(472, 20),
                    Size = new Size(290, 50)
                };

            var edit =
                CreateEditorActionButton("EDIT");

            edit.Size = new Size(96, 32);
            edit.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            edit.Location =
                new Point(
                    width - 220,
                    65);

            var remove =
                CreateEditorActionButton("REMOVE");

            remove.Size = new Size(96, 32);
            remove.ForeColor =
                Color.FromArgb(
                    255,
                    105,
                    105);

            remove.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            remove.Location =
                new Point(
                    width - 112,
                    65);

            edit.Click +=
                (_, _) =>
                    OpenSkillEditTab(
                        state.Service,
                        record.Node,
                        isNew: false);

            remove.Click +=
                (_, _) =>
                {
                    DialogResult answer =
                        MessageBox.Show(
                            $"Remove Skill {record.Id} — {record.DisplayName}?\n\n" +
                            "This removes the complete <SkillData> block from Skill.xml.",
                            "Remove Skill",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                    if (answer != DialogResult.Yes)
                        return;

                    state.Service.Remove(
                        record.Node);

                    state.Service.Save();

                    EditorPreloadService.ReplaceSkillEditor(
                        state.Service.FilePath,
                        state.Service);

                    EditorPreloadService.InvalidateSkillReferences();

                    RefreshSkillBrowser(state);
                };

            card.Controls.Add(icon);
            card.Controls.Add(name);
            card.Controls.Add(id);
            card.Controls.Add(comment);
            card.Controls.Add(stats);
            card.Controls.Add(edit);
            card.Controls.Add(remove);

            return card;
        }

        private void OpenSkillEditTab(
            SkillEditorService service,
            XElement original,
            bool isNew)
        {
            uint id =
                SkillEditorService.U(
                    original,
                    "s_dwID");

            string key =
                $"skill-edit:{service.FilePath}:{id}:{original.GetHashCode()}";

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        x =>
                            x.Name.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            string name =
                SkillEditorService.S(
                    original,
                    "s_szName");

            var page =
                CreateDarkTab(
                    $"{(string.IsNullOrWhiteSpace(name) ? id.ToString() : name)} [Edit]");

            page.Name = key;

            var loading =
                new EditorLoadingView(
                    "Loading Skill Editor",
                    "Preparing skill fields, effect slots and icon preview.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (page.IsDisposed)
                            return;

                        BuildSkillEditor(
                            page,
                            service,
                            original,
                            isNew,
                            loading);
                    }));
        }

        private void BuildSkillEditor(
            TabPage page,
            SkillEditorService service,
            XElement original,
            bool isNew,
            Control loading)
        {
            XElement working =
                new XElement(original);

            var state =
                new SkillEditState
                {
                    Page = page,
                    Service = service,
                    Original = original,
                    Working = working,
                    IsNew = isNew
                };

            page.Tag = state;

            var top =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    BackColor = Color.FromArgb(24, 24, 24),
                    Padding = new Padding(12, 10, 12, 8)
                };

            var save =
                CreateEditorActionButton("SAVE");

            save.Location = new Point(12, 12);
            save.Size = new Size(104, 34);

            var raw =
                CreateEditorActionButton("VIEW XML BLOCK");

            raw.Location = new Point(126, 12);
            raw.Size = new Size(142, 34);

            var dirty =
                new Label
                {
                    Text = isNew ? "NEW SKILL — Unsaved" : "Saved",
                    ForeColor =
                        isNew
                            ? Color.FromArgb(255, 190, 90)
                            : CMuted,
                    Font = new Font("Segoe UI", 8.3F),
                    Location = new Point(284, 20),
                    AutoSize = true
                };

            top.Controls.Add(save);
            top.Controls.Add(raw);
            top.Controls.Add(dirty);

            var body =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(18, 18, 18),
                    Padding = new Padding(12, 14, 28, 48)
                };

            body.AutoScrollMinSize = new Size(0, 1850);
            DarkUi.ApplyDarkScrollBar(body);

            state.Body = body;

            var content =
                new Panel
                {
                    Location = new Point(10, 10),
                    Width =
                        Math.Max(
                            500,
                            page.ClientSize.Width - 70),
                    Height = 1810,
                    BackColor = Color.FromArgb(18, 18, 18),
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            void ResizeSkillEditorContent()
            {
                int width =
                    Math.Max(
                        480,
                        body.ClientSize.Width - 36);

                content.Width = width;
                content.Left = 8;

                // Prevent WinForms from creating a useless horizontal scrollbar.
                body.AutoScrollMinSize =
                    new Size(
                        0,
                        content.Bottom + 28);

                if (body.HorizontalScroll.Visible)
                {
                    try
                    {
                        body.HorizontalScroll.Value =
                            body.HorizontalScroll.Minimum;
                    }
                    catch
                    {
                    }
                }
            }

            body.Resize +=
                (_, _) =>
                    ResizeSkillEditorContent();

            body.Controls.Add(content);

            Panel hero =
                CreateSkillSection(
                    "SKILL IDENTITY",
                    "Core identification, icon and description.",
                    0,
                    260,
                    content.Width);

            content.Controls.Add(hero);

            var icon =
                new PictureBox
                {
                    Location = new Point(16, 54),
                    Size = new Size(104, 104),
                    BackColor = Color.Black,
                    BorderStyle = BorderStyle.FixedSingle,
                    SizeMode = PictureBoxSizeMode.Zoom
                };

            state.Icon = icon;

            var iconId =
                new Label
                {
                    ForeColor = Color.FromArgb(125, 220, 140),
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(16, 163),
                    Size = new Size(180, 20)
                };

            state.IconIdLabel = iconId;

            var chooseIcon =
                CreateEditorActionButton("SELECT ICON");

            chooseIcon.Location = new Point(16, 184);
            chooseIcon.Size = new Size(104, 28);

            var idBox =
                CreateSkillTextBox(
                    SkillEditorService.S(
                        working,
                        "s_dwID"));

            idBox.Location = new Point(142, 72);
            idBox.Width = 200;

            state.Id = idBox;

            var idTitle =
                CreateSkillFieldLabel(
                    "Skill ID",
                    142,
                    52);

            var idStatus =
                new Label
                {
                    Font = new Font(
                        "Segoe UI Semibold",
                        7.8F),
                    Location = new Point(142, 102),
                    Size = new Size(250, 20)
                };

            state.IdStatus = idStatus;

            var nameTitle =
                CreateSkillFieldLabel(
                    "Skill Name",
                    366,
                    52);

            var nameBox =
                CreateSkillTextBox(
                    SkillEditorService.S(
                        working,
                        "s_szName"));

            nameBox.Location = new Point(366, 72);
            nameBox.Width =
                Math.Max(
                    220,
                    hero.Width - 392);

            state.Name = nameBox;

            var commentTitle =
                CreateSkillFieldLabel(
                    "Description / Comment",
                    142,
                    130);

            var commentBox =
                new TextBox
                {
                    Text =
                        SkillEditorService.S(
                            working,
                            "s_szComment"),
                    BackColor = Color.FromArgb(10, 10, 10),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Location = new Point(142, 152),
                    Size = new Size(
                        Math.Max(300, hero.Width - 174),
                        82)
                };

            state.Comment = commentBox;

            hero.Controls.Add(icon);
            hero.Controls.Add(iconId);
            hero.Controls.Add(chooseIcon);
            hero.Controls.Add(idTitle);
            hero.Controls.Add(idBox);
            hero.Controls.Add(idStatus);
            hero.Controls.Add(nameTitle);
            hero.Controls.Add(nameBox);
            hero.Controls.Add(commentTitle);
            hero.Controls.Add(commentBox);

            Panel progression =
                CreateSkillSection(
                    "PROGRESSION / COST",
                    "Level progression, resource cost and skill metadata.",
                    276,
                    254,
                    content.Width);

            content.Controls.Add(progression);

            AddSkillField(
                state,
                progression,
                "Level Up Point",
                "s_nLevelupPoint",
                16,
                58,
                210);

            AddSkillField(
                state,
                progression,
                "Max Level",
                "s_nMaxLevel",
                242,
                58,
                210);

            AddSkillField(
                state,
                progression,
                "Required Digimon Level",
                "s_nLimitLevel",
                468,
                58,
                210);

            AddSkillField(
                state,
                progression,
                "Use HP",
                "s_nUseHP",
                16,
                130,
                210);

            AddSkillField(
                state,
                progression,
                "Use DS",
                "s_nUseDS",
                242,
                130,
                210);

            AddSkillField(
                state,
                progression,
                "Skill Rank",
                "s_nSkillRank",
                468,
                130,
                210);

            AddSkillField(
                state,
                progression,
                "Skill Group",
                "s_nSkillGroup",
                16,
                202,
                210);

            AddSkillField(
                state,
                progression,
                "Memory Skill",
                "s_nMemorySkill",
                242,
                202,
                210);

            AddSkillField(
                state,
                progression,
                "Required Item / Mode",
                "s_nReq_Item",
                468,
                202,
                210);

            Panel classification =
                CreateSkillSection(
                    "CLASSIFICATION",
                    "Raw classification values preserved from Skill.xml.",
                    546,
                    184,
                    content.Width);

            content.Controls.Add(classification);

            AddSkillField(
                state,
                classification,
                "Attribute Type",
                "s_nAttributeType",
                16,
                58,
                210);

            AddSkillField(
                state,
                classification,
                "Nature Type",
                "s_nNatureType",
                242,
                58,
                210);

            AddSkillField(
                state,
                classification,
                "Family Type",
                "s_nFamilyType",
                468,
                58,
                210);

            AddSkillField(
                state,
                classification,
                "Skill Type",
                "s_nSkillType",
                16,
                130,
                210);

            AddSkillField(
                state,
                classification,
                "Target",
                "s_nTarget",
                242,
                130,
                210);

            AddSkillField(
                state,
                classification,
                "Attack Type",
                "s_nAttType",
                468,
                130,
                210);

            Panel combat =
                CreateSkillSection(
                    "COMBAT / RANGE / TIMING",
                    "Attack geometry, cast/damage timing, cooldown and projectile movement.",
                    746,
                    344,
                    content.Width);

            content.Controls.Add(combat);

            string[] combatFields =
            {
                "s_fAttRange",
                "s_fAttRange_MinDmg",
                "s_fAttRange_NorDmg",
                "s_fAttRange_MaxDmg",
                "s_nAttSphere",
                "s_fCastingTime",
                "s_fDamageTime",
                "s_nDamageDay",
                "s_nDistanceTime",
                "s_fCooldownTime",
                "s_nCooldownDay",
                "s_fSkill_Velocity",
                "s_fSkill_Accel"
            };

            string[] combatLabels =
            {
                "Attack Range",
                "Min Damage Range",
                "Normal Damage Range",
                "Max Damage Range",
                "Attack Sphere",
                "Casting Time",
                "Damage Time",
                "Damage Day",
                "Distance Time",
                "Cooldown Time",
                "Cooldown Day",
                "Skill Velocity",
                "Skill Acceleration"
            };

            for (int i = 0; i < combatFields.Length; i++)
            {
                int col = i % 3;
                int row = i / 3;

                AddSkillField(
                    state,
                    combat,
                    combatLabels[i],
                    combatFields[i],
                    16 + col * 226,
                    58 + row * 64,
                    210);
            }

            Panel effects =
                CreateSkillSection(
                    "SKILL APPLY / EFFECTS",
                    "The supplied Skill.xml contains exactly three IncreaseApply records for every skill.",
                    1106,
                    500,
                    content.Width);

            content.Controls.Add(effects);

            XElement apply =
                working.Element("SkillApply") ??
                new XElement("SkillApply");

            if (apply.Parent == null)
                working.AddFirst(apply);

            List<XElement> effectNodes =
                apply.Elements("IncreaseApply")
                    .ToList();

            while (effectNodes.Count < 3)
            {
                var created =
                    new XElement(
                        "IncreaseApply",
                        new XElement("s_nA", 0),
                        new XElement("s_nInvoke_Rate", 0),
                        new XElement("s_nB", 0),
                        new XElement("s_nC", 0),
                        new XElement("s_nBuffCode", 0),
                        new XElement("s_nID", 0),
                        new XElement("s_nIncrease_B_Point", 0));

                apply.Add(created);
                effectNodes.Add(created);
            }

            int effectGap = 12;
            int effectCardWidth =
                Math.Max(
                    190,
                    (effects.ClientSize.Width - 32 - effectGap * 2) / 3);

            for (int i = 0; i < 3; i++)
            {
                effects.Controls.Add(
                    CreateSkillEffectCard(
                        state,
                        effectNodes[i],
                        i,
                        16 + i * (effectCardWidth + effectGap),
                        58,
                        effectCardWidth,
                        420));
            }

            Panel advanced =
                CreateSkillSection(
                    "ADVANCED / UNKNOWN",
                    "Fields whose gameplay meaning is not safely established are exposed without inventing semantics.",
                    1622,
                    164,
                    content.Width);

            content.Controls.Add(advanced);

            AddSkillField(
                state,
                advanced,
                "ink",
                "ink",
                16,
                62,
                210);

            AddSkillField(
                state,
                advanced,
                "unk",
                "unk",
                242,
                62,
                210);

            AddSkillField(
                state,
                advanced,
                "unk2",
                "unk2",
                468,
                62,
                210);

            void MarkDirty()
            {
                state.Dirty = true;
                dirty.Text = "UNSAVED CHANGES";
                dirty.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);
            }

            foreach (TextBox box
                     in state.Fields.Values
                         .Concat(
                             new[]
                             {
                                 idBox,
                                 nameBox,
                                 commentBox
                             }))
            {
                box.TextChanged +=
                    (_, _) => MarkDirty();
            }

            foreach (TextBox box in state.EffectFields)
            {
                box.TextChanged +=
                    (_, _) => MarkDirty();
            }

            idBox.TextChanged +=
                (_, _) =>
                    UpdateSkillIdStatus(state);

            chooseIcon.Click +=
                async (_, _) =>
                {
                    uint current =
                        ParseSkillUInt(
                            SkillEditorService.S(
                                state.Working,
                                "s_nIcon"));

                    uint? selected =
                        await OpenSkillIconPickerAsync(
                            state,
                            current);

                    if (!selected.HasValue)
                        return;

                    SkillEditorService.Set(
                        state.Working,
                        "s_nIcon",
                        selected.Value.ToString(
                            CultureInfo.InvariantCulture));

                    state.IconIdLabel.Text =
                        $"Icon ID: {selected.Value}";

                    Image? old =
                        state.Icon.Image;

                    state.Icon.Image =
                        await Task.Run(
                            () =>
                                state.Service.TryLoadIcon(
                                    selected.Value));

                    old?.Dispose();
                    MarkDirty();
                };

            raw.Click +=
                (_, _) =>
                    OpenRawBlockTab(
                        state.Service.FilePath,
                        new XElement(state.Working));

            save.Click +=
                (_, _) =>
                {
                    if (SaveSkillEditor(
                        state,
                        showSuccess: true))
                    {
                        dirty.Text = "SAVED";
                        dirty.ForeColor =
                            Color.FromArgb(
                                125,
                                220,
                                140);
                    }
                };

            var editorLayout =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18, 18, 18),
                    ColumnCount = 1,
                    RowCount = 2,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };

            editorLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    100F));

            editorLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    72F));

            editorLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100F));

            editorLayout.Controls.Add(top, 0, 0);
            editorLayout.Controls.Add(body, 0, 1);

            page.Controls.Add(editorLayout);
            editorLayout.BringToFront();

            loading.Dispose();

            ResizeSkillEditorContent();
            UpdateSkillIdStatus(state);
            RefreshSkillEditorIcon(state);
        }

        private Panel CreateSkillSection(
            string title,
            string subtitle,
            int top,
            int height,
            int width)
        {
            var panel =
                new Panel
                {
                    Location = new Point(0, top),
                    Size = new Size(width, height),
                    BackColor = Color.FromArgb(27, 27, 27),
                    BorderStyle = BorderStyle.FixedSingle,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            var heading =
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        9.5F,
                        FontStyle.Bold),
                    Location = new Point(14, 10),
                    AutoSize = true
                };

            var help =
                new Label
                {
                    Text = subtitle,
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.5F),
                    Location = new Point(14, 31),
                    AutoSize = true
                };

            panel.Controls.Add(heading);
            panel.Controls.Add(help);

            return panel;
        }

        private Label CreateSkillFieldLabel(
            string text,
            int x,
            int y) =>
            new Label
            {
                Text = text,
                ForeColor = CText,
                Font = new Font(
                    "Segoe UI Semibold",
                    8F),
                Location = new Point(x, y),
                AutoSize = true
            };

        private TextBox CreateSkillTextBox(
            string value) =>
            new TextBox
            {
                Text = value,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 8.5F),
                Height = 26
            };

        private void AddSkillField(
            SkillEditState state,
            Panel owner,
            string label,
            string xmlName,
            int x,
            int y,
            int width)
        {
            owner.Controls.Add(
                CreateSkillFieldLabel(
                    label,
                    x,
                    y));

            var box =
                CreateSkillTextBox(
                    SkillEditorService.S(
                        state.Working,
                        xmlName));

            box.Location =
                new Point(
                    x,
                    y + 20);

            box.Width = width;

            owner.Controls.Add(box);
            state.Fields[xmlName] = box;
        }

        private Panel CreateSkillEffectCard(
            SkillEditState state,
            XElement effect,
            int index,
            int x,
            int y,
            int width,
            int height)
        {
            var card =
                new Panel
                {
                    Location = new Point(x, y),
                    Size = new Size(width, height),
                    BackColor = Color.FromArgb(20, 20, 20),
                    BorderStyle = BorderStyle.FixedSingle
                };

            var title =
                new Label
                {
                    Text = $"EFFECT SLOT {index + 1}",
                    ForeColor =
                        Color.FromArgb(
                            125,
                            220,
                            140),
                    Font = new Font(
                        "Segoe UI Semibold",
                        8.8F,
                        FontStyle.Bold),
                    Location = new Point(10, 10),
                    AutoSize = true
                };

            card.Controls.Add(title);

            string[] names =
            {
                "s_nA",
                "s_nInvoke_Rate",
                "s_nB",
                "s_nC"
            };

            string[] labels =
            {
                "A",
                "Invoke Rate",
                "B",
                "C"
            };

            for (int i = 0; i < names.Length; i++)
            {
                int top =
                    42 + i * 42;

                var label =
                    new Label
                    {
                        Text = labels[i],
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 7.4F),
                        Location = new Point(10, top),
                        Size = new Size(82, 18)
                    };

                var box =
                    CreateSkillTextBox(
                        effect.Element(names[i])?.Value
                        ?? "0");

                box.Location =
                    new Point(
                        94,
                        top - 2);

                box.Size =
                    new Size(
                        Math.Max(68, width - 104),
                        24);

                box.Tag =
                    new Tuple<XElement, string>(
                        effect,
                        names[i]);

                if (names[i] == "s_nInvoke_Rate")
                {
                    var pct =
                        new Label
                        {
                            ForeColor =
                                Color.FromArgb(
                                    100,
                                    200,
                                    255),
                            Font = new Font(
                                "Segoe UI",
                                7F),
                            Location = new Point(
                                10,
                                top + 19),
                            Size = new Size(
                                width - 20,
                                17)
                        };

                    void UpdatePercent()
                    {
                        if (double.TryParse(
                            box.Text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double raw))
                        {
                            pct.Text =
                                $"≈ {raw / 100.0:0.##}%";
                        }
                        else
                        {
                            pct.Text = string.Empty;
                        }
                    }

                    box.TextChanged +=
                        (_, _) =>
                            UpdatePercent();

                    UpdatePercent();
                    card.Controls.Add(pct);
                }

                card.Controls.Add(label);
                card.Controls.Add(box);
                state.EffectFields.Add(box);
            }

            int buffTop = 218;

            var buffLabel =
                new Label
                {
                    Text = "Buff / Bonus Code",
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        7.8F),
                    Location = new Point(10, buffTop),
                    AutoSize = true
                };

            var buffCode =
                CreateSkillTextBox(
                    effect.Element("s_nBuffCode")?.Value
                    ?? "0");

            buffCode.Location =
                new Point(
                    10,
                    buffTop + 22);

            buffCode.Width =
                Math.Max(
                    86,
                    width - 20);

            buffCode.Tag =
                new Tuple<XElement, string>(
                    effect,
                    "s_nBuffCode");

            var buffStatus =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7F),
                    Location = new Point(
                        10,
                        buffTop + 50),
                    Size = new Size(
                        width - 20,
                        34),
                    AutoEllipsis = true
                };

            var selectBuff =
                CreateEditorActionButton(
                    "SELECT BUFF");

            selectBuff.Location =
                new Point(
                    10,
                    buffTop + 88);

            selectBuff.Size =
                new Size(
                    Math.Max(
                        90,
                        width - 20),
                    28);

            card.Controls.Add(buffLabel);
            card.Controls.Add(buffCode);
            card.Controls.Add(buffStatus);
            card.Controls.Add(selectBuff);

            state.EffectFields.Add(buffCode);
            state.BuffStatusLabels[effect] =
                buffStatus;

            int effectIdTop =
                buffTop + 128;

            var effectIdLabel =
                new Label
                {
                    Text = "Effect ID",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.4F),
                    Location = new Point(10, effectIdTop),
                    Size = new Size(82, 18)
                };

            var effectId =
                CreateSkillTextBox(
                    effect.Element("s_nID")?.Value
                    ?? "0");

            effectId.Location =
                new Point(
                    94,
                    effectIdTop - 2);

            effectId.Size =
                new Size(
                    Math.Max(68, width - 104),
                    24);

            effectId.Tag =
                new Tuple<XElement, string>(
                    effect,
                    "s_nID");

            var increaseLabel =
                new Label
                {
                    Text = "Increase B",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.4F),
                    Location = new Point(
                        10,
                        effectIdTop + 38),
                    Size = new Size(82, 18)
                };

            var increase =
                CreateSkillTextBox(
                    effect.Element("s_nIncrease_B_Point")?.Value
                    ?? "0");

            increase.Location =
                new Point(
                    94,
                    effectIdTop + 36);

            increase.Size =
                new Size(
                    Math.Max(68, width - 104),
                    24);

            increase.Tag =
                new Tuple<XElement, string>(
                    effect,
                    "s_nIncrease_B_Point");

            card.Controls.Add(effectIdLabel);
            card.Controls.Add(effectId);
            card.Controls.Add(increaseLabel);
            card.Controls.Add(increase);

            state.EffectFields.Add(effectId);
            state.EffectFields.Add(increase);

            void RefreshBuffStatus()
            {
                RefreshSkillBuffStatus(
                    state,
                    effect,
                    buffCode,
                    buffStatus);
            }

            buffCode.TextChanged +=
                (_, _) =>
                    RefreshBuffStatus();

            selectBuff.Click +=
                async (_, _) =>
                {
                    BuffReferenceRecord? selected =
                        await OpenSkillBuffPickerAsync(
                            state,
                            ParseSkillUInt(
                                buffCode.Text));

                    if (selected == null)
                        return;

                    buffCode.Text =
                        selected.Id.ToString(
                            CultureInfo.InvariantCulture);

                    RefreshBuffStatus();
                };

            RefreshBuffStatus();

            return card;
        }

        private void RefreshSkillBuffStatus(
            SkillEditState state,
            XElement effect,
            TextBox codeBox,
            Label status)
        {
            uint code =
                ParseSkillUInt(
                    codeBox.Text);

            if (code == 0)
            {
                status.Text =
                    "No buff / no bonus code";
                status.ForeColor = CMuted;
                return;
            }

            BuffReferenceService? buffs =
                EditorPreloadService
                    .TryGetBuffReferences();

            BuffReferenceRecord? buff =
                buffs?.FindById(code);

            if (buff != null)
            {
                status.Text =
                    $"Buff.xml: {buff.Name}  •  Icon {buff.IconId}";
                status.ForeColor =
                    Color.FromArgb(
                        125,
                        220,
                        140);
            }
            else
            {
                status.Text =
                    "Raw bonus code — no Buff.xml ID match";
                status.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);
            }
        }

        private async Task<BuffReferenceRecord?> OpenSkillBuffPickerAsync(
            SkillEditState owner,
            uint currentCode)
        {
            BuffReferenceService? service =
                await EditorPreloadService
                    .GetBuffReferencesAsync();

            if (service == null)
            {
                MessageBox.Show(
                    "Buff.xml was not found under XML\\Buff\\Buff.xml.\\n\\n" +
                    "You can still type the raw bonus code manually.",
                    "Skill Buff Selector",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return null;
            }

            var completion =
                new TaskCompletionSource<BuffReferenceRecord?>();

            var overlay =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor =
                        Color.FromArgb(
                            18,
                            18,
                            18)
                };

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 118,
                    BackColor =
                        Color.FromArgb(
                            24,
                            24,
                            24)
                };

            var title =
                new Label
                {
                    Text =
                        "Select Buff — Buff.xml + sicon01..sicon07",
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        12F,
                        FontStyle.Bold),
                    Location = new Point(16, 12),
                    AutoSize = true
                };

            var help =
                new Label
                {
                    Text =
                        "Selecting a Buff stores its BuffData s_dwID in s_nBuffCode. " +
                        "For internal bonus codes such as 51/13/32, close this selector and type the raw code directly.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.7F),
                    Location = new Point(16, 38),
                    AutoSize = true
                };

            var search =
                new TextBox
                {
                    PlaceholderText =
                        "Search Buff ID, name, description, class or icon...",
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Location = new Point(16, 72),
                    Height = 28,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            var close =
                CreateEditorActionButton("CLOSE");

            close.Size = new Size(104, 30);
            close.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            void LayoutPickerHeader()
            {
                close.Left =
                    header.ClientSize.Width -
                    close.Width -
                    16;

                close.Top = 68;

                search.Width =
                    Math.Max(
                        250,
                        close.Left -
                        search.Left -
                        10);
            }

            header.Resize +=
                (_, _) =>
                    LayoutPickerHeader();

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    WrapContents = false,
                    FlowDirection =
                        FlowDirection.TopDown,
                    BackColor =
                        Color.FromArgb(
                            18,
                            18,
                            18),
                    Padding =
                        new Padding(
                            12,
                            12,
                            22,
                            34)
                };

            DarkUi.ApplyDarkScrollBar(results);

            void ClosePicker(
                BuffReferenceRecord? value)
            {
                completion.TrySetResult(value);

                if (!overlay.IsDisposed)
                {
                    owner.Page.Controls.Remove(
                        overlay);
                    overlay.Dispose();
                }
            }

            close.Click +=
                (_, _) =>
                    ClosePicker(null);

            void Render()
            {
                IReadOnlyList<BuffReferenceRecord> found =
                    service.Search(
                        search.Text);

                results.SuspendLayout();

                foreach (Control c
                         in results.Controls
                             .Cast<Control>()
                             .ToArray())
                {
                    results.Controls.Remove(c);
                    c.Dispose();
                }

                foreach (BuffReferenceRecord buff
                         in found.Take(80))
                {
                    int width =
                        Math.Max(
                            620,
                            results.ClientSize.Width -
                            40);

                    var card =
                        new Panel
                        {
                            Width = width,
                            Height = 92,
                            BackColor =
                                Color.FromArgb(
                                    29,
                                    29,
                                    29),
                            BorderStyle =
                                BorderStyle.FixedSingle,
                            Margin =
                                new Padding(
                                    2,
                                    0,
                                    2,
                                    8)
                        };

                    var icon =
                        new PictureBox
                        {
                            Location =
                                new Point(
                                    10,
                                    10),
                            Size =
                                new Size(
                                    64,
                                    64),
                            BackColor =
                                Color.Black,
                            SizeMode =
                                PictureBoxSizeMode.Zoom
                        };

                    _ = LoadBuffIconIntoAsync(
                        service,
                        buff,
                        icon);

                    var name =
                        new Label
                        {
                            Text =
                                $"{buff.Id} — {buff.Name}",
                            ForeColor =
                                buff.Id == currentCode
                                    ? Color.FromArgb(
                                        125,
                                        220,
                                        140)
                                    : CText,
                            Font = new Font(
                                "Segoe UI Semibold",
                                9F),
                            Location =
                                new Point(
                                    86,
                                    10),
                            Size =
                                new Size(
                                    Math.Max(
                                        280,
                                        width - 240),
                                    22),
                            AutoEllipsis = true
                        };

                    var meta =
                        new Label
                        {
                            Text =
                                $"Icon {buff.IconId}  •  Class {buff.BuffClass}  •  " +
                                $"Type {buff.BuffType}  •  SkillCode {buff.SkillCode}",
                            ForeColor =
                                Color.FromArgb(
                                    125,
                                    220,
                                    140),
                            Font =
                                new Font(
                                    "Segoe UI",
                                    7.5F),
                            Location =
                                new Point(
                                    86,
                                    34),
                            Size =
                                new Size(
                                    Math.Max(
                                        280,
                                        width - 240),
                                    18)
                        };

                    var comment =
                        new Label
                        {
                            Text =
                                string.IsNullOrWhiteSpace(
                                    buff.Comment)
                                    ? "No description."
                                    : buff.Comment,
                            ForeColor = CMuted,
                            Font =
                                new Font(
                                    "Segoe UI",
                                    7.2F),
                            Location =
                                new Point(
                                    86,
                                    54),
                            Size =
                                new Size(
                                    Math.Max(
                                        280,
                                        width - 240),
                                    28),
                            AutoEllipsis = true
                        };

                    var select =
                        CreateEditorActionButton(
                            "SELECT");

                    select.Size =
                        new Size(
                            96,
                            32);

                    select.Location =
                        new Point(
                            width - 112,
                            28);

                    select.Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Right;

                    select.Click +=
                        (_, _) =>
                            ClosePicker(buff);

                    card.Controls.Add(icon);
                    card.Controls.Add(name);
                    card.Controls.Add(meta);
                    card.Controls.Add(comment);
                    card.Controls.Add(select);
                    results.Controls.Add(card);
                }

                results.ResumeLayout(true);
                ResetSkillScrollToTop(results);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 150
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            header.Controls.Add(title);
            header.Controls.Add(help);
            header.Controls.Add(search);
            header.Controls.Add(close);

            overlay.Controls.Add(results);
            overlay.Controls.Add(header);

            owner.Page.Controls.Add(overlay);
            overlay.BringToFront();

            LayoutPickerHeader();
            Render();
            search.Focus();

            return await completion.Task;
        }

        private async Task LoadBuffIconIntoAsync(
            BuffReferenceService service,
            BuffReferenceRecord buff,
            PictureBox box)
        {
            Bitmap? image =
                await Task.Run(
                    () =>
                        service.TryLoadIcon(
                            buff));

            if (box.IsDisposed)
            {
                image?.Dispose();
                return;
            }

            box.Image = image;
        }

        private bool SaveSkillEditor(
            SkillEditState state,
            bool showSuccess)
        {
            if (!uint.TryParse(
                    state.Id.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint newId) ||
                newId == 0)
            {
                MessageBox.Show(
                    "Skill ID must be a valid UInt32 greater than zero.",
                    "Skill Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return false;
            }

            uint oldId =
                SkillEditorService.U(
                    state.Original,
                    "s_dwID");

            if (newId != oldId &&
                !state.Service.IsIdAvailable(
                    newId,
                    state.Original))
            {
                DialogResult duplicate =
                    MessageBox.Show(
                        $"Skill ID {newId} already exists in Skill.xml.\n\n" +
                        "The supplied Skill.xml already contains a small number of duplicate IDs, " +
                        "so duplicates are not globally forbidden. Do you intentionally want another duplicate?",
                        "Duplicate Skill ID",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                if (duplicate != DialogResult.Yes)
                    return false;
            }

            SkillEditorService.Set(
                state.Working,
                "s_dwID",
                newId.ToString(
                    CultureInfo.InvariantCulture));

            SkillEditorService.Set(
                state.Working,
                "s_szName",
                state.Name.Text);

            SkillEditorService.Set(
                state.Working,
                "s_szComment",
                state.Comment.Text);

            foreach ((string key, TextBox box)
                     in state.Fields)
            {
                SkillEditorService.Set(
                    state.Working,
                    key,
                    box.Text.Trim());
            }

            foreach (TextBox box
                     in state.EffectFields)
            {
                if (box.Tag is not Tuple<XElement, string> binding)
                    continue;

                SkillEditorService.Set(
                    binding.Item1,
                    binding.Item2,
                    box.Text.Trim());
            }

            if (state.IsNew)
            {
                // CreateDefaultSkill already inserted the original node.
                state.Original.ReplaceWith(
                    new XElement(state.Working));

                state.Original =
                    state.Service.Root
                        .Elements("SkillData")
                        .Last();
            }
            else
            {
                state.Original.ReplaceWith(
                    new XElement(state.Working));

                state.Original =
                    state.Service.Root
                        .Elements("SkillData")
                        .FirstOrDefault(
                            x =>
                                SkillEditorService.U(
                                    x,
                                    "s_dwID") == newId &&
                                SkillEditorService.S(
                                    x,
                                    "s_szName") ==
                                state.Name.Text)
                    ?? state.Service.Root
                        .Elements("SkillData")
                        .Last();
            }

            state.Service.Reindex();
            state.Service.Save();

            EditorPreloadService.ReplaceSkillEditor(
                state.Service.FilePath,
                state.Service);

            EditorPreloadService.InvalidateSkillReferences();

            state.Working =
                new XElement(
                    state.Original);

            state.Dirty = false;
            state.IsNew = false;

            if (showSuccess)
            {
                MessageBox.Show(
                    $"Skill {newId} saved successfully.",
                    "Skill Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            RefreshOpenSkillBrowser(
                state.Service);

            return true;
        }

        private void RefreshOpenSkillBrowser(
            SkillEditorService service)
        {
            foreach (TabPage page
                     in editorTabs.TabPages)
            {
                if (page.Tag is SkillBrowseState browse &&
                    ReferenceEquals(
                        browse.Service,
                        service))
                {
                    RefreshSkillBrowser(browse);
                }
            }
        }

        private void UpdateSkillIdStatus(
            SkillEditState state)
        {
            if (!uint.TryParse(
                    state.Id.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint id) ||
                id == 0)
            {
                state.IdStatus.Text =
                    "INVALID ID";

                state.IdStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        95,
                        95);

                return;
            }

            uint original =
                SkillEditorService.U(
                    state.Original,
                    "s_dwID");

            int count =
                state.Service.CountId(id);

            if (id == original)
            {
                state.IdStatus.Text =
                    count > 1
                        ? $"EXISTING ID — {count} definitions in source XML"
                        : "CURRENT ID";

                state.IdStatus.ForeColor =
                    count > 1
                        ? Color.FromArgb(255, 190, 90)
                        : Color.FromArgb(125, 220, 140);

                return;
            }

            bool available =
                state.Service.IsIdAvailable(
                    id,
                    state.Original);

            state.IdStatus.Text =
                available
                    ? "ID AVAILABLE"
                    : $"ID EXISTS — {count} definition(s)";

            state.IdStatus.ForeColor =
                available
                    ? Color.FromArgb(125, 220, 140)
                    : Color.FromArgb(255, 190, 90);
        }

        private async void RefreshSkillEditorIcon(
            SkillEditState state)
        {
            uint iconId =
                SkillEditorService.U(
                    state.Working,
                    "s_nIcon");

            state.IconIdLabel.Text =
                $"Icon ID: {iconId}";

            Image? previous =
                state.Icon.Image;

            state.Icon.Image =
                await Task.Run(
                    () =>
                        state.Service.TryLoadIcon(
                            iconId));

            previous?.Dispose();
        }

        private async Task LoadSkillIconIntoAsync(
            SkillEditorService service,
            uint iconId,
            PictureBox box)
        {
            if (iconId == 0)
                return;

            Bitmap? image =
                await Task.Run(
                    () =>
                        service.TryLoadIcon(
                            iconId));

            if (box.IsDisposed)
            {
                image?.Dispose();
                return;
            }

            box.Image = image;
        }

        private async Task<uint?> OpenSkillIconPickerAsync(
            SkillEditState owner,
            uint current)
        {
            var completion =
                new TaskCompletionSource<uint?>();

            var page =
                CreateDarkTab("Select Skill Icon");

            page.Name =
                $"skill-icon-picker:{owner.Page.Name}:{Guid.NewGuid():N}";

            var loading =
                new EditorLoadingView(
                    "Loading Skill Icons",
                    "Preparing unique s_nIcon values and DDS/atlas previews.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (page.IsDisposed)
                        {
                            completion.TrySetResult(null);
                            return;
                        }

                        BuildSkillIconPicker(
                            page,
                            owner.Service,
                            current,
                            completion,
                            loading);
                    }));

            return await completion.Task;
        }

        private void BuildSkillIconPicker(
            TabPage page,
            SkillEditorService service,
            uint current,
            TaskCompletionSource<uint?> completion,
            Control loading)
        {
            var root =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18, 18, 18),
                    Padding = new Padding(16)
                };

            var title =
                new Label
                {
                    Text = "Skill Icon Browser",
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        12F,
                        FontStyle.Bold),
                    Location = new Point(16, 12),
                    AutoSize = true
                };

            var help =
                new Label
                {
                    Text =
                        "Icons are resolved from s_nIcon through the Skill/sicon atlas database. " +
                        "DDS atlases are decoded by DdsImageLoader when required.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.7F),
                    Location = new Point(16, 39),
                    AutoSize = true
                };

            var search =
                new TextBox
                {
                    PlaceholderText = "Search icon ID...",
                    BackColor = Color.FromArgb(10, 10, 10),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(16, 70),
                    Size = new Size(360, 28)
                };

            var manual =
                new TextBox
                {
                    Text =
                        current.ToString(
                            CultureInfo.InvariantCulture),
                    BackColor = Color.FromArgb(10, 10, 10),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(390, 70),
                    Size = new Size(150, 28)
                };

            var useManual =
                CreateEditorActionButton("USE ICON ID");

            useManual.Location = new Point(550, 68);
            useManual.Size = new Size(116, 32);

            var results =
                new FlowLayoutPanel
                {
                    Location = new Point(16, 112),
                    Size = new Size(
                        Math.Max(
                            650,
                            page.ClientSize.Width - 48),
                        Math.Max(
                            420,
                            page.ClientSize.Height - 144)),
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Bottom |
                        AnchorStyles.Left |
                        AnchorStyles.Right,
                    AutoScroll = true,
                    WrapContents = true,
                    BackColor = Color.FromArgb(18, 18, 18),
                    Padding = new Padding(4, 4, 18, 30)
                };

            DarkUi.ApplyDarkScrollBar(results);

            List<uint> all =
                service.IconIds.ToList();

            void Choose(uint id)
            {
                completion.TrySetResult(id);
                editorTabs.TabPages.Remove(page);
                page.Dispose();
            }

            void Render()
            {
                string query =
                    search.Text.Trim();

                IEnumerable<uint> filtered =
                    all;

                if (query.Length != 0)
                {
                    filtered =
                        filtered.Where(
                            x =>
                                x.ToString(
                                    CultureInfo.InvariantCulture)
                                    .Contains(
                                        query,
                                        StringComparison.OrdinalIgnoreCase));
                }

                results.SuspendLayout();

                foreach (Control c
                         in results.Controls
                             .Cast<Control>()
                             .ToArray())
                {
                    results.Controls.Remove(c);
                    c.Dispose();
                }

                foreach (uint id
                         in filtered.Take(160))
                {
                    var card =
                        new Panel
                        {
                            Size = new Size(112, 128),
                            BackColor = Color.FromArgb(27, 27, 27),
                            BorderStyle = BorderStyle.FixedSingle,
                            Margin = new Padding(4)
                        };

                    var icon =
                        new PictureBox
                        {
                            Location = new Point(24, 10),
                            Size = new Size(64, 64),
                            BackColor = Color.Black,
                            SizeMode = PictureBoxSizeMode.Zoom
                        };

                    _ = LoadSkillIconIntoAsync(
                        service,
                        id,
                        icon);

                    var label =
                        new Label
                        {
                            Text = id.ToString(
                                CultureInfo.InvariantCulture),
                            ForeColor =
                                id == current
                                    ? Color.FromArgb(125, 220, 140)
                                    : CText,
                            Font = new Font("Segoe UI", 8F),
                            TextAlign = ContentAlignment.MiddleCenter,
                            Location = new Point(4, 78),
                            Size = new Size(104, 18)
                        };

                    var select =
                        CreateEditorActionButton("SELECT");

                    select.Location = new Point(14, 98);
                    select.Size = new Size(84, 24);
                    select.Click +=
                        (_, _) => Choose(id);

                    card.Controls.Add(icon);
                    card.Controls.Add(label);
                    card.Controls.Add(select);
                    results.Controls.Add(card);
                }

                results.ResumeLayout(true);
                ResetSkillScrollToTop(results);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 150
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            useManual.Click +=
                (_, _) =>
                {
                    if (uint.TryParse(
                        manual.Text.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out uint id))
                    {
                        Choose(id);
                    }
                };

            root.Controls.Add(title);
            root.Controls.Add(help);
            root.Controls.Add(search);
            root.Controls.Add(manual);
            root.Controls.Add(useManual);
            root.Controls.Add(results);

            page.Controls.Add(root);
            root.BringToFront();
            loading.Dispose();

            Render();
        }

        private static uint ParseSkillUInt(
            string value) =>
            uint.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result)
                    ? result
                    : 0;

        private static string FormatSkillTime(
            float value)
        {
            if (Math.Abs(value) < 0.001F)
                return "0";

            return value.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        private static void ResetSkillScrollToTop(
            ScrollableControl control)
        {
            if (control.IsDisposed)
                return;

            control.AutoScrollPosition = Point.Empty;

            if (control.IsHandleCreated)
            {
                control.BeginInvoke(
                    new Action(
                        () =>
                        {
                            if (!control.IsDisposed)
                                control.AutoScrollPosition =
                                    Point.Empty;
                        }));
            }
        }
    }
}
