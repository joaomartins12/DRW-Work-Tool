using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private Panel editorWorkspace = null!;
        private EditorTabControl editorTabs = null!;
        private Label editorEmptyLabel = null!;

        private readonly ToolTip editorToolTip =
            new ToolTip
            {
                AutoPopDelay = 12000,
                InitialDelay = 350,
                ReshowDelay = 120,
                ShowAlways = true
            };

        private readonly Dictionary<uint, Bitmap?> itemIconCache = new();
        private readonly Dictionary<uint, Bitmap?> skillIconCache = new();

        private sealed class LinkedReferencePickerState
        {
            public required ItemEditState ItemState { get; init; }
            public required XElement SkillNode { get; init; }
            public required LinkedItemReferenceService Service { get; init; }
            public required TextBox Search { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required Label CountLabel { get; init; }
            public required Button SkillSourceButton { get; init; }
            public required Button AccessorySourceButton { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }

            public LinkedItemReferenceSource Source { get; set; }
        }

        private sealed class EntityTabState
        {
            public required string Entity { get; init; }
        }

        private sealed class ItemListBrowseState
        {
            public required ItemListEditorService Service { get; init; }
            public required TextBox Search { get; init; }
            public required Label CountLabel { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
            public required System.Windows.Forms.Timer IconTimer { get; init; }
        }

        private sealed class ItemEditState
        {
            public required ItemListEditorService Service { get; init; }
            public required XElement Working { get; init; }
            public uint? OriginalId { get; set; }
            public bool IsNew { get; set; }
            public bool Dirty { get; set; }
            public bool SavedOnce { get; set; }
            public required Dictionary<XElement, Control> Editors { get; init; }
            public Label IdStatus { get; set; } = null!;
            public PictureBox IconPreview { get; set; } = null!;
            public Panel FormPanel { get; set; } = null!;
            public RichTextBox XmlPreview { get; set; } = null!;
            public Button ToggleXmlButton { get; set; } = null!;

            public ItemDisplayEditorService? ItemDisplayService { get; set; }
            public CheckBox SyncItemDisplay { get; set; } = null!;
            public Label ItemDisplayStatus { get; set; } = null!;

            public uint? OriginalSection { get; set; }

            public LinkedItemReferenceService? LinkedReferenceService { get; set; }
            public Label LinkedReferenceStatus { get; set; } = null!;
        }

        private sealed class GenericBrowseState
        {
            public required GenericXmlBlockService Service { get; init; }
            public required TextBox Search { get; init; }
            public required Label CountLabel { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
        }

        private void BuildEditorWorkspace()
        {
            editorWorkspace = new Panel
            {
                BackColor = CEditor,
                Visible = false
            };

            editorTabs = new EditorTabControl
            {
                Location = new Point(10, 10),
                Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Bottom |
                    AnchorStyles.Left |
                    AnchorStyles.Right,
                Visible = true
            };

            editorTabs.TabClosing +=
                EditorTabs_TabClosing;

            editorTabs.SelectedIndexChanged +=
                (_, _) =>
                    UpdateEditorEmptyState();

            editorEmptyLabel = new Label
            {
                Text =
                    "Seleciona EDIT numa entidade para abrir os XMLs disponíveis.",
                ForeColor = CMuted,
                BackColor = CEditor,
                Font =
                    new Font(
                        "Segoe UI",
                        11F),
                TextAlign =
                    ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };

            editorWorkspace.Controls.Add(
                editorEmptyLabel);

            editorWorkspace.Controls.Add(
                editorTabs);

            rightPanel.Controls.Add(
                editorWorkspace);
        }

        private void LayoutEditorWorkspace()
        {
            if (editorWorkspace == null)
                return;

            editorWorkspace.Location =
                new Point(0, 0);

            editorWorkspace.Size =
                rightPanel.ClientSize;

            if (editorTabs != null)
            {
                editorTabs.Location =
                    new Point(8, 8);

                editorTabs.Size =
                    new Size(
                        Math.Max(
                            100,
                            editorWorkspace.Width - 16),
                        Math.Max(
                            100,
                            editorWorkspace.Height - 16));
            }
        }

        private void SetEditorWorkspaceVisible(
            bool visible)
        {
            if (editorWorkspace == null)
                return;

            editorWorkspace.Visible = visible;
            editorWorkspace.BringToFront();

            UpdateEditorEmptyState();
        }

        private void UpdateEditorEmptyState()
        {
            if (editorEmptyLabel == null ||
                editorTabs == null)
            {
                return;
            }

            bool isEmpty =
                editorTabs.TabPages.Count == 0;

            editorEmptyLabel.Visible = isEmpty;
            editorTabs.Visible = !isEmpty;

            if (isEmpty)
                editorEmptyLabel.BringToFront();
            else
                editorTabs.BringToFront();
        }

        // Kept because older calls may still invoke it.
        // Custom EditorTabControl has no native white chrome.
        private void UpdateEditorTabChrome()
        {
        }

        private Panel ShowEditorBusyOverlay(
            Control host,
            string title,
            string message)
        {
            foreach (Control existing in host.Controls.OfType<Control>().ToArray())
            {
                if (string.Equals(existing.Name, "EditorBusyOverlay", StringComparison.Ordinal))
                {
                    host.Controls.Remove(existing);
                    existing.Dispose();
                }
            }

            var overlay = new Panel
            {
                Name = "EditorBusyOverlay",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20)
            };

            var loading = new EditorLoadingView(title, message);
            overlay.Controls.Add(loading);

            host.Controls.Add(overlay);
            overlay.BringToFront();
            overlay.Focus();
            host.Update();
            return overlay;
        }

        private void HideEditorBusyOverlay(
            Control host,
            Control? overlay)
        {
            if (overlay == null)
                return;

            if (!host.IsDisposed && host.Controls.Contains(overlay))
                host.Controls.Remove(overlay);

            overlay.Dispose();

            if (!host.IsDisposed)
                host.Update();
        }

        private async Task RunEditorBusyAsync(
            Control host,
            string title,
            string message,
            Action action)
        {
            if (host.IsDisposed)
                return;

            Control overlay = ShowEditorBusyOverlay(host, title, message);

            try
            {
                await Task.Yield();

                if (host.IsDisposed)
                    return;

                action();
            }
            finally
            {
                if (!host.IsDisposed)
                    HideEditorBusyOverlay(host, overlay);
            }
        }

        private void OpenEntityEditor(string entity)
        {
            if (!editorMode)
                return;

            TabPage? existing = editorTabs.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(x =>
                    x.Tag is EntityTabState state &&
                    state.Entity.Equals(entity, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            string folder = Path.Combine(AppPaths.Xml, entity);

            var page = CreateDarkTab(entity);
            page.Tag = new EntityTabState { Entity = entity };

            var entityHeader = new Panel
            {
                Name = "EntityEditorHeader",
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = CEditor,
                Padding = new Padding(0)
            };

            entityHeader.Paint += (_, e) =>
            {
                using var p =
                    new Pen(
                        Color.FromArgb(
                            48,
                            48,
                            48));

                e.Graphics.DrawLine(
                    p,
                    20,
                    entityHeader.Height - 1,
                    Math.Max(
                        20,
                        entityHeader.Width - 20),
                    entityHeader.Height - 1);
            };

            var title = new Label
            {
                Text = $"{entity} - XML disponíveis",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                Location = new Point(20, 14),
                Size = new Size(500, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var subtitle = new Label
            {
                Text = folder,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(22, 49),
                Size = new Size(600, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AutoEllipsis = true
            };

            var listHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(20, 12, 20, 12)
            };

            var list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(0, 0, 8, 0)
            };

            DarkUi.ApplyDarkScrollBar(list);

            entityHeader.Controls.Add(title);
            entityHeader.Controls.Add(subtitle);

            listHost.Controls.Add(list);

            page.Controls.Add(listHost);
            page.Controls.Add(entityHeader);

            AddDatabaseImportButtonIfSupported(
                entityHeader,
                entity,
                folder);

            void LayoutEntityHeaderText()
            {
                Control? importHost =
                    entityHeader.Controls[
                        "DatabaseImportButtonHost"];

                int reservedRight =
                    importHost?.Width ?? 0;

                title.Width =
                    Math.Max(
                        220,
                        entityHeader.ClientSize.Width -
                        title.Left -
                        reservedRight -
                        16);

                subtitle.Width =
                    Math.Max(
                        220,
                        entityHeader.ClientSize.Width -
                        subtitle.Left -
                        reservedRight -
                        16);
            }

            entityHeader.Resize +=
                (_, _) =>
                    LayoutEntityHeaderText();

            LayoutEntityHeaderText();

            if (!Directory.Exists(folder))
            {
                list.Controls.Add(CreateInfoLabel(
                    $"A pasta XML ainda não existe:\n{folder}\n\nFaz EXPORT primeiro para gerar os XMLs."));
            }
            else
            {
                List<string> xmlFiles = Directory
                    .EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (xmlFiles.Count == 0)
                {
                    list.Controls.Add(CreateInfoLabel("Não existem ficheiros XML nesta entidade."));
                }
                else
                {
                    foreach (string xml in xmlFiles)
                        list.Controls.Add(CreateXmlFileCard(entity, folder, xml));
                }
            }

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            UpdateEditorEmptyState();
            UpdateEditorTabChrome();
            UpdateEditorTabChrome();
        }

        private Control CreateXmlFileCard(string entity, string entityFolder, string xmlPath)
        {
            var card = new Panel
            {
                Width = 720,
                Height = 58,
                BackColor = Color.FromArgb(30, 30, 30),
                Margin = new Padding(0, 0, 0, 8)
            };

            card.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(55, 55, 55));
                e.Graphics.DrawRectangle(
                    p,
                    0,
                    0,
                    card.Width - 1,
                    card.Height - 1);
            };

            card.MouseEnter += (_, _) =>
                card.BackColor = Color.FromArgb(39, 39, 39);

            card.MouseLeave += (_, _) =>
                card.BackColor = Color.FromArgb(31, 31, 31);

            string relative = Path.GetRelativePath(entityFolder, xmlPath);

            var fileLabel = new Label
            {
                Text = relative,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Location = new Point(14, 6),
                Size = new Size(515, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var pathLabel = new Label
            {
                Text = xmlPath,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 7.8F),
                Location = new Point(14, 30),
                Size = new Size(520, 20),
                AutoEllipsis = true
            };

            var open = CreateEditorActionButton("OPEN");
            open.Location = new Point(590, 11);
            open.Size = new Size(110, 34);
            open.Click += (_, _) => OpenXmlEditor(entity, xmlPath);

            card.Controls.Add(fileLabel);
            card.Controls.Add(pathLabel);
            card.Controls.Add(open);
            return card;
        }

        private void OpenXmlEditor(string entity, string xmlPath)
        {
            string full = Path.GetFullPath(xmlPath);
            string xmlFileName = Path.GetFileName(xmlPath);

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        x => string.Equals(
                            x.Name,
                            full,
                            StringComparison.OrdinalIgnoreCase));

            // Special editors are routed BEFORE normal stale-tab reuse.
            // This prevents Digimon_List.xml from ever being reopened as the
            // generic XML Block Browser.
            if (xmlFileName.Equals(
                    "Digimon_List.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "DigimonBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenDigimonListBrowser(xmlPath);
                return;
            }

            if (xmlFileName.Equals(
                    "DigimonEvo.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "DigimonEvoBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenDigimonEvoBrowser(xmlPath);
                return;
            }

            if (xmlFileName.Equals(
                    "Skill.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "SkillBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenSkillBrowser(xmlPath);
                return;
            }

            if (xmlFileName.Equals(
                    "Buff.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "BuffBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenBuffBrowser(xmlPath);
                return;
            }

            if (xmlFileName.Equals(
                    "Monster.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "MonsterBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenMonsterBrowser(xmlPath);
                return;
            }

            if (xmlFileName.Equals(
                    "MonstersSkill.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "MonsterSkillBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenMonsterSkillBrowser(xmlPath);
                return;
            }

            if (xmlFileName.Equals(
                    "MonstersSkillTerms.xml",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    string stateType =
                        existing.Tag?.GetType().Name
                        ?? string.Empty;

                    if (stateType.Contains(
                            "MonsterSkillTermsBrowseState",
                            StringComparison.Ordinal))
                    {
                        editorTabs.SelectedTab = existing;
                        return;
                    }

                    editorTabs.TabPages.Remove(existing);
                    existing.Dispose();
                }

                OpenMonsterSkillTermsBrowser(xmlPath);
                return;
            }

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            if (xmlFileName.Equals(
                "ItemList.xml",
                StringComparison.OrdinalIgnoreCase))
            {
                OpenItemListBrowser(xmlPath);
            }
            else if (xmlFileName.Equals(
                "ItemAcessorys.xml",
                StringComparison.OrdinalIgnoreCase))
            {
                OpenItemAccessoryBrowser(xmlPath);
            }
            else if (xmlFileName.Equals(
                "ItemMaking.xml",
                StringComparison.OrdinalIgnoreCase))
            {
                OpenItemMakingBrowser(xmlPath);
            }
            else if (xmlFileName.Equals(
                "ItemDisplay.xml",
                StringComparison.OrdinalIgnoreCase))
            {
                OpenItemDisplayBrowser(xmlPath);
            }
            else if (xmlFileName.Equals(
                "Npc.xml",
                StringComparison.OrdinalIgnoreCase))
            {
                OpenNpcBrowser(xmlPath);
            }
            else
            {
                OpenGenericXmlBrowser(entity, xmlPath);
            }
        }

        private async void OpenItemListBrowser(string xmlPath)
        {
            string fullPath = Path.GetFullPath(xmlPath);

            // A tab aparece imediatamente. O parsing/cache termina em background
            // sem bloquear a UI.
            var page = CreateDarkTab("ItemList.xml");
            page.Name = fullPath;

            var loading =
                new EditorLoadingView(
                    "Loading ItemList",
                    "Preparing ItemList.xml, indexes and cached icons before the editor becomes visible.");

            page.Controls.Add(loading);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            UpdateEditorEmptyState();
            UpdateEditorTabChrome();

            ItemListEditorService service;

            try
            {
                service =
                    await EditorPreloadService.GetItemListAsync(xmlPath);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "ItemList.xml could not be loaded",
                        ex.Message);
                }

                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();
            page.Controls.Clear();

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 124,
                BackColor = Color.FromArgb(27, 27, 27)
            };

            header.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(58, 58, 58));
                e.Graphics.DrawLine(
                    p,
                    0,
                    header.Height - 1,
                    header.Width,
                    header.Height - 1);
            };

            var title = new Label
            {
                Text = "ItemList.xml",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                Location = new Point(20, 12),
                Size = new Size(240, 30)
            };

            var subtitle = new Label
            {
                Text = "Item Database Editor",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(22, 40),
                Size = new Size(260, 20)
            };

            var searchPanel = new Panel
            {
                Location = new Point(20, 68),
                Size = new Size(475, 34),
                BackColor = Color.FromArgb(12, 12, 12)
            };

            searchPanel.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(74, 74, 74));
                e.Graphics.DrawRectangle(
                    p,
                    0,
                    0,
                    searchPanel.Width - 1,
                    searchPanel.Height - 1);
            };

            var search = new TextBox
            {
                Location = new Point(9, 6),
                Size = new Size(455, 22),
                BackColor = Color.FromArgb(12, 12, 12),
                ForeColor = CText,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10F),
                PlaceholderText = "Pesquisar por ItemID ou Item Name..."
            };

            searchPanel.Controls.Add(search);

            var newButton = CreateEditorActionButton("NEW ITEM");
            newButton.Location = new Point(508, 68);
            newButton.Size = new Size(120, 34);

            var countLabel = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.6F),
                Location = new Point(20, 103),
                Size = new Size(760, 19)
            };

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(14, 14, 14, 14)
            };

            DarkUi.ApplyDarkScrollBar(results);

            page.Controls.Add(results);
            page.Controls.Add(header);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(searchPanel);
            header.Controls.Add(newButton);
            header.Controls.Add(countLabel);

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 220
                };

            var iconTimer =
                new System.Windows.Forms.Timer
                {
                    Interval = 80
                };

            var state = new ItemListBrowseState
            {
                Service = service,
                Search = search,
                CountLabel = countLabel,
                Results = results,
                SearchTimer = timer,
                IconTimer = iconTimer
            };

            page.Tag = state;

            timer.Tick += (_, _) =>
            {
                timer.Stop();
                RefreshItemSearch(state);
            };

            iconTimer.Tick += (_, _) =>
            {
                iconTimer.Stop();
                LoadVisibleItemIcons(state);
            };

            search.TextChanged += (_, _) =>
            {
                timer.Stop();
                timer.Start();
            };

            results.Scroll += (_, _) =>
            {
                iconTimer.Stop();
                iconTimer.Start();
            };

            results.MouseWheel += (_, _) =>
            {
                iconTimer.Stop();
                iconTimer.Start();
            };

            newButton.Click += (_, _) =>
            {
                XElement template = service.CreateTemplate();
                OpenItemEditTab(
                    service,
                    template,
                    null,
                    isNew: true);
            };

            page.Disposed += (_, _) =>
            {
                timer.Dispose();
                iconTimer.Dispose();
            };

            // Renderiza poucos cards inicialmente. O utilizador pode começar
            // a pesquisar imediatamente; não criamos centenas de Controls.
            RefreshItemSearch(state);
            page.ResumeLayout(true);
        }

        private void RefreshItemSearch(ItemListBrowseState state)
        {
            string query = state.Search.Text;
            int totalMatches = state.Service.CountSearch(query);

            // O bottleneck do WinForms eram centenas de Panels/PictureBoxes,
            // não a pesquisa. 60 cards tornam o refresh praticamente imediato.
            const int RenderLimit = 60;

            IReadOnlyList<ItemListRecord> rows =
                state.Service.Search(
                    query,
                    RenderLimit);

            DisposeChildImages(state.Results);

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            foreach (ItemListRecord item in rows)
            {
                state.Results.Controls.Add(
                    CreateItemResultCard(
                        state.Service,
                        item));
            }

            state.Results.ResumeLayout(true);

            state.CountLabel.Text =
                $"Total items: {state.Service.TotalItems:N0}    |    " +
                $"Resultados: {totalMatches:N0}" +
                (totalMatches > rows.Count
                    ? $"    |    Mostrando os primeiros {rows.Count:N0}"
                    : string.Empty);

            state.IconTimer.Stop();
            state.IconTimer.Start();
        }

        private Control CreateItemResultCard(ItemListEditorService service, ItemListRecord item)
        {
            var card = new Panel
            {
                Width = 720,
                Height = 68,
                BackColor = Color.FromArgb(31, 31, 31),
                Margin = new Padding(0, 0, 0, 8)
            };

            card.Paint += (_, e) =>
            {
                using var p = new Pen(CBorder);
                e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            var id = new Label
            {
                Text = item.ItemId.ToString(),
                ForeColor = CText,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Location = new Point(12, 0),
                Size = new Size(90, 64),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var icon = new PictureBox
            {
                Location = new Point(105, 12),
                Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(12, 12, 12),
                BorderStyle = BorderStyle.None,
                Tag = item.IconId
            };

            icon.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(62, 62, 62));
                e.Graphics.DrawRectangle(
                    p,
                    0,
                    0,
                    icon.Width - 1,
                    icon.Height - 1);
            };

            var name = new Label
            {
                Text = item.Name,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(158, 5),
                Size = new Size(355, 29),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var iconId = new Label
            {
                Text = $"Icon: {item.IconId}",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(160, 34),
                Size = new Size(180, 20)
            };

            var edit = CreateEditorActionButton("EDIT");
            edit.Location = new Point(520, 14);
            edit.Size = new Size(82, 34);
            edit.Click += (_, _) =>
                OpenItemEditTab(service, service.GetClone(item.ItemId), item.ItemId, isNew: false);

            var delete = CreateEditorActionButton("DELETE");
            delete.Location = new Point(610, 14);
            delete.Size = new Size(92, 34);
            delete.Click += (_, _) => DeleteItem(service, item.ItemId, item.Name);

            card.Controls.Add(id);
            card.Controls.Add(icon);
            card.Controls.Add(name);
            card.Controls.Add(iconId);
            card.Controls.Add(edit);
            card.Controls.Add(delete);
            return card;
        }

        private void LoadVisibleItemIcons(
            ItemListBrowseState state)
        {
            if (state.Results.IsDisposed)
                return;

            Rectangle viewport =
                state.Results.ClientRectangle;

            // Pequena margem acima/abaixo para o próximo scroll já estar pronto.
            viewport.Inflate(0, 120);

            foreach (Control card in state.Results.Controls)
            {
                Rectangle cardBounds =
                    new Rectangle(
                        card.Left + state.Results.AutoScrollPosition.X,
                        card.Top + state.Results.AutoScrollPosition.Y,
                        card.Width,
                        card.Height);

                if (!viewport.IntersectsWith(cardBounds))
                    continue;

                foreach (PictureBox picture in
                         card.Controls.OfType<PictureBox>())
                {
                    if (picture.Image != null ||
                        picture.Tag is not uint iconId)
                    {
                        continue;
                    }

                    picture.Image =
                        GetItemIconPreview(iconId);
                }
            }
        }

        private void DeleteItem(ItemListEditorService service, uint itemId, string itemName)
        {
            if (!TryCloseItemTabs(service, itemId))
                return;

            DialogResult confirm = MessageBox.Show(
                $"Eliminar permanentemente o item?\n\nItemID: {itemId}\nName: {itemName}\n\n" +
                "O <icount> será decrementado automaticamente e será criado um backup .editor.bak.",
                "Delete Item",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                service.Delete(itemId);
                RefreshAllItemListBrowsers(service);
                AppLogger.Success($"ItemList Editor: ItemID {itemId} eliminado. Total={service.TotalItems:N0}.");
            }
            catch (Exception ex)
            {
                ShowEditorError("Delete Item", ex);
            }
        }

        private bool TryCloseItemTabs(ItemListEditorService service, uint itemId)
        {
            List<TabPage> pages = editorTabs.TabPages
                .Cast<TabPage>()
                .Where(x =>
                    x.Tag is ItemEditState state &&
                    ReferenceEquals(state.Service, service) &&
                    state.OriginalId == itemId)
                .ToList();

            foreach (TabPage page in pages)
            {
                if (!CanCloseEditorPage(page))
                    return false;

                editorTabs.TabPages.Remove(page);
                page.Dispose();
            }

            return true;
        }

        private async void OpenItemEditTab(
            ItemListEditorService service,
            XElement working,
            uint? originalId,
            bool isNew)
        {
            if (originalId.HasValue)
            {
                TabPage? existing = editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(x =>
                        x.Tag is ItemEditState state &&
                        ReferenceEquals(state.Service, service) &&
                        state.OriginalId == originalId.Value);

                if (existing != null)
                {
                    editorTabs.SelectedTab = existing;
                    return;
                }
            }

            string currentName = working.Element("s_szName")?.Value ?? "Item";
            var page = CreateDarkTab(isNew ? "New Item [Unsaved]" : $"{currentName} [Edit]");

            var opening =
                new EditorLoadingView(
                    "Loading Item Editor",
                    "Preparing ItemList fields, linked references, ItemDisplay data and icon previews.");

            page.Controls.Add(opening);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            await Task.Yield();
            if(page.IsDisposed)return;
            page.Controls.Clear();

            var state = new ItemEditState
            {
                Service = service,
                Working = working,
                OriginalId = originalId,
                IsNew = isNew,
                Dirty = isNew,
                SavedOnce = false,
                Editors = new Dictionary<XElement, Control>(),
                OriginalSection =
                    TryReadItemUInt(
                        working,
                        "s_nSection")
            };

            string itemDisplayPath =
                Path.Combine(
                    Path.GetDirectoryName(
                        service.FilePath)
                    ?? string.Empty,
                    "ItemDisplay.xml");

            if (File.Exists(itemDisplayPath))
            {
                try
                {
                    state.ItemDisplayService =
                        ItemDisplayEditorService.OpenShared(
                            itemDisplayPath);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning(
                        "ItemList Editor: ItemDisplay.xml não pôde ser carregado: " +
                        ex.Message);
                }
            }

            try
            {
                state.LinkedReferenceService =
                    LinkedItemReferenceService.GetShared();
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    "ItemList Editor: Skill/Accessory reference catalog não pôde ser carregado: " +
                    ex.Message);
            }

            page.Tag = state;

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = CPanel
            };

            var save = CreateEditorActionButton("SAVE");
            save.Location = new Point(12, 10);
            save.Size = new Size(90, 34);
            save.Click += (_, _) => SaveItemTab(page, state, showSuccess: true);

            var xml = CreateEditorActionButton("VIEW XML BLOCK");
            xml.Location = new Point(112, 10);
            xml.Size = new Size(145, 34);
            state.ToggleXmlButton = xml;

            var status = new Label
            {
                Text = isNew ? "Novo item - ainda não guardado" : "A editar XML existente",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(274, 10),
                Size = new Size(430, 34),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            toolbar.Controls.Add(save);
            toolbar.Controls.Add(xml);
            toolbar.Controls.Add(status);

            var formHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor
            };
            state.FormPanel = formHost;

            var fields = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = CEditor,
                Padding = new Padding(16, 18, 16, 22)
            };

            void CenterItemEditorContent()
            {
                const int preferredContentWidth = 740;

                int horizontal =
                    Math.Max(
                        16,
                        (fields.ClientSize.Width -
                         preferredContentWidth) / 2);

                fields.Padding =
                    new Padding(
                        horizontal,
                        18,
                        horizontal,
                        22);
            }

            fields.Resize +=
                (_, _) =>
                    CenterItemEditorContent();

            DarkUi.ApplyDarkScrollBar(fields);
            formHost.Controls.Add(fields);

            CenterItemEditorContent();

            var xmlPreview = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Visible = false,
                ReadOnly = true,
                BackColor = Color.FromArgb(12, 12, 12),
                ForeColor = Color.FromArgb(225, 225, 225),
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };
            DarkUi.ApplyDarkScrollBar(xmlPreview);
            state.XmlPreview = xmlPreview;

            page.Controls.Add(formHost);
            page.Controls.Add(xmlPreview);
            page.Controls.Add(toolbar);

            BuildItemFields(page, state, fields);
            UpdateItemIdStatus(state);
            UpdateItemIconPreview(state);
            UpdateItemDisplayStatus(state);
            UpdateLinkedReferenceStatus(state);

            xml.Click += (_, _) =>
            {
                bool showXml = !state.XmlPreview.Visible;
                state.XmlPreview.Visible = showXml;
                state.FormPanel.Visible = !showXml;
                state.ToggleXmlButton.Text = showXml ? "BACK TO FORM" : "VIEW XML BLOCK";

                if (showXml)
                    state.XmlPreview.Text = ItemListEditorService.FormatBlock(state.Working);
            };

        }

        private void BuildItemFields(
            TabPage page,
            ItemEditState state,
            FlowLayoutPanel fields)
        {
            var occurrence =
                new Dictionary<string, int>(
                    StringComparer.Ordinal);

            string? lastSection = null;

            foreach (XElement node in state.Working.Elements())
            {
                string tag =
                    node.Name.LocalName;

                string section =
                    ItemEditorFieldCatalog.GetSection(tag);

                if (!string.Equals(
                    section,
                    lastSection,
                    StringComparison.Ordinal))
                {
                    lastSection = section;

                    if (section.Equals(
                        "CLASSIFICATION",
                        StringComparison.Ordinal))
                    {
                        Panel syncPanel =
                            CreateItemDisplaySyncPanel(state);

                        fields.Controls.Add(syncPanel);
                    }

                    var sectionHeader =
                        new Panel
                        {
                            Width = 720,
                            Height = 44,
                            BackColor =
                                Color.FromArgb(
                                    24,
                                    24,
                                    24),
                            Margin =
                                new Padding(
                                    0,
                                    8,
                                    10,
                                    8)
                        };

                    var sectionTitle =
                        new Label
                        {
                            Text =
                                ItemEditorFieldCatalog
                                    .GetSectionTitle(
                                        section),
                            ForeColor =
                                Color.FromArgb(
                                    205,
                                    205,
                                    205),
                            Font =
                                new Font(
                                    "Segoe UI Semibold",
                                    9.2F,
                                    FontStyle.Bold),
                            Location =
                                new Point(
                                    12,
                                    0),
                            Size =
                                new Size(
                                    676,
                                    44),
                            TextAlign =
                                ContentAlignment.MiddleLeft
                        };

                    sectionHeader.Paint +=
                        (_, e) =>
                        {
                            using var line =
                                new Pen(
                                    Color.FromArgb(
                                        62,
                                        62,
                                        62));

                            e.Graphics.DrawLine(
                                line,
                                10,
                                sectionHeader.Height - 1,
                                sectionHeader.Width - 10,
                                sectionHeader.Height - 1);
                        };

                    sectionHeader.Controls.Add(
                        sectionTitle);

                    fields.Controls.Add(
                        sectionHeader);
                }

                occurrence.TryGetValue(
                    tag,
                    out int current);

                current++;
                occurrence[tag] = current;

                int totalSame =
                    state.Working
                        .Elements(tag)
                        .Count();

                string suffix =
                    totalSame > 1
                        ? $" [{current}]"
                        : string.Empty;

                bool multiline =
                    tag == "s_szComment";

                int width =
                    multiline
                        ? 720
                        : 355;

                int height =
                    multiline
                        ? 252
                        : tag == "s_dwSkill"
                            ? 132
                            : 102;

                var field =
                    new Panel
                    {
                        Width = width,
                        Height = height,
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

                field.Paint +=
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
                            field.Width - 1,
                            field.Height - 1);
                    };

                string friendly =
                    FriendlyFieldName(tag);

                var label =
                    new Label
                    {
                        Text =
                            string.IsNullOrWhiteSpace(
                                friendly)
                                ? $"{tag}{suffix}"
                                : $"{friendly}{suffix}",
                        ForeColor = CText,
                        Font =
                            new Font(
                                "Segoe UI Semibold",
                                9.2F,
                                FontStyle.Bold),
                        Location =
                            new Point(
                                11,
                                7),
                        Size =
                            new Size(
                                width - 22,
                                20),
                        AutoEllipsis = true
                    };

                field.Controls.Add(label);

                string helpText =
                    ItemEditorFieldCatalog.GetHelpText(tag);

                if (!string.IsNullOrWhiteSpace(helpText))
                {
                    Button helpBubble =
                        CreateHelpBubble(helpText);

                    helpBubble.Location =
                        new Point(
                            width - 31,
                            5);

                    field.Controls.Add(
                        helpBubble);

                    label.Width =
                        Math.Max(
                            40,
                            width - 54);
                }

                int editorX = 10;
                int editorWidth =
                    width - 20;

                if (tag == "s_nIcon")
                {
                    var preview =
                        new PictureBox
                        {
                            Location =
                                new Point(
                                    10,
                                    34),
                            Size =
                                new Size(
                                    52,
                                    52),
                            SizeMode =
                                PictureBoxSizeMode.Zoom,
                            BackColor =
                                Color.FromArgb(
                                    11,
                                    11,
                                    11),
                            BorderStyle =
                                BorderStyle.None
                        };

                    preview.Paint +=
                        (_, e) =>
                        {
                            using var p =
                                new Pen(
                                    Color.FromArgb(
                                        68,
                                        68,
                                        68));

                            e.Graphics.DrawRectangle(
                                p,
                                0,
                                0,
                                preview.Width - 1,
                                preview.Height - 1);
                        };

                    state.IconPreview = preview;
                    field.Controls.Add(preview);

                    editorX = 72;
                    editorWidth =
                        width - 82;
                }

                Control editorControl;

                if (tag == "s_dwSkill")
                {
                    var skillEditor =
                        new TextBox
                        {
                            Text = node.Value,
                            BackColor =
                                Color.FromArgb(
                                    12,
                                    12,
                                    12),
                            ForeColor = CText,
                            BorderStyle =
                                BorderStyle.FixedSingle,
                            Font =
                                new Font(
                                    "Consolas",
                                    8.8F),
                            Location =
                                new Point(
                                    editorX,
                                    34),
                            Size =
                                new Size(
                                    Math.Max(
                                        120,
                                        editorWidth - 94),
                                    27)
                        };

                    var selectReference =
                        CreateEditorActionButton(
                            "SELECT");

                    selectReference.Location =
                        new Point(
                            editorX + editorWidth - 86,
                            34);

                    selectReference.Size =
                        new Size(
                            86,
                            27);

                    var resolvedStatus =
                        new Label
                        {
                            ForeColor = CMuted,
                            Font =
                                new Font(
                                    "Segoe UI",
                                    7.5F),
                            Location =
                                new Point(
                                    editorX,
                                    67),
                            Size =
                                new Size(
                                    editorWidth,
                                    42),
                            AutoEllipsis = true
                        };

                    state.LinkedReferenceStatus =
                        resolvedStatus;

                    skillEditor.TextChanged +=
                        (_, _) =>
                        {
                            node.Value =
                                skillEditor.Text;

                            MarkItemDirty(
                                page,
                                state);

                            UpdateLinkedReferenceStatus(
                                state);
                        };

                    selectReference.Click +=
                        (_, _) =>
                        {
                            OpenLinkedReferencePicker(
                                page,
                                state,
                                node);
                        };

                    field.Controls.Add(
                        skillEditor);

                    field.Controls.Add(
                        selectReference);

                    field.Controls.Add(
                        resolvedStatus);

                    editorControl =
                        skillEditor;

                    state.Editors[node] =
                        editorControl;

                    fields.Controls.Add(
                        field);

                    continue;
                }

                IReadOnlyList<string> observed =
                    ItemEditorFieldCatalog
                        .ShouldUseSelection(tag)
                        ? state.Service.GetObservedValues(
                            tag,
                            32)
                        : Array.Empty<string>();

                bool useCombo =
                    !multiline &&
                    observed.Count > 0;

                if (useCombo)
                {
                    var combo =
                        new DarkComboBox
                        {
                            Location =
                                new Point(
                                    editorX,
                                    34),
                            Size =
                                new Size(
                                    editorWidth,
                                    28),
                            Font =
                                new Font(
                                    "Segoe UI",
                                    8.7F)
                        };

                    foreach (string value in observed)
                    {
                        combo.Items.Add(
                            new DarkComboOption
                            {
                                Value = value,
                                Label =
                                    ItemEditorFieldCatalog
                                        .GetChoiceLabel(
                                            tag,
                                            value)
                            });
                    }

                    DarkComboOption? selected =
                        combo.Items
                            .OfType<DarkComboOption>()
                            .FirstOrDefault(
                                option =>
                                    option.Value.Equals(
                                        node.Value,
                                        StringComparison.Ordinal));

                    if (selected != null)
                        combo.SelectedItem = selected;
                    else if (combo.Items.Count > 0)
                        combo.SelectedIndex = 0;

                    combo.SelectedIndexChanged +=
                        (_, _) =>
                        {
                            if (combo.SelectedItem is not
                                DarkComboOption option)
                            {
                                return;
                            }

                            node.Value = option.Value;

                            MarkItemDirty(
                                page,
                                state);

                            if (tag == "s_nSkillCodeType")
                                UpdateLinkedReferenceStatus(state);
                        };

                    editorControl = combo;
                }
                else
                {
                    var textEditor =
                        new TextBox
                        {
                            Text = node.Value,
                            BackColor =
                                Color.FromArgb(
                                    12,
                                    12,
                                    12),
                            ForeColor = CText,
                            BorderStyle =
                                BorderStyle.FixedSingle,
                            Font =
                                new Font(
                                    multiline
                                        ? "Segoe UI"
                                        : "Consolas",
                                    multiline
                                        ? 10F
                                        : 9.3F),
                            Multiline = multiline,
                            AcceptsReturn = multiline,
                            AcceptsTab = multiline,
                            WordWrap = multiline,
                            ScrollBars =
                                multiline
                                    ? ScrollBars.Vertical
                                    : ScrollBars.None,
                            Location =
                                new Point(
                                    editorX,
                                    34),
                            Size =
                                multiline
                                    ? new Size(
                                        editorWidth,
                                        184)
                                    : new Size(
                                        editorWidth,
                                        27)
                        };

                    if (multiline)
                        DarkUi.ApplyDarkScrollBar(textEditor);

                    textEditor.TextChanged +=
                        (_, _) =>
                        {
                            node.Value =
                                textEditor.Text;

                            MarkItemDirty(
                                page,
                                state);

                            if (tag == "s_dwItemID")
                            {
                                UpdateItemIdStatus(state);
                                UpdateItemDisplayStatus(state);
                            }
                            else if (tag == "s_nSection")
                            {
                                UpdateItemDisplayStatus(state);
                            }
                            else if (tag == "s_nIcon")
                            {
                                UpdateItemIconPreview(state);
                            }
                        };

                    editorControl = textEditor;

                    if (tag == "s_nIcon")
                    {
                        int browseWidth = 100;

                        textEditor.Width =
                            Math.Max(
                                90,
                                textEditor.Width -
                                browseWidth -
                                8);

                        var browseIcon =
                            CreateEditorActionButton(
                                "BROWSE");

                        browseIcon.Location =
                            new Point(
                                textEditor.Right + 8,
                                textEditor.Top);

                        browseIcon.Size =
                            new Size(
                                browseWidth,
                                textEditor.Height);

                        editorToolTip.SetToolTip(
                            browseIcon,
                            "Abre o Item Icon Browser com os atlases DDS mapeados. " +
                            "Podes usar zoom, arrastar, Previous/Next e clicar num slot para aplicar o seu Icon ID.");

                        browseIcon.Click +=
                            (_, _) =>
                            {
                                OpenItemIconBrowser(
                                    page,
                                    state,
                                    node);
                            };

                        field.Controls.Add(
                            browseIcon);
                    }
                }

                field.Controls.Add(editorControl);

                string fieldHint =
                    string.Empty;

                if (multiline)
                {
                    var multilineInfo =
                        new Label
                        {
                            Text =
                                "Enter cria uma nova linha. As quebras são preservadas exatamente no XML.",
                            ForeColor =
                                Color.FromArgb(
                                    150,
                                    150,
                                    150),
                            Font =
                                new Font(
                                    "Segoe UI",
                                    7.4F),
                            Location =
                                new Point(
                                    editorX,
                                    221),
                            Size =
                                new Size(
                                    editorWidth,
                                    16),
                            AutoEllipsis = true
                        };

                    field.Controls.Add(
                        multilineInfo);
                }
                else if (!string.IsNullOrWhiteSpace(fieldHint))
                {
                    var hint =
                        new Label
                        {
                            Text = fieldHint,
                            ForeColor = CMuted,
                            Font =
                                new Font(
                                    "Segoe UI",
                                    7.2F),
                            Location =
                                new Point(
                                    editorX,
                                    65),
                            Size =
                                new Size(
                                    editorWidth,
                                    18),
                            AutoEllipsis = true
                        };

                    field.Controls.Add(hint);
                }

                if (tag == "s_dwItemID" &&
                    editorControl is TextBox idEditor)
                {
                    idEditor.Width =
                        Math.Max(
                            120,
                            idEditor.Width - 115);

                    var idStatus =
                        new Label
                        {
                            Location =
                                new Point(
                                    idEditor.Right + 8,
                                    34),
                            Size =
                                new Size(
                                    105,
                                    27),
                            TextAlign =
                                ContentAlignment.MiddleLeft,
                            Font =
                                new Font(
                                    "Segoe UI Semibold",
                                    8F,
                                    FontStyle.Bold)
                        };

                    state.IdStatus =
                        idStatus;

                    field.Controls.Add(
                        idStatus);
                }

                state.Editors[node] =
                    editorControl;

                fields.Controls.Add(
                    field);
            }
        }

        private Panel CreateItemDisplaySyncPanel(
            ItemEditState state)
        {
            var panel =
                new Panel
                {
                    Width = 704,
                    Height = 64,
                    BackColor =
                        Color.FromArgb(
                            27,
                            27,
                            27),
                    Margin =
                        new Padding(
                            0,
                            8,
                            10,
                            8)
                };

            panel.Paint +=
                (_, e) =>
                {
                    using var border =
                        new Pen(
                            Color.FromArgb(
                                54,
                                54,
                                54));

                    e.Graphics.DrawRectangle(
                        border,
                        0,
                        0,
                        panel.Width - 1,
                        panel.Height - 1);
                };

            var syncDisplay =
                new CheckBox
                {
                    Text = "SYNC WITH ITEMDISPLAY.XML",
                    AutoSize = true,

                    // User request: ALWAYS starts disabled/unchecked.
                    Checked = false,

                    Enabled =
                        state.ItemDisplayService != null,
                    ForeColor = CText,
                    BackColor =
                        Color.Transparent,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.2F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            12,
                            10),
                    Cursor = Cursors.Hand
                };

            state.SyncItemDisplay =
                syncDisplay;

            Button displayHelp =
                CreateHelpBubble(
                    "Optional ItemDisplay synchronization. " +
                    "When enabled, SAVE writes the current Item Section / Display Mapping ID " +
                    "to <nItemS> and the current Item ID to <dwDispID>. " +
                    "It is unchecked by default so an ordinary ItemList edit never changes ItemDisplay.xml accidentally.");

            displayHelp.Location =
                new Point(
                    208,
                    8);

            var itemDisplayStatus =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            7.8F),
                    Location =
                        new Point(
                            12,
                            34),
                    Size =
                        new Size(
                            670,
                            20),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };

            state.ItemDisplayStatus =
                itemDisplayStatus;

            syncDisplay.CheckedChanged +=
                (_, _) =>
                    UpdateItemDisplayStatus(state);

            panel.Controls.Add(
                syncDisplay);

            panel.Controls.Add(
                displayHelp);

            panel.Controls.Add(
                itemDisplayStatus);

            UpdateItemDisplayStatus(state);

            return panel;
        }

        private void UpdateLinkedReferenceStatus(
            ItemEditState state)
        {
            if (state.LinkedReferenceStatus == null)
                return;

            if (state.LinkedReferenceService == null)
            {
                state.LinkedReferenceStatus.Text =
                    "Skill.xml / ItemAcessorys.xml catalog unavailable.";

                state.LinkedReferenceStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        175,
                        90);

                return;
            }

            uint id =
                TryReadItemUInt(
                    state.Working,
                    "s_dwSkill")
                ?? 0;

            int codeType =
                (int)(
                    TryReadItemUInt(
                        state.Working,
                        "s_nSkillCodeType")
                    ?? 0);

            if (id == 0)
            {
                state.LinkedReferenceStatus.Text =
                    "No linked Skill / Accessory reference.";

                state.LinkedReferenceStatus.ForeColor =
                    CMuted;

                return;
            }

            bool inSkill =
                state.LinkedReferenceService.ExistsInSkill(id);

            bool inAccessory =
                state.LinkedReferenceService.ExistsInAccessory(id);

            if (state.LinkedReferenceService.TryResolvePreferred(
                id,
                codeType,
                out LinkedItemReferenceRecord resolved))
            {
                string source =
                    resolved.Source ==
                    LinkedItemReferenceSource.Skill
                        ? "Skill.xml"
                        : "ItemAcessorys.xml";

                string ambiguity =
                    inSkill && inAccessory
                        ? " | ID exists in BOTH catalogs"
                        : string.Empty;

                state.LinkedReferenceStatus.Text =
                    $"{source}: {resolved.Id} — {resolved.Name}{ambiguity}";

                state.LinkedReferenceStatus.ForeColor =
                    Color.FromArgb(
                        125,
                        220,
                        140);

                return;
            }

            state.LinkedReferenceStatus.Text =
                $"Reference {id} not found in Skill.xml or ItemAcessorys.xml.";

            state.LinkedReferenceStatus.ForeColor =
                Color.FromArgb(
                    255,
                    95,
                    95);
        }

        private void OpenLinkedReferencePicker(
            TabPage ownerPage,
            ItemEditState itemState,
            XElement skillNode)
        {
            if (itemState.LinkedReferenceService == null)
            {
                MessageBox.Show(
                    "Skill.xml / ItemAcessorys.xml catalog não está disponível.",
                    "Linked Reference",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            int codeType =
                (int)(
                    TryReadItemUInt(
                        itemState.Working,
                        "s_nSkillCodeType")
                    ?? 0);

            LinkedItemReferenceSource defaultSource =
                codeType == 2
                    ? LinkedItemReferenceSource.Accessory
                    : LinkedItemReferenceSource.Skill;

            var page =
                CreateDarkTab(
                    defaultSource ==
                    LinkedItemReferenceSource.Skill
                        ? "Select Skill"
                        : "Select Accessory");

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 110,
                    BackColor =
                        Color.FromArgb(
                            27,
                            27,
                            27)
                };

            var title =
                new Label
                {
                    Text =
                        "Linked Skill / Accessory Reference",
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
                            410,
                            26)
                };

            var skillSource =
                CreateEditorActionButton(
                    "SKILL.XML");

            skillSource.Location =
                new Point(
                    16,
                    43);

            skillSource.Size =
                new Size(
                    108,
                    28);

            var accessorySource =
                CreateEditorActionButton(
                    "ITEM ACCESSORY");

            accessorySource.Location =
                new Point(
                    130,
                    43);

            accessorySource.Size =
                new Size(
                    126,
                    28);

            var search =
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
                    Font =
                        new Font(
                            "Segoe UI",
                            9F),
                    PlaceholderText =
                        "Search by ID or name...",
                    Location =
                        new Point(
                            270,
                            43),
                    Size =
                        new Size(
                            330,
                            28)
                };

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
                            78),
                    Size =
                        new Size(
                            680,
                            22)
                };

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
                            12)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            header.Controls.Add(
                title);

            header.Controls.Add(
                skillSource);

            header.Controls.Add(
                accessorySource);

            header.Controls.Add(
                search);

            header.Controls.Add(
                count);

            page.Controls.Add(
                results);

            page.Controls.Add(
                header);

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 180
                };

            var picker =
                new LinkedReferencePickerState
                {
                    ItemState = itemState,
                    SkillNode = skillNode,
                    Service =
                        itemState.LinkedReferenceService,
                    Search = search,
                    Results = results,
                    CountLabel = count,
                    SkillSourceButton =
                        skillSource,
                    AccessorySourceButton =
                        accessorySource,
                    SearchTimer = timer,
                    Source = defaultSource
                };

            page.Tag = picker;

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();

                    RefreshLinkedReferencePicker(
                        page,
                        picker);
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            skillSource.Click +=
                (_, _) =>
                {
                    picker.Source =
                        LinkedItemReferenceSource.Skill;

                    page.Text =
                        "Select Skill";

                    RefreshLinkedReferencePicker(
                        page,
                        picker);
                };

            accessorySource.Click +=
                (_, _) =>
                {
                    picker.Source =
                        LinkedItemReferenceSource.Accessory;

                    page.Text =
                        "Select Accessory";

                    RefreshLinkedReferencePicker(
                        page,
                        picker);
                };

            page.Disposed +=
                (_, _) =>
                    timer.Dispose();

            RefreshLinkedReferencePicker(
                page,
                picker);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;
        }

        private void RefreshLinkedReferencePicker(
            TabPage pickerPage,
            LinkedReferencePickerState state)
        {
            IReadOnlyList<LinkedItemReferenceRecord> rows =
                state.Service.Search(
                    state.Source,
                    state.Search.Text,
                    80);

            int totalMatches =
                state.Service.CountSearch(
                    state.Source,
                    state.Search.Text);

            DisposeChildImages(
                state.Results);

            state.Results.SuspendLayout();

            state.Results.Controls.Clear();

            foreach (LinkedItemReferenceRecord row in rows)
            {
                Control card =
                    row.Source ==
                    LinkedItemReferenceSource.Skill
                        ? CreateSkillReferenceCard(
                            pickerPage,
                            state,
                            row)
                        : CreateAccessoryReferenceCard(
                            pickerPage,
                            state,
                            row);

                state.Results.Controls.Add(
                    card);
            }

            state.Results.ResumeLayout(
                true);

            string sourceText =
                state.Source ==
                LinkedItemReferenceSource.Skill
                    ? "Skill.xml"
                    : "ItemAcessorys.xml";

            state.CountLabel.Text =
                $"{sourceText} | Results: {totalMatches:N0}" +
                (totalMatches > rows.Count
                    ? $" | Showing first {rows.Count:N0}"
                    : string.Empty);

            state.SkillSourceButton.BackColor =
                state.Source ==
                LinkedItemReferenceSource.Skill
                    ? Color.FromArgb(
                        55,
                        55,
                        55)
                    : Color.Transparent;

            state.AccessorySourceButton.BackColor =
                state.Source ==
                LinkedItemReferenceSource.Accessory
                    ? Color.FromArgb(
                        55,
                        55,
                        55)
                    : Color.Transparent;
        }

        private Control CreateSkillReferenceCard(
            TabPage pickerPage,
            LinkedReferencePickerState state,
            LinkedItemReferenceRecord skill)
        {
            var card =
                new Panel
                {
                    Width = 704,
                    Height = 72,
                    BackColor =
                        Color.FromArgb(
                            30,
                            30,
                            30),
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
                            12),
                    Size =
                        new Size(
                            48,
                            48),
                    SizeMode =
                        PictureBoxSizeMode.Zoom,
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    Image =
                        GetSkillIconPreview(
                            skill.IconId)
                };

            var id =
                new Label
                {
                    Text =
                        skill.Id.ToString(),
                    ForeColor =
                        Color.FromArgb(
                            180,
                            180,
                            180),
                    Font =
                        new Font(
                            "Consolas",
                            8F),
                    Location =
                        new Point(
                            70,
                            10),
                    Size =
                        new Size(
                            120,
                            20)
                };

            var name =
                new Label
                {
                    Text =
                        skill.Name,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            70,
                            30),
                    Size =
                        new Size(
                            470,
                            22),
                    AutoEllipsis = true
                };

            var select =
                CreateEditorActionButton(
                    "SELECT");

            select.Location =
                new Point(
                    590,
                    20);

            select.Size =
                new Size(
                    96,
                    32);

            select.Click +=
                (_, _) =>
                {
                    SelectLinkedReference(
                        pickerPage,
                        state,
                        skill);
                };

            card.Controls.Add(icon);
            card.Controls.Add(id);
            card.Controls.Add(name);
            card.Controls.Add(select);

            return card;
        }

        private Control CreateAccessoryReferenceCard(
            TabPage pickerPage,
            LinkedReferencePickerState state,
            LinkedItemReferenceRecord accessory)
        {
            var card =
                new Panel
                {
                    Width = 704,
                    Height = 72,
                    BackColor =
                        Color.FromArgb(
                            30,
                            30,
                            30),
                    Margin =
                        new Padding(
                            0,
                            0,
                            0,
                            8)
                };

            var source =
                new Label
                {
                    Text =
                        "ACCESSORY",
                    ForeColor =
                        Color.FromArgb(
                            255,
                            190,
                            90),
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            7.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            12,
                            8),
                    Size =
                        new Size(
                            90,
                            18)
                };

            var id =
                new Label
                {
                    Text =
                        $"ID {accessory.Id}",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Consolas",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            12,
                            27),
                    Size =
                        new Size(
                            210,
                            22)
                };

            var meta =
                new Label
                {
                    Text =
                        $"Gain Options: {accessory.GainOption} | " +
                        $"Changeable Options: {accessory.ChangeableOptionNumber}",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8F),
                    Location =
                        new Point(
                            232,
                            25),
                    Size =
                        new Size(
                            340,
                            24),
                    AutoEllipsis = true
                };

            var select =
                CreateEditorActionButton(
                    "SELECT");

            select.Location =
                new Point(
                    590,
                    20);

            select.Size =
                new Size(
                    96,
                    32);

            select.Click +=
                (_, _) =>
                {
                    SelectLinkedReference(
                        pickerPage,
                        state,
                        accessory);
                };

            card.Controls.Add(source);
            card.Controls.Add(id);
            card.Controls.Add(meta);
            card.Controls.Add(select);

            return card;
        }

        private void SelectLinkedReference(
            TabPage pickerPage,
            LinkedReferencePickerState picker,
            LinkedItemReferenceRecord selected)
        {
            picker.SkillNode.Value =
                selected.Id.ToString();

            if (picker.ItemState.Editors.TryGetValue(
                picker.SkillNode,
                out Control? skillControl) &&
                skillControl is TextBox skillText)
            {
                skillText.Text =
                    selected.Id.ToString();
            }

            XElement? codeTypeNode =
                picker.ItemState.Working.Element(
                    "s_nSkillCodeType");

            if (codeTypeNode != null)
            {
                uint currentType =
                    TryReadItemUInt(
                        picker.ItemState.Working,
                        "s_nSkillCodeType")
                    ?? 0;

                uint recommendedType =
                    selected.Source ==
                    LinkedItemReferenceSource.Accessory
                        ? 2u
                        : currentType == 0
                            ? 0u
                            : 1u;

                codeTypeNode.Value =
                    recommendedType.ToString();

                if (picker.ItemState.Editors.TryGetValue(
                    codeTypeNode,
                    out Control? codeControl))
                {
                    if (codeControl is DarkComboBox combo)
                    {
                        DarkComboOption? match =
                            combo.Items
                                .OfType<DarkComboOption>()
                                .FirstOrDefault(
                                    option =>
                                        option.Value ==
                                        recommendedType.ToString());

                        if (match != null)
                            combo.SelectedItem = match;
                    }
                    else if (codeControl is TextBox codeText)
                    {
                        codeText.Text =
                            recommendedType.ToString();
                    }
                }
            }

            TabPage? owner =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        tab =>
                            ReferenceEquals(
                                tab.Tag,
                                picker.ItemState));

            if (owner != null)
            {
                MarkItemDirty(
                    owner,
                    picker.ItemState);
            }

            UpdateLinkedReferenceStatus(
                picker.ItemState);

            // Closing the picker uses the tab-history behavior and returns
            // to the item editor that opened it.
            editorTabs.TabPages.Remove(
                pickerPage);

            pickerPage.Dispose();
        }

        private Bitmap? GetSkillIconPreview(
            uint iconId)
        {
            if (iconId == 0)
                return null;

            if (skillIconCache.TryGetValue(
                iconId,
                out Bitmap? cached))
            {
                return cached == null
                    ? null
                    : new Bitmap(cached);
            }

            if (skillIconCache.Count > 512)
            {
                foreach (Bitmap? image
                         in skillIconCache.Values)
                {
                    image?.Dispose();
                }

                skillIconCache.Clear();
            }

            Bitmap? loaded =
                ImageDatabasePreview.TryLoadInterfaceIcon(
                    iconId,
                    "Skill");

            skillIconCache[iconId] =
                loaded == null
                    ? null
                    : new Bitmap(loaded);

            return loaded;
        }

        private void UpdateItemDisplayStatus(
            ItemEditState state)
        {
            if (state.ItemDisplayStatus == null)
                return;

            if (state.ItemDisplayService == null)
            {
                state.ItemDisplayStatus.Text =
                    "ItemDisplay.xml não encontrado nesta pasta.";
                state.ItemDisplayStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        175,
                        90);
                return;
            }

            uint? itemId =
                TryReadItemUInt(
                    state.Working,
                    "s_dwItemID");

            uint? section =
                TryReadItemUInt(
                    state.Working,
                    "s_nSection");

            if (!itemId.HasValue ||
                !section.HasValue)
            {
                state.ItemDisplayStatus.Text =
                    "ItemID/Section inválido para ItemDisplay.";
                state.ItemDisplayStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        95,
                        95);
                return;
            }

            bool syncEnabled =
                state.SyncItemDisplay != null &&
                state.SyncItemDisplay.Checked;

            if (state.ItemDisplayService.ContainsExact(
                section.Value,
                itemId.Value))
            {
                state.ItemDisplayStatus.Text =
                    (syncEnabled
                        ? "SYNC enabled — "
                        : "Optional mapping — ") +
                    $"already exists: Section {section.Value} → ItemID {itemId.Value}";

                state.ItemDisplayStatus.ForeColor =
                    Color.FromArgb(
                        125,
                        220,
                        140);
                return;
            }

            IReadOnlyList<uint> existingSections =
                state.ItemDisplayService.GetSectionsForItem(
                    itemId.Value);

            if (existingSections.Count > 0)
            {
                state.ItemDisplayStatus.Text =
                    (syncEnabled
                        ? "SYNC enabled — "
                        : "Optional mapping — ") +
                    $"ItemID already exists in other Section(s): " +
                    string.Join(
                        ", ",
                        existingSections);
                state.ItemDisplayStatus.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);
                return;
            }

            state.ItemDisplayStatus.Text =
                (syncEnabled
                    ? "SYNC enabled — will add/update: "
                    : "Optional mapping — would add: ") +
                $"Section {section.Value} → ItemID {itemId.Value}";
            state.ItemDisplayStatus.ForeColor =
                CMuted;
        }

        private Button CreateHelpBubble(
            string helpText)
        {
            var button =
                new Button
                {
                    Text = string.Empty,
                    Size =
                        new Size(
                            22,
                            22),
                    FlatStyle =
                        FlatStyle.Flat,
                    BackColor =
                        Color.FromArgb(
                            47,
                            47,
                            47),
                    ForeColor =
                        Color.FromArgb(
                            242,
                            242,
                            242),
                    Cursor =
                        Cursors.Help,
                    TabStop = false,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    UseVisualStyleBackColor = false
                };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(
                    68,
                    68,
                    68);

            button.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(
                    82,
                    82,
                    82);

            void ApplyRoundRegion()
            {
                using var path =
                    new GraphicsPath();

                path.AddEllipse(
                    0,
                    0,
                    button.Width,
                    button.Height);

                button.Region =
                    new Region(path);
            }

            ApplyRoundRegion();

            button.Resize +=
                (_, _) =>
                    ApplyRoundRegion();

            button.Paint +=
                (_, e) =>
                {
                    e.Graphics.SmoothingMode =
                        SmoothingMode.AntiAlias;

                    using var font =
                        new Font(
                            "Segoe UI",
                            9F,
                            FontStyle.Bold);

                    TextRenderer.DrawText(
                        e.Graphics,
                        "?",
                        font,
                        button.ClientRectangle,
                        Color.FromArgb(
                            245,
                            245,
                            245),
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding |
                        TextFormatFlags.NoPrefix);
                };

            editorToolTip.SetToolTip(
                button,
                helpText);

            return button;
        }

        private static uint? TryReadItemUInt(
            XElement item,
            string tag)
        {
            string raw =
                item.Element(tag)?.Value
                    ?.Trim()
                ?? string.Empty;

            return uint.TryParse(
                raw,
                out uint value)
                    ? value
                    : null;
        }

        private static uint ReadRequiredItemUInt(
            XElement item,
            string tag)
        {
            uint? value =
                TryReadItemUInt(
                    item,
                    tag);

            if (!value.HasValue)
            {
                throw new InvalidDataException(
                    $"<{tag}> não possui um UInt32 válido.");
            }

            return value.Value;
        }

        private void MarkItemDirty(TabPage page, ItemEditState state)
        {
            state.Dirty = true;
            string name = state.Working.Element("s_szName")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                name = state.IsNew ? "New Item" : "Item";

            page.Text = $"{name} [Unsaved]";
        }

        private bool SaveItemTab(TabPage page, ItemEditState state, bool showSuccess)
        {
            try
            {
                uint newId = ItemListEditorService.GetId(state.Working);

                if (state.IsNew)
                {
                    if (state.Service.Exists(newId))
                        throw new InvalidDataException($"ItemID {newId} já existe. Escolhe outro ID.");

                    state.Service.AppendNew(state.Working);
                    state.OriginalId = newId;
                    state.IsNew = false;
                }
                else
                {
                    if (!state.OriginalId.HasValue)
                        throw new InvalidDataException("Editor não possui OriginalId para este item.");

                    state.Service.SaveExisting(state.OriginalId.Value, state.Working);
                    state.OriginalId = newId;
                }

                uint newSection =
                    ReadRequiredItemUInt(
                        state.Working,
                        "s_nSection");

                ItemDisplaySyncResult? displayResult =
                    null;

                if (state.SyncItemDisplay != null &&
                    state.SyncItemDisplay.Checked)
                {
                    if (state.ItemDisplayService == null)
                    {
                        throw new InvalidDataException(
                            "SYNC ITEMDISPLAY está ativo, mas ItemDisplay.xml não está disponível.");
                    }

                    displayResult =
                        state.ItemDisplayService.Sync(
                            newSection,
                            newId,
                            state.OriginalSection,
                            state.OriginalId);
                }

                state.OriginalSection =
                    newSection;

                state.Dirty = false;
                state.SavedOnce = true;

                string name = state.Working.Element("s_szName")?.Value ?? $"Item {newId}";
                page.Text = $"{name} [Saved]";

                UpdateItemIdStatus(state);
                UpdateItemDisplayStatus(state);
                RefreshAllItemListBrowsers(state.Service);

                string displayLog =
                    displayResult == null
                        ? "ItemDisplay=Skipped"
                        : $"ItemDisplay={displayResult.Action} " +
                          $"[{displayResult.Section}->{displayResult.ItemId}]";

                AppLogger.Success(
                    $"ItemList Editor: ItemID {newId} guardado. " +
                    $"Total={state.Service.TotalItems:N0}. {displayLog}.");

                if (showSuccess)
                {
                    MessageBox.Show(
                        $"Item guardado com sucesso.\n\n" +
                        $"ItemID: {newId}\n" +
                        $"Section: {newSection}\n" +
                        $"Total items: {state.Service.TotalItems:N0}\n" +
                        (displayResult == null
                            ? "ItemDisplay: não sincronizado"
                            : $"ItemDisplay: {displayResult.Action}"),
                        "ItemList Editor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                ShowEditorError("Save Item", ex);
                return false;
            }
        }

        private void UpdateItemIdStatus(ItemEditState state)
        {
            if (state.IdStatus == null)
                return;

            XElement? idNode = state.Working.Element("s_dwItemID");
            if (!uint.TryParse(idNode?.Value, out uint id))
            {
                state.IdStatus.Text = "INVALID ID";
                state.IdStatus.ForeColor = Color.FromArgb(255, 95, 95);
                return;
            }

            bool isOwn = state.OriginalId.HasValue && state.OriginalId.Value == id;
            bool exists = state.Service.Exists(id) && !isOwn;

            state.IdStatus.Text = exists ? "ID EXISTS" : "ID AVAILABLE";
            state.IdStatus.ForeColor = exists
                ? Color.FromArgb(255, 95, 95)
                : Color.FromArgb(125, 220, 140);
        }

        private void UpdateItemIconPreview(ItemEditState state)
        {
            if (state.IconPreview == null)
                return;

            state.IconPreview.Image?.Dispose();
            state.IconPreview.Image = null;

            if (uint.TryParse(state.Working.Element("s_nIcon")?.Value, out uint iconId))
                state.IconPreview.Image = GetItemIconPreview(iconId);
        }

        private Bitmap? GetItemIconPreview(uint iconId)
        {
            if (itemIconCache.TryGetValue(iconId, out Bitmap? cached))
                return cached == null ? null : new Bitmap(cached);

            if (itemIconCache.Count > 512)
            {
                foreach (Bitmap? image in itemIconCache.Values)
                    image?.Dispose();
                itemIconCache.Clear();
            }

            Bitmap? loaded = ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Item");
            itemIconCache[iconId] = loaded == null ? null : new Bitmap(loaded);
            return loaded;
        }

        private void RefreshAllItemListBrowsers(ItemListEditorService service)
        {
            foreach (TabPage page in editorTabs.TabPages)
            {
                if (page.Tag is ItemListBrowseState browse &&
                    ReferenceEquals(browse.Service, service))
                {
                    RefreshItemSearch(browse);
                }
            }
        }

        private async void OpenGenericXmlBrowser(
            string entity,
            string xmlPath)
        {
            string fullPath =
                Path.GetFullPath(
                    xmlPath);

            var page =
                CreateDarkTab(
                    Path.GetFileName(
                        xmlPath));

            page.Name =
                fullPath;

            var loading =
                new EditorLoadingView(
                    $"Loading {Path.GetFileName(xmlPath)}",
                    "Parsing XML and preparing the block browser before content is shown.");

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            UpdateEditorEmptyState();

            GenericXmlBlockService service;

            try
            {
                service =
                    await System.Threading.Tasks.Task.Run(
                        () =>
                        {
                            var loaded =
                                new GenericXmlBlockService();

                            loaded.Load(
                                fullPath);

                            return loaded;
                        });
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "XML could not be loaded",
                        ex.Message);
                }

                return;
            }

            if (page.IsDisposed)
                return;

            page.SuspendLayout();
            page.Controls.Clear();

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 102,
                    BackColor = CPanel
                };

            var title =
                new Label
                {
                    Text =
                        $"{Path.GetFileName(xmlPath)} - Block Browser",
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
                            500,
                            28)
                };

            var search =
                new TextBox
                {
                    Location =
                        new Point(
                            16,
                            45),
                    Size =
                        new Size(
                            500,
                            28),
                    BackColor =
                        Color.FromArgb(
                            16,
                            16,
                            16),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    PlaceholderText =
                        "Pesquisar texto dentro dos blocos..."
                };

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Location =
                        new Point(
                            16,
                            76),
                    Size =
                        new Size(
                            650,
                            22)
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

            page.Controls.Add(
                results);

            page.Controls.Add(
                header);

            header.Controls.Add(
                title);

            header.Controls.Add(
                search);

            header.Controls.Add(
                count);

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 250
                };

            var state =
                new GenericBrowseState
                {
                    Service = service,
                    Search = search,
                    CountLabel = count,
                    Results = results,
                    SearchTimer = timer
                };

            page.Tag =
                state;

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();

                    RefreshGenericSearch(
                        state);
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            page.Disposed +=
                (_, _) =>
                    timer.Dispose();

            page.ResumeLayout(
                true);

            // Let the tab paint first, then create the first result cards.
            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (!page.IsDisposed)
                        {
                            RefreshGenericSearch(
                                state);
                        }
                    }));
        }

        private void RefreshGenericSearch(GenericBrowseState state)
        {
            int total = state.Service.CountSearch(state.Search.Text);
            IReadOnlyList<XElement> blocks = state.Service.Search(state.Search.Text, 50);

            state.Results.SuspendLayout();
            state.Results.Controls.Clear();

            int index = 0;
            foreach (XElement block in blocks)
            {
                index++;
                string summary = BuildBlockSummary(block);

                var card = new Panel
                {
                    Width = 720,
                    Height = 66,
                    BackColor = CRow1,
                    Margin = new Padding(0, 0, 0, 7)
                };

                var label = new Label
                {
                    Text = summary,
                    ForeColor = CText,
                    Location = new Point(12, 7),
                    Size = new Size(560, 50),
                    AutoEllipsis = true
                };

                var view = CreateEditorActionButton("VIEW BLOCK");
                view.Location = new Point(585, 15);
                view.Size = new Size(115, 34);
                XElement captured = new XElement(block);
                view.Click += (_, _) => OpenRawBlockTab(state.Service.FilePath, captured);

                card.Controls.Add(label);
                card.Controls.Add(view);
                state.Results.Controls.Add(card);
            }

            state.Results.ResumeLayout();
            state.CountLabel.Text =
                $"Root: <{state.Service.RootName}>    |    Total blocos: {state.Service.TotalBlocks:N0}    |    " +
                $"Resultados: {total:N0}" + (total > blocks.Count ? $"    |    Mostrando {blocks.Count:N0}" : string.Empty);
        }

        private async void OpenRawBlockTab(string filePath, XElement block)
        {
            string title = $"<{block.Name.LocalName}> Block";
            var page = CreateDarkTab(title);

            var rawHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(8)
            };

            var text = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(11, 11, 11),
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Consolas", 9F),
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Text = block.ToString()
            };

            DarkUi.ApplyDarkScrollBar(text);
            rawHost.Controls.Add(text);
            page.Controls.Add(rawHost);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
        }

        private void EditorTabs_TabClosing(object? sender, EditorTabClosingEventArgs e)
        {
            e.Cancel = !CanCloseEditorPage(e.Page);
            UpdateEditorEmptyState();
        }

        private bool CanCloseEditorPage(TabPage page)
        {
            if (page.Tag is BuffEditState buffState &&
                buffState.Dirty)
            {
                DialogResult result =
                    MessageBox.Show(
                        "There are unsaved changes in this Buff.xml entry.\n\n" +
                        "YES = Save and close\n" +
                        "NO = Close without saving\n" +
                        "CANCEL = Return to editor",
                        "Unsaved Buff Changes",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    return false;

                if (result == DialogResult.Yes)
                    return SaveBuffEditor(
                        buffState,
                        showSuccess: false);

                return true;
            }

            if (page.Tag is SkillEditState skillState &&
                skillState.Dirty)
            {
                DialogResult result =
                    MessageBox.Show(
                        "There are unsaved changes in this Skill.xml entry.\n\n" +
                        "YES = Save and close\n" +
                        "NO = Close without saving\n" +
                        "CANCEL = Return to editor",
                        "Unsaved Skill Changes",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    return false;

                if (result == DialogResult.Yes)
                    return SaveSkillEditor(
                        skillState,
                        showSuccess: false);

                return true;
            }

            if (page.Tag is DigimonEvoEditState evoState &&
                evoState.Dirty)
            {
                DialogResult result =
                    MessageBox.Show(
                        "There are unsaved changes in this DigimonEvo evolution tree.\n\n" +
                        "YES = Save and close\n" +
                        "NO = Close without saving\n" +
                        "CANCEL = Return to editor",
                        "Unsaved DigimonEvo Changes",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    return false;

                if (result == DialogResult.Yes)
                    return SaveDigimonEvoEditor(
                        evoState,
                        showSuccess: false);

                return true;
            }

            if (page.Tag is ItemMakingEditorState makingState &&
                makingState.Dirty)
            {
                DialogResult result =
                    MessageBox.Show(
                        "Existem alterações por guardar no ItemMaking.xml.\n\n" +
                        "YES = Guardar e fechar\n" +
                        "NO = Fechar sem guardar\n" +
                        "CANCEL = Voltar ao editor",
                        "Unsaved ItemMaking Changes",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    return false;

                if (result == DialogResult.Yes)
                    return SaveItemMakingPage(
                        page,
                        makingState,
                        showSuccess: false);

                return true;
            }

            if (page.Tag is NpcEditState npcState &&
                npcState.Dirty)
            {
                DialogResult result =
                    MessageBox.Show(
                        "Existem alterações por guardar neste NPC.\n\n" +
                        "YES = Guardar e fechar\n" +
                        "NO = Fechar sem guardar\n" +
                        "CANCEL = Voltar ao editor",
                        "Unsaved NPC Changes",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);

                if (result == DialogResult.Cancel)
                    return false;

                if (result == DialogResult.Yes)
                    return SaveNpcEditPage(
                        page,
                        npcState,
                        showSuccess: false);

                return true;
            }

            if (page.Tag is not ItemEditState state || !state.Dirty)
                return true;

            DialogResult itemResult = MessageBox.Show(
                "Existem alterações por guardar neste item.\n\n" +
                "YES = Guardar e fechar\nNO = Fechar sem guardar\nCANCEL = Voltar ao editor",
                "Unsaved Item Changes",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (itemResult == DialogResult.Cancel)
                return false;

            if (itemResult == DialogResult.Yes)
                return SaveItemTab(page, state, showSuccess: false);

            return true;
        }

        private TabPage CreateDarkTab(string text) =>
            new TabPage
            {
                Text = text,
                BackColor = CEditor,
                ForeColor = CText,
                Padding = new Padding(0),
                UseVisualStyleBackColor = false,
                BorderStyle = BorderStyle.None
            };

        private Button CreateEditorActionButton(
            string text)
        {
            var button =
                new Button
                {
                    Text = text,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.Transparent,
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.4F,
                            FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TabStop = false
                };

            button.FlatAppearance.BorderColor =
                Color.FromArgb(
                    65,
                    65,
                    65);

            button.FlatAppearance.BorderSize = 1;

            button.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(
                    48,
                    48,
                    48);

            button.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(
                    62,
                    62,
                    62);

            return button;
        }

        private Label CreateInfoLabel(string text) =>
            new Label
            {
                Text = text,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 9.5F),
                Size = new Size(700, 100),
                TextAlign = ContentAlignment.MiddleLeft
            };

        private static string BuildBlockSummary(XElement block)
        {
            List<XElement> children = block.Elements().Take(3).ToList();
            if (children.Count == 0)
                return $"<{block.Name.LocalName}> {block.Value}";

            string values = string.Join(
                "   |   ",
                children.Select(x => $"{x.Name.LocalName}={TrimSummary(x.Value, 48)}"));

            return $"<{block.Name.LocalName}>   {values}";
        }

        private static string TrimSummary(string text, int max)
        {
            string flat = (text ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            return flat.Length <= max ? flat : flat[..max] + "...";
        }

        private static string FriendlyFieldName(string tag) => tag switch
        {
            "s_dwItemID" => "Item ID",
            "s_szName" => "Item Name",
            "s_nIcon" => "Icon ID",
            "s_szComment" => "Description",
            "s_cNif" => "Item NIF Resource",
            "s_nClass" => "Item Class / Rarity",
            "s_szTypeComment" => "Displayed Item Type",

            "s_btCodeTag" => "Internal Code Tag",
            "s_nType_L" => "Main Item Type / Equipment Slot Type",
            "s_nType_S" => "Secondary Item Type / Subtype",
            "s_nTypeValue" => "Type Parameter Value",
            "s_nSection" => "Item Section / Display Mapping ID",

            "s_nSellType" => "Sell / Price Handling Type",
            "s_nUseMode" => "Item Use Mode",
            "s_nUseTimeGroup" => "Cooldown Group ID",
            "s_nOverlap" => "Maximum Stack Size",

            "s_nTamerReqMinLevel" => "Minimum Tamer Level",
            "s_nTamerReqMaxLevel" => "Maximum Tamer Level",
            "s_nDigimonReqMinLevel" => "Minimum Digimon Level",
            "s_nDigimonReqMaxLevel" => "Maximum Digimon Level",
            "s_nPossess" => "Possession Restriction",
            "s_nEquipSeries" => "Equipment Series / Group",
            "s_nUseCharacter" => "Character Restriction Group",

            "s_bDummy" => "Internal Dummy Flag",
            "s_nDrop" => "Drop Permission / Mode",
            "s_nEventItemType" => "Event Item Type",
            "s_dwEventItemPrice" => "Event Item Price",
            "s_dwDigiCorePrice" => "DigiCore Price",
            "s_dwScanPrice" => "Scan Cost",
            "s_dwSale" => "Vendor Sale Value",

            "s_cModel_Nif" => "Model NIF Resource",
            "s_cModel_Effect" => "Model Effect Resource",
            "s_bModel_Loop" => "Model / Effect Loop",
            "s_bModel_Shader" => "Model Shader",
            "s_nSkillCodeType" => "Linked Skill Mode",
            "s_dwSkill" => "Linked Skill ID",

            "s_btApplyRateMax" => "Socket Attribute Rate — Maximum",
            "s_btApplyRateMin" => "Socket Attribute Rate — Minimum",
            "s_btApplyElement" => "Attribute / Element Raw Value",
            "s_nSocketCount" => "Socket Count",
            "s_dwSoundID" => "Sound ID",
            "s_nBelonging" => "Trade / Binding Rule",

            "s_nQuest1" => "Quest Reference 1",
            "s_nQuest2" => "Quest Reference 2",
            "s_nQuest3" => "Quest Reference 3",
            "s_nDigiviceSkillSlot" => "Digivice Tamer Skill Slot Count",
            "s_nDigiviceChipsetSlot" => "Digivice Chipset Slot Count",
            "s_nQuestRequire" => "Required Quest Reference",

            "s_btUseTimeType" => "Timed Item Rule",
            "s_nUseTime_Min" => "Timed Item Duration",
            "s_nUseBattle" => "Usable During Battle",
            "s_nDoNotUseType" => "Usage Restriction Mode",
            "s_bUseTimeType" => "Timed Rule Enabled",

            "unkt" => "Unconfirmed Internal Field (unkt)",
            "unkr" => "Unconfirmed Internal Field (unkr)",
            "ukteste1" => "Unconfirmed Internal Field (ukteste1)",
            "uktest" => "Unconfirmed Internal Field (uktest)",
            "unk2" => "Unconfirmed Binary Field (unk2)",
            "unk3" => "Unconfirmed Binary Field (unk3)",
            "unk4" => "Unconfirmed Binary Field (unk4)",
            "unks" => "Unconfirmed Internal Field (unks)",
            "unkss" => "Unconfirmed Binary Field (unkss)",

            _ => string.Empty
        };

        private static string FieldHint(string tag) => tag switch
        {
            "s_dwItemID" => "Tem de ser único.",
            "s_nIcon" => "Resolve o icon através de ImgDatabase / InterfaceIconMap.json.",
            "s_nClass" => "Classe/raridade. Não é limitado pelo editor enquanto confirmamos todos os valores válidos.",
            "s_nOverlap" => "Quantidade máxima por stack.",
            "s_nDrop" => "No teu XML aparecem valores além de 0/1; o editor preserva o valor original.",
            "s_dwScanPrice" => "Valor numérico bruto usado pelo client.",
            "s_btApplyRateMax" => "Rate máximo de attributes/socket.",
            "s_btApplyRateMin" => "Rate mínimo de attributes/socket.",
            "s_nSocketCount" => "Número de sockets/slots.",
            "s_nBelonging" => "0/1/2 são usados no XML; significado final deve seguir o client.",
            "s_nUseTime_Min" => "Valor temporal bruto do XML.",
            "s_nUseBattle" => "Flag de utilização em batalha.",
            _ => string.Empty
        };

        private static void DisposeChildImages(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is PictureBox pb)
                {
                    pb.Image?.Dispose();
                    pb.Image = null;
                }

                if (child.HasChildren)
                    DisposeChildImages(child);
            }
        }

        private void DisposeEditorResources()
        {
            foreach (Bitmap? image in itemIconCache.Values)
                image?.Dispose();

            itemIconCache.Clear();
        }

        private void ShowEditorError(string operation, Exception ex)
        {
            AppLogger.ErrorDetailed(
                $"Editor - {operation}",
                ex.Message,
                "Revê os campos indicados e volta a tentar. O editor cria backup .editor.bak antes de substituir o XML.");

            MessageBox.Show(
                ex.Message,
                operation,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
