using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private sealed class AccessoryBrowseState
        {
            public required ItemAccessoryEditorService Service { get; init; }
            public required TextBox Search { get; init; }
            public required Label Count { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
        }

        private sealed class AccessoryEditState
        {
            public required ItemAccessoryEditorService Service { get; init; }
            public required ItemAccessoryRecord Working { get; init; }

            public bool IsNew { get; init; }
            public bool Dirty { get; set; }

            public required TextBox AccessoryId { get; init; }
            public required NumericUpDown GainOption { get; init; }
            public required NumericUpDown RenewalChanges { get; init; }
            public required Label IdStatus { get; init; }

            public required List<AccessoryStatEditorControls> Slots { get; init; }
        }

        private sealed class AccessoryStatEditorControls
        {
            public required int SlotIndex { get; init; }
            public required DarkComboBox Stat { get; init; }
            public required TextBox Min { get; init; }
            public required TextBox Max { get; init; }
            public required Label Unit { get; init; }
        }

        private async void OpenItemAccessoryBrowser(
            string xmlPath)
        {
            string full =
                Path.GetFullPath(xmlPath);

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                full,
                                StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab =
                    existing;

                return;
            }

            var page =
                CreateDarkTab(
                    "ItemAcessorys.xml");

            page.Name =
                full;

            var loading =
                new EditorLoadingView(
                    "Loading Item Accessory Database",
                    "Preparing ItemAcessorys.xml, ItemList links and stat records before displaying cards.");

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            UpdateEditorEmptyState();

            ItemAccessoryEditorService service;

            try
            {
                service =
                    await System.Threading.Tasks.Task.Run(
                        () =>
                        {
                            var loaded =
                                new ItemAccessoryEditorService();

                            loaded.Load(
                                full,
                                Path.Combine(
                                    Path.GetDirectoryName(full)
                                    ?? string.Empty,
                                    "ItemList.xml"));

                            return loaded;
                        });
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "ItemAcessorys.xml could not be loaded",
                        ex.Message);
                }

                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();
            page.SuspendLayout();

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 112,
                    BackColor = CPanel
                };

            var title =
                new Label
                {
                    Text =
                        "Item Accessory Database",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            12F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            16,
                            10),
                    Size =
                        new Size(
                            380,
                            28)
                };

            var search =
                new TextBox
                {
                    Location =
                        new Point(
                            16,
                            47),
                    Size =
                        new Size(
                            480,
                            28),
                    BackColor =
                        Color.FromArgb(
                            14,
                            14,
                            14),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    PlaceholderText =
                        "Search Accessory ID, ItemID, item name or stat..."
                };

            var create =
                CreateEditorActionButton(
                    "NEW ACCESSORY");

            create.Location =
                new Point(
                    510,
                    47);

            create.Size =
                new Size(
                    145,
                    28);

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            7.8F),
                    Location =
                        new Point(
                            16,
                            82),
                    Size =
                        new Size(
                            680,
                            20)
                };

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection =
                        FlowDirection.TopDown,
                    WrapContents = false,
                    Padding =
                        new Padding(
                            12),
                    BackColor = CEditor
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 180
                };

            var state =
                new AccessoryBrowseState
                {
                    Service = service,
                    Search = search,
                    Count = count,
                    Results = results,
                    SearchTimer = timer
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();

                    RefreshAccessoryBrowser(
                        page,
                        state);
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            create.Click +=
                (_, _) =>
                {
                    OpenAccessoryEditTab(
                        service,
                        service.CreateNewWorking(),
                        isNew: true);
                };

            page.Disposed +=
                (_, _) =>
                    timer.Dispose();

            page.Tag = state;

            header.Controls.Add(title);
            header.Controls.Add(search);
            header.Controls.Add(create);
            header.Controls.Add(count);

            page.Controls.Add(results);
            page.Controls.Add(header);

            loading.BringToFront();
            page.ResumeLayout(true);
            loading.Refresh();

            editorTabs.SelectedTab = page;
            RefreshAccessoryBrowser(page, state);

            page.Controls.Remove(loading);
            loading.Dispose();
            page.PerformLayout();
            page.Update();
        }

        private void RefreshAccessoryBrowser(
            TabPage page,
            AccessoryBrowseState state)
        {
            IReadOnlyList<ItemAccessoryRecord> rows =
                state.Service.Search(
                    state.Search.Text,
                    40);

            int total =
                state.Service.CountSearch(
                    state.Search.Text);

            DisposeChildImages(
                state.Results);

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (ItemAccessoryRecord row in rows)
            {
                state.Results.Controls.Add(
                    CreateAccessoryDatabaseCard(
                        state.Service,
                        row));
            }

            state.Results.ResumeLayout(
                true);

            state.Count.Text =
                $"Accessory records: {state.Service.TotalRecords:N0} | " +
                $"Distinct IDs: {state.Service.DistinctAccessoryIds:N0} | " +
                $"Results: {total:N0}" +
                (total > rows.Count
                    ? $" | Showing first {rows.Count:N0}"
                    : string.Empty);
        }

        private Control CreateAccessoryDatabaseCard(
            ItemAccessoryEditorService service,
            ItemAccessoryRecord record)
        {
            var card =
                new Panel
                {
                    Width = 720,
                    Height = 92,
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

            card.Paint +=
                (_, e) =>
                {
                    using var p =
                        new Pen(
                            Color.FromArgb(
                                50,
                                50,
                                50));

                    e.Graphics.DrawRectangle(
                        p,
                        0,
                        0,
                        card.Width - 1,
                        card.Height - 1);
                };

            ItemAccessoryLinkedItem? linked =
                record.PrimaryLinkedItem;

            var icon =
                new PictureBox
                {
                    Location =
                        new Point(
                            12,
                            14),
                    Size =
                        new Size(
                            56,
                            56),
                    SizeMode =
                        PictureBoxSizeMode.Zoom,
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    Image =
                        linked != null
                            ? GetItemIconPreview(
                                linked.IconId)
                            : null
                };

            var accessoryId =
                new Label
                {
                    Text =
                        $"Accessory ID  {record.AccessoryId}",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Consolas",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            82,
                            10),
                    Size =
                        new Size(
                            250,
                            22)
                };

            string itemTitle =
                linked == null
                    ? "No ItemList item linked to this Accessory ID"
                    : $"{linked.ItemName}   [ItemID {linked.ItemId}]";

            var itemName =
                new Label
                {
                    Text = itemTitle,
                    ForeColor =
                        linked == null
                            ? Color.FromArgb(
                                255,
                                175,
                                90)
                            : Color.FromArgb(
                                225,
                                225,
                                225),
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.8F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            82,
                            34),
                    Size =
                        new Size(
                            440,
                            22),
                    AutoEllipsis = true
                };

            string linkedMore =
                record.LinkedItems.Count > 1
                    ? $" | +{record.LinkedItems.Count - 1} other linked ItemList item(s)"
                    : string.Empty;

            var meta =
                new Label
                {
                    Text =
                        $"Stats gained: {record.GainOption} | " +
                        $"Renewal Changes: {record.RenewalChanges}" +
                        linkedMore,
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            7.7F),
                    Location =
                        new Point(
                            82,
                            59),
                    Size =
                        new Size(
                            485,
                            20),
                    AutoEllipsis = true
                };

            var edit =
                CreateEditorActionButton(
                    "EDIT");

            edit.Location =
                new Point(
                    600,
                    27);

            edit.Size =
                new Size(
                    100,
                    34);

            edit.Click +=
                (_, _) =>
                {
                    OpenAccessoryEditTab(
                        service,
                        record.CloneWorking(),
                        isNew: false);
                };

            card.Controls.Add(icon);
            card.Controls.Add(accessoryId);
            card.Controls.Add(itemName);
            card.Controls.Add(meta);
            card.Controls.Add(edit);

            return card;
        }

        private async void OpenAccessoryEditTab(
            ItemAccessoryEditorService service,
            ItemAccessoryRecord working,
            bool isNew)
        {
            string tabName =
                isNew
                    ? "New Accessory [Edit]"
                    : $"Accessory {working.AccessoryId} [Edit]";

            var page =
                CreateDarkTab(
                    tabName);

            var opening =
                new EditorLoadingView(
                    "Loading Accessory Editor",
                    "Preparing accessory stats, linked ItemList information and editable stat slots.");
            page.Controls.Add(opening);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            await System.Threading.Tasks.Task.Yield();
            if(page.IsDisposed)return;
            page.SuspendLayout();

            var toolbar =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 54,
                    BackColor = CPanel
                };

            var save =
                CreateEditorActionButton(
                    "SAVE");

            save.Location =
                new Point(
                    12,
                    10);

            save.Size =
                new Size(
                    90,
                    32);

            var view =
                CreateEditorActionButton(
                    "VIEW XML BLOCK");

            view.Location =
                new Point(
                    112,
                    10);

            view.Size =
                new Size(
                    145,
                    32);

            var body =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection =
                        FlowDirection.LeftToRight,
                    WrapContents = true,
                    Padding =
                        new Padding(
                            14),
                    BackColor = CEditor
                };

            DarkUi.ApplyDarkScrollBar(
                body);

            var idBox =
                CreateAccessoryTextBox(
                    working.AccessoryId == 0
                        ? string.Empty
                        : working.AccessoryId.ToString(
                            CultureInfo.InvariantCulture));

            var idStatus =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            7.8F,
                            FontStyle.Bold),
                    AutoSize = false
                };

            var gain =
                new NumericUpDown
                {
                    Minimum = 0,
                    Maximum =
                        ItemAccessoryEditorService.SlotCount,
                    Value =
                        Math.Max(
                            0,
                            Math.Min(
                                ItemAccessoryEditorService.SlotCount,
                                working.GainOption)),
                    BackColor =
                        Color.FromArgb(
                            13,
                            13,
                            13),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Font =
                        new Font(
                            "Segoe UI",
                            9F)
                };

            var renewal =
                new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = ushort.MaxValue,
                    Value =
                        Math.Max(
                            0,
                            Math.Min(
                                ushort.MaxValue,
                                working.RenewalChanges)),
                    BackColor =
                        Color.FromArgb(
                            13,
                            13,
                            13),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Font =
                        new Font(
                            "Segoe UI",
                            9F)
                };

            Panel idField =
                CreateAccessoryHeaderField(
                    "Accessory ID",
                    "Both index_Accessory1 and index_Accessory are saved with this same ID.",
                    idBox);

            idStatus.Location =
                new Point(
                    12,
                    69);

            idStatus.Size =
                new Size(
                    315,
                    19);

            idField.Controls.Add(
                idStatus);

            Panel gainField =
                CreateAccessoryHeaderField(
                    "Stats gained by equipment",
                    "Gain_Option. Maximum 16.",
                    gain);

            Panel renewalField =
                CreateAccessoryHeaderField(
                    "Renewal Changes",
                    "Changeable_Option_Number — how many Renewal changes/rolls this definition allows.",
                    renewal);

            body.Controls.Add(idField);
            body.Controls.Add(gainField);
            body.Controls.Add(renewalField);

            var editors =
                new List<AccessoryStatEditorControls>();

            for (int i = 0;
                 i < ItemAccessoryEditorService.SlotCount;
                 i++)
            {
                ItemAccessoryStatSlot slot =
                    working.Slots[i];

                Panel slotPanel =
                    CreateAccessoryStatBlock(
                        i,
                        slot,
                        out AccessoryStatEditorControls controls);

                editors.Add(controls);
                body.Controls.Add(slotPanel);
            }

            var state =
                new AccessoryEditState
                {
                    Service = service,
                    Working = working,
                    IsNew = isNew,
                    AccessoryId = idBox,
                    GainOption = gain,
                    RenewalChanges = renewal,
                    IdStatus = idStatus,
                    Slots = editors
                };

            void markDirty()
            {
                state.Dirty = true;

                page.Text =
                    isNew
                        ? "New Accessory [Unsaved]"
                        : $"Accessory {working.AccessoryId} [Unsaved]";
            }

            idBox.TextChanged +=
                (_, _) =>
                {
                    UpdateAccessoryIdStatus(
                        state);

                    markDirty();
                };

            gain.ValueChanged +=
                (_, _) =>
                {
                    ApplyGainOptionVisualState(
                        state);

                    markDirty();
                };

            renewal.ValueChanged +=
                (_, _) =>
                    markDirty();

            foreach (AccessoryStatEditorControls controls in editors)
            {
                controls.Stat.SelectedIndexChanged +=
                    (_, _) =>
                    {
                        AccessoryStatDefinition definition =
                            GetSelectedAccessoryStat(
                                controls.Stat);

                        controls.Unit.Text =
                            definition.IsPercent
                                ? definition.UsesHundredScale
                                    ? "%  — XML stores value ×100"
                                    : "%  — enter the percentage value directly"
                                : string.Empty;

                        // Reformat existing raw values when switching stat.
                        markDirty();
                    };

                controls.Min.TextChanged +=
                    (_, _) =>
                        markDirty();

                controls.Max.TextChanged +=
                    (_, _) =>
                        markDirty();
            }

            save.Click +=
                (_, _) =>
                {
                    try
                    {
                        ReadAccessoryFormIntoWorking(
                            state);

                        int duplicateCount =
                            service.CountById(
                                working.AccessoryId);

                        if (isNew &&
                            duplicateCount > 0)
                        {
                            DialogResult duplicate =
                                MessageBox.Show(
                                    $"Accessory ID {working.AccessoryId} já possui " +
                                    $"{duplicateCount} definition(s) no XML.\r\n\r\n" +
                                    "Queres criar outra definição com o mesmo ID?",
                                    "Accessory ID already exists",
                                    MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning);

                            if (duplicate !=
                                DialogResult.Yes)
                            {
                                return;
                            }
                        }

                        service.Save(
                            working,
                            isNew);

                        state.Dirty = false;

                        page.Text =
                            $"Accessory {working.AccessoryId} [Saved]";

                        LinkedItemReferenceService.InvalidateShared();

                        RefreshOpenAccessoryBrowsers(
                            service.FilePath);

                        MessageBox.Show(
                            $"Accessory {working.AccessoryId} guardado.\r\n\r\n" +
                            $"Stats gained: {working.GainOption}\r\n" +
                            $"Renewal Changes: {working.RenewalChanges}",
                            "Item Accessory Editor",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "Accessory validation",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };

            view.Click +=
                (_, _) =>
                {
                    try
                    {
                        ReadAccessoryFormIntoWorking(
                            state);

                        ShowAccessoryXmlPreview(
                            working,
                            isNew,
                            service);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "Accessory validation",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };

            toolbar.Controls.Add(save);
            toolbar.Controls.Add(view);

            page.Controls.Add(body);
            page.Controls.Add(toolbar);

            page.Tag = state;

            UpdateAccessoryIdStatus(state);
            ApplyGainOptionVisualState(state);

            opening.BringToFront();
            page.ResumeLayout(true);
            opening.Refresh();

            page.Controls.Remove(opening);
            opening.Dispose();
            page.PerformLayout();
            page.Update();
        }

        private Panel CreateAccessoryHeaderField(
            string title,
            string hint,
            Control editor)
        {
            var panel =
                new Panel
                {
                    Width = 342,
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
                            10,
                            10)
                };

            var label =
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.6F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            11,
                            8),
                    Size =
                        new Size(
                            290,
                            20)
                };

            editor.Location =
                new Point(
                    11,
                    34);

            editor.Size =
                new Size(
                    318,
                    28);

            var help =
                CreateHelpBubble(
                    hint);

            help.Location =
                new Point(
                    311,
                    6);

            panel.Controls.Add(label);
            panel.Controls.Add(editor);
            panel.Controls.Add(help);

            return panel;
        }

        private Panel CreateAccessoryStatBlock(
            int slotIndex,
            ItemAccessoryStatSlot slot,
            out AccessoryStatEditorControls controls)
        {
            var panel =
                new Panel
                {
                    Width = 342,
                    Height = 150,
                    BackColor =
                        Color.FromArgb(
                            29,
                            29,
                            29),
                    Margin =
                        new Padding(
                            0,
                            0,
                            10,
                            10)
                };

            var title =
                new Label
                {
                    Text =
                        $"STAT SLOT {slotIndex + 1}",
                    ForeColor =
                        Color.FromArgb(
                            205,
                            205,
                            205),
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            10,
                            7),
                    Size =
                        new Size(
                            180,
                            20)
                };

            var stat =
                new DarkComboBox
                {
                    Location =
                        new Point(
                            10,
                            32),
                    Size =
                        new Size(
                            320,
                            28)
                };

            foreach (AccessoryStatDefinition definition
                     in AccessoryStatCatalog.All)
            {
                stat.Items.Add(
                    new DarkComboOption
                    {
                        Value =
                            definition.Id.ToString(
                                CultureInfo.InvariantCulture),
                        Label =
                            string.IsNullOrWhiteSpace(
                                definition.Code)
                                ? definition.Name
                                : $"{definition.Code} — {definition.Name}"
                    });
            }

            DarkComboOption? selected =
                stat.Items
                    .OfType<DarkComboOption>()
                    .FirstOrDefault(
                        x =>
                            x.Value ==
                            slot.StatId.ToString(
                                CultureInfo.InvariantCulture));

            if (selected != null)
                stat.SelectedItem = selected;
            else
                stat.SelectedIndex = 0;

            var minLabel =
                new Label
                {
                    Text = "MIN",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            7.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            10,
                            69),
                    Size =
                        new Size(
                            45,
                            18)
                };

            var maxLabel =
                new Label
                {
                    Text = "MAX",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            7.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            174,
                            69),
                    Size =
                        new Size(
                            45,
                            18)
                };

            var min =
                CreateAccessoryTextBox(
                    AccessoryStatCatalog.FormatUiValue(
                        slot.StatId,
                        slot.MinRaw));

            min.Location =
                new Point(
                    10,
                    89);

            min.Size =
                new Size(
                    150,
                    27);

            var max =
                CreateAccessoryTextBox(
                    AccessoryStatCatalog.FormatUiValue(
                        slot.StatId,
                        slot.MaxRaw));

            max.Location =
                new Point(
                    174,
                    89);

            max.Size =
                new Size(
                    156,
                    27);

            AccessoryStatDefinition current =
                AccessoryStatCatalog.Get(
                    slot.StatId);

            var unit =
                new Label
                {
                    Text =
                        current.IsPercent
                            ? current.UsesHundredScale
                                ? "%  — XML stores value ×100"
                                : "%  — enter percentage directly"
                            : string.Empty,
                    ForeColor =
                        Color.FromArgb(
                            125,
                            220,
                            140),
                    Font =
                        new Font(
                            "Segoe UI",
                            7.3F),
                    Location =
                        new Point(
                            10,
                            121),
                    Size =
                        new Size(
                            310,
                            18)
                };

            var help =
                CreateHelpBubble(
                    "s_nOptIdx selects the stat. " +
                    "s_nMin and s_nMax define its minimum/maximum. " +
                    "Critical Damage and Attack Speed are stored ×100: " +
                    "22 becomes 2200 and 19.63 becomes 1963. " +
                    "For new Accessory definitions, hidden <unknow> is written as 0.");

            help.Location =
                new Point(
                    311,
                    6);

            panel.Controls.Add(title);
            panel.Controls.Add(stat);
            panel.Controls.Add(minLabel);
            panel.Controls.Add(maxLabel);
            panel.Controls.Add(min);
            panel.Controls.Add(max);
            panel.Controls.Add(unit);
            panel.Controls.Add(help);

            controls =
                new AccessoryStatEditorControls
                {
                    SlotIndex = slotIndex,
                    Stat = stat,
                    Min = min,
                    Max = max,
                    Unit = unit
                };

            return panel;
        }

        private static TextBox CreateAccessoryTextBox(
            string text)
        {
            return new TextBox
            {
                Text = text,
                BackColor =
                    Color.FromArgb(
                        12,
                        12,
                        12),
                ForeColor =
                    Color.FromArgb(
                        240,
                        240,
                        240),
                BorderStyle =
                    BorderStyle.FixedSingle,
                Font =
                    new Font(
                        "Consolas",
                        8.8F)
            };
        }

        private void UpdateAccessoryIdStatus(
            AccessoryEditState state)
        {
            if (!uint.TryParse(
                state.AccessoryId.Text.Trim(),
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

            int count =
                state.Service.CountById(id);

            if (count == 0)
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
                state.IdStatus.Text =
                    $"ID EXISTS — {count} definition(s)";

                state.IdStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);
            }
        }

        private void ApplyGainOptionVisualState(
            AccessoryEditState state)
        {
            // IMPORTANT:
            // Gain_Option is NOT the number of editable Option definitions.
            //
            // The real supplied ItemAcessorys.xml contains records where
            // Gain_Option is smaller than the number of non-zero stat slots.
            // Example patterns in the original data:
            //   Gain_Option = 3
            //   but 8 or more <s_nOptIdx> entries can be configured.
            //
            // Therefore ALL 16 physical stat slots must remain editable.
            foreach (AccessoryStatEditorControls slot
                     in state.Slots)
            {
                slot.Stat.Enabled = true;
                slot.Min.Enabled = true;
                slot.Max.Enabled = true;
            }
        }

        private void ReadAccessoryFormIntoWorking(
            AccessoryEditState state)
        {
            if (!uint.TryParse(
                state.AccessoryId.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint accessoryId) ||
                accessoryId == 0)
            {
                throw new InvalidDataException(
                    "Accessory ID inválido.");
            }

            state.Working.AccessoryId =
                accessoryId;

            state.Working.GainOption =
                (int)state.GainOption.Value;

            state.Working.RenewalChanges =
                (int)state.RenewalChanges.Value;

            for (int i = 0;
                 i < ItemAccessoryEditorService.SlotCount;
                 i++)
            {
                AccessoryStatEditorControls controls =
                    state.Slots[i];

                ItemAccessoryStatSlot slot =
                    state.Working.Slots[i];

                AccessoryStatDefinition definition =
                    GetSelectedAccessoryStat(
                        controls.Stat);

                slot.StatId =
                    definition.Id;

                if (state.IsNew)
                    slot.Unknown = 0;

                slot.MinRaw =
                    AccessoryStatCatalog.ParseUiValue(
                        definition.Id,
                        controls.Min.Text,
                        $"Stat Slot {i + 1} MIN");

                slot.MaxRaw =
                    AccessoryStatCatalog.ParseUiValue(
                        definition.Id,
                        controls.Max.Text,
                        $"Stat Slot {i + 1} MAX");

                if (slot.MinRaw > slot.MaxRaw)
                {
                    throw new InvalidDataException(
                        $"Stat Slot {i + 1}: MIN não pode ser maior que MAX.");
                }
            }
        }

        private static AccessoryStatDefinition GetSelectedAccessoryStat(
            DarkComboBox combo)
        {
            if (combo.SelectedItem is not
                DarkComboOption option ||
                !int.TryParse(
                    option.Value,
                    out int id))
            {
                return AccessoryStatCatalog.Get(0);
            }

            return AccessoryStatCatalog.Get(id);
        }

        private static void SelectDarkComboValue(
            DarkComboBox combo,
            string value)
        {
            DarkComboOption? item =
                combo.Items
                    .OfType<DarkComboOption>()
                    .FirstOrDefault(
                        x =>
                            x.Value == value);

            if (item != null)
                combo.SelectedItem = item;
        }

        private void ShowAccessoryXmlPreview(
            ItemAccessoryRecord working,
            bool isNew,
            ItemAccessoryEditorService service)
        {
            string xml =
                service.BuildPreviewXml(
                    working,
                    isNew);

            var page =
                CreateDarkTab(
                    $"Accessory {working.AccessoryId} XML");

            var text =
                new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle =
                        BorderStyle.None,
                    BackColor =
                        Color.FromArgb(
                            12,
                            12,
                            12),
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Consolas",
                            9F),
                    Text = xml
                };

            DarkUi.ApplyDarkScrollBar(
                text);

            page.Controls.Add(text);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
        }

        private void RefreshOpenAccessoryBrowsers(
            string filePath)
        {
            foreach (TabPage tab in
                     editorTabs.TabPages
                         .Cast<TabPage>()
                         .ToArray())
            {
                if (!string.Equals(
                    tab.Name,
                    Path.GetFullPath(filePath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AccessoryBrowseState? state =
                    FindAccessoryBrowseState(
                        tab);

                if (state == null)
                    continue;

                state.Service.Load(
                    state.Service.FilePath,
                    state.Service.ItemListPath);

                RefreshAccessoryBrowser(
                    tab,
                    state);
            }
        }

        private static AccessoryBrowseState? FindAccessoryBrowseState(
            TabPage tab)
        {
            foreach (Control control in tab.Controls)
            {
                if (control is FlowLayoutPanel results)
                {
                    // State is not stored on the result control in the older build.
                    // Browser refresh remains best-effort.
                }
            }

            return tab.Tag as AccessoryBrowseState;
        }
    }
}
