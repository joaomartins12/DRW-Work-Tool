using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using DRW_Work_Tool.Core;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private const int BuffCardsPerPage = 22;

        private sealed class BuffBrowseState
        {
            public TabPage Page = null!;
            public BuffEditorService Service = null!;
            public FlowLayoutPanel Results = null!;
            public TextBox Search = null!;
            public DarkComboBox BuffType = null!;
            public DarkComboBox LifeType = null!;
            public DarkComboBox TimeType = null!;
            public Label Count = null!;
            public Label PageInfo = null!;
            public Button Previous = null!;
            public Button Next = null!;
            public Button FilterButton = null!;
            public Panel FilterPanel = null!;
            public bool FiltersVisible;
            public int PageIndex;
            public IReadOnlyList<BuffEditorRecord> Filtered =
                Array.Empty<BuffEditorRecord>();
        }

        private sealed class BuffEditState
        {
            public TabPage Page = null!;
            public BuffEditorService Service = null!;
            public XElement Original = null!;
            public XElement Working = null!;
            public int OriginalPhysicalIndex;
            public uint OriginalId;
            public bool IsNew;
            public bool Dirty;
            public Dictionary<string, TextBox> Fields = new();
            public PictureBox Icon = null!;
            public Label IdStatus = null!;
        }

        private sealed class BuffSkillPickerState
        {
            public SkillEditorService Service = null!;
            public TextBox Search = null!;
            public FlowLayoutPanel Results = null!;
            public Label Count = null!;
            public Label PageInfo = null!;
            public Button Previous = null!;
            public Button Next = null!;
            public int PageIndex;
            public IReadOnlyList<SkillEditorRecord> Filtered =
                Array.Empty<SkillEditorRecord>();
            public Action<uint> Select = null!;
        }

        private async void OpenBuffBrowser(
            string xmlPath)
        {
            string fullPath =
                Path.GetFullPath(
                    xmlPath);

            var page =
                CreateDarkTab(
                    "Buff.xml");

            page.Name = fullPath;

            var loading =
                new EditorLoadingView(
                    "Loading Buff Database",
                    "Preparing Buff.xml records, indexes, filters, skill references and icon previews.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            await Task.Yield();

            try
            {
                BuffEditorService service =
                    await EditorPreloadService
                        .GetBuffEditorAsync(
                            fullPath);

                if (page.IsDisposed)
                    return;

                BuildBuffBrowser(
                    page,
                    service);
            }
            catch (Exception ex)
            {
                if (page.IsDisposed)
                    return;

                page.Controls.Clear();
                page.Controls.Add(
                    CreateInfoLabel(
                        "Buff.xml could not be loaded.\r\n\r\n" +
                        ex.Message));

                AppLogger.ErrorDetailed(
                    "Buff Editor",
                    ex.Message,
                    "Verify XML\\Buff\\Buff.xml and the image database/index.");
            }
        }

        private void BuildBuffBrowser(
            TabPage page,
            BuffEditorService service)
        {
            page.SuspendLayout();
            page.Controls.Clear();

            var root =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    Padding = new Padding(
                        18,
                        14,
                        18,
                        14)
                };

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 170,
                    BackColor = CEditor
                };

            var title =
                new Label
                {
                    Text = "Buff Database",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            14F,
                            FontStyle.Bold),
                    Location = new Point(12, 4),
                    AutoSize = true
                };

            var subtitle =
                new Label
                {
                    Text =
                        $"Buff.xml  •  {service.Count:N0} BuffData  •  Buff/Debuff visual editor",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8.5F),
                    Location = new Point(14, 35),
                    AutoSize = true
                };

            var newBuff =
                CreateEditorActionButton(
                    "NEW BUFF");

            newBuff.Size =
                new Size(
                    146,
                    34);

            newBuff.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var search =
                new TextBox
                {
                    PlaceholderText =
                        "Search Buff ID, name, comment, SkillCode, DigimonSkillCode, effect or icon ID...",
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Location =
                        new Point(
                            12,
                            70),
                    Height = 28,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            var filter =
                CreateEditorActionButton(
                    "FILTERS");

            filter.Size =
                new Size(
                    110,
                    30);

            filter.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var filterPanel =
                new Panel
                {
                    BackColor =
                        Color.FromArgb(
                            25,
                            25,
                            25),
                    Height = 44,
                    Visible = false,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left |
                        AnchorStyles.Right
                };

            var buffType =
                CreateBuffFilterCombo(
                    "All Buff Types",
                    service.BuffTypes);

            var lifeType =
                CreateBuffFilterCombo(
                    "All Life Types",
                    service.LifeTypes);

            var timeType =
                CreateBuffFilterCombo(
                    "All Time Types",
                    service.TimeTypes);

            buffType.Location =
                new Point(
                    8,
                    8);

            lifeType.Location =
                new Point(
                    158,
                    8);

            timeType.Location =
                new Point(
                    308,
                    8);

            var reset =
                CreateEditorActionButton(
                    "RESET");

            reset.Location =
                new Point(
                    458,
                    8);

            reset.Size =
                new Size(
                    88,
                    28);

            filterPanel.Controls.Add(
                buffType);

            filterPanel.Controls.Add(
                lifeType);

            filterPanel.Controls.Add(
                timeType);

            filterPanel.Controls.Add(
                reset);

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8.4F),
                    Location =
                        new Point(
                            14,
                            119),
                    AutoSize = true
                };

            var previous =
                CreateEditorActionButton(
                    "◀ PREVIOUS");

            previous.Size =
                new Size(
                    116,
                    30);

            previous.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var pageInfo =
                new Label
                {
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.6F),
                    TextAlign =
                        ContentAlignment.MiddleCenter,
                    Size =
                        new Size(
                            76,
                            30),
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Right
                };

            var next =
                CreateEditorActionButton(
                    "NEXT ▶");

            next.Size =
                new Size(
                    104,
                    30);

            next.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection =
                        FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = CEditor,
                    Padding =
                        new Padding(
                            4,
                            8,
                            16,
                            8)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            header.Controls.Add(
                title);

            header.Controls.Add(
                subtitle);

            header.Controls.Add(
                newBuff);

            header.Controls.Add(
                search);

            header.Controls.Add(
                filter);

            header.Controls.Add(
                filterPanel);

            header.Controls.Add(
                count);

            header.Controls.Add(
                previous);

            header.Controls.Add(
                pageInfo);

            header.Controls.Add(
                next);

            root.Controls.Add(
                results);

            root.Controls.Add(
                header);

            page.Controls.Add(
                root);

            var state =
                new BuffBrowseState
                {
                    Page = page,
                    Service = service,
                    Results = results,
                    Search = search,
                    BuffType = buffType,
                    LifeType = lifeType,
                    TimeType = timeType,
                    Count = count,
                    PageInfo = pageInfo,
                    Previous = previous,
                    Next = next,
                    FilterButton = filter,
                    FilterPanel = filterPanel
                };

            page.Tag = state;

            void Layout()
            {
                int width =
                    Math.Max(
                        560,
                        header.ClientSize.Width);

                newBuff.Location =
                    new Point(
                        width -
                        newBuff.Width -
                        10,
                        3);

                filter.Location =
                    new Point(
                        width -
                        filter.Width -
                        10,
                        68);

                search.Width =
                    Math.Max(
                        220,
                        filter.Left -
                        search.Left -
                        10);

                filterPanel.Location =
                    new Point(
                        12,
                        105);

                filterPanel.Width =
                    Math.Max(
                        300,
                        width - 24);

                int navY =
                    state.FiltersVisible
                        ? 151
                        : 116;

                count.Top = navY + 6;

                next.Location =
                    new Point(
                        width -
                        next.Width -
                        10,
                        navY);

                pageInfo.Location =
                    new Point(
                        next.Left -
                        pageInfo.Width -
                        8,
                        navY);

                previous.Location =
                    new Point(
                        pageInfo.Left -
                        previous.Width -
                        8,
                        navY);

                header.Height =
                    state.FiltersVisible
                        ? 192
                        : 157;
            }

            header.Resize +=
                (_, _) =>
                    Layout();

            results.Resize +=
                (_, _) =>
                    ResizeBuffCards(
                        results);

            search.TextChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshBuffBrowser(
                        state);
                };

            buffType.SelectedIndexChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshBuffBrowser(
                        state);
                };

            lifeType.SelectedIndexChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshBuffBrowser(
                        state);
                };

            timeType.SelectedIndexChanged +=
                (_, _) =>
                {
                    state.PageIndex = 0;
                    RefreshBuffBrowser(
                        state);
                };

            filter.Click +=
                (_, _) =>
                {
                    state.FiltersVisible =
                        !state.FiltersVisible;

                    filterPanel.Visible =
                        state.FiltersVisible;

                    filter.Text =
                        state.FiltersVisible
                            ? "HIDE FILTERS"
                            : "FILTERS";

                    Layout();
                };

            reset.Click +=
                (_, _) =>
                {
                    search.Text =
                        string.Empty;

                    buffType.SelectedIndex = 0;
                    lifeType.SelectedIndex = 0;
                    timeType.SelectedIndex = 0;
                    state.PageIndex = 0;
                    RefreshBuffBrowser(
                        state);
                };

            previous.Click +=
                (_, _) =>
                {
                    if (state.PageIndex <= 0)
                        return;

                    state.PageIndex--;
                    RefreshBuffBrowser(
                        state);

                    results.VerticalScroll.Value = 0;
                };

            next.Click +=
                (_, _) =>
                {
                    int pages =
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                state.Filtered.Count /
                                (double)
                                BuffCardsPerPage));

                    if (state.PageIndex >=
                        pages - 1)
                    {
                        return;
                    }

                    state.PageIndex++;
                    RefreshBuffBrowser(
                        state);

                    results.VerticalScroll.Value = 0;
                };

            newBuff.Click +=
                (_, _) =>
                    OpenBuffEditTab(
                        service,
                        service.CreateNewNode(),
                        original: null,
                        isNew: true);

            Layout();
            RefreshBuffBrowser(
                state);

            page.ResumeLayout();
        }

        private DarkComboBox CreateBuffFilterCombo(
            string first,
            IReadOnlyList<int> values)
        {
            var combo =
                new DarkComboBox
                {
                    Size =
                        new Size(
                            140,
                            28)
                };

            combo.Items.Add(
                first);

            foreach (int value in values)
                combo.Items.Add(value);

            combo.SelectedIndex = 0;
            return combo;
        }

        private static int? SelectedBuffFilter(
            DarkComboBox combo)
        {
            if (combo.SelectedIndex <= 0)
                return null;

            object? item =
                combo.SelectedItem;

            if (item is int value)
                return value;

            return int.TryParse(
                item?.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value)
                    ? value
                    : null;
        }

        private void RefreshBuffBrowser(
            BuffBrowseState state)
        {
            state.Filtered =
                state.Service.Search(
                    state.Search.Text,
                    SelectedBuffFilter(
                        state.BuffType),
                    SelectedBuffFilter(
                        state.LifeType),
                    SelectedBuffFilter(
                        state.TimeType));

            int pages =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        state.Filtered.Count /
                        (double)
                        BuffCardsPerPage));

            state.PageIndex =
                Math.Clamp(
                    state.PageIndex,
                    0,
                    pages - 1);

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (BuffEditorRecord record in
                state.Filtered
                    .Skip(
                        state.PageIndex *
                        BuffCardsPerPage)
                    .Take(
                        BuffCardsPerPage))
            {
                state.Results.Controls.Add(
                    CreateBuffCard(
                        state,
                        record));
            }

            state.Results.ResumeLayout();

            state.Count.Text =
                $"Total: {state.Service.Count:N0}   •   Results: {state.Filtered.Count:N0}";

            state.PageInfo.Text =
                $"{state.PageIndex + 1} / {pages}";

            state.Previous.Enabled =
                state.PageIndex > 0;

            state.Next.Enabled =
                state.PageIndex <
                pages - 1;

            ResizeBuffCards(
                state.Results);
        }

        private void ResizeBuffCards(
            FlowLayoutPanel results)
        {
            int width =
                Math.Max(
                    520,
                    results.ClientSize.Width -
                    26);

            foreach (Control control in
                results.Controls)
            {
                if (control is Panel card)
                    card.Width = width;
            }
        }

        private Control CreateBuffCard(
            BuffBrowseState state,
            BuffEditorRecord record)
        {
            var card =
                new Panel
                {
                    Width =
                        Math.Max(
                            520,
                            state.Results.ClientSize.Width -
                            26),
                    Height = 104,
                    BackColor =
                        Color.FromArgb(
                            29,
                            29,
                            29),
                    Margin =
                        new Padding(
                            0,
                            0,
                            0,
                            9)
                };

            card.Paint +=
                (_, e) =>
                {
                    using var pen =
                        new Pen(
                            Color.FromArgb(
                                75,
                                75,
                                75));

                    e.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        card.Width - 1,
                        card.Height - 1);
                };

            var icon =
                new PictureBox
                {
                    Location =
                        new Point(
                            14,
                            14),
                    Size =
                        new Size(
                            72,
                            72),
                    BackColor = Color.Black,
                    SizeMode =
                        PictureBoxSizeMode.Zoom
                };

            var name =
                new Label
                {
                    Text =
                        record.DisplayName,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            10.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            100,
                            12),
                    Size =
                        new Size(
                            360,
                            24),
                    AutoEllipsis = true
                };

            var identity =
                new Label
                {
                    Text =
                        $"Buff ID {record.Id}  •  Icon {record.IconId}  •  Class {record.BuffClass}",
                    ForeColor =
                        Color.FromArgb(
                            110,
                            235,
                            145),
                    Font =
                        new Font(
                            "Segoe UI",
                            8.2F),
                    Location =
                        new Point(
                            100,
                            38),
                    Size =
                        new Size(
                            430,
                            20),
                    AutoEllipsis = true
                };

            var relations =
                new Label
                {
                    Text =
                        $"Type {record.BuffType}  •  Life {record.LifeType}  •  Time {record.TimeType}  •  " +
                        $"Skill {record.SkillCode}  •  Digimon Skill {record.DigimonSkillCode}",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8F),
                    Location =
                        new Point(
                            100,
                            59),
                    Size =
                        new Size(
                            520,
                            20),
                    AutoEllipsis = true
                };

            var comment =
                new Label
                {
                    Text =
                        record.Comment,
                    ForeColor =
                        Color.FromArgb(
                            205,
                            205,
                            205),
                    Font =
                        new Font(
                            "Segoe UI",
                            7.7F),
                    Location =
                        new Point(
                            100,
                            79),
                    Size =
                        new Size(
                            530,
                            18),
                    AutoEllipsis = true
                };

            var edit =
                CreateEditorActionButton(
                    "EDIT");

            edit.Size =
                new Size(
                    96,
                    32);

            edit.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var remove =
                CreateEditorActionButton(
                    "REMOVE");

            remove.Size =
                new Size(
                    96,
                    32);

            remove.ForeColor =
                Color.FromArgb(
                    255,
                    105,
                    115);

            remove.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            void LayoutCard()
            {
                remove.Location =
                    new Point(
                        card.ClientSize.Width -
                        remove.Width -
                        14,
                        36);

                edit.Location =
                    new Point(
                        remove.Left -
                        edit.Width -
                        8,
                        36);

                int contentRight =
                    edit.Left - 14;

                name.Width =
                    Math.Max(
                        150,
                        contentRight -
                        name.Left);

                identity.Width =
                    name.Width;

                relations.Width =
                    name.Width;

                comment.Width =
                    name.Width;
            }

            card.Resize +=
                (_, _) =>
                    LayoutCard();

            edit.Click +=
                (_, _) =>
                    OpenBuffEditTab(
                        state.Service,
                        new XElement(
                            record.Node),
                        record.Node,
                        isNew: false);

            remove.Click +=
                (_, _) =>
                {
                    DialogResult answer =
                        MessageBox.Show(
                            $"Remove Buff?\r\n\r\nID: {record.Id}\r\nName: {record.DisplayName}\r\n\r\n" +
                            "A .editor.bak backup will be created.",
                            "Remove Buff",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                    if (answer !=
                        DialogResult.Yes)
                    {
                        return;
                    }

                    try
                    {
                        state.Service.Delete(
                            record.PhysicalIndex);

                        EditorPreloadService.ReplaceBuffEditor(
                            state.Service.FilePath,
                            state.Service);

                        RefreshAllBuffBrowsers(
                            state.Service);
                    }
                    catch (Exception ex)
                    {
                        ShowEditorError(
                            "Remove Buff",
                            ex);
                    }
                };

            card.Controls.Add(
                icon);

            card.Controls.Add(
                name);

            card.Controls.Add(
                identity);

            card.Controls.Add(
                relations);

            card.Controls.Add(
                comment);

            card.Controls.Add(
                edit);

            card.Controls.Add(
                remove);

            LayoutCard();

            _ =
                LoadBuffIconIntoAsync(
                    state.Service,
                    record.IconId,
                    icon);

            return card;
        }

        private async Task LoadBuffIconIntoAsync(
            BuffEditorService service,
            uint iconId,
            PictureBox target)
        {
            if (iconId == 0)
                return;

            Bitmap? image =
                await Task.Run(
                    () =>
                        service.TryLoadIcon(
                            iconId));

            if (image == null ||
                target.IsDisposed)
            {
                image?.Dispose();
                return;
            }

            if (target.InvokeRequired)
            {
                target.BeginInvoke(
                    new Action(
                        () =>
                        {
                            if (target.IsDisposed)
                            {
                                image.Dispose();
                                return;
                            }

                            target.Image?.Dispose();
                            target.Image = image;
                        }));
            }
            else
            {
                target.Image?.Dispose();
                target.Image = image;
            }
        }

        private void OpenBuffEditTab(
            BuffEditorService service,
            XElement working,
            XElement? original,
            bool isNew)
        {
            var page =
                CreateDarkTab(
                    isNew
                        ? "New Buff [Unsaved]"
                        : $"{ReadBuffText(working, "s_szName", "Buff")} [Edit]");

            var state =
                new BuffEditState
                {
                    Page = page,
                    Service = service,
                    Original = original ?? working,
                    Working = working,
                    OriginalPhysicalIndex =
                        original == null
                            ? 0
                            : service.FindByNode(original)?.PhysicalIndex ?? 0,
                    OriginalId =
                        uint.TryParse(
                            working.Element("s_dwID")?.Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out uint originalId)
                                ? originalId
                                : 0,
                    IsNew = isNew,
                    Dirty = isNew
                };

            page.Tag = state;

            var root =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor
                };

            var top =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 68,
                    BackColor = CEditor,
                    Padding =
                        new Padding(
                            18,
                            12,
                            18,
                            10)
                };

            var save =
                CreateEditorActionButton(
                    "SAVE");

            save.Size =
                new Size(
                    110,
                    34);

            var viewXml =
                CreateEditorActionButton(
                    "VIEW XML");

            viewXml.Size =
                new Size(
                    110,
                    34);

            viewXml.Location =
                new Point(
                    120,
                    0);

            top.Controls.Add(
                save);

            top.Controls.Add(
                viewXml);

            var scroll =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BackColor = CEditor,
                    Padding =
                        new Padding(
                            18,
                            10,
                            28,
                            30)
                };

            DarkUi.ApplyDarkScrollBar(
                scroll);

            root.Controls.Add(
                scroll);

            root.Controls.Add(
                top);

            page.Controls.Add(
                root);

            var content =
                new Panel
                {
                    Location =
                        new Point(
                            0,
                            0),
                    Width = 760,
                    Height = 1110,
                    BackColor = CEditor
                };

            scroll.Controls.Add(
                content);

            var visualCard =
                CreateBuffSection(
                    "BUFF PREVIEW",
                    "Visual identity from s_nBuffIcon and the principal Buff fields.",
                    0,
                    142);

            content.Controls.Add(
                visualCard);

            var icon =
                new PictureBox
                {
                    Location =
                        new Point(
                            18,
                            50),
                    Size =
                        new Size(
                            72,
                            72),
                    BackColor = Color.Black,
                    SizeMode =
                        PictureBoxSizeMode.Zoom
                };

            state.Icon = icon;

            visualCard.Controls.Add(
                icon);

            AddBuffTextField(
                state,
                visualCard,
                "Buff ID",
                "s_dwID",
                110,
                46,
                235);

            state.IdStatus =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            7.6F),
                    Location =
                        new Point(
                            110,
                            103),
                    Size =
                        new Size(
                            240,
                            20)
                };

            visualCard.Controls.Add(
                state.IdStatus);

            AddBuffTextField(
                state,
                visualCard,
                "Icon ID",
                "s_nBuffIcon",
                370,
                46,
                170);

            var selectIcon =
                CreateEditorActionButton(
                    "REFRESH ICON");

            selectIcon.Location =
                new Point(
                    552,
                    73);

            selectIcon.Size =
                new Size(
                    150,
                    27);

            visualCard.Controls.Add(
                selectIcon);

            var identity =
                CreateBuffSection(
                    "IDENTITY / TEXT",
                    "Name, description and effect file. XML color/control codes are preserved exactly.",
                    154,
                    250);

            content.Controls.Add(
                identity);

            AddBuffTextField(
                state,
                identity,
                "Name",
                "s_szName",
                16,
                50,
                686);

            AddBuffMultilineField(
                state,
                identity,
                "Comment / Description",
                "s_szComment",
                16,
                112,
                686,
                70);

            AddBuffTextField(
                state,
                identity,
                "Effect File",
                "s_szEffectFile",
                16,
                196,
                686);

            var relation =
                CreateBuffSection(
                    "SKILL RELATIONS",
                    "Choose directly from Skill.xml. Search supports Skill ID, name, description and icon ID.",
                    416,
                    220);

            content.Controls.Add(
                relation);

            AddBuffSkillField(
                state,
                relation,
                "SkillCode",
                "s_dwSkillCode",
                16,
                50);

            AddBuffSkillField(
                state,
                relation,
                "Digimon SkillCode",
                "s_dwDigimonSkillCode",
                16,
                126);

            var behavior =
                CreateBuffSection(
                    "BUFF BEHAVIOR",
                    "Core type, lifetime, timing, class and level conditions.",
                    648,
                    322);

            content.Controls.Add(
                behavior);

            AddBuffTextField(
                state,
                behavior,
                "Buff Type",
                "s_nBuffType",
                16,
                52,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Life Type",
                "s_nBuffLifeType",
                248,
                52,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Time Type",
                "s_nBuffTimeType",
                480,
                52,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Min Level",
                "s_nMinLv",
                16,
                118,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Condition Level",
                "s_nConditionLv",
                248,
                118,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Class",
                "s_nBuffClass",
                480,
                118,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Delete Flag",
                "s_bDelete",
                16,
                184,
                210);

            AddBuffTextField(
                state,
                behavior,
                "Unknown",
                "unknow",
                248,
                184,
                210);

            AddBuffTextField(
                state,
                behavior,
                "u",
                "u",
                480,
                184,
                210);

            var notes =
                new Label
                {
                    Text =
                        "DB importer columns: BuffId, Name, DigimonSkillCode, SkillCode, MinLevel, ConditionLevel, Class, Type, LifeType, TimeType.\r\n" +
                        "Comment, Icon, EffectFile, DeleteFlag, unknow and u remain XML-only because Asset.Buff has no supplied column for them.",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8F),
                    Location =
                        new Point(
                            18,
                            252),
                    Size =
                        new Size(
                            680,
                            52)
                };

            behavior.Controls.Add(
                notes);

            void LayoutContent()
            {
                int width =
                    Math.Max(
                        620,
                        scroll.ClientSize.Width -
                        scroll.Padding.Horizontal -
                        18);

                content.Width = width;

                foreach (Control section in
                    content.Controls)
                {
                    if (section is Panel panel)
                        panel.Width = width;
                }
            }

            scroll.Resize +=
                (_, _) =>
                    LayoutContent();

            foreach (TextBox box in
                state.Fields.Values)
            {
                box.TextChanged +=
                    (_, _) =>
                    {
                        state.Dirty = true;

                        if (box.Tag is string field)
                        {
                            XElement? element =
                                state.Working.Element(
                                    field);

                            if (element != null)
                                element.Value = box.Text;

                            if (field.Equals(
                                    "s_dwID",
                                    StringComparison.Ordinal))
                            {
                                UpdateBuffIdStatus(
                                    state);
                            }

                            if (field.Equals(
                                    "s_nBuffIcon",
                                    StringComparison.Ordinal))
                            {
                                RefreshBuffEditIcon(
                                    state);
                            }
                        }
                    };
            }

            selectIcon.Click +=
                (_, _) =>
                    RefreshBuffEditIcon(
                        state);

            save.Click +=
                (_, _) =>
                    SaveBuffEditor(
                        state,
                        showSuccess: true);

            viewXml.Click +=
                (_, _) =>
                    OpenRawBlockTab(
                        state.Service.FilePath,
                        new XElement(
                            state.Working));

            LayoutContent();
            UpdateBuffIdStatus(
                state);

            RefreshBuffEditIcon(
                state);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;
        }

        private Panel CreateBuffSection(
            string title,
            string subtitle,
            int top,
            int height)
        {
            var panel =
                new Panel
                {
                    Location =
                        new Point(
                            0,
                            top),
                    Width = 760,
                    Height = height,
                    BackColor =
                        Color.FromArgb(
                            26,
                            26,
                            26)
                };

            panel.Paint +=
                (_, e) =>
                {
                    using var p =
                        new Pen(
                            Color.FromArgb(
                                65,
                                65,
                                65));

                    e.Graphics.DrawRectangle(
                        p,
                        0,
                        0,
                        panel.Width - 1,
                        panel.Height - 1);
                };

            panel.Controls.Add(
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            16,
                            12),
                    AutoSize = true
                });

            panel.Controls.Add(
                new Label
                {
                    Text = subtitle,
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            7.8F),
                    Location =
                        new Point(
                            16,
                            31),
                    Size =
                        new Size(
                            680,
                            18),
                    AutoEllipsis = true
                });

            return panel;
        }

        private void AddBuffTextField(
            BuffEditState state,
            Control host,
            string label,
            string element,
            int x,
            int y,
            int width)
        {
            host.Controls.Add(
                new Label
                {
                    Text = label,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8F),
                    Location =
                        new Point(
                            x,
                            y),
                    Size =
                        new Size(
                            width,
                            18)
                });

            var box =
                new TextBox
                {
                    Text =
                        state.Working
                            .Element(element)?
                            .Value
                        ?? string.Empty,
                    Tag = element,
                    Location =
                        new Point(
                            x,
                            y + 22),
                    Size =
                        new Size(
                            width,
                            23),
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Left
                };

            state.Fields[element] = box;
            host.Controls.Add(
                box);
        }

        private void AddBuffMultilineField(
            BuffEditState state,
            Control host,
            string label,
            string element,
            int x,
            int y,
            int width,
            int height)
        {
            host.Controls.Add(
                new Label
                {
                    Text = label,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8F),
                    Location =
                        new Point(
                            x,
                            y),
                    Size =
                        new Size(
                            width,
                            18)
                });

            var box =
                new TextBox
                {
                    Text =
                        state.Working
                            .Element(element)?
                            .Value
                        ?? string.Empty,
                    Tag = element,
                    Location =
                        new Point(
                            x,
                            y + 22),
                    Size =
                        new Size(
                            width,
                            height),
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Multiline = true,
                    ScrollBars =
                        ScrollBars.Vertical
                };

            state.Fields[element] = box;
            host.Controls.Add(
                box);
        }

        private void AddBuffSkillField(
            BuffEditState state,
            Control host,
            string label,
            string element,
            int x,
            int y)
        {
            host.Controls.Add(
                new Label
                {
                    Text = label,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8F),
                    Location =
                        new Point(
                            x,
                            y),
                    Size =
                        new Size(
                            220,
                            18)
                });

            var box =
                new TextBox
                {
                    Text =
                        state.Working
                            .Element(element)?
                            .Value
                        ?? "0",
                    Tag = element,
                    Location =
                        new Point(
                            x,
                            y + 22),
                    Size =
                        new Size(
                            470,
                            23),
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle
                };

            state.Fields[element] = box;

            var select =
                CreateEditorActionButton(
                    "SELECT SKILL");

            select.Location =
                new Point(
                    x + 482,
                    y + 20);

            select.Size =
                new Size(
                    190,
                    28);

            select.Click +=
                async (_, _) =>
                {
                    uint? selected =
                        await OpenBuffSkillPickerAsync();

                    if (selected.HasValue)
                        box.Text =
                            selected.Value.ToString(
                                CultureInfo.InvariantCulture);
                };

            host.Controls.Add(
                box);

            host.Controls.Add(
                select);
        }

        private void UpdateBuffIdStatus(
            BuffEditState state)
        {
            if (!state.Fields.TryGetValue(
                    "s_dwID",
                    out TextBox? box))
            {
                return;
            }

            if (!uint.TryParse(
                    box.Text,
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

            bool unchangedExistingId =
                !state.IsNew &&
                id ==
                state.OriginalId;

            if (unchangedExistingId ||
                state.Service.IsIdAvailable(
                    id,
                    state.IsNew
                        ? null
                        : state.OriginalPhysicalIndex))
            {
                state.IdStatus.Text =
                    "ID AVAILABLE";

                state.IdStatus.ForeColor =
                    Color.FromArgb(
                        125,
                        220,
                        140);
            }
            else
            {
                uint suggestion =
                    state.Service.SuggestAvailableId(
                        id + 1);

                state.IdStatus.Text =
                    $"ID ALREADY USED • Suggested {suggestion}";

                state.IdStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);
            }
        }

        private void RefreshBuffEditIcon(
            BuffEditState state)
        {
            if (!state.Fields.TryGetValue(
                    "s_nBuffIcon",
                    out TextBox? box) ||
                !uint.TryParse(
                    box.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint iconId))
            {
                state.Icon.Image?.Dispose();
                state.Icon.Image = null;
                return;
            }

            _ =
                LoadBuffIconIntoAsync(
                    state.Service,
                    iconId,
                    state.Icon);
        }

        private bool SaveBuffEditor(
            BuffEditState state,
            bool showSuccess)
        {
            try
            {
                if (!uint.TryParse(
                        state.Working
                            .Element("s_dwID")?
                            .Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out uint id) ||
                    id == 0)
                {
                    throw new InvalidDataException(
                        "Buff ID must be a valid UInt32 greater than zero.");
                }

                bool unchangedExistingId =
                    !state.IsNew &&
                    id ==
                    state.OriginalId;

                if (!unchangedExistingId &&
                    !state.Service.IsIdAvailable(
                        id,
                        state.IsNew
                            ? null
                            : state.OriginalPhysicalIndex))
                {
                    throw new InvalidDataException(
                        $"Buff ID {id} already exists. " +
                        $"Suggested free ID: {state.Service.SuggestAvailableId(id + 1)}.");
                }

                foreach (string required in
                    new[]
                    {
                        "s_nBuffIcon",
                        "s_nBuffType",
                        "s_nBuffLifeType",
                        "s_nBuffTimeType",
                        "s_nMinLv",
                        "s_nBuffClass",
                        "unknow",
                        "s_dwSkillCode",
                        "s_dwDigimonSkillCode",
                        "s_bDelete",
                        "s_nConditionLv",
                        "u"
                    })
                {
                    string value =
                        state.Working
                            .Element(required)?
                            .Value
                        ?? string.Empty;

                    if (!uint.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out _))
                    {
                        throw new InvalidDataException(
                            $"<{required}> must contain a non-negative integer.");
                    }
                }

                if (state.IsNew)
                {
                    state.Service.CommitNew(
                        state.Working);

                    state.OriginalPhysicalIndex =
                        state.Service.Count;
                }
                else
                {
                    state.Service.CommitEdit(
                        state.OriginalPhysicalIndex,
                        state.Working);
                }

                state.OriginalId = id;

                BuffEditorRecord? refreshed =
                    state.Service.Records
                        .FirstOrDefault(
                            x =>
                                x.PhysicalIndex ==
                                state.OriginalPhysicalIndex);

                if (refreshed != null)
                {
                    state.Original = refreshed.Node;
                    state.Working =
                        new XElement(
                            refreshed.Node);
                }

                EditorPreloadService.ReplaceBuffEditor(
                    state.Service.FilePath,
                    state.Service);

                state.Dirty = false;
                state.IsNew = false;

                state.Page.Text =
                    $"{ReadBuffText(state.Working, "s_szName", "Buff")} [Edit]";

                RefreshAllBuffBrowsers(
                    state.Service);

                if (showSuccess)
                {
                    MessageBox.Show(
                        "Buff.xml saved successfully.",
                        "Buff Editor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowEditorError(
                    "Save Buff",
                    ex);

                return false;
            }
        }

        private void RefreshAllBuffBrowsers(
            BuffEditorService service)
        {
            foreach (TabPage page in
                editorTabs.TabPages)
            {
                if (page.Tag is BuffBrowseState state &&
                    ReferenceEquals(
                        state.Service,
                        service))
                {
                    RefreshBuffBrowser(
                        state);
                }
            }
        }

        private async Task<uint?> OpenBuffSkillPickerAsync()
        {
            string skillPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Skill",
                    "Skill.xml");

            if (!File.Exists(
                    skillPath))
            {
                MessageBox.Show(
                    "Skill.xml was not found:\r\n\r\n" +
                    skillPath,
                    "Select SkillCode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return null;
            }

            uint? selectedValue = null;
            var completion =
                new TaskCompletionSource<uint?>();

            var page =
                CreateDarkTab(
                    "Select SkillCode");

            var loading =
                new EditorLoadingView(
                    "Loading Skill Catalog",
                    "Reading Skill.xml and preparing skill names, icons and search index.");

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            await Task.Yield();

            try
            {
                SkillEditorService service =
                    await EditorPreloadService
                        .GetSkillEditorAsync(
                            skillPath);

                if (page.IsDisposed)
                    return null;

                page.Controls.Clear();

                var root =
                    new Panel
                    {
                        Dock = DockStyle.Fill,
                        BackColor = CEditor,
                        Padding =
                            new Padding(
                                18,
                                14,
                                18,
                                14)
                    };

                var header =
                    new Panel
                    {
                        Dock = DockStyle.Top,
                        Height = 102,
                        BackColor = CEditor
                    };

                var title =
                    new Label
                    {
                        Text = "Select SkillCode from Skill.xml",
                        ForeColor = CText,
                        Font =
                            new Font(
                                "Segoe UI Semibold",
                                13F,
                                FontStyle.Bold),
                        Location =
                            new Point(
                                10,
                                2),
                        AutoSize = true
                    };

                var search =
                    new TextBox
                    {
                        PlaceholderText =
                            "Search Skill ID, name, description or icon ID...",
                        Location =
                            new Point(
                                10,
                                42),
                        Height = 28,
                        BackColor =
                            Color.FromArgb(
                                10,
                                10,
                                10),
                        ForeColor = CText,
                        BorderStyle =
                            BorderStyle.FixedSingle,
                        Anchor =
                            AnchorStyles.Top |
                            AnchorStyles.Left |
                            AnchorStyles.Right
                    };

                var count =
                    new Label
                    {
                        ForeColor = CMuted,
                        Font =
                            new Font(
                                "Segoe UI",
                                8.3F),
                        Location =
                            new Point(
                                12,
                                76),
                        AutoSize = true
                    };

                var previous =
                    CreateEditorActionButton(
                        "◀ PREVIOUS");

                previous.Size =
                    new Size(
                        110,
                        28);

                previous.Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Right;

                var pageInfo =
                    new Label
                    {
                        ForeColor = CText,
                        TextAlign =
                            ContentAlignment.MiddleCenter,
                        Size =
                            new Size(
                                70,
                                28),
                        Anchor =
                            AnchorStyles.Top |
                            AnchorStyles.Right
                    };

                var next =
                    CreateEditorActionButton(
                        "NEXT ▶");

                next.Size =
                    new Size(
                        96,
                        28);

                next.Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Right;

                var results =
                    new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        AutoScroll = true,
                        FlowDirection =
                            FlowDirection.TopDown,
                        WrapContents = false,
                        BackColor = CEditor,
                        Padding =
                            new Padding(
                                4,
                                8,
                                16,
                                8)
                    };

                DarkUi.ApplyDarkScrollBar(
                    results);

                header.Controls.Add(
                    title);

                header.Controls.Add(
                    search);

                header.Controls.Add(
                    count);

                header.Controls.Add(
                    previous);

                header.Controls.Add(
                    pageInfo);

                header.Controls.Add(
                    next);

                root.Controls.Add(
                    results);

                root.Controls.Add(
                    header);

                page.Controls.Add(
                    root);

                var picker =
                    new BuffSkillPickerState
                    {
                        Service = service,
                        Search = search,
                        Results = results,
                        Count = count,
                        PageInfo = pageInfo,
                        Previous = previous,
                        Next = next,
                        Select =
                            value =>
                            {
                                selectedValue = value;

                                if (!completion.Task.IsCompleted)
                                    completion.SetResult(value);

                                if (editorTabs.TabPages.Contains(page))
                                    editorTabs.TabPages.Remove(page);

                                page.Dispose();
                            }
                    };

                void Layout()
                {
                    int width =
                        header.ClientSize.Width;

                    next.Location =
                        new Point(
                            width -
                            next.Width -
                            8,
                            69);

                    pageInfo.Location =
                        new Point(
                            next.Left -
                            pageInfo.Width -
                            8,
                            69);

                    previous.Location =
                        new Point(
                            pageInfo.Left -
                            previous.Width -
                            8,
                            69);

                    search.Width =
                        Math.Max(
                            260,
                            width - 20);
                }

                header.Resize +=
                    (_, _) =>
                        Layout();

                search.TextChanged +=
                    (_, _) =>
                    {
                        picker.PageIndex = 0;
                        RefreshBuffSkillPicker(
                            picker);
                    };

                previous.Click +=
                    (_, _) =>
                    {
                        if (picker.PageIndex <= 0)
                            return;

                        picker.PageIndex--;
                        RefreshBuffSkillPicker(
                            picker);
                    };

                next.Click +=
                    (_, _) =>
                    {
                        int pages =
                            Math.Max(
                                1,
                                (int)Math.Ceiling(
                                    picker.Filtered.Count /
                                    20d));

                        if (picker.PageIndex >=
                            pages - 1)
                        {
                            return;
                        }

                        picker.PageIndex++;
                        RefreshBuffSkillPicker(
                            picker);
                    };

                page.Disposed +=
                    (_, _) =>
                    {
                        if (!completion.Task.IsCompleted)
                            completion.TrySetResult(null);
                    };

                results.Resize +=
                    (_, _) =>
                    {
                        int width =
                            Math.Max(
                                500,
                                results.ClientSize.Width -
                                28);

                        foreach (Control c in
                            results.Controls)
                        {
                            c.Width = width;
                        }
                    };

                Layout();
                RefreshBuffSkillPicker(
                    picker);

                return await completion.Task;
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    page.Controls.Clear();
                    page.Controls.Add(
                        CreateInfoLabel(
                            ex.Message));
                }

                return null;
            }
        }

        private void RefreshBuffSkillPicker(
            BuffSkillPickerState state)
        {
            state.Filtered =
                state.Service.Search(
                    state.Search.Text,
                    null,
                    null,
                    null);

            int pages =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        state.Filtered.Count /
                        20d));

            state.PageIndex =
                Math.Clamp(
                    state.PageIndex,
                    0,
                    pages - 1);

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (SkillEditorRecord skill in
                state.Filtered
                    .Skip(
                        state.PageIndex *
                        20)
                    .Take(
                        20))
            {
                var card =
                    new Panel
                    {
                        Width =
                            Math.Max(
                                500,
                                state.Results.ClientSize.Width -
                                28),
                        Height = 84,
                        BackColor =
                            Color.FromArgb(
                                29,
                                29,
                                29),
                        Margin =
                            new Padding(
                                0,
                                0,
                                0,
                                8)
                    };

                var icon =
                    new PictureBox
                    {
                        Location =
                            new Point(
                                12,
                                10),
                        Size =
                            new Size(
                                62,
                                62),
                        BackColor = Color.Black,
                        SizeMode =
                            PictureBoxSizeMode.Zoom
                    };

                var name =
                    new Label
                    {
                        Text = skill.DisplayName,
                        ForeColor = CText,
                        Font =
                            new Font(
                                "Segoe UI Semibold",
                                9.5F,
                                FontStyle.Bold),
                        Location =
                            new Point(
                                88,
                                10),
                        Size =
                            new Size(
                                380,
                                22),
                        AutoEllipsis = true
                    };

                var info =
                    new Label
                    {
                        Text =
                            $"Skill ID {skill.Id}  •  Icon {skill.IconId}  •  Type {skill.SkillType}  •  Target {skill.Target}",
                        ForeColor =
                            Color.FromArgb(
                                110,
                                235,
                                145),
                        Font =
                            new Font(
                                "Segoe UI",
                                8F),
                        Location =
                            new Point(
                                88,
                                34),
                        Size =
                            new Size(
                                440,
                                18),
                        AutoEllipsis = true
                    };

                var comment =
                    new Label
                    {
                        Text = skill.Comment,
                        ForeColor = CMuted,
                        Font =
                            new Font(
                                "Segoe UI",
                                7.7F),
                        Location =
                            new Point(
                                88,
                                54),
                        Size =
                            new Size(
                                440,
                                18),
                        AutoEllipsis = true
                    };

                var select =
                    CreateEditorActionButton(
                        "SELECT");

                select.Size =
                    new Size(
                        100,
                        32);

                select.Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Right;

                void LayoutCard()
                {
                    select.Location =
                        new Point(
                            card.ClientSize.Width -
                            select.Width -
                            12,
                            26);

                    int width =
                        Math.Max(
                            120,
                            select.Left -
                            name.Left -
                            12);

                    name.Width = width;
                    info.Width = width;
                    comment.Width = width;
                }

                card.Resize +=
                    (_, _) =>
                        LayoutCard();

                select.Click +=
                    (_, _) =>
                        state.Select(
                            skill.Id);

                card.Controls.Add(
                    icon);

                card.Controls.Add(
                    name);

                card.Controls.Add(
                    info);

                card.Controls.Add(
                    comment);

                card.Controls.Add(
                    select);

                LayoutCard();

                _ =
                    LoadSkillPickerIconAsync(
                        state.Service,
                        skill.IconId,
                        icon);

                state.Results.Controls.Add(
                    card);
            }

            state.Results.ResumeLayout();

            state.Count.Text =
                $"Skills: {state.Filtered.Count:N0} / {state.Service.Count:N0}";

            state.PageInfo.Text =
                $"{state.PageIndex + 1} / {pages}";

            state.Previous.Enabled =
                state.PageIndex > 0;

            state.Next.Enabled =
                state.PageIndex < pages - 1;
        }

        private async Task LoadSkillPickerIconAsync(
            SkillEditorService service,
            uint iconId,
            PictureBox target)
        {
            if (iconId == 0)
                return;

            Bitmap? image =
                await Task.Run(
                    () =>
                        service.TryLoadIcon(
                            iconId));

            if (image == null ||
                target.IsDisposed)
            {
                image?.Dispose();
                return;
            }

            if (target.InvokeRequired)
            {
                target.BeginInvoke(
                    new Action(
                        () =>
                        {
                            if (target.IsDisposed)
                            {
                                image.Dispose();
                                return;
                            }

                            target.Image?.Dispose();
                            target.Image = image;
                        }));
            }
            else
            {
                target.Image?.Dispose();
                target.Image = image;
            }
        }

        private static string ReadBuffText(
            XElement node,
            string element,
            string fallback)
        {
            string value =
                node.Element(element)?.Value
                ?? string.Empty;

            return string.IsNullOrWhiteSpace(
                value)
                    ? fallback
                    : value.Trim();
        }
    }
}
