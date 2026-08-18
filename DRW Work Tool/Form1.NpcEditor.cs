using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
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
        private sealed class NpcBrowserState
        {
            public required NpcEditorService Service { get; init; }
            public required EditorReferenceCatalogService References { get; init; }
            public required TextBox Search { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required Label Count { get; init; }
            public required System.Windows.Forms.Timer Timer { get; init; }
        }

        private sealed class NpcEditState
        {
            public required NpcEditorService Service { get; init; }
            public required EditorReferenceCatalogService References { get; init; }
            public required XElement Working { get; init; }

            public uint? OriginalId { get; set; }
            public bool Dirty { get; set; }
            public bool IsNew { get; set; }
            public bool LockType20 { get; init; }

            public Action<uint>? OnSaved { get; init; }

            public required TextBox Id { get; init; }
            public required Label IdStatus { get; init; }

            public required TextBox MapSearch { get; init; }
            public required Label MapStatus { get; init; }
            public required FlowLayoutPanel MapSuggestions { get; init; }

            public required DarkComboBox Type { get; init; }

            public required TextBox Name { get; init; }
            public required TextBox Tag { get; init; }
            public required TextBox Model { get; init; }
            public required TextBox Description { get; init; }

            public required PictureBox Preview { get; init; }
            public required Label PreviewStatus { get; init; }

            public required CheckBox IsQuestNpc { get; init; }
            public required TextBox QuestInitState { get; init; }
            public required TextBox QuestActions { get; init; }
            public required Label QuestExternalReferences { get; init; }

            public required CheckBox IsTeleportNpc { get; init; }
            public required TextBox PortalType { get; init; }
            public required TextBox PortalEntries { get; init; }
        }

        private async void OpenNpcBrowser(string xmlPath)
        {
            string fullPath = Path.GetFullPath(xmlPath);

            TabPage? existing = editorTabs.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        "NPC_BROWSER:" + fullPath,
                        StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page = CreateDarkTab("Npc.xml");
            page.Name = "NPC_BROWSER:" + fullPath;

            var loading =
                new EditorLoadingView(
                    "Loading NPC Database",
                    "Preparing Npc.xml, MapList, models, Digimon references and preview data.");

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            NpcEditorService service;
            EditorReferenceCatalogService references;

            try
            {
                service =
                    await EditorPreloadService
                        .GetNpcServiceAsync(
                            fullPath);

                references =
                    await EditorPreloadService
                        .GetReferencesAsync(
                            fullPath);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                    loading.SetError("Npc.xml could not be loaded",ex.Message);
                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 124,
                BackColor = Color.FromArgb(27, 27, 27)
            };

            var title = new Label
            {
                Text = "NPC Database",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                Location = new Point(20, 12),
                Size = new Size(250, 30)
            };

            var subtitle = new Label
            {
                Text = "Npc.xml — search, edit and create NPC definitions",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(22, 41),
                Size = new Size(430, 22)
            };

            var search = new TextBox
            {
                Location = new Point(20, 72),
                Size = new Size(440, 30),
                BackColor = Color.FromArgb(12, 12, 12),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.3F),
                PlaceholderText = "Search NPC ID, name or tag..."
            };

            var create = CreateEditorActionButton("NEW NPC");
            create.Location = new Point(474, 72);
            create.Size = new Size(110, 30);

            var count = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(600, 72),
                Size = new Size(260, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(18, 16, 18, 20)
            };

            DarkUi.ApplyDarkScrollBar(results);

            var timer = new System.Windows.Forms.Timer
            {
                Interval = 150
            };

            var state = new NpcBrowserState
            {
                Service = service,
                References = references,
                Search = search,
                Results = results,
                Count = count,
                Timer = timer
            };

            page.Tag = state;

            timer.Tick += (_, _) =>
            {
                timer.Stop();

                if (!page.IsDisposed)
                    RefreshNpcBrowser(state);
            };

            search.TextChanged += (_, _) =>
            {
                timer.Stop();
                timer.Start();
            };

            create.Click += (_, _) =>
            {
                XElement template = service.CreateTemplate();

                OpenNpcEditTab(
                    service,
                    references,
                    template,
                    originalId: null,
                    isNew: true,
                    lockType20: false,
                    onSaved: _ => RefreshNpcBrowser(state));
            };

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(search);
            header.Controls.Add(create);
            header.Controls.Add(count);

            page.Controls.Add(results);
            page.Controls.Add(header);

            editorTabs.SelectedTab = page;

            // Keep the loading surface visible while the first 50 NPC cards
            // are created. Only reveal the browser after the complete first
            // layout is ready.
            loading.BringToFront();
            page.ResumeLayout(true);
            loading.Refresh();

            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (page.IsDisposed)
                            return;

                        try
                        {
                            page.SuspendLayout();

                            RefreshNpcBrowser(
                                state);

                            page.ResumeLayout(true);
                            page.PerformLayout();
                            page.Invalidate(true);
                            page.Update();
                        }
                        catch (Exception ex)
                        {
                            page.ResumeLayout(true);

                            if (!loading.IsDisposed)
                            {
                                loading.SetError(
                                    "NPC Database could not be rendered",
                                    ex.Message);

                                loading.BringToFront();
                            }

                            return;
                        }

                        if (!loading.IsDisposed)
                        {
                            page.Controls.Remove(
                                loading);

                            loading.Dispose();
                        }

                        page.PerformLayout();
                        page.Invalidate(true);
                        page.Update();
                    }));
        }

        private void RefreshNpcBrowser(NpcBrowserState state)
        {
            var rows = state.Service.Search(state.Search.Text, 50);

            state.Results.SuspendLayout();
            DisposeChildImages(state.Results);
            state.Results.Controls.Clear();

            foreach (XElement npc in rows)
            {
                uint.TryParse(npc.Element("NpcID")?.Value, out uint id);
                uint.TryParse(npc.Element("MapID")?.Value, out uint mapId);
                int.TryParse(npc.Element("NPCType")?.Value, out int type);
                uint.TryParse(npc.Element("Model")?.Value, out uint model);

                string name = npc.Element("NPCName")?.Value ?? string.Empty;
                string tag = npc.Element("NPCTag")?.Value ?? string.Empty;

                var card = CreateNpcCard(740, 88);

                var image = new PictureBox
                {
                    Location = new Point(12, 12),
                    Size = new Size(62, 62),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(8, 8, 8)
                };

                LoadNpcPreviewInto(
                    image,
                    model,
                    id,
                    state.References);

                var main = new Label
                {
                    Text = $"{id}   {name}",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                    Location = new Point(88, 12),
                    Size = new Size(410, 24),
                    AutoEllipsis = true
                };

                string mapText =
                    state.References.TryGetMap(mapId, out EditorMapReference? map)
                        ? $"{mapId} — {map.Name}"
                        : mapId.ToString(CultureInfo.InvariantCulture);

                var info = new Label
                {
                    Text =
                        $"{type} — {NpcTypeCatalog.GetName(type)}   |   Map: {mapText}\r\n{tag}",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(88, 37),
                    Size = new Size(455, 40),
                    AutoEllipsis = true
                };

                var edit = CreateEditorActionButton("EDIT");
                edit.Location = new Point(610, 25);
                edit.Size = new Size(104, 34);

                edit.Click += (_, _) =>
                {
                    XElement? working = state.Service.GetClone(id);
                    if (working == null)
                        return;

                    OpenNpcEditTab(
                        state.Service,
                        state.References,
                        working,
                        id,
                        isNew: false,
                        lockType20: false,
                        onSaved: _ => RefreshNpcBrowser(state));
                };

                card.Controls.Add(image);
                card.Controls.Add(main);
                card.Controls.Add(info);
                card.Controls.Add(edit);

                state.Results.Controls.Add(card);
            }

            state.Results.ResumeLayout();

            state.Count.Text =
                $"Total: {state.Service.TotalNpcs:N0}   Results: {rows.Count:N0}";
        }

        private async void OpenNpcEditTab(
            NpcEditorService service,
            EditorReferenceCatalogService references,
            XElement working,
            uint? originalId,
            bool isNew,
            bool lockType20,
            Action<uint>? onSaved)
        {
            string titleName = working.Element("NPCName")?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(titleName))
                titleName = isNew ? "New NPC" : "NPC";

            var page = CreateDarkTab(
                isNew
                    ? $"{titleName} [Unsaved]"
                    : $"{titleName} [Edit]");

            var loading =
                new EditorLoadingView(
                    "Loading NPC Editor",
                    "Preparing NPC fields, linked references, quests, teleports and preview data.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            await System.Threading.Tasks.Task.Yield();

            if (page.IsDisposed)
                return;

            page.SuspendLayout();

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = CPanel
            };

            var save = CreateEditorActionButton("SAVE");
            save.Location = new Point(14, 10);
            save.Size = new Size(100, 34);

            var importDatabase =
                CreateEditorActionButton(
                    "IMPORT TO DATABASE");

            importDatabase.Size =
                new Size(
                    170,
                    34);

            importDatabase.Location =
                new Point(
                    128,
                    10);

            editorToolTip.SetToolTip(
                importDatabase,
                "Limpa e reimporta as tabelas NPC relacionadas: " +
                "Npc, NpcItem, NpcPortal, NpcPortalsAmount, NpcPortals e NpcColiseum.");

            var status = new Label
            {
                Text =
                    lockType20
                        ? "Quick NPC Create — NPCType locked to 20 / Item Creator"
                        : "Npc.xml editor",
                ForeColor =
                    lockType20
                        ? Color.FromArgb(125, 220, 140)
                        : CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(310, 10),
                Size = new Size(390, 34),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            toolbar.Controls.Add(save);
            toolbar.Controls.Add(importDatabase);
            toolbar.Controls.Add(status);

            var scroll = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = CEditor,
                Padding = new Padding(20, 20, 20, 30)
            };

            DarkUi.ApplyDarkScrollBar(scroll);

            var id = CreateNpcTextBox(working.Element("NpcID")?.Value ?? "0");
            var idStatus = CreateNpcStatusLabel();
            var idCard = CreateNpcFieldCard(
                "NPC ID",
                id,
                "Unique NpcID in Npc.xml.",
                idStatus);

            var type = new DarkComboBox
            {
                Width = 320,
                Height = 30
            };

            foreach (var pair in NpcTypeCatalog.All)
            {
                type.Items.Add(
                    new DarkComboOption
                    {
                        Value = pair.Key.ToString(CultureInfo.InvariantCulture),
                        Label = pair.Value
                    });
            }

            int.TryParse(
                working.Element("NPCType")?.Value,
                out int currentType);

            DarkComboOption? selectedType =
                type.Items
                    .OfType<DarkComboOption>()
                    .FirstOrDefault(x =>
                        x.Value ==
                        currentType.ToString(CultureInfo.InvariantCulture));

            if (selectedType != null)
                type.SelectedItem = selectedType;

            if (lockType20)
            {
                DarkComboOption itemCreator =
                    type.Items
                        .OfType<DarkComboOption>()
                        .First(x => x.Value == "20");

                type.SelectedItem = itemCreator;
                type.Enabled = false;

                EnsureNpcElement(working, "NPCType").Value = "20";
            }

            var typeCard = CreateNpcFieldCard(
                "NPC Type",
                type,
                lockType20
                    ? "Locked: ItemMaking creators use NPCType 20."
                    : "Functional NPC system/type.");

            var name = CreateNpcTextBox(working.Element("NPCName")?.Value ?? string.Empty);
            var nameCard = CreateNpcFieldCard(
                "NPC Name",
                name,
                "Visible NPC name.");

            var model = CreateNpcTextBox(working.Element("Model")?.Value ?? "0");
            var modelCard = CreateNpcFieldCard(
                "Model ID",
                model,
                "Model.xml reference. Data\\Digimon models use ImgDatabase/Digimon; Data\\Npc models use ImgDatabase/Npc.",
                extraHeight: 48);

            var selectModel = CreateEditorActionButton("SELECT MODEL");
            selectModel.Location = new Point(12, 75);
            selectModel.Size = new Size(136, 30);

            var modelInfo = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 7.7F),
                Location = new Point(158, 73),
                Size = new Size(174, 34),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };

            modelCard.Controls.Add(selectModel);
            modelCard.Controls.Add(modelInfo);

            var mapSearch = CreateNpcTextBox(working.Element("MapID")?.Value ?? "0");
            mapSearch.PlaceholderText = "Map ID or Map Name...";

            var mapStatus = CreateNpcStatusLabel();

            var mapSuggestions = new FlowLayoutPanel
            {
                Width = 320,
                Height = 150,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(16, 16, 16),
                Visible = false
            };

            DarkUi.ApplyDarkScrollBar(mapSuggestions);

            var mapCard = CreateNpcFieldCard(
                "Map",
                mapSearch,
                "Search MapList.xml by MapID or MapName.",
                mapStatus,
                extraHeight: 156);

            mapSuggestions.Location = new Point(12, 91);
            mapCard.Controls.Add(mapSuggestions);

            var tag = CreateNpcTextBox(working.Element("NPCTag")?.Value ?? string.Empty);
            var tagCard = CreateNpcFieldCard(
                "NPC Tag / Title",
                tag,
                "Text above/near the NPC name.");

            var desc = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.2F),
                Text = working.Element("NPCDesc")?.Value ?? string.Empty,
                Width = 688,
                Height = 150,
                AcceptsReturn = true
            };

            var descCard = CreateNpcWideCard(
                "NPC Description",
                desc,
                "Line breaks are preserved.");

            var preview = new PictureBox
            {
                Size = new Size(120, 120),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(8, 8, 8)
            };

            var previewStatus = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8F),
                AutoSize = false,
                Width = 190,
                Height = 90
            };

            var previewCard = new Panel
            {
                Width = 344,
                Height = 160,
                BackColor = Color.FromArgb(29, 29, 29),
                Margin = new Padding(0, 0, 10, 10)
            };

            preview.Location = new Point(12, 22);
            previewStatus.Location = new Point(145, 35);
            previewCard.Controls.Add(preview);
            previewCard.Controls.Add(previewStatus);

            var isQuestNpc =
                new CheckBox
                {
                    Text = "QUEST NPC / EXTRA QUEST LOGIC",
                    ForeColor = CText,
                    AutoSize = true,
                    Checked = working.Element("Quest") != null,
                    Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold)
                };

            var questInitState =
                CreateNpcTextBox(
                    working.Element("Quest")?.Element("s_nEInitSate")?.Value ?? "0");

            var questActions =
                new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Color.FromArgb(10, 10, 10),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 8.5F),
                    Width = 688,
                    Height = 120,
                    AcceptsReturn = true,
                    Text = SerializeNpcQuestActions(working)
                };

            var questExternalReferences =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8F),
                    AutoSize = false,
                    Width = 680,
                    Height = 88,
                    Text = "Quest.xml references will be resolved from the current NPC ID."
                };

            var questCard =
                CreateNpcRelationWideCard(
                    "QUEST RELATIONS",
                    isQuestNpc,
                    questInitState,
                    questActions,
                    questExternalReferences,
                    "Action format: ActionType | ECompState | QuestID,QuestID");

            var isTeleportNpc =
                new CheckBox
                {
                    Text = "TELEPORT / PORTAL NPC",
                    ForeColor = CText,
                    AutoSize = true,
                    Checked = working.Element("Portals") != null,
                    Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold)
                };

            var portalType =
                CreateNpcTextBox(
                    working.Element("Portals")?
                        .Element("Portal")?
                        .Element("s_nPortalType")?.Value ?? "0");

            var portalEntries =
                new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BackColor = Color.FromArgb(10, 10, 10),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Consolas", 8.5F),
                    Width = 688,
                    Height = 140,
                    AcceptsReturn = true,
                    Text = SerializeNpcPortalEntries(working)
                };

            var portalCard =
                CreateNpcPortalWideCard(
                    isTeleportNpc,
                    portalType,
                    portalEntries);

            var state = new NpcEditState
            {
                Service = service,
                References = references,
                Working = working,
                OriginalId = originalId,
                Dirty = isNew,
                IsNew = isNew,
                LockType20 = lockType20,
                OnSaved = onSaved,
                Id = id,
                IdStatus = idStatus,
                MapSearch = mapSearch,
                MapStatus = mapStatus,
                MapSuggestions = mapSuggestions,
                Type = type,
                Name = name,
                Tag = tag,
                Model = model,
                Description = desc,
                Preview = preview,
                PreviewStatus = previewStatus,
                IsQuestNpc = isQuestNpc,
                QuestInitState = questInitState,
                QuestActions = questActions,
                QuestExternalReferences = questExternalReferences,
                IsTeleportNpc = isTeleportNpc,
                PortalType = portalType,
                PortalEntries = portalEntries
            };

            page.Tag = state;

            void RefreshModelInfo()
            {
                if (uint.TryParse(model.Text.Trim(), out uint currentModel) &&
                    references.TryGetModel(currentModel, out EditorModelReference? modelReference))
                {
                    string related =
                        !string.IsNullOrWhiteSpace(modelReference.DigimonNames)
                            ? modelReference.DigimonNames
                            : modelReference.NpcNames;

                    modelInfo.Text =
                        $"{modelReference.Kind}: {related}";

                    modelInfo.ForeColor =
                        modelReference.Kind.Equals(
                            "Digimon",
                            StringComparison.OrdinalIgnoreCase)
                            ? Color.FromArgb(125, 220, 140)
                            : CMuted;
                }
                else
                {
                    modelInfo.Text = "Model not found in Model.xml";
                    modelInfo.ForeColor = Color.FromArgb(255, 95, 95);
                }
            }

            selectModel.Click += (_, _) =>
                ShowNpcModelPicker(page, state);

            void MarkDirty()
            {
                state.Dirty = true;
                PullNpcStateToXml(state);
            }

            id.TextChanged += (_, _) =>
            {
                MarkDirty();
                loading.BringToFront();
            page.ResumeLayout(true);
            loading.Refresh();

            UpdateNpcIdState(state);

            page.Controls.Remove(loading);
            loading.Dispose();
            page.PerformLayout();
            page.Update();
                UpdateNpcPreview(state);
                RefreshNpcQuestExternalReferences(state);
            };

            mapSearch.TextChanged += (_, _) =>
            {
                MarkDirty();
                RefreshNpcMapSuggestions(state);
            };

            type.SelectedIndexChanged += (_, _) => MarkDirty();
            name.TextChanged += (_, _) => MarkDirty();
            tag.TextChanged += (_, _) => MarkDirty();

            model.TextChanged += (_, _) =>
            {
                MarkDirty();
                RefreshModelInfo();
                UpdateNpcPreview(state);
            };

            desc.TextChanged += (_, _) => MarkDirty();
            isQuestNpc.CheckedChanged += (_, _) => MarkDirty();
            questInitState.TextChanged += (_, _) => MarkDirty();
            questActions.TextChanged += (_, _) => MarkDirty();

            isTeleportNpc.CheckedChanged += (_, _) =>
            {
                if (isTeleportNpc.Checked && !state.LockType20)
                {
                    DarkComboOption? teleport =
                        type.Items.OfType<DarkComboOption>()
                            .FirstOrDefault(x => x.Value == "3");

                    if (teleport != null)
                        type.SelectedItem = teleport;
                }

                MarkDirty();
            };

            portalType.TextChanged += (_, _) => MarkDirty();
            portalEntries.TextChanged += (_, _) => MarkDirty();

            save.Click += (_, _) =>
                SaveNpcEditPage(page, state, showSuccess: true);

            importDatabase.Click +=
                async (_, _) =>
                {
                    if (state.Dirty)
                    {
                        DialogResult answer =
                            MessageBox.Show(
                                "Npc.xml tem alterações não guardadas.\r\n\r\n" +
                                "Guardar antes de importar para a database?",
                                "NPC Database Import",
                                MessageBoxButtons.YesNoCancel,
                                MessageBoxIcon.Question);

                        if (answer == DialogResult.Cancel)
                            return;

                        if (answer == DialogResult.Yes &&
                            !SaveNpcEditPage(page, state, showSuccess: false))
                        {
                            return;
                        }
                    }

                    await OpenNpcDatabaseImportTabAndRunAsync(
                        state.Service.FilePath);
                };

            scroll.Controls.Add(idCard);
            scroll.Controls.Add(typeCard);
            scroll.Controls.Add(nameCard);
            scroll.Controls.Add(modelCard);
            scroll.Controls.Add(mapCard);
            scroll.Controls.Add(previewCard);
            scroll.Controls.Add(tagCard);
            scroll.Controls.Add(descCard);
            scroll.Controls.Add(questCard);
            scroll.Controls.Add(portalCard);

            page.Controls.Add(scroll);
            page.Controls.Add(toolbar);

            UpdateNpcIdState(state);
            UpdateNpcMapState(state);
            RefreshModelInfo();
            UpdateNpcPreview(state);
            RefreshNpcQuestExternalReferences(state);
        }

        private bool SaveNpcEditPage(
            TabPage page,
            NpcEditState state,
            bool showSuccess)
        {
            try
            {
                PullNpcStateToXml(state);

                if (!uint.TryParse(
                        state.Working.Element("MapID")?.Value,
                        out uint mapId) ||
                    !state.References.TryGetMap(mapId, out _))
                {
                    throw new InvalidDataException(
                        $"MapID {state.Working.Element("MapID")?.Value} não existe em MapList.xml.");
                }

                ValidateNpcQuestAndPortalRelations(state);

                state.Service.Save(state.Working, state.OriginalId);

                EditorPreloadService.InvalidateNpc(
                    state.Service.FilePath);

                uint newId = uint.Parse(state.Working.Element("NpcID")!.Value);

                state.OriginalId = newId;
                state.IsNew = false;
                state.Dirty = false;

                state.References.ReloadNpc(state.Service.FilePath);

                string name =
                    state.Working.Element("NPCName")?.Value
                    ?? $"NPC {newId}";

                page.Text = $"{name} [Saved]";

                state.OnSaved?.Invoke(newId);

                if (showSuccess)
                {
                    MessageBox.Show(
                        $"NPC guardado.\r\n\r\n" +
                        $"NpcID: {newId}\r\n" +
                        $"MapID: {state.Working.Element("MapID")?.Value}\r\n" +
                        $"NPCType: {state.Working.Element("NPCType")?.Value}",
                        "Npc.xml",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowEditorError("Save NPC", ex);
                return false;
            }
        }

        private void PullNpcStateToXml(NpcEditState state)
        {
            EnsureNpcElement(state.Working, "NpcID").Value =
                state.Id.Text.Trim();

            if (TryExtractLeadingUInt(state.MapSearch.Text, out uint mapId))
            {
                EnsureNpcElement(state.Working, "MapID").Value =
                    mapId.ToString(CultureInfo.InvariantCulture);
            }

            DarkComboOption? selected =
                state.Type.SelectedItem as DarkComboOption;

            if (state.LockType20)
            {
                EnsureNpcElement(state.Working, "NPCType").Value = "20";
            }
            else if (selected != null)
            {
                EnsureNpcElement(state.Working, "NPCType").Value =
                    selected.Value;
            }

            EnsureNpcElement(state.Working, "NPCName").Value =
                state.Name.Text;

            EnsureNpcElement(state.Working, "NPCTag").Value =
                NormalizeNpcTag(state.Tag.Text);

            EnsureNpcElement(state.Working, "Model").Value =
                state.Model.Text.Trim();

            EnsureNpcElement(state.Working, "NPCDesc").Value =
                state.Description.Text;

            ApplyNpcQuestEditorToXml(state);
            ApplyNpcPortalEditorToXml(state);
        }

        private void RefreshNpcQuestExternalReferences(
            NpcEditState state)
        {
            if (!int.TryParse(
                    state.Id.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int npcId) ||
                npcId <= 0)
            {
                state.QuestExternalReferences.Text =
                    "Quest.xml: enter a valid NPC ID to inspect references.";
                return;
            }

            var service = new NpcQuestReferenceService();
            IReadOnlyList<NpcQuestReference> refs =
                service.FindNpcReferences(npcId);

            if (refs.Count == 0)
            {
                state.QuestExternalReferences.Text =
                    "Quest.xml: no StartTarget/Target/Talk-goal references found.";
                return;
            }

            state.QuestExternalReferences.Text =
                "Quest.xml references:\r\n" +
                string.Join(
                    "\r\n",
                    refs.Take(5).Select(x =>
                        $"{x.QuestId} — {x.Relation} — {x.Title}")) +
                (refs.Count > 5
                    ? $"\r\n... +{refs.Count - 5} more"
                    : string.Empty);
        }

        private static string SerializeNpcQuestActions(XElement npc)
        {
            XElement? quest = npc.Element("Quest");
            if (quest == null)
                return string.Empty;

            return string.Join(
                Environment.NewLine,
                quest.Elements("Action").Select(action =>
                {
                    string ids = string.Join(
                        ",",
                        action.Element("QuestIds")?
                            .Elements("QuestId")
                            .Select(x => x.Value)
                        ?? Enumerable.Empty<string>());

                    return
                        $"{action.Element("ActionType")?.Value ?? "0"} | " +
                        $"{action.Element("ECompState")?.Value ?? "0"} | {ids}";
                }));
        }

        private static string SerializeNpcPortalEntries(XElement npc)
        {
            XElement? portal = npc.Element("Portals")?.Element("Portal");
            if (portal == null)
                return string.Empty;

            return string.Join(
                Environment.NewLine,
                portal.Element("PortalsType")?
                    .Elements("PortalType")
                    .Select(pt =>
                    {
                        var parts = new List<string>
                        {
                            pt.Element("s_dwEventID")?.Value ?? "0"
                        };

                        foreach (XElement req in
                            pt.Element("Req")?.Elements("ReqItem")
                            ?? Enumerable.Empty<XElement>())
                        {
                            parts.Add(
                                $"{req.Element("s_eEnableType")?.Value ?? "0"}," +
                                $"{req.Element("s_nEnableID")?.Value ?? "0"}," +
                                $"{req.Element("s_nEnableCount")?.Value ?? "0"}");
                        }

                        while (parts.Count < 4)
                            parts.Add("0,0,0");

                        return string.Join(" | ", parts.Take(4));
                    })
                ?? Enumerable.Empty<string>());
        }

        private static void ApplyNpcQuestEditorToXml(
            NpcEditState state)
        {
            state.Working.Element("Quest")?.Remove();

            if (!state.IsQuestNpc.Checked)
            {
                EnsureNpcElement(state.Working, "nExtraData").Value = "0";
                return;
            }

            if (!int.TryParse(
                    state.QuestInitState.Text.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int initState))
            {
                throw new InvalidDataException("Quest Init State inválido.");
            }

            var quest = new XElement(
                "Quest",
                new XElement("s_nEInitSate", initState));

            var actions = new List<XElement>();

            foreach (string raw in state.QuestActions.Lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                string[] parts = line.Split('|');
                if (parts.Length != 3)
                    throw new InvalidDataException(
                        $"Quest action inválida: '{line}'. Esperado ActionType | ECompState | QuestIDs.");

                int actionType = ParseEditorInt(parts[0], "ActionType");
                int compState = ParseEditorInt(parts[1], "ECompState");

                int[] ids = parts[2]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => ParseEditorInt(x, "QuestId"))
                    .ToArray();

                actions.Add(
                    new XElement(
                        "Action",
                        new XElement("ActionType", actionType),
                        new XElement("ECompState", compState),
                        new XElement("QuestCount", ids.Length),
                        new XElement(
                            "QuestIds",
                            ids.Select(x => new XElement("QuestId", x)))));
            }

            quest.Add(new XElement("nActcnt", actions.Count));
            quest.Add(actions);

            EnsureNpcElement(state.Working, "nExtraData").Value = "1";
            EnsureNpcElement(state.Working, "nExtraData").AddAfterSelf(quest);
        }

        private static void ApplyNpcPortalEditorToXml(
            NpcEditState state)
        {
            state.Working.Element("Portals")?.Remove();

            if (!state.IsTeleportNpc.Checked)
                return;

            if (state.LockType20)
                throw new InvalidDataException(
                    "Quick ItemMaking NPC não pode simultaneamente ser Teleport NPC.");

            int portalType =
                ParseEditorInt(
                    state.PortalType.Text,
                    "Portal Type");

            var portalTypes = new List<XElement>();

            foreach (string raw in state.PortalEntries.Lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                string[] parts = line.Split('|');
                if (parts.Length != 4)
                    throw new InvalidDataException(
                        $"Portal line inválida: '{line}'. Esperado EventID | Type,ID,Count | Type,ID,Count | Type,ID,Count.");

                int eventId = ParseEditorInt(parts[0], "Portal EventID");
                var reqItems = new List<XElement>();

                for (int i = 1; i <= 3; i++)
                {
                    string[] req = parts[i]
                        .Split(',', StringSplitOptions.TrimEntries);

                    if (req.Length != 3)
                        throw new InvalidDataException(
                            $"Portal Req {i} inválido em '{line}'.");

                    reqItems.Add(
                        new XElement(
                            "ReqItem",
                            new XElement("s_eEnableType", ParseEditorInt(req[0], "Req Type")),
                            new XElement("s_nEnableID", ParseEditorInt(req[1], "Req ID")),
                            new XElement("s_nEnableCount", ParseEditorInt(req[2], "Req Count"))));
                }

                portalTypes.Add(
                    new XElement(
                        "PortalType",
                        new XElement("s_dwEventID", eventId),
                        new XElement("Req", reqItems)));
            }

            var portals =
                new XElement(
                    "Portals",
                    new XElement(
                        "Portal",
                        new XElement("s_nPortalType", portalType),
                        new XElement("s_nPortalCount", portalTypes.Count),
                        new XElement("PortalsType", portalTypes)));

            EnsureNpcElement(state.Working, "NPCDesc").AddAfterSelf(portals);
            EnsureNpcElement(state.Working, "NPCType").Value = "3";
        }

        private static void ValidateNpcQuestAndPortalRelations(
            NpcEditState state)
        {
            if (state.IsTeleportNpc.Checked)
            {
                XElement? portal =
                    state.Working.Element("Portals")?.Element("Portal");

                if (portal == null ||
                    !portal.Element("PortalsType")!.Elements("PortalType").Any())
                {
                    throw new InvalidDataException(
                        "Teleport NPC precisa de pelo menos um PortalType.");
                }
            }

            if (state.IsQuestNpc.Checked)
            {
                var quests = new NpcQuestReferenceService();

                foreach (XElement id in
                    state.Working.Element("Quest")?
                        .Descendants("QuestId")
                    ?? Enumerable.Empty<XElement>())
                {
                    int questId = ParseEditorInt(id.Value, "QuestId");

                    if (!quests.Exists(questId))
                    {
                        throw new InvalidDataException(
                            $"QuestId {questId} não existe em XML\\Quest\\Quest.xml.");
                    }
                }
            }
        }

        private static int ParseEditorInt(
            string value,
            string field)
        {
            if (!int.TryParse(
                    value.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int result))
            {
                throw new InvalidDataException(
                    $"{field}: valor inteiro inválido '{value}'.");
            }

            return result;
        }

        private Panel CreateNpcRelationWideCard(
            string title,
            CheckBox enabled,
            TextBox initState,
            TextBox actions,
            Label external,
            string hint)
        {
            var card = new Panel
            {
                Width = 710,
                Height = 310,
                BackColor = Color.FromArgb(29, 29, 29),
                Margin = new Padding(0, 0, 10, 12)
            };

            var titleLabel = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(14, 12),
                Size = new Size(300, 24)
            };

            enabled.Location = new Point(14, 43);

            var initLabel = new Label
            {
                Text = "Init State",
                ForeColor = CMuted,
                Location = new Point(14, 73),
                Size = new Size(90, 22)
            };

            initState.Location = new Point(105, 70);
            initState.Width = 100;

            var hintLabel = new Label
            {
                Text = hint,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 7.8F),
                Location = new Point(220, 72),
                Size = new Size(470, 22)
            };

            actions.Location = new Point(14, 101);
            actions.Width = 680;

            external.Location = new Point(14, 229);

            card.Controls.Add(titleLabel);
            card.Controls.Add(enabled);
            card.Controls.Add(initLabel);
            card.Controls.Add(initState);
            card.Controls.Add(hintLabel);
            card.Controls.Add(actions);
            card.Controls.Add(external);
            return card;
        }

        private Panel CreateNpcPortalWideCard(
            CheckBox enabled,
            TextBox portalType,
            TextBox entries)
        {
            var card = new Panel
            {
                Width = 710,
                Height = 250,
                BackColor = Color.FromArgb(29, 29, 29),
                Margin = new Padding(0, 0, 10, 12)
            };

            var title = new Label
            {
                Text = "TELEPORT / PORTAL",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(14, 12),
                Size = new Size(260, 24)
            };

            enabled.Location = new Point(14, 43);

            var typeLabel = new Label
            {
                Text = "Portal Type",
                ForeColor = CMuted,
                Location = new Point(14, 73),
                Size = new Size(90, 22)
            };

            portalType.Location = new Point(105, 70);
            portalType.Width = 100;

            var hint = new Label
            {
                Text =
                    "One line per PortalType: EventID | ReqType,ReqID,ReqCount | Req2 | Req3. " +
                    "PortalCount is calculated automatically.",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 7.8F),
                Location = new Point(220, 69),
                Size = new Size(470, 35)
            };

            entries.Location = new Point(14, 107);
            entries.Width = 680;
            entries.Height = 126;

            card.Controls.Add(title);
            card.Controls.Add(enabled);
            card.Controls.Add(typeLabel);
            card.Controls.Add(portalType);
            card.Controls.Add(hint);
            card.Controls.Add(entries);
            return card;
        }

        private static string NormalizeNpcTag(
            string? value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
                return string.Empty;

            while (text.StartsWith("<", StringComparison.Ordinal))
                text = text[1..].TrimStart();

            while (text.EndsWith(">", StringComparison.Ordinal))
                text = text[..^1].TrimEnd();

            return text.Length == 0
                ? string.Empty
                : "<" + text + ">";
        }

        private void RefreshNpcMapSuggestions(NpcEditState state)
        {
            var maps = state.References.SearchMaps(state.MapSearch.Text, 20);

            state.MapSuggestions.SuspendLayout();
            state.MapSuggestions.Controls.Clear();

            foreach (EditorMapReference map in maps)
            {
                var button =
                    CreateEditorActionButton($"{map.Id} — {map.Name}");

                button.Width = 300;
                button.Height = 28;
                button.TextAlign = ContentAlignment.MiddleLeft;

                button.Click += (_, _) =>
                {
                    state.MapSearch.Text =
                        $"{map.Id} — {map.Name}";

                    EnsureNpcElement(state.Working, "MapID").Value =
                        map.Id.ToString(CultureInfo.InvariantCulture);

                    state.MapSuggestions.Visible = false;
                    UpdateNpcMapState(state);
                };

                state.MapSuggestions.Controls.Add(button);
            }

            state.MapSuggestions.ResumeLayout();

            state.MapSuggestions.Visible =
                state.MapSearch.Focused &&
                maps.Count > 0;

            UpdateNpcMapState(state);
        }

        private void UpdateNpcMapState(NpcEditState state)
        {
            if (!TryExtractLeadingUInt(
                    state.MapSearch.Text,
                    out uint mapId) ||
                !state.References.TryGetMap(
                    mapId,
                    out EditorMapReference? map))
            {
                state.MapStatus.Text = "MAP ID INVALID";
                state.MapStatus.ForeColor = Color.FromArgb(255, 95, 95);
                return;
            }

            state.MapStatus.Text =
                $"VALID — {map.Id} — {map.Name}";

            state.MapStatus.ForeColor =
                Color.FromArgb(125, 220, 140);
        }

        private void UpdateNpcIdState(NpcEditState state)
        {
            if (!uint.TryParse(
                    state.Id.Text.Trim(),
                    out uint id) ||
                id == 0)
            {
                state.IdStatus.Text = "INVALID ID";
                state.IdStatus.ForeColor = Color.FromArgb(255, 95, 95);
                return;
            }

            bool own =
                state.OriginalId.HasValue &&
                state.OriginalId.Value == id;

            bool exists =
                state.Service.Exists(id) &&
                !own;

            state.IdStatus.Text =
                exists
                    ? "ID EXISTS"
                    : "ID AVAILABLE";

            state.IdStatus.ForeColor =
                exists
                    ? Color.FromArgb(255, 95, 95)
                    : Color.FromArgb(125, 220, 140);
        }

        private void UpdateNpcPreview(NpcEditState state)
        {
            state.Preview.Image?.Dispose();
            state.Preview.Image = null;

            uint.TryParse(
                state.Model.Text.Trim(),
                out uint model);

            uint.TryParse(
                state.Id.Text.Trim(),
                out uint npcId);

            state.PreviewStatus.Text =
                "Loading model preview...";

            LoadNpcPreviewInto(
                state.Preview,
                model,
                npcId,
                state.References,
                loaded =>
                {
                    state.PreviewStatus.Text =
                        loaded
                            ? $"NPC image loaded\r\nModel: {model}\r\nNpcID: {npcId}"
                            : "No image found for this Model/NpcID.";
                });
        }

        private async void LoadNpcPreviewInto(
            PictureBox target,
            uint modelId,
            uint npcId,
            EditorReferenceCatalogService references,
            Action<bool>? completed = null)
        {
            try
            {
                Bitmap? bitmap =
                    await NpcPreviewCache
                        .GetPreviewAsync(
                            modelId,
                            npcId,
                            references);

                if (target.IsDisposed ||
                    target.Disposing)
                {
                    bitmap?.Dispose();
                    return;
                }

                Image? old =
                    target.Image;

                target.Image =
                    bitmap;

                old?.Dispose();

                completed?.Invoke(
                    bitmap != null);
            }
            catch
            {
                if (!target.IsDisposed)
                    completed?.Invoke(false);
            }
        }

        private void ShowNpcModelPicker(
            TabPage page,
            NpcEditState state)
        {
            var overlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20)
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = Color.FromArgb(31, 31, 31)
            };

            var title = new Label
            {
                Text = "Select Model — Model.xml + Digimon_List.xml + Npc.xml",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Location = new Point(16, 8),
                Size = new Size(600, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var close = CreateEditorActionButton("CLOSE");
            close.Dock = DockStyle.Right;
            close.Width = 90;
            close.Click += (_, _) => overlay.Dispose();

            header.Controls.Add(title);
            header.Controls.Add(close);

            var search = CreateNpcTextBox(string.Empty);
            search.PlaceholderText =
                "Search Model ID, Digimon name, NPC name or KFM path...";
            search.Location = new Point(18, 62);
            search.Size = new Size(700, 31);

            var resultCount = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(730, 62),
                Size = new Size(130, 31),
                TextAlign = ContentAlignment.MiddleRight
            };

            var results = new FlowLayoutPanel
            {
                Location = new Point(18, 105),
                Size = new Size(842, 470),
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
                IReadOnlyList<EditorModelReference> models =
                    state.References.SearchModels(
                        search.Text,
                        50);

                results.SuspendLayout();
                DisposeChildImages(results);
                results.Controls.Clear();

                foreach (EditorModelReference entry in models)
                {
                    var card = CreateNpcCard(800, 92);

                    var image = new PictureBox
                    {
                        Location = new Point(10, 10),
                        Size = new Size(70, 70),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.FromArgb(8, 8, 8)
                    };

                    LoadNpcPreviewInto(
                        image,
                        entry.Id,
                        0,
                        state.References);

                    string relatedText = string.Empty;

                    if (!string.IsNullOrWhiteSpace(entry.DigimonNames))
                        relatedText += $"Digimon: {entry.DigimonNames}";

                    if (!string.IsNullOrWhiteSpace(entry.NpcNames))
                    {
                        if (relatedText.Length > 0)
                            relatedText += "   |   ";

                        relatedText += $"NPC: {entry.NpcNames}";
                    }

                    var name = new Label
                    {
                        Text =
                            $"{entry.Id} — {entry.DisplayName}   [{entry.Kind}]",
                        ForeColor =
                            entry.Kind.Equals(
                                "Digimon",
                                StringComparison.OrdinalIgnoreCase)
                                ? Color.FromArgb(125, 220, 140)
                                : CText,
                        Font = new Font(
                            "Segoe UI Semibold",
                            9.2F,
                            FontStyle.Bold),
                        Location = new Point(94, 10),
                        Size = new Size(490, 23),
                        AutoEllipsis = true
                    };

                    var related = new Label
                    {
                        Text = relatedText,
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 7.8F),
                        Location = new Point(94, 35),
                        Size = new Size(490, 20),
                        AutoEllipsis = true
                    };

                    var path = new Label
                    {
                        Text = entry.KfmPath,
                        ForeColor = Color.FromArgb(145, 145, 145),
                        Font = new Font("Consolas", 7.2F),
                        Location = new Point(94, 58),
                        Size = new Size(500, 20),
                        AutoEllipsis = true
                    };

                    var select = CreateEditorActionButton("SELECT");
                    select.Location = new Point(680, 28);
                    select.Size = new Size(90, 34);

                    select.Click += (_, _) =>
                    {
                        state.Model.Text =
                            entry.Id.ToString(
                                CultureInfo.InvariantCulture);

                        overlay.Dispose();
                    };

                    card.Controls.Add(image);
                    card.Controls.Add(name);
                    card.Controls.Add(related);
                    card.Controls.Add(path);
                    card.Controls.Add(select);

                    results.Controls.Add(card);
                }

                results.ResumeLayout();
                resultCount.Text = $"{models.Count} models";
            }

            search.TextChanged += (_, _) => Refresh();

            overlay.Controls.Add(header);
            overlay.Controls.Add(search);
            overlay.Controls.Add(resultCount);
            overlay.Controls.Add(results);

            page.Controls.Add(overlay);
            overlay.BringToFront();

            Refresh();
        }

        private Panel CreateNpcFieldCard(
            string title,
            Control editor,
            string hint,
            Label? status = null,
            int extraHeight = 0)
        {
            var panel = CreateNpcCard(344, 104 + extraHeight);

            var label = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold),
                Location = new Point(12, 10),
                Size = new Size(260, 23)
            };

            var help = CreateHelpBubble(hint);
            help.Location = new Point(310, 7);

            editor.Location = new Point(12, 39);
            editor.Width = 320;
            editor.Height = 30;

            panel.Controls.Add(label);
            panel.Controls.Add(help);
            panel.Controls.Add(editor);

            if (status != null)
            {
                status.Location = new Point(12, 73);
                status.Size = new Size(320, 22);
                panel.Controls.Add(status);
            }

            return panel;
        }

        private Panel CreateNpcWideCard(
            string title,
            Control editor,
            string hint)
        {
            var panel = CreateNpcCard(708, 205);

            var label = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9.2F, FontStyle.Bold),
                Location = new Point(12, 9),
                Size = new Size(620, 24)
            };

            var help = CreateHelpBubble(hint);
            help.Location = new Point(674, 7);

            editor.Location = new Point(12, 39);

            panel.Controls.Add(label);
            panel.Controls.Add(help);
            panel.Controls.Add(editor);

            return panel;
        }

        private static Panel CreateNpcCard(int width, int height)
        {
            var panel = new Panel
            {
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(29, 29, 29),
                Margin = new Padding(0, 0, 10, 10)
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

        private static TextBox CreateNpcTextBox(string value) =>
            new TextBox
            {
                Text = value,
                BackColor = Color.FromArgb(11, 11, 11),
                ForeColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.3F)
            };

        private static Label CreateNpcStatusLabel() =>
            new Label
            {
                ForeColor = Color.FromArgb(125, 220, 140),
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold)
            };

        private static XElement EnsureNpcElement(XElement npc, string tag)
        {
            XElement? element = npc.Element(tag);

            if (element != null)
                return element;

            element = new XElement(tag);
            npc.Add(element);

            return element;
        }

        private static bool TryExtractLeadingUInt(
            string text,
            out uint value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string first = new string(
                text
                    .Trim()
                    .TakeWhile(char.IsDigit)
                    .ToArray());

            return uint.TryParse(first, out value);
        }
    }
}
