using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private enum ItemMakingViewKind
        {
            NpcList,
            Npc,
            Abar,
            SubCategory,
            Craft
        }

        private sealed record ItemMakingViewContext(
            ItemMakingViewKind Kind,
            XElement? Npc = null,
            XElement? Abar = null,
            XElement? SubCategory = null,
            XElement? Craft = null);

        private sealed class ItemMakingEditorState
        {
            public required ItemMakingEditorService Service { get; init; }
            public required EditorReferenceCatalogService References { get; init; }
            public required XDocument Working { get; init; }
            public required FlowLayoutPanel Body { get; init; }
            public required Label Breadcrumb { get; init; }
            public required Label Status { get; init; }
            public required Button Back { get; init; }
            public required Button Add { get; init; }

            public bool Dirty { get; set; }

            // Keeps the NPC/ItemMaking search when entering an NPC and returning
            // with BACK, so the user does not lose the current result list.
            public string NpcSearchText { get; set; } = string.Empty;

            public System.Collections.Generic.List<ItemMakingViewContext> History { get; } = new();
            public ItemMakingViewContext Current { get; set; } =
                new(ItemMakingViewKind.NpcList);
        }

        private async void OpenItemMakingBrowser(string xmlPath)
        {
            string fullPath = Path.GetFullPath(xmlPath);

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(x =>
                        string.Equals(
                            x.Name,
                            "ITEMMAKING:" + fullPath,
                            StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page = CreateDarkTab("ItemMaking.xml");
            page.Name = "ITEMMAKING:" + fullPath;

            var loading =
                new EditorLoadingView(
                    "Loading ItemMaking",
                    "Preparing crafts, NPCs, items, models and editor references before content is displayed.");

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            ItemMakingEditorService service;
            EditorReferenceCatalogService references;

            try
            {
                service =
                    await EditorPreloadService
                        .GetItemMakingServiceAsync(
                            fullPath);

                references =
                    await EditorPreloadService
                        .GetReferencesAsync(
                            fullPath);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                    loading.SetError("ItemMaking.xml could not be loaded",ex.Message);
                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = Color.FromArgb(27, 27, 27)
            };

            var save = CreateEditorActionButton("SAVE");
            save.Location = new Point(14, 11);
            save.Size = new Size(90, 34);

            var back = CreateEditorActionButton("◀ BACK");
            back.Location = new Point(114, 11);
            back.Size = new Size(90, 34);
            back.Visible = false;

            var add = CreateEditorActionButton("ADD NPC CREATOR");
            add.Location = new Point(214, 11);
            add.Size = new Size(160, 34);

            var importDatabase =
                CreateEditorActionButton(
                    "IMPORT TO DATABASE");

            importDatabase.Location =
                new Point(
                    384,
                    11);

            importDatabase.Size =
                new Size(
                    170,
                    34);

            editorToolTip.SetToolTip(
                importDatabase,
                "Limpa e volta a importar ItemMaking.xml para " +
                "dmo.Asset.ItemCraft e dmo.Asset.ItemCraftMaterial.");

            var breadcrumb = new Label
            {
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(14, 55),
                Size = new Size(540, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var status = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.2F),
                Location = new Point(565, 54),
                Size = new Size(300, 28),
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoEllipsis = true
            };

            toolbar.Controls.Add(save);
            toolbar.Controls.Add(back);
            toolbar.Controls.Add(add);
            toolbar.Controls.Add(importDatabase);
            toolbar.Controls.Add(breadcrumb);
            toolbar.Controls.Add(status);

            var body = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(20, 18, 20, 28)
            };

            DarkUi.ApplyDarkScrollBar(body);

            var state = new ItemMakingEditorState
            {
                Service = service,
                References = references,
                Working = service.CreateWorkingCopy(),
                Body = body,
                Breadcrumb = breadcrumb,
                Status = status,
                Back = back,
                Add = add
            };

            page.Tag = state;

            save.Click += (_, _) =>
                SaveItemMakingPage(page, state, showSuccess: true);

            importDatabase.Click +=
                async (_, _) =>
                {
                    if (state.Dirty)
                    {
                        DialogResult answer =
                            MessageBox.Show(
                                "ItemMaking.xml tem alterações não guardadas.\r\n\r\n" +
                                "Guardar antes de importar para a database?",
                                "ItemMaking Database Import",
                                MessageBoxButtons.YesNoCancel,
                                MessageBoxIcon.Question);

                        if (answer == DialogResult.Cancel)
                            return;

                        if (answer == DialogResult.Yes &&
                            !SaveItemMakingPage(page, state, showSuccess: false))
                        {
                            return;
                        }
                    }

                    await OpenItemMakingDatabaseImportTabAndRunAsync(
                        state.Service.FilePath);
                };

            back.Click += (_, _) =>
            {
                if (state.History.Count == 0)
                    return;

                state.Current = state.History[^1];
                state.History.RemoveAt(state.History.Count - 1);
                loading.BringToFront();
            page.ResumeLayout(true);
            loading.Refresh();

            RenderItemMakingView(page, state);

            page.Controls.Remove(loading);
            loading.Dispose();
            page.PerformLayout();
            page.Update();
            };

            add.Click += (_, _) =>
                HandleItemMakingAdd(page, state);

            page.Controls.Add(body);
            page.Controls.Add(toolbar);

            editorTabs.SelectedTab = page;

            BeginInvoke(
                new Action(
                    () =>
                        RenderItemMakingView(
                            page,
                            state)));
        }

        private void RenderItemMakingView(
            TabPage page,
            ItemMakingEditorState state)
        {
            Control? overlay = null;

            try
            {
                overlay =
                    ShowEditorBusyOverlay(
                        page,
                        "Loading ItemMaking View",
                        "Preparing NPC creators, recipes, items and related editor controls.");

                state.Body.SuspendLayout();
                DisposeChildImages(state.Body);
                state.Body.Controls.Clear();

                state.Back.Visible = state.History.Count > 0;

                switch (state.Current.Kind)
                {
                    case ItemMakingViewKind.NpcList:
                        state.Breadcrumb.Text = "ItemMaking / NPC Creators";
                        state.Add.Text = "ADD NPC CREATOR";
                        RenderItemMakingNpcList(page, state);
                        break;

                    case ItemMakingViewKind.Npc:
                        RenderItemMakingNpc(page, state, state.Current.Npc!);
                        break;

                    case ItemMakingViewKind.Abar:
                        RenderItemMakingAbar(page, state, state.Current.Npc!, state.Current.Abar!);
                        break;

                    case ItemMakingViewKind.SubCategory:
                        RenderItemMakingSubCategory(
                            page,
                            state,
                            state.Current.Npc!,
                            state.Current.Abar!,
                            state.Current.SubCategory!);
                        break;

                    case ItemMakingViewKind.Craft:
                        RenderItemMakingCraft(
                            page,
                            state,
                            state.Current.Npc!,
                            state.Current.Abar!,
                            state.Current.SubCategory!,
                            state.Current.Craft!);
                        break;
                }

                ItemMakingEditorService.NormalizeCountsAndHiddenSizes(state.Working);

                int npcCount =
                    ItemMakingEditorService.GetNpcBlocks(state.Working).Count();

                state.Status.Text =
                    state.Dirty
                        ? $"UNSAVED • NPC Creators: {npcCount}"
                        : $"Saved • NPC Creators: {npcCount}";

                state.Status.ForeColor =
                    state.Dirty
                        ? Color.FromArgb(255, 190, 90)
                        : CMuted;
            }
            finally
            {
                state.Body.ResumeLayout();
                HideEditorBusyOverlay(page, overlay);
            }
        }

        private void RenderItemMakingNpcList(
            TabPage page,
            ItemMakingEditorState state)
        {
            var searchPanel = CreateMakingCard(740, 92);

            var searchTitle = new Label
            {
                Text = "NPC / MAKING SEARCH",
                ForeColor = CText,
                Font = new Font(
                    "Segoe UI Semibold",
                    9.2F,
                    FontStyle.Bold),
                Location = new Point(14, 10),
                Size = new Size(250, 22)
            };

            var search = CreateMakingTextBox(state.NpcSearchText);
            search.PlaceholderText =
                "Search NpcID, NPC name, tag, Model ID or NPC type...";
            search.Location = new Point(14, 39);
            search.Size = new Size(540, 30);

            var resultCount = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(565, 39),
                Size = new Size(158, 30),
                TextAlign = ContentAlignment.MiddleRight
            };

            searchPanel.Controls.Add(searchTitle);
            searchPanel.Controls.Add(search);
            searchPanel.Controls.Add(resultCount);
            state.Body.Controls.Add(searchPanel);

            string query =
                (state.NpcSearchText ?? string.Empty)
                    .Trim();

            var allNpcBlocks =
                ItemMakingEditorService
                    .GetNpcBlocks(state.Working)
                    .ToList();

            var filteredNpcBlocks =
                allNpcBlocks
                    .Where(npcBlock =>
                    {
                        if (query.Length == 0)
                            return true;

                        uint npcId =
                            ReadMakingUInt(
                                npcBlock,
                                "m_dwNpcIdx");

                        if (npcId
                            .ToString(CultureInfo.InvariantCulture)
                            .Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (!state.References.TryGetNpc(
                            npcId,
                            out EditorNpcReference? npcReference))
                        {
                            return "NPC NOT FOUND".Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase);
                        }

                        string typeName =
                            NpcTypeCatalog.GetName(
                                npcReference.Type);

                        return
                            npcReference.Name.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase) ||
                            npcReference.Tag.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase) ||
                            npcReference.Model
                                .ToString(CultureInfo.InvariantCulture)
                                .Contains(
                                    query,
                                    StringComparison.OrdinalIgnoreCase) ||
                            npcReference.Type
                                .ToString(CultureInfo.InvariantCulture)
                                .Contains(
                                    query,
                                    StringComparison.OrdinalIgnoreCase) ||
                            typeName.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

            resultCount.Text =
                query.Length == 0
                    ? $"Total: {allNpcBlocks.Count:N0}"
                    : $"Results: {filteredNpcBlocks.Count:N0} / {allNpcBlocks.Count:N0}";

            search.TextChanged += (_, _) =>
            {
                state.NpcSearchText = search.Text;

                // Re-render only this in-page view. The search text is persisted
                // in ItemMakingEditorState, so the caret/result state survives
                // navigation back from the selected NPC.
                RenderItemMakingView(page, state);

                if (editorTabs.SelectedTab == page)
                {
                    Control? newSearch =
                        state.Body.Controls
                            .Cast<Control>()
                            .SelectMany(x => x.Controls.Cast<Control>())
                            .FirstOrDefault(x =>
                                x is TextBox tb &&
                                tb.PlaceholderText.StartsWith(
                                    "Search NpcID",
                                    StringComparison.OrdinalIgnoreCase));

                    if (newSearch is TextBox newSearchBox)
                    {
                        newSearchBox.Focus();
                        newSearchBox.SelectionStart =
                            newSearchBox.Text.Length;
                    }
                }
            };

            if (filteredNpcBlocks.Count == 0)
            {
                var empty = new Label
                {
                    Text =
                        "No NPC ItemMaking entries match this search.",
                    ForeColor = CMuted,
                    Font = new Font(
                        "Segoe UI",
                        9F),
                    Width = 740,
                    Height = 56,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Margin = new Padding(0, 12, 0, 0)
                };

                state.Body.Controls.Add(empty);
                return;
            }

            foreach (XElement npcBlock in filteredNpcBlocks)
            {
                uint npcId = ReadMakingUInt(npcBlock, "m_dwNpcIdx");
                bool found = state.References.TryGetNpc(npcId, out EditorNpcReference? npc);
                int type = found ? npc!.Type : -1;

                var card = CreateMakingCard(740, 108);

                var image = new PictureBox
                {
                    Location = new Point(14, 15),
                    Size = new Size(76, 76),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(8, 8, 8)
                };

                if (found)
                {
                    LoadNpcPreviewInto(
                        image,
                        npc!.Model,
                        npcId,
                        state.References);
                }

                var title = new Label
                {
                    Text = $"{npcId}   {(found ? npc!.Name : "NPC NOT FOUND")}",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    Location = new Point(106, 14),
                    Size = new Size(390, 24),
                    AutoEllipsis = true
                };

                var info = new Label
                {
                    Text =
                        found
                            ? $"{type} — {NpcTypeCatalog.GetName(type)}\r\n" +
                              $"Tabs: {ItemMakingEditorService.GetAbars(npcBlock).Count()}"
                            : "Missing from Npc.xml",
                    ForeColor =
                        !found
                            ? Color.FromArgb(255, 95, 95)
                            : type == 20
                                ? Color.FromArgb(125, 220, 140)
                                : Color.FromArgb(255, 190, 90),
                    Font = new Font("Segoe UI", 8.4F),
                    Location = new Point(106, 43),
                    Size = new Size(420, 48)
                };

                var open = CreateEditorActionButton("OPEN");
                open.Location = new Point(554, 20);
                open.Size = new Size(78, 32);

                var remove = CreateEditorActionButton("REMOVE");
                remove.Location = new Point(640, 20);
                remove.Size = new Size(82, 32);

                open.Click += (_, _) =>
                {
                    NavigateMaking(
                        state,
                        new ItemMakingViewContext(
                            ItemMakingViewKind.Npc,
                            npcBlock));

                    RenderItemMakingView(page, state);
                };

                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show(
                        $"Remove ItemMaking for NPC {npcId}?\r\n\r\n" +
                        "All tabs, crafts and materials under this NPC will be removed.",
                        "Remove ItemMaking NPC",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }

                    npcBlock.Remove();
                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                };

                card.Controls.Add(image);
                card.Controls.Add(title);
                card.Controls.Add(info);
                card.Controls.Add(open);
                card.Controls.Add(remove);

                state.Body.Controls.Add(card);
            }
        }

        private void RenderItemMakingNpc(
            TabPage page,
            ItemMakingEditorState state,
            XElement npcBlock)
        {
            uint npcId = ReadMakingUInt(npcBlock, "m_dwNpcIdx");
            state.References.TryGetNpc(npcId, out EditorNpcReference? npc);

            state.Breadcrumb.Text =
                $"ItemMaking / NPC {npcId} / {npc?.Name ?? "Missing NPC"}";

            state.Add.Text = "ADD TAB";

            var summary = CreateMakingCard(740, 112);

            var image = new PictureBox
            {
                Location = new Point(14, 16),
                Size = new Size(76, 76),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(8, 8, 8)
            };

            if (npc != null)
            {
                LoadNpcPreviewInto(
                    image,
                    npc.Model,
                    npc.Id,
                    state.References);
            }

            var info = new Label
            {
                Text =
                    npc == null
                        ? $"NPC {npcId} is missing from Npc.xml."
                        : $"{npc.Id} — {npc.Name}\r\n" +
                          $"{npc.Type} — {NpcTypeCatalog.GetName(npc.Type)}",
                ForeColor =
                    npc == null
                        ? Color.FromArgb(255, 95, 95)
                        : npc.Type == 20
                            ? Color.FromArgb(125, 220, 140)
                            : Color.FromArgb(255, 190, 90),
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(108, 20),
                Size = new Size(430, 60)
            };

            var editNpc = CreateEditorActionButton(
                npc == null
                    ? "QUICK NPC CREATE"
                    : "EDIT NPC");

            editNpc.Location = new Point(560, 30);
            editNpc.Size = new Size(160, 34);

            editNpc.Click += (_, _) =>
                OpenNpcForMaking(page, state, npcId, npc);

            summary.Controls.Add(image);
            summary.Controls.Add(info);
            summary.Controls.Add(editNpc);

            state.Body.Controls.Add(summary);

            foreach (XElement abar in ItemMakingEditorService.GetAbars(npcBlock))
            {
                int id = ReadMakingInt(abar, "ID");
                string name = abar.Element("Abaname")?.Value ?? string.Empty;

                var card = CreateMakingCard(740, 82);

                var label = new Label
                {
                    Text = $"TAB {id}   {name}",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9.6F, FontStyle.Bold),
                    Location = new Point(16, 12),
                    Size = new Size(420, 24),
                    AutoEllipsis = true
                };

                var details = new Label
                {
                    Text = $"Subcategories: {ItemMakingEditorService.GetSubCategories(abar).Count()}",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(17, 40),
                    Size = new Size(360, 22)
                };

                var open = CreateEditorActionButton("EDIT");
                open.Location = new Point(554, 22);
                open.Size = new Size(78, 32);

                var remove = CreateEditorActionButton("REMOVE");
                remove.Location = new Point(640, 22);
                remove.Size = new Size(82, 32);

                open.Click += (_, _) =>
                {
                    NavigateMaking(
                        state,
                        new ItemMakingViewContext(
                            ItemMakingViewKind.Abar,
                            npcBlock,
                            abar));

                    RenderItemMakingView(page, state);
                };

                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show(
                        $"Remove tab '{name}' and everything inside it?",
                        "Remove Tab",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }

                    abar.Remove();
                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                };

                card.Controls.Add(label);
                card.Controls.Add(details);
                card.Controls.Add(open);
                card.Controls.Add(remove);

                state.Body.Controls.Add(card);
            }
        }

        private void RenderItemMakingAbar(
            TabPage page,
            ItemMakingEditorState state,
            XElement npcBlock,
            XElement abar)
        {
            uint npcId = ReadMakingUInt(npcBlock, "m_dwNpcIdx");
            int tabId = ReadMakingInt(abar, "ID");

            state.Breadcrumb.Text =
                $"ItemMaking / NPC {npcId} / Tab {tabId}";

            state.Add.Text = "ADD SUBCATEGORY";

            TextBox name =
                CreateMakingTextBox(abar.Element("Abaname")?.Value ?? string.Empty);

            name.MaxLength = ItemMakingEditorService.UiTextCharacterLimit;

            var nameCard = CreateMakingEditCard(
                "Tab Name",
                name,
                "CarteSize is hidden and grows automatically from the UTF-16 text size.");

            name.TextChanged += (_, _) =>
            {
                EnsureMakingElement(abar, "Abaname").Value = name.Text;
                MarkItemMakingDirty(page, state);
            };

            state.Body.Controls.Add(nameCard);

            foreach (XElement sub in ItemMakingEditorService.GetSubCategories(abar))
            {
                int id = ReadMakingInt(sub, "ID");
                string subName = sub.Element("Name")?.Value ?? string.Empty;

                var card = CreateMakingCard(740, 82);

                var label = new Label
                {
                    Text = $"SUBCATEGORY {id}   {subName}",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                    Location = new Point(16, 12),
                    Size = new Size(440, 24),
                    AutoEllipsis = true
                };

                var info = new Label
                {
                    Text = $"Craft entries: {ItemMakingEditorService.GetCrafts(sub).Count()}",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(17, 40),
                    Size = new Size(300, 22)
                };

                var edit = CreateEditorActionButton("EDIT");
                edit.Location = new Point(554, 22);
                edit.Size = new Size(78, 32);

                var remove = CreateEditorActionButton("REMOVE");
                remove.Location = new Point(640, 22);
                remove.Size = new Size(82, 32);

                edit.Click += (_, _) =>
                {
                    NavigateMaking(
                        state,
                        new ItemMakingViewContext(
                            ItemMakingViewKind.SubCategory,
                            npcBlock,
                            abar,
                            sub));

                    RenderItemMakingView(page, state);
                };

                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show(
                        $"Remove subcategory '{subName}' and its crafts?",
                        "Remove Subcategory",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }

                    sub.Remove();
                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                };

                card.Controls.Add(label);
                card.Controls.Add(info);
                card.Controls.Add(edit);
                card.Controls.Add(remove);

                state.Body.Controls.Add(card);
            }
        }

        private void RenderItemMakingSubCategory(
            TabPage page,
            ItemMakingEditorState state,
            XElement npcBlock,
            XElement abar,
            XElement sub)
        {
            uint npcId = ReadMakingUInt(npcBlock, "m_dwNpcIdx");
            int tabId = ReadMakingInt(abar, "ID");
            int subId = ReadMakingInt(sub, "ID");

            state.Breadcrumb.Text =
                $"ItemMaking / NPC {npcId} / Tab {tabId} / Category {subId}";

            state.Add.Text = "ADD CRAFT";

            TextBox name =
                CreateMakingTextBox(sub.Element("Name")?.Value ?? string.Empty);

            name.MaxLength = ItemMakingEditorService.UiTextCharacterLimit;

            var nameCard = CreateMakingEditCard(
                "Subcategory Name",
                name,
                "SizeNameCate is hidden and grows automatically from the UTF-16 text size.");

            name.TextChanged += (_, _) =>
            {
                EnsureMakingElement(sub, "Name").Value = name.Text;
                MarkItemMakingDirty(page, state);
            };

            state.Body.Controls.Add(nameCard);

            foreach (XElement craft in ItemMakingEditorService.GetCrafts(sub))
            {
                int unique = ReadMakingInt(craft, "m_nUniqueIdx");
                uint itemId = ReadMakingUInt(craft, "m_dwItemIdx");
                int quantity = ReadMakingInt(craft, "m_nItemNum");
                int probability = ReadMakingInt(craft, "m_nProbabilityofSuccess");
                long bits = ReadMakingLong(craft, "Valor");
                int materialCount = ItemMakingEditorService.GetMaterials(craft).Count();

                state.References.TryGetItem(itemId, out EditorItemReference? item);

                var card = CreateMakingCard(740, 112);

                var icon = new PictureBox
                {
                    Location = new Point(14, 18),
                    Size = new Size(64, 64),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(8, 8, 8),
                    Image = item == null
                        ? null
                        : GetItemIconPreview(item.IconId)
                };

                var title = new Label
                {
                    Text =
                        $"Craft {unique}   {itemId} — {item?.Name ?? "ITEM NOT FOUND"}   x{quantity}",
                    ForeColor =
                        item == null
                            ? Color.FromArgb(255, 95, 95)
                            : CText,
                    Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                    Location = new Point(94, 13),
                    Size = new Size(430, 25),
                    AutoEllipsis = true
                };

                var info = new Label
                {
                    Text =
                        $"Success: {probability / 100.0:0.00}%   |   Materials: {materialCount}   |   Bits: {bits:N0}",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.2F),
                    Location = new Point(94, 42),
                    Size = new Size(440, 25)
                };

                var edit = CreateEditorActionButton("EDIT");
                edit.Location = new Point(554, 22);
                edit.Size = new Size(78, 32);

                var remove = CreateEditorActionButton("REMOVE");
                remove.Location = new Point(640, 22);
                remove.Size = new Size(82, 32);

                edit.Click += (_, _) =>
                {
                    NavigateMaking(
                        state,
                        new ItemMakingViewContext(
                            ItemMakingViewKind.Craft,
                            npcBlock,
                            abar,
                            sub,
                            craft));

                    RenderItemMakingView(page, state);
                };

                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show(
                        $"Remove craft {unique}?",
                        "Remove Craft",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }

                    craft.Remove();
                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                };

                card.Controls.Add(icon);
                card.Controls.Add(title);
                card.Controls.Add(info);
                card.Controls.Add(edit);
                card.Controls.Add(remove);

                state.Body.Controls.Add(card);
            }
        }

        private void RenderItemMakingCraft(
            TabPage page,
            ItemMakingEditorState state,
            XElement npcBlock,
            XElement abar,
            XElement sub,
            XElement craft)
        {
            int unique = ReadMakingInt(craft, "m_nUniqueIdx");

            state.Breadcrumb.Text = $"ItemMaking / Craft {unique}";
            state.Add.Text = "ADD MATERIAL";

            uint outputId = ReadMakingUInt(craft, "m_dwItemIdx");
            state.References.TryGetItem(outputId, out EditorItemReference? output);

            var outputCard = CreateMakingCard(740, 122);

            var outputIcon = new PictureBox
            {
                Location = new Point(15, 25),
                Size = new Size(70, 70),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(8, 8, 8),
                Image = output == null
                    ? null
                    : GetItemIconPreview(output.IconId)
            };

            var outputText = new Label
            {
                Text =
                    output == null
                        ? $"{outputId} — ITEM NOT FOUND"
                        : $"{output.Id} — {output.Name}",
                ForeColor =
                    output == null
                        ? Color.FromArgb(255, 95, 95)
                        : CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(102, 25),
                Size = new Size(430, 28),
                AutoEllipsis = true
            };

            var selectOutput = CreateEditorActionButton("SELECT OUTPUT ITEM");
            selectOutput.Location = new Point(554, 30);
            selectOutput.Size = new Size(168, 34);

            selectOutput.Click += (_, _) =>
                ShowMakingItemPicker(
                    page,
                    state,
                    selected =>
                    {
                        EnsureMakingElement(craft, "m_dwItemIdx").Value =
                            selected.Id.ToString(CultureInfo.InvariantCulture);

                        MarkItemMakingDirty(page, state);
                        RenderItemMakingView(page, state);
                    });

            outputCard.Controls.Add(outputIcon);
            outputCard.Controls.Add(outputText);
            outputCard.Controls.Add(selectOutput);

            state.Body.Controls.Add(outputCard);

            TextBox uniqueBox =
                CreateMakingTextBox(unique.ToString(CultureInfo.InvariantCulture));

            TextBox quantity =
                CreateMakingTextBox(craft.Element("m_nItemNum")?.Value ?? "1");

            TextBox probability =
                CreateMakingTextBox(
                    (ReadMakingInt(craft, "m_nProbabilityofSuccess") / 100.0)
                    .ToString("0.00", CultureInfo.InvariantCulture));

            TextBox bits =
                CreateMakingTextBox(craft.Element("Valor")?.Value ?? "0");

            var uniqueCard =
                CreateMakingEditCard(
                    "Craft Unique ID",
                    uniqueBox,
                    "m_nUniqueIdx must be unique globally inside ItemMaking.xml.");

            var quantityCard =
                CreateMakingEditCard(
                    "Output Quantity",
                    quantity,
                    "m_nItemNum — amount of output item received.");

            var probabilityCard =
                CreateMakingEditCard(
                    "Success Chance (%)",
                    probability,
                    "UI shows percent. XML raw value is percent ×100. 100.00% = 10000.");

            var bitsCard =
                CreateMakingEditCard(
                    "Bits Cost",
                    bits,
                    "Valor — Bits consumed by the craft.");

            var money = CreateMoneyPreview();
            money.Location = new Point(12, 75);

            bitsCard.Height = 122;
            bitsCard.Controls.Add(money);

            void UpdateMoney()
            {
                RenderMoneyPreview(
                    money,
                    long.TryParse(bits.Text.Trim(), out long raw)
                        ? raw
                        : 0);
            }

            uniqueBox.TextChanged += (_, _) =>
            {
                EnsureMakingElement(craft, "m_nUniqueIdx").Value =
                    uniqueBox.Text.Trim();

                MarkItemMakingDirty(page, state);
            };

            quantity.TextChanged += (_, _) =>
            {
                EnsureMakingElement(craft, "m_nItemNum").Value =
                    quantity.Text.Trim();

                MarkItemMakingDirty(page, state);
            };

            probability.TextChanged += (_, _) =>
            {
                if (decimal.TryParse(
                    probability.Text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal percent))
                {
                    percent = Math.Max(0, Math.Min(100, percent));

                    int raw =
                        (int)Math.Round(
                            percent * 100,
                            MidpointRounding.AwayFromZero);

                    EnsureMakingElement(craft, "m_nProbabilityofSuccess").Value =
                        raw.ToString(CultureInfo.InvariantCulture);
                }

                MarkItemMakingDirty(page, state);
            };

            bits.TextChanged += (_, _) =>
            {
                EnsureMakingElement(craft, "Valor").Value = bits.Text.Trim();
                UpdateMoney();
                MarkItemMakingDirty(page, state);
            };

            state.Body.Controls.Add(uniqueCard);
            state.Body.Controls.Add(quantityCard);
            state.Body.Controls.Add(probabilityCard);
            state.Body.Controls.Add(bitsCard);

            UpdateMoney();

            int materialIndex = 0;

            foreach (XElement material in ItemMakingEditorService.GetMaterials(craft).ToList())
            {
                int capturedIndex = materialIndex++;
                uint materialId = ReadMakingUInt(material, "m_dwItemIdx");
                int materialQty = ReadMakingInt(material, "m_nItemNum");

                state.References.TryGetItem(materialId, out EditorItemReference? item);

                var card = CreateMakingCard(740, 112);

                var icon = new PictureBox
                {
                    Location = new Point(14, 20),
                    Size = new Size(64, 64),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(8, 8, 8),
                    Image = item == null
                        ? null
                        : GetItemIconPreview(item.IconId)
                };

                var title = new Label
                {
                    Text =
                        $"MATERIAL #{capturedIndex}   {materialId} — {item?.Name ?? "ITEM NOT FOUND"}",
                    ForeColor =
                        item == null
                            ? Color.FromArgb(255, 95, 95)
                            : CText,
                    Font = new Font("Segoe UI Semibold", 9.4F, FontStyle.Bold),
                    Location = new Point(94, 16),
                    Size = new Size(410, 24),
                    AutoEllipsis = true
                };

                var qty = CreateMakingTextBox(materialQty.ToString(CultureInfo.InvariantCulture));
                qty.Location = new Point(94, 52);
                qty.Size = new Size(120, 29);

                var qtyLabel = new Label
                {
                    Text = "Quantity",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.7F),
                    Location = new Point(224, 53),
                    Size = new Size(70, 25),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                var select = CreateEditorActionButton("SELECT ITEM");
                select.Location = new Point(520, 18);
                select.Size = new Size(110, 32);

                var remove = CreateEditorActionButton("REMOVE");
                remove.Location = new Point(638, 18);
                remove.Size = new Size(84, 32);

                qty.TextChanged += (_, _) =>
                {
                    EnsureMakingElement(material, "m_nItemNum").Value =
                        qty.Text.Trim();

                    MarkItemMakingDirty(page, state);
                };

                select.Click += (_, _) =>
                    ShowMakingItemPicker(
                        page,
                        state,
                        selected =>
                        {
                            EnsureMakingElement(material, "m_dwItemIdx").Value =
                                selected.Id.ToString(CultureInfo.InvariantCulture);

                            MarkItemMakingDirty(page, state);
                            RenderItemMakingView(page, state);
                        });

                remove.Click += (_, _) =>
                {
                    material.Remove();
                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                };

                card.Controls.Add(icon);
                card.Controls.Add(title);
                card.Controls.Add(qty);
                card.Controls.Add(qtyLabel);
                card.Controls.Add(select);
                card.Controls.Add(remove);

                state.Body.Controls.Add(card);
            }
        }

        private void HandleItemMakingAdd(
            TabPage page,
            ItemMakingEditorState state)
        {
            switch (state.Current.Kind)
            {
                case ItemMakingViewKind.NpcList:
                    ShowMakingNpcPicker(page, state);
                    break;

                case ItemMakingViewKind.Npc:
                {
                    XElement npc = state.Current.Npc!;
                    int next = ItemMakingEditorService.GetAbars(npc).Count() + 1;
                    EnsureMakingElement(npc, "index")
                        .Add(ItemMakingEditorService.CreateAbar(next));

                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                    break;
                }

                case ItemMakingViewKind.Abar:
                {
                    XElement abar = state.Current.Abar!;
                    int next = ItemMakingEditorService.GetSubCategories(abar).Count() + 1;
                    EnsureMakingElement(abar, "index")
                        .Add(ItemMakingEditorService.CreateSubCategory(next));

                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                    break;
                }

                case ItemMakingViewKind.SubCategory:
                {
                    XElement sub = state.Current.SubCategory!;
                    XElement craft = ItemMakingEditorService.CreateCraft(state.Working);

                    EnsureMakingElement(sub, "index").Add(craft);

                    MarkItemMakingDirty(page, state);

                    NavigateMaking(
                        state,
                        new ItemMakingViewContext(
                            ItemMakingViewKind.Craft,
                            state.Current.Npc,
                            state.Current.Abar,
                            sub,
                            craft));

                    RenderItemMakingView(page, state);
                    break;
                }

                case ItemMakingViewKind.Craft:
                {
                    XElement craft = state.Current.Craft!;
                    EnsureMakingElement(craft, "index")
                        .Add(ItemMakingEditorService.CreateMaterial());

                    MarkItemMakingDirty(page, state);
                    RenderItemMakingView(page, state);
                    break;
                }
            }
        }

        private void ShowMakingNpcPicker(
            TabPage page,
            ItemMakingEditorState state)
        {
            Panel overlay =
                CreateMakingOverlay(
                    page,
                    "Select Item Creator NPC");

            Panel? overlayHeader =
                overlay.Controls
                    .OfType<Panel>()
                    .FirstOrDefault(
                        x =>
                            x.Dock ==
                            DockStyle.Top);

            var content =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    BackColor =
                        Color.FromArgb(
                            20,
                            20,
                            20),
                    Padding =
                        new Padding(
                            16,
                            8,
                            16,
                            14),
                    Margin = Padding.Empty
                };

            content.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    54F));

            content.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    30F));

            content.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100F));

            var searchLayout =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 1,
                    BackColor =
                        Color.Transparent,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };

            searchLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    78F));

            searchLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    22F));

            var search =
                CreateMakingTextBox(
                    string.Empty);

            search.PlaceholderText =
                "Search NPC ID, name, tag, Model ID or NPC type...";

            search.Dock = DockStyle.Fill;
            search.Margin =
                new Padding(
                    0,
                    5,
                    10,
                    5);

            var quick =
                CreateEditorActionButton(
                    "QUICK NPC CREATE");

            quick.Dock = DockStyle.Fill;
            quick.Margin =
                new Padding(
                    0,
                    5,
                    0,
                    5);

            quick.Font =
                new Font(
                    "Segoe UI Semibold",
                    8.2F,
                    FontStyle.Bold);

            searchLayout.Controls.Add(
                search,
                0,
                0);

            searchLayout.Controls.Add(
                quick,
                1,
                0);

            var resultInfo =
                new Label
                {
                    Dock = DockStyle.Fill,
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8F),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    Margin =
                        new Padding(
                            4,
                            0,
                            0,
                            0)
                };

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection =
                        FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor =
                        Color.FromArgb(
                            17,
                            17,
                            17),
                    Padding =
                        new Padding(
                            10,
                            10,
                            10,
                            20),
                    Margin = Padding.Empty
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            content.Controls.Add(
                searchLayout,
                0,
                0);

            content.Controls.Add(
                resultInfo,
                0,
                1);

            content.Controls.Add(
                results,
                0,
                2);

            int GetCardWidth() =>
                Math.Max(
                    500,
                    results.ClientSize.Width -
                    results.Padding.Horizontal -
                    22);

            void Refresh()
            {
                IReadOnlyList<EditorNpcReference> npcs =
                    state.References.SearchNpcs(
                        search.Text,
                        50);

                results.SuspendLayout();
                DisposeChildImages(results);
                results.Controls.Clear();

                int cardWidth =
                    GetCardWidth();

                foreach (EditorNpcReference npc
                         in npcs)
                {
                    bool already =
                        ItemMakingEditorService
                            .GetNpcBlocks(
                                state.Working)
                            .Any(
                                x =>
                                    ReadMakingUInt(
                                        x,
                                        "m_dwNpcIdx") ==
                                    npc.Id);

                    var card =
                        CreateMakingCard(
                            cardWidth,
                            98);

                    var image =
                        new PictureBox
                        {
                            Location =
                                new Point(
                                    12,
                                    13),
                            Size =
                                new Size(
                                    70,
                                    70),
                            SizeMode =
                                PictureBoxSizeMode.Zoom,
                            BackColor =
                                Color.FromArgb(
                                    8,
                                    8,
                                    8)
                        };

                    LoadNpcPreviewInto(
                        image,
                        npc.Model,
                        npc.Id,
                        state.References);

                    var actionHost =
                        new Panel
                        {
                            Dock =
                                DockStyle.Right,
                            Width = 168,
                            BackColor =
                                Color.Transparent,
                            Padding =
                                new Padding(
                                    10,
                                    30,
                                    12,
                                    30)
                        };

                    var select =
                        CreateEditorActionButton(
                            already
                                ? "ALREADY ADDED"
                                : npc.Type == 20
                                    ? "SELECT"
                                    : "EDIT NPC TYPE");

                    select.Dock = DockStyle.Fill;
                    select.Enabled = !already;

                    int textWidth =
                        Math.Max(
                            170,
                            cardWidth -
                            96 -
                            actionHost.Width -
                            16);

                    var name =
                        new Label
                        {
                            Text =
                                $"{npc.Id} — {npc.Name}",
                            ForeColor =
                                npc.Type == 20
                                    ? Color.FromArgb(
                                        125,
                                        220,
                                        140)
                                    : Color.FromArgb(
                                        255,
                                        190,
                                        90),
                            Font =
                                new Font(
                                    "Segoe UI Semibold",
                                    9.4F,
                                    FontStyle.Bold),
                            Location =
                                new Point(
                                    96,
                                    12),
                            Size =
                                new Size(
                                    textWidth,
                                    24),
                            AutoEllipsis = true
                        };

                    string modelText =
                        state.References.TryGetModel(
                            npc.Model,
                            out EditorModelReference? modelReference)
                            ? $"{npc.Model} — {modelReference.DisplayName} [{modelReference.Kind}]"
                            : npc.Model.ToString(
                                CultureInfo.InvariantCulture);

                    var details =
                        new Label
                        {
                            Text =
                                $"{npc.Type} — {NpcTypeCatalog.GetName(npc.Type)}\r\n" +
                                $"Model: {modelText}",
                            ForeColor =
                                Color.FromArgb(
                                    185,
                                    185,
                                    185),
                            Font =
                                new Font(
                                    "Segoe UI",
                                    8F),
                            Location =
                                new Point(
                                    96,
                                    39),
                            Size =
                                new Size(
                                    textWidth,
                                    44),
                            AutoEllipsis = true
                        };

                    if (npc.Type == 20)
                    {
                        select.Click +=
                            (_, _) =>
                            {
                                AddMakingNpc(
                                    page,
                                    state,
                                    npc.Id);

                                overlay.Dispose();
                            };
                    }
                    else
                    {
                        select.Click +=
                            (_, _) =>
                                OpenNpcForMaking(
                                    page,
                                    state,
                                    npc.Id,
                                    npc);
                    }

                    actionHost.Controls.Add(
                        select);

                    card.Controls.Add(
                        image);

                    card.Controls.Add(
                        name);

                    card.Controls.Add(
                        details);

                    card.Controls.Add(
                        actionHost);

                    results.Controls.Add(
                        card);
                }

                results.ResumeLayout();

                resultInfo.Text =
                    string.IsNullOrWhiteSpace(
                        search.Text)
                        ? $"NPCs available: {npcs.Count:N0} shown"
                        : $"Search results: {npcs.Count:N0} shown";
            }

            var searchTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            searchTimer.Tick +=
                (_, _) =>
                {
                    searchTimer.Stop();

                    if (!overlay.IsDisposed)
                        Refresh();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    searchTimer.Stop();
                    searchTimer.Start();
                };

            var resizeTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 120
                };

            resizeTimer.Tick +=
                (_, _) =>
                {
                    resizeTimer.Stop();

                    if (!overlay.IsDisposed)
                        Refresh();
                };

            results.Resize +=
                (_, _) =>
                {
                    resizeTimer.Stop();
                    resizeTimer.Start();
                };

            overlay.Disposed +=
                (_, _) =>
                {
                    searchTimer.Stop();
                    searchTimer.Dispose();

                    resizeTimer.Stop();
                    resizeTimer.Dispose();
                };

            quick.Click +=
                async (_, _) =>
                {
                    string npcPath =
                        EditorReferenceCatalogService
                            .ResolveWorkspaceXml(
                                state.Service.FilePath,
                                "Npc",
                                "Npc.xml");

                    try
                    {
                        NpcEditorService npcService =
                            await EditorPreloadService
                                .GetNpcServiceAsync(
                                    npcPath);

                        if (overlay.IsDisposed)
                            return;

                        XElement template =
                            npcService.CreateTemplate(
                                suggestedId: 0,
                                npcType: 20);

                        OpenNpcEditTab(
                            npcService,
                            state.References,
                            template,
                            originalId: null,
                            isNew: true,
                            lockType20: true,
                            onSaved:
                                savedId =>
                                {
                                    EditorPreloadService
                                        .InvalidateNpc(
                                            npcPath);

                                    state.References.ReloadNpc(
                                        state.Service.FilePath);

                                    AddMakingNpc(
                                        page,
                                        state,
                                        savedId);

                                    if (!overlay.IsDisposed)
                                        overlay.Dispose();
                                });
                    }
                    catch (Exception ex)
                    {
                        ShowEditorError(
                            "Quick NPC Create",
                            ex);
                    }
                };

            overlay.Controls.Add(
                content);

            if (overlayHeader != null)
                overlayHeader.BringToFront();

            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (overlay.IsDisposed)
                            return;

                        Refresh();
                        search.Focus();
                    }));
        }

        private void OpenNpcForMaking(
            TabPage makingPage,
            ItemMakingEditorState state,
            uint npcId,
            EditorNpcReference? npcReference)
        {
            string npcPath =
                EditorReferenceCatalogService.ResolveWorkspaceXml(
                    state.Service.FilePath,
                    "Npc",
                    "Npc.xml");

            if (!File.Exists(npcPath))
            {
                MessageBox.Show(
                    "Npc.xml not found.",
                    "NPC Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            var npcService = new NpcEditorService(npcPath);

            bool exists = npcService.Exists(npcId);

            XElement working =
                npcService.GetClone(npcId)
                ?? npcService.CreateTemplate(npcId, 20);

            OpenNpcEditTab(
                npcService,
                state.References,
                working,
                originalId: exists ? npcId : null,
                isNew: !exists,
                lockType20: !exists,
                onSaved: _ =>
                {
                    state.References.ReloadNpc(state.Service.FilePath);
                    RenderItemMakingView(makingPage, state);
                });
        }

        private void AddMakingNpc(
            TabPage page,
            ItemMakingEditorState state,
            uint npcId)
        {
            bool exists =
                ItemMakingEditorService
                    .GetNpcBlocks(state.Working)
                    .Any(x =>
                        ReadMakingUInt(x, "m_dwNpcIdx") == npcId);

            if (exists)
            {
                MessageBox.Show(
                    $"NPC {npcId} already has an ItemMaking block.",
                    "ItemMaking",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            state.Working.Root!
                .Element("index")!
                .Add(ItemMakingEditorService.CreateNpcBlock(npcId));

            MarkItemMakingDirty(page, state);
            RenderItemMakingView(page, state);
        }

        private void ShowMakingItemPicker(
            TabPage page,
            ItemMakingEditorState state,
            Action<EditorItemReference> onSelect)
        {
            Panel overlay =
                CreateMakingOverlay(page, "Select Item from ItemList.xml");

            var search = CreateMakingTextBox(string.Empty);
            search.PlaceholderText = "Search ItemID or item name...";
            search.Location = new Point(20, 58);
            search.Size = new Size(620, 31);

            var results = new FlowLayoutPanel
            {
                Location = new Point(20, 103),
                Size = new Size(840, 470),
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Bottom |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(17, 17, 17),
                Padding = new Padding(8)
            };

            DarkUi.ApplyDarkScrollBar(results);

            void Refresh()
            {
                var items = state.References.SearchItems(search.Text, 80);

                results.SuspendLayout();
                DisposeChildImages(results);
                results.Controls.Clear();

                foreach (EditorItemReference item in items)
                {
                    var card = CreateMakingCard(800, 72);

                    var icon = new PictureBox
                    {
                        Location = new Point(10, 10),
                        Size = new Size(50, 50),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.FromArgb(8, 8, 8),
                        Image = GetItemIconPreview(item.IconId)
                    };

                    var label = new Label
                    {
                        Text =
                            $"{item.Id} — {item.Name}\r\n" +
                            $"Icon ID: {item.IconId}",
                        ForeColor = CText,
                        Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                        Location = new Point(74, 11),
                        Size = new Size(520, 48),
                        AutoEllipsis = true
                    };

                    var select = CreateEditorActionButton("SELECT");
                    select.Location = new Point(680, 19);
                    select.Size = new Size(90, 32);

                    select.Click += (_, _) =>
                    {
                        onSelect(item);
                        overlay.Dispose();
                    };

                    card.Controls.Add(icon);
                    card.Controls.Add(label);
                    card.Controls.Add(select);
                    results.Controls.Add(card);
                }

                results.ResumeLayout();
            }

            search.TextChanged += (_, _) => Refresh();

            overlay.Controls.Add(search);
            overlay.Controls.Add(results);

            Refresh();
        }

        private Panel CreateMakingOverlay(
            TabPage page,
            string title)
        {
            var overlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20)
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(31, 31, 31)
            };

            var label = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Location = new Point(16, 8),
                Size = new Size(520, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var close = CreateEditorActionButton("CLOSE");
            close.Dock = DockStyle.Right;
            close.Width = 90;
            close.Click += (_, _) => overlay.Dispose();

            header.Controls.Add(label);
            header.Controls.Add(close);

            overlay.Controls.Add(header);
            page.Controls.Add(overlay);
            overlay.BringToFront();

            return overlay;
        }

        private bool SaveItemMakingPage(
            TabPage page,
            ItemMakingEditorState state,
            bool showSuccess)
        {
            try
            {
                state.Service.Save(state.Working, state.References);

                EditorPreloadService.InvalidateItemMaking(
                    state.Service.FilePath);

                ItemMakingValidationResult validation =
                    state.Service.Validate(state.Working, state.References);

                state.Dirty = false;
                page.Text = "ItemMaking.xml [Saved]";

                RenderItemMakingView(page, state);

                if (validation.Warnings.Count > 0)
                {
                    AppLogger.Warning(
                        "ItemMaking Editor: saved with warnings: " +
                        string.Join(" | ", validation.Warnings));
                }

                if (showSuccess)
                {
                    string warningText =
                        validation.Warnings.Count == 0
                            ? string.Empty
                            : "\r\n\r\nWarnings:\r\n- " +
                              string.Join(
                                  "\r\n- ",
                                  validation.Warnings.Take(12));

                    MessageBox.Show(
                        "ItemMaking.xml saved successfully." + warningText,
                        "ItemMaking Editor",
                        MessageBoxButtons.OK,
                        validation.Warnings.Count == 0
                            ? MessageBoxIcon.Information
                            : MessageBoxIcon.Warning);
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowEditorError("Save ItemMaking", ex);
                return false;
            }
        }

        private void NavigateMaking(
            ItemMakingEditorState state,
            ItemMakingViewContext next)
        {
            state.History.Add(state.Current);
            state.Current = next;
        }

        private void MarkItemMakingDirty(
            TabPage page,
            ItemMakingEditorState state)
        {
            state.Dirty = true;

            if (!page.Text.Contains(
                "[Unsaved]",
                StringComparison.OrdinalIgnoreCase))
            {
                page.Text = "ItemMaking.xml [Unsaved]";
            }
        }

        private static Panel CreateMakingCard(
            int width,
            int height)
        {
            var panel = new Panel
            {
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(29, 29, 29),
                Margin = new Padding(0, 0, 0, 10)
            };

            panel.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(52, 52, 52));
                e.Graphics.DrawRectangle(
                    p,
                    0,
                    0,
                    panel.Width - 1,
                    panel.Height - 1);
            };

            return panel;
        }

        private Panel CreateMakingEditCard(
            string title,
            TextBox editor,
            string hint)
        {
            var panel = CreateMakingCard(740, 104);

            var label = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9.3F, FontStyle.Bold),
                Location = new Point(14, 10),
                Size = new Size(620, 24)
            };

            var help = CreateHelpBubble(hint);
            help.Location = new Point(704, 8);

            editor.Location = new Point(14, 43);
            editor.Size = new Size(690, 30);

            panel.Controls.Add(label);
            panel.Controls.Add(help);
            panel.Controls.Add(editor);

            return panel;
        }

        private static TextBox CreateMakingTextBox(string value) =>
            new TextBox
            {
                Text = value,
                BackColor = Color.FromArgb(11, 11, 11),
                ForeColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.3F)
            };

        private static RichTextBox CreateMoneyPreview() =>
            new RichTextBox
            {
                Width = 690,
                Height = 28,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(29, 29, 29),
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None
            };

        private static void RenderMoneyPreview(
            RichTextBox box,
            long bits)
        {
            bits = Math.Max(0, bits);

            long tera = bits / 1_000_000;
            long mega = (bits % 1_000_000) / 1_000;
            long blue = bits % 1_000;

            box.Clear();

            box.SelectionColor = Color.FromArgb(230, 85, 85);
            box.AppendText($"{tera}T ");

            box.SelectionColor = Color.FromArgb(105, 220, 125);
            box.AppendText($"{mega:000}M ");

            box.SelectionColor = Color.FromArgb(100, 175, 255);
            box.AppendText($"{blue:000}B");
        }

        private static XElement EnsureMakingElement(
            XElement owner,
            string tag)
        {
            XElement? element = owner.Element(tag);

            if (element != null)
                return element;

            element = new XElement(tag);
            owner.Add(element);

            return element;
        }

        private static int ReadMakingInt(XElement owner, string tag) =>
            int.TryParse(owner.Element(tag)?.Value, out int value)
                ? value
                : 0;

        private static uint ReadMakingUInt(XElement owner, string tag) =>
            uint.TryParse(owner.Element(tag)?.Value, out uint value)
                ? value
                : 0;

        private static long ReadMakingLong(XElement owner, string tag) =>
            long.TryParse(owner.Element(tag)?.Value, out long value)
                ? value
                : 0;
    }
}
