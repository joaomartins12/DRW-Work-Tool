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
        private sealed class ItemDisplayBrowserState
        {
            public required ItemDisplayEditorService Service { get; init; }
            public required ItemListEditorService ItemService { get; init; }
            public required Dictionary<uint, ItemDisplayItemReference> Items { get; init; }
            public required TextBox Search { get; init; }
            public required Label Count { get; init; }
            public required Panel Results { get; init; }
            public required TableLayoutPanel Grid { get; init; }
            public required TabPage Page { get; init; }

            public int RenderGeneration { get; set; }
        }

        private async void OpenItemDisplayBrowser(
            string xmlPath)
        {
            string fullPath =
                Path.GetFullPath(
                    xmlPath);

            var page =
                CreateDarkTab(
                    "ItemDisplay.xml");

            page.Name =
                fullPath;

            var loading =
                new EditorLoadingView(
                    "Loading ItemDisplay",
                    "Linking ItemDisplay.xml with ItemList.xml and cached item icons before drawing the visual grid.");

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            UpdateEditorEmptyState();

            ItemDisplayEditorService displayService;
            ItemListEditorService itemService;
            Dictionary<uint, ItemDisplayItemReference> items;

            try
            {
                string itemListPath =
                    Path.Combine(
                        Path.GetDirectoryName(
                            fullPath)
                        ?? string.Empty,
                        "ItemList.xml");

                // ItemDisplayEditorService has its own shared cache.
                // The startup preload populates this same cache, so opening
                // ItemDisplay here is instant when preload has completed.
                displayService =
                    await Task.Run(
                        () =>
                            ItemDisplayEditorService
                                .OpenShared(
                                    fullPath));

                itemService =
                    await EditorPreloadService
                        .GetItemListAsync(
                            itemListPath);

                items =
                    await Task.Run(
                        () =>
                            BuildItemDisplayReferenceIndex(
                                itemService));
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "ItemDisplay editor could not be loaded",
                        ex.Message);
                }

                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 116,
                    BackColor = CPanel,
                    Padding =
                        new Padding(
                            18,
                            12,
                            18,
                            10)
                };

            var title =
                new Label
                {
                    Text =
                        "ItemDisplay — Visual Item Database",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            12.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            18,
                            10),
                    Size =
                        new Size(
                            520,
                            28)
                };

            var newButton =
                CreateEditorActionButton(
                    "NEW ITEM DISPLAY");

            newButton.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            newButton.Size =
                new Size(
                    160,
                    34);

            newButton.Location =
                new Point(
                    Math.Max(
                        560,
                        header.ClientSize.Width - 178),
                    10);

            var search =
                new TextBox
                {
                    Location =
                        new Point(
                            18,
                            52),
                    Size =
                        new Size(
                            Math.Max(
                                360,
                                header.ClientSize.Width - 220),
                            30),
                    BackColor =
                        Color.FromArgb(
                            15,
                            15,
                            15),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    PlaceholderText =
                        "Search ItemID, Item Name or Section..."
                };

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8F),
                    Location =
                        new Point(
                            18,
                            86),
                    Size =
                        new Size(
                            650,
                            20)
                };

            // Real outer inset: native WinForms scrollbars are painted
            // at the edge of the AutoScroll control, so an outer host is the
            // reliable way to keep the scrollbar away from the tab border.
            var resultsViewport =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    Padding =
                        new Padding(
                            14,
                            6,
                            18,
                            10)
                };

            var results =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BackColor = CEditor,
                    Padding =
                        new Padding(
                            10,
                            8,
                            10,
                            52)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            DarkUi.ApplyScrollableEndSpacing(
                results,
                endSpacing: 52);

            var grid =
                new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 3,
                    RowCount = 0,
                    GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                    BackColor = CEditor,
                    Margin = Padding.Empty,
                    Padding = new Padding(0, 0, 0, 52),
                    Location = new Point(0, 0)
                };

            grid.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    33.3333F));

            grid.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    33.3333F));

            grid.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    33.3334F));

            results.Controls.Add(grid);
            resultsViewport.Controls.Add(results);

            header.Controls.Add(
                title);

            header.Controls.Add(
                newButton);

            header.Controls.Add(
                search);

            header.Controls.Add(
                count);

            page.Controls.Add(
                resultsViewport);

            page.Controls.Add(
                header);

            loading.BringToFront();

            var state =
                new ItemDisplayBrowserState
                {
                    Service = displayService,
                    ItemService = itemService,
                    Items = items,
                    Search = search,
                    Count = count,
                    Results = results,
                    Grid = grid,
                    Page = page
                };

            page.Tag =
                state;

            void LayoutHeader()
            {
                int width =
                    header.ClientSize.Width;

                newButton.Location =
                    new Point(
                        Math.Max(
                            420,
                            width -
                            newButton.Width -
                            18),
                        10);

                search.Size =
                    new Size(
                        Math.Max(
                            260,
                            width -
                            36),
                        30);
            }

            header.SizeChanged +=
                (_, _) =>
                    LayoutHeader();

            var searchTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 180
                };

            searchTimer.Tick +=
                async (_, _) =>
                {
                    searchTimer.Stop();

                    await RefreshItemDisplayGridAsync(
                        state);
                };

            search.TextChanged +=
                (_, _) =>
                {
                    searchTimer.Stop();
                    searchTimer.Start();
                };

            void LayoutGrid()
            {
                if (page.IsDisposed ||
                    results.IsDisposed ||
                    grid.IsDisposed)
                {
                    return;
                }

                // Keep a compact 3-card composition instead of stretching the
                // grid to the whole editor width.
                const int desiredGridWidth = 660;

                int scrollbarReserve =
                    results.VerticalScroll.Visible
                        ? SystemInformation.VerticalScrollBarWidth + 6
                        : 6;

                int usableWidth =
                    Math.Max(
                        1,
                        results.ClientSize.Width -
                        results.Padding.Horizontal -
                        scrollbarReserve);

                grid.Width =
                    Math.Min(
                        desiredGridWidth,
                        usableWidth);

                int left =
                    results.Padding.Left +
                    Math.Max(
                        0,
                        (usableWidth - grid.Width) / 2);

                grid.Location =
                    new Point(
                        left,
                        results.Padding.Top);
            }

            results.SizeChanged +=
                (_, _) =>
                    LayoutGrid();

            resultsViewport.SizeChanged +=
                (_, _) =>
                    LayoutGrid();

            page.Disposed +=
                (_, _) =>
                {
                    searchTimer.Stop();
                    searchTimer.Dispose();

                };

            newButton.Click +=
                (_, _) =>
                    ShowItemDisplayEntryEditor(
                        state,
                        existing: null);

            page.ResumeLayout(true);
            loading.BringToFront();
            loading.Refresh();

            LayoutHeader();
            LayoutGrid();

            await RefreshItemDisplayGridAsync(state);

            if (!page.IsDisposed)
            {
                page.Controls.Remove(loading);
                loading.Dispose();
                page.PerformLayout();
                page.Invalidate(true);
                page.Update();
            }
        }

        private static Dictionary<uint, ItemDisplayItemReference>
            BuildItemDisplayReferenceIndex(
                ItemListEditorService itemService)
        {
            var result =
                new Dictionary<uint, ItemDisplayItemReference>();

            // No visual/result limit here: every ItemList entry is indexed.
            IReadOnlyList<ItemListRecord> all =
                itemService.Search(
                    string.Empty,
                    int.MaxValue);

            foreach (ItemListRecord item
                     in all)
            {
                XElement element =
                    itemService.GetClone(
                        item.ItemId);

                uint section = 0;

                uint.TryParse(
                    element.Element("s_nSection")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out section);

                result[item.ItemId] =
                    new ItemDisplayItemReference(
                        item.ItemId,
                        item.Name,
                        item.IconId,
                        section);
            }

            return result;
        }

        private async Task RefreshItemDisplayGridAsync(
            ItemDisplayBrowserState state)
        {
            if (state.Page.IsDisposed)
                return;

            int generation =
                ++state.RenderGeneration;

            IReadOnlyList<ItemDisplayRecord> rows =
                await Task.Run(
                    () =>
                        state.Service.Search(
                            state.Search.Text,
                            state.Items));

            if (state.Page.IsDisposed ||
                generation != state.RenderGeneration)
            {
                return;
            }

            state.Count.Text =
                string.IsNullOrWhiteSpace(
                    state.Search.Text)
                    ? $"Total ItemDisplay entries: {state.Service.TotalEntries:N0}"
                    : $"Results: {rows.Count:N0} / {state.Service.TotalEntries:N0}";

            state.Results.SuspendLayout();
            state.Grid.SuspendLayout();

            DisposeChildImages(
                state.Grid);

            state.Grid.Controls.Clear();
            state.Grid.RowStyles.Clear();
            state.Grid.RowCount = 0;

            state.Grid.ResumeLayout(false);
            state.Results.ResumeLayout(true);

            const int batchSize = 30;

            for (int start = 0;
                 start < rows.Count;
                 start += batchSize)
            {
                if (state.Page.IsDisposed ||
                    generation != state.RenderGeneration)
                {
                    return;
                }

                state.Grid.SuspendLayout();

                foreach (ItemDisplayRecord record
                         in rows.Skip(start).Take(batchSize))
                {
                    int index =
                        state.Grid.Controls.Count;

                    int row = index / 3;
                    int column = index % 3;

                    while (state.Grid.RowCount <= row)
                    {
                        state.Grid.RowCount++;

                        state.Grid.RowStyles.Add(
                            new RowStyle(
                                SizeType.Absolute,
                                188F));
                    }

                    Panel card =
                        CreateItemDisplayCard(
                            state,
                            record);

                    card.Anchor =
                        AnchorStyles.None;

                    state.Grid.Controls.Add(
                        card,
                        column,
                        row);
                }

                state.Grid.ResumeLayout(true);

                await Task.Yield();
            }

            int footerRow =
                state.Grid.RowCount;

            state.Grid.RowCount++;

            state.Grid.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    52F));

            var footer =
                new Panel
                {
                    Height = 52,
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    Margin = Padding.Empty
                };

            state.Grid.Controls.Add(
                footer,
                0,
                footerRow);

            state.Grid.SetColumnSpan(
                footer,
                3);
        }

        private Panel CreateItemDisplayCard(
            ItemDisplayBrowserState state,
            ItemDisplayRecord record)
        {
            const int cardWidth = 194;
            const int cardHeight = 174;

            var card =
                new Panel
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    BackColor =
                        Color.FromArgb(
                            29,
                            29,
                            29),
                    Margin =
                        new Padding(
                            7,
                            6,
                            7,
                            8),
                    BorderStyle =
                        BorderStyle.FixedSingle
                };

            state.Items.TryGetValue(
                record.ItemId,
                out ItemDisplayItemReference? item);

            var icon =
                new PictureBox
                {
                    Size =
                        new Size(
                            52,
                            52),
                    Location =
                        new Point(
                            (card.Width - 52) / 2,
                            13),
                    SizeMode =
                        PictureBoxSizeMode.Zoom,
                    BackColor =
                        Color.FromArgb(
                            8,
                            8,
                            8)
                };

            if (item != null)
            {
                icon.Image =
                    ImageDatabasePreview
                        .TryLoadInterfaceIcon(
                            item.IconId,
                            "Item");
            }

            string nameText =
                item?.Name
                ?? $"Unknown Item {record.ItemId}";

            var name =
                new Label
                {
                    Text = nameText,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.8F,
                            FontStyle.Bold),
                    TextAlign =
                        ContentAlignment.MiddleCenter,
                    Location =
                        new Point(
                            8,
                            70),
                    Size =
                        new Size(
                            card.Width - 16,
                            32),
                    AutoEllipsis = true
                };

            var ids =
                new Label
                {
                    Text =
                        $"ItemID: {record.ItemId}  |  Section: {record.Section}",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            7.4F),
                    TextAlign =
                        ContentAlignment.MiddleCenter,
                    Location =
                        new Point(
                            8,
                            104),
                    Size =
                        new Size(
                            card.Width - 16,
                            24),
                    AutoEllipsis = true
                };

            var edit =
                CreateEditorActionButton(
                    "EDIT");

            edit.Size =
                new Size(
                    100,
                    30);

            edit.Location =
                new Point(
                    (card.Width - edit.Width) / 2,
                    136);

            edit.Click +=
                (_, _) =>
                    ShowItemDisplayEntryEditor(
                        state,
                        record);

            card.Controls.Add(icon);
            card.Controls.Add(name);
            card.Controls.Add(ids);
            card.Controls.Add(edit);

            return card;
        }

        private async void ShowItemDisplayEntryEditor(
            ItemDisplayBrowserState state,
            ItemDisplayRecord? existing)
        {
            var overlay =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18,18,18)
                };

            state.Page.Controls.Add(overlay);
            overlay.BringToFront();
            overlay.Controls.Add(
                new EditorLoadingView(
                    existing == null ? "Loading New ItemDisplay Entry" : "Loading ItemDisplay Entry",
                    "Preparing linked item data, preview icon and editable ItemDisplay fields."));
            await Task.Yield();
            if(overlay.IsDisposed)return;
            overlay.Controls.Clear();

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 50,
                    BackColor =
                        Color.FromArgb(
                            31,
                            31,
                            31)
                };

            var title =
                new Label
                {
                    Text =
                        existing == null
                            ? "Create ItemDisplay Entry"
                            : $"Edit ItemDisplay — ItemID {existing.ItemId}",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            11F,
                            FontStyle.Bold),
                    Dock = DockStyle.Fill,
                    Padding =
                        new Padding(
                            16,
                            0,
                            10,
                            0),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            var close =
                CreateEditorActionButton(
                    "CLOSE");

            close.Dock =
                DockStyle.Right;

            close.Width = 90;

            close.Click +=
                (_, _) =>
                    overlay.Dispose();

            header.Controls.Add(
                title);

            header.Controls.Add(
                close);

            close.BringToFront();

            var body =
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
                            22,
                            18,
                            22,
                            40)
                };

            DarkUi.ApplyDarkScrollBar(
                body);

            var itemId =
                new TextBox
                {
                    BackColor =
                        Color.FromArgb(
                            12,
                            12,
                            12),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Text =
                        existing?.ItemId
                            .ToString(
                                CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    Width = 330
                };

            var section =
                new TextBox
                {
                    BackColor =
                        Color.FromArgb(
                            12,
                            12,
                            12),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Text =
                        existing?.Section
                            .ToString(
                                CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    Width = 330
                };

            var preview =
                new PictureBox
                {
                    Size =
                        new Size(
                            80,
                            80),
                    SizeMode =
                        PictureBoxSizeMode.Zoom,
                    BackColor =
                        Color.FromArgb(
                            8,
                            8,
                            8)
                };

            var previewName =
                new Label
                {
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Width = 500,
                    Height = 28
                };

            var validation =
                new Label
                {
                    ForeColor = CMuted,
                    Width = 500,
                    Height = 24
                };

            int GetEditorContentWidth()
            {
                int scrollbarReserve =
                    body.VerticalScroll.Visible
                        ? SystemInformation.VerticalScrollBarWidth + 18
                        : 18;

                return Math.Max(
                    500,
                    body.ClientSize.Width -
                    body.Padding.Horizontal -
                    scrollbarReserve);
            }

            Panel MakeField(
                string labelText,
                Control control)
            {
                var panel =
                    new Panel
                    {
                        Width =
                            GetEditorContentWidth(),
                        Height = 78,
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
                                10)
                    };

                var label =
                    new Label
                    {
                        Text = labelText,
                        ForeColor = CText,
                        Font =
                            new Font(
                                "Segoe UI Semibold",
                                8.8F,
                                FontStyle.Bold),
                        Location =
                            new Point(
                                14,
                                10),
                        Size =
                            new Size(
                                300,
                                22)
                    };

                control.Location =
                    new Point(
                        14,
                        38);

                control.Width =
                    Math.Max(
                        180,
                        panel.ClientSize.Width - 28);

                control.Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Left |
                    AnchorStyles.Right;

                panel.Controls.Add(
                    label);

                panel.Controls.Add(
                    control);

                return panel;
            }

            var previewPanel =
                new Panel
                {
                    Width =
                        GetEditorContentWidth(),
                    Height = 120,
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
                            10)
                };

            preview.Location =
                new Point(
                    14,
                    18);

            previewName.Location =
                new Point(
                    108,
                    24);

            previewName.Width =
                Math.Max(
                    160,
                    previewPanel.ClientSize.Width - 108 - 190);

            validation.Location =
                new Point(
                    108,
                    58);

            validation.Width =
                Math.Max(
                    160,
                    previewPanel.ClientSize.Width - 108 - 190);

            var selectItem =
                CreateEditorActionButton(
                    "SELECT ITEM");

            selectItem.Size =
                new Size(
                    150,
                    34);

            selectItem.Location =
                new Point(
                    Math.Max(
                        330,
                        previewPanel.ClientSize.Width -
                        selectItem.Width -
                        16),
                    44);

            selectItem.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            previewPanel.Controls.Add(
                preview);

            previewPanel.Controls.Add(
                previewName);

            previewPanel.Controls.Add(
                validation);

            previewPanel.Controls.Add(
                selectItem);

            var actions =
                new Panel
                {
                    Width =
                        GetEditorContentWidth(),
                    Height = 58,
                    BackColor =
                        Color.FromArgb(
                            24,
                            24,
                            24)
                };

            var save =
                CreateEditorActionButton(
                    "SAVE");

            save.Location =
                new Point(
                    14,
                    12);

            save.Size =
                new Size(
                    120,
                    34);

            actions.Controls.Add(
                save);

            if (existing != null)
            {
                var delete =
                    CreateEditorActionButton(
                        "DELETE");

                delete.Location =
                    new Point(
                        148,
                        12);

                delete.Size =
                    new Size(
                        120,
                        34);

                delete.Click +=
                    async (_, _) =>
                    {
                        DialogResult answer =
                            MessageBox.Show(
                                $"Delete ItemDisplay entry for ItemID {existing.ItemId}?",
                                "Delete ItemDisplay",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning);

                        if (answer !=
                            DialogResult.Yes)
                        {
                            return;
                        }

                        try
                        {
                            state.Service.DeleteAt(
                                existing.RowIndex);

                            overlay.Dispose();

                            await RefreshItemDisplayGridAsync(
                                state);
                        }
                        catch (Exception ex)
                        {
                            ShowEditorError(
                                "Delete ItemDisplay",
                                ex);
                        }
                    };

                actions.Controls.Add(
                    delete);
            }

            void RefreshPreview()
            {
                preview.Image?.Dispose();
                preview.Image = null;

                if (!uint.TryParse(
                    itemId.Text.Trim(),
                    out uint id))
                {
                    previewName.Text =
                        "Invalid ItemID";

                    validation.Text =
                        "Enter a valid UInt32 ItemID.";

                    validation.ForeColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);

                    return;
                }

                if (!state.Items.TryGetValue(
                    id,
                    out ItemDisplayItemReference? item))
                {
                    previewName.Text =
                        $"ItemID {id} not found in ItemList.xml";

                    validation.Text =
                        "This ItemDisplay entry would reference an unknown item.";

                    validation.ForeColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);

                    return;
                }

                previewName.Text =
                    $"{item.ItemId} — {item.Name}";

                validation.Text =
                    $"Icon {item.IconId} | ItemList Section {item.Section}";

                validation.ForeColor =
                    Color.FromArgb(
                        125,
                        220,
                        140);

                preview.Image =
                    ImageDatabasePreview
                        .TryLoadInterfaceIcon(
                            item.IconId,
                            "Item");
            }

            itemId.TextChanged +=
                (_, _) =>
                    RefreshPreview();

            selectItem.Click +=
                (_, _) =>
                    ShowItemDisplayItemPicker(
                        state,
                        overlay,
                        selected =>
                        {
                            itemId.Text =
                                selected.ItemId
                                    .ToString(
                                        CultureInfo.InvariantCulture);

                            section.Text =
                                selected.Section
                                    .ToString(
                                        CultureInfo.InvariantCulture);
                        });

            save.Click +=
                async (_, _) =>
                {
                    if (!uint.TryParse(
                        itemId.Text.Trim(),
                        out uint id) ||
                        !state.Items.ContainsKey(
                            id))
                    {
                        MessageBox.Show(
                            "Choose a valid ItemID from ItemList.xml.",
                            "ItemDisplay",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    if (!uint.TryParse(
                        section.Text.Trim(),
                        out uint sectionValue))
                    {
                        MessageBox.Show(
                            "Section must be a valid UInt32.",
                            "ItemDisplay",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    try
                    {
                        if (existing == null)
                        {
                            state.Service.Add(
                                sectionValue,
                                id);
                        }
                        else
                        {
                            state.Service.UpdateAt(
                                existing.RowIndex,
                                sectionValue,
                                id);
                        }

                        overlay.Dispose();

                        await RefreshItemDisplayGridAsync(
                            state);
                    }
                    catch (Exception ex)
                    {
                        ShowEditorError(
                            "Save ItemDisplay",
                            ex);
                    }
                };

            void LayoutEntryEditor()
            {
                int width =
                    GetEditorContentWidth();

                previewPanel.Width =
                    width;

                actions.Width =
                    width;

                previewName.Width =
                    Math.Max(
                        140,
                        width - 108 - 190);

                validation.Width =
                    Math.Max(
                        140,
                        width - 108 - 190);

                selectItem.Location =
                    new Point(
                        Math.Max(
                            330,
                            width -
                            selectItem.Width -
                            16),
                        44);

                foreach (Control child
                         in body.Controls)
                {
                    if (child == previewPanel ||
                        child == actions)
                    {
                        continue;
                    }

                    if (child is Panel field &&
                        field.Height == 78)
                    {
                        field.Width =
                            width;
                    }
                }
            }

            body.SizeChanged +=
                (_, _) =>
                    LayoutEntryEditor();

            body.Controls.Add(
                previewPanel);

            body.Controls.Add(
                MakeField(
                    "Item ID (dwDispID)",
                    itemId));

            body.Controls.Add(
                MakeField(
                    "Section (nItemS)",
                    section));

            body.Controls.Add(
                actions);

            LayoutEntryEditor();

            overlay.Controls.Add(
                body);

            overlay.Controls.Add(
                header);

            RefreshPreview();
        }

        private async void ShowItemDisplayItemPicker(
            ItemDisplayBrowserState state,
            Control parentOverlay,
            Action<ItemDisplayItemReference> onSelected)
        {
            var picker =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18,18,18)
                };

            parentOverlay.Controls.Add(picker);
            picker.BringToFront();
            picker.Controls.Add(
                new EditorLoadingView(
                    "Loading Item Picker",
                    "Preparing ItemList search cards and cached item icons."));
            await Task.Yield();
            if(picker.IsDisposed)return;
            picker.Controls.Clear();

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 50,
                    BackColor =
                        Color.FromArgb(
                            31,
                            31,
                            31)
                };

            var title =
                new Label
                {
                    Text =
                        "Select Item from ItemList.xml",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            11F,
                            FontStyle.Bold),
                    Dock = DockStyle.Fill,
                    Padding =
                        new Padding(
                            16,
                            0,
                            10,
                            0),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            var close =
                CreateEditorActionButton(
                    "CLOSE");

            close.Dock =
                DockStyle.Right;

            close.Width = 90;

            close.Click +=
                (_, _) =>
                    picker.Dispose();

            header.Controls.Add(
                title);

            header.Controls.Add(
                close);

            close.BringToFront();

            var searchHost =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 58,
                    BackColor = CEditor,
                    Padding =
                        new Padding(
                            18,
                            12,
                            18,
                            8)
                };

            var search =
                new TextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor =
                        Color.FromArgb(
                            12,
                            12,
                            12),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    PlaceholderText =
                        "Search ItemID or Item Name..."
                };

            searchHost.Controls.Add(
                search);

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
                            14,
                            10,
                            14,
                            30)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            int generation = 0;

            async Task RefreshAsync()
            {
                int myGeneration =
                    ++generation;

                string query =
                    search.Text.Trim();

                // No result limit: searches the complete ItemList reference
                // dictionary. Rendering is progressive in batches.
                ItemDisplayItemReference[] matches =
                    await Task.Run(
                        () =>
                            state.Items
                                .Values
                                .Where(
                                    item =>
                                        query.Length == 0 ||
                                        item.ItemId
                                            .ToString(
                                                CultureInfo.InvariantCulture)
                                            .Contains(
                                                query,
                                                StringComparison.OrdinalIgnoreCase) ||
                                        item.Name.Contains(
                                            query,
                                            StringComparison.OrdinalIgnoreCase))
                                .OrderBy(
                                    item => item.ItemId)
                                .ToArray());

                if (picker.IsDisposed ||
                    myGeneration != generation)
                {
                    return;
                }

                results.SuspendLayout();
                DisposeChildImages(results);
                results.Controls.Clear();
                results.ResumeLayout(true);

                const int batch = 30;

                for (int i = 0;
                     i < matches.Length;
                     i += batch)
                {
                    if (picker.IsDisposed ||
                        myGeneration != generation)
                    {
                        return;
                    }

                    results.SuspendLayout();

                    foreach (ItemDisplayItemReference item
                             in matches
                                .Skip(i)
                                .Take(batch))
                    {
                        int width =
                            Math.Max(
                                540,
                                results.ClientSize.Width -
                                results.Padding.Horizontal -
                                SystemInformation.VerticalScrollBarWidth -
                                12);

                        var card =
                            new Panel
                            {
                                Width = width,
                                Height = 74,
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
                                        10,
                                        9),
                                Size =
                                    new Size(
                                        54,
                                        54),
                                SizeMode =
                                    PictureBoxSizeMode.Zoom,
                                BackColor =
                                    Color.FromArgb(
                                        8,
                                        8,
                                        8),
                                Image =
                                    ImageDatabasePreview
                                        .TryLoadInterfaceIcon(
                                            item.IconId,
                                            "Item")
                            };

                        var name =
                            new Label
                            {
                                Text =
                                    $"{item.ItemId} — {item.Name}",
                                ForeColor = CText,
                                Font =
                                    new Font(
                                        "Segoe UI Semibold",
                                        9F,
                                        FontStyle.Bold),
                                Location =
                                    new Point(
                                        76,
                                        12),
                                Size =
                                    new Size(
                                        Math.Max(
                                            180,
                                            width - 240),
                                        24),
                                AutoEllipsis = true
                            };

                        var details =
                            new Label
                            {
                                Text =
                                    $"Icon: {item.IconId} | Section: {item.Section}",
                                ForeColor = CMuted,
                                Font =
                                    new Font(
                                        "Segoe UI",
                                        7.8F),
                                Location =
                                    new Point(
                                        76,
                                        39),
                                Size =
                                    new Size(
                                        Math.Max(
                                            180,
                                            width - 240),
                                        20)
                            };

                        var select =
                            CreateEditorActionButton(
                                "SELECT");

                        select.Size =
                            new Size(
                                110,
                                34);

                        select.Location =
                            new Point(
                                width - 126,
                                20);

                        select.Click +=
                            (_, _) =>
                            {
                                onSelected(
                                    item);

                                picker.Dispose();
                            };

                        card.Controls.Add(
                            icon);

                        card.Controls.Add(
                            name);

                        card.Controls.Add(
                            details);

                        card.Controls.Add(
                            select);

                        results.Controls.Add(
                            card);
                    }

                    results.ResumeLayout(true);

                    await Task.Yield();
                }
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 180
                };

            timer.Tick +=
                async (_, _) =>
                {
                    timer.Stop();

                    await RefreshAsync();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            picker.Disposed +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                };

            picker.Controls.Add(
                results);

            picker.Controls.Add(
                searchHost);

            picker.Controls.Add(
                header);



            _ =
                RefreshAsync();
        }
    }
}
