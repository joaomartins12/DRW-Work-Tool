using DRW_Work_Tool.Core;
using System;
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
        private bool _achievementIntegrationReady;
        private bool _achievementRefreshPending;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (_achievementIntegrationReady)
                return;

            _achievementIntegrationReady = true;

            if (editorTabs != null)
            {
                editorTabs.SelectedIndexChanged += (_, _) =>
                    QueueAchievementIntegrationRefresh();

                editorTabs.ControlAdded += (_, _) =>
                    QueueAchievementIntegrationRefresh();
            }

            QueueAchievementIntegrationRefresh();
        }

        private void QueueAchievementIntegrationRefresh()
        {
            if (_achievementRefreshPending || IsDisposed || !IsHandleCreated)
                return;

            _achievementRefreshPending = true;

            BeginInvoke(new Action(() =>
            {
                _achievementRefreshPending = false;
                RefreshAchievementIntegration();
            }));
        }

        private void RefreshAchievementIntegration()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            foreach (TabPage page in editorTabs.TabPages)
            {
                if (page.IsDisposed)
                    continue;

                if (page.Tag is EntityTabState entityState &&
                    entityState.Entity.Equals("Achieve", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureAchievementImportButton(page);
                    EnsureAchievementDirectOpenButton(page);
                    continue;
                }

                if (page.Tag is AchievementBrowseState state &&
                    state.Results.Tag is string marker &&
                    marker == "PreparedAchievementBrowser")
                {
                    ApplyPreparedAchievementIcons(state);
                }
            }
        }

        private void EnsureAchievementDirectOpenButton(TabPage page)
        {
            if (page.Controls.Find("AchievementDirectOpenButton", true).Length > 0)
                return;

            Button? oldOpen = EnumerateAchievementIntegrationControls(page)
                .OfType<Button>()
                .FirstOrDefault(x =>
                    x.Text.Equals("OPEN", StringComparison.OrdinalIgnoreCase));

            if (oldOpen == null || oldOpen.Parent == null)
                return;

            Control parent = oldOpen.Parent;

            var open = CreateEditorActionButton("OPEN");
            open.Name = "AchievementDirectOpenButton";
            open.Location = oldOpen.Location;
            open.Size = oldOpen.Size;
            open.Anchor = oldOpen.Anchor;
            open.TabIndex = oldOpen.TabIndex;

            editorToolTip.SetToolTip(
                open,
                "Open Achieve.xml in the visual Achievement / Title Editor. " +
                "The loading screen remains until every initial card and title icon is ready.");

            open.Click += async (_, _) =>
            {
                string path = Path.Combine(
                    AppPaths.Xml,
                    "Achieve",
                    "Achieve.xml");

                if (!File.Exists(path))
                {
                    MessageBox.Show(
                        "Achieve.xml was not found:\r\n\r\n" + path,
                        "Achievement Editor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                TabPage? existing = editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(x =>
                        x.Tag is AchievementBrowseState ||
                        ContainsAchievementLoadingView(x));

                if (existing != null)
                {
                    editorTabs.SelectedTab = existing;
                    return;
                }

                await OpenPreparedAchievementBrowserAsync(path);
            };

            parent.Controls.Remove(oldOpen);
            oldOpen.Dispose();
            parent.Controls.Add(open);
            open.BringToFront();
        }

        private async Task OpenPreparedAchievementBrowserAsync(string xmlPath)
        {
            string full = Path.GetFullPath(xmlPath);

            var page = CreateDarkTab("Achieve.xml");
            page.Name = full;

            var loading = new EditorLoadingView(
                "Loading Achievement / Title Editor",
                "Reading Achieve.xml, Quest.xml and Buff.xml, then decoding achieve_icon.dds, achieve_icon_02.dds and achieve_icon_03.dds.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            // Allow the loading view to be painted before any expensive work.
            await Task.Delay(80);

            try
            {
                AchievementService service = await Task.Run(() =>
                {
                    var loaded = new AchievementService(full);

                    // This is intentionally NOT ImageDatabasePreview. Achievement
                    // titles require the real DDS atlas files and the DDS decoder.
                    AchievementIconAtlasCache.Preload();

                    return loaded;
                });

                if (page.IsDisposed)
                    return;

                await BuildPreparedAchievementBrowserAsync(
                    page,
                    service,
                    loading);
            }
            catch (Exception ex)
            {
                if (page.IsDisposed)
                    return;

                page.Controls.Clear();
                page.Controls.Add(CreateInfoLabel(
                    "Achievement editor could not be prepared.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nExpected DDS atlases: achieve_icon.dds, achieve_icon_02.dds, achieve_icon_03.dds."));

                AppLogger.ErrorDetailed(
                    "Achievement Editor",
                    ex.Message,
                    "Verify ImgDatabase/ImageDatabase.json and the three achieve_icon DDS atlas files.");
            }
        }

        private async Task BuildPreparedAchievementBrowserAsync(
            TabPage page,
            AchievementService service,
            EditorLoadingView loading)
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(18),
                Visible = false
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 112,
                BackColor = CEditor
            };

            var title = new Label
            {
                Text = "Achievement / Title Editor",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                Location = new Point(8, 4),
                AutoSize = true
            };

            var sub = new Label
            {
                Text = $"{service.Records.Count:N0} titles • achieve_icon DDS atlases • Buff.xml • title quests only",
                ForeColor = CMuted,
                Location = new Point(10, 35),
                AutoSize = true
            };

            var search = new TextBox
            {
                Location = new Point(8, 68),
                Height = 28,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Search QuestID, title, name, type, group, BuffID...",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var create = CreateEditorActionButton("NEW TITLE");
            create.Size = new Size(130, 34);
            create.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var count = new Label
            {
                ForeColor = CMuted,
                AutoSize = true,
                Location = new Point(10, 96)
            };

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = CEditor,
                Padding = new Padding(4, 8, 16, 8),
                Tag = "PreparedAchievementBrowser"
            };

            DarkUi.ApplyDarkScrollBar(results);

            header.Controls.AddRange(new Control[]
            {
                title,
                sub,
                search,
                create,
                count
            });

            root.Controls.Add(results);
            root.Controls.Add(header);
            page.Controls.Add(root);
            loading.BringToFront();

            var state = new AchievementBrowseState
            {
                Service = service,
                Results = results,
                Search = search,
                Count = count
            };

            page.Tag = state;

            void Layout()
            {
                create.Location = new Point(
                    Math.Max(150, header.ClientSize.Width - create.Width - 8),
                    6);

                search.Width = Math.Max(220, header.ClientSize.Width - 16);
            }

            header.Resize += (_, _) => Layout();
            results.Resize += (_, _) => ResizeAchievementCards(results);

            search.TextChanged += async (_, _) =>
                await RefreshPreparedAchievementBrowserAsync(state);

            create.Click += (_, _) =>
                OpenAchievementEditTab(
                    service,
                    service.CreateNewNode(),
                    null,
                    true);

            Layout();

            // The loading overlay remains visible while ALL initial cards are
            // created in batches. Task.Yield lets its animation repaint between
            // batches rather than freezing the UI while hundreds of controls are
            // allocated.
            await RefreshPreparedAchievementBrowserAsync(
                state,
                keepLoadingResponsive: true,
                loading);

            if (page.IsDisposed)
                return;

            root.Visible = true;
            root.BringToFront();

            page.Controls.Remove(loading);
            loading.Dispose();

            root.PerformLayout();
            results.PerformLayout();
            ApplyPreparedAchievementIcons(state);
        }

        private async Task RefreshPreparedAchievementBrowserAsync(
            AchievementBrowseState state,
            bool keepLoadingResponsive = false,
            EditorLoadingView? loading = null)
        {
            if (state.Results.IsDisposed)
                return;

            string query = state.Search.Text.Trim();

            state.Filtered = state.Service.Records
                .Where(x =>
                    query.Length == 0 ||
                    x.ToString(SaveOptions.DisableFormatting)
                        .Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            DisposeAchievementCardImages(state.Results);
            state.Results.SuspendLayout();
            state.Results.Controls.Clear();
            state.Results.ResumeLayout(false);

            const int batchSize = 32;

            for (int index = 0; index < state.Filtered.Count; index++)
            {
                XElement node = state.Filtered[index];
                state.Results.Controls.Add(
                    CreatePreparedAchievementCard(state, node));

                if (keepLoadingResponsive &&
                    (index + 1) % batchSize == 0)
                {
                    loading?.BringToFront();
                    await Task.Yield();
                }
            }

            state.Count.Text =
                $"Results: {state.Filtered.Count:N0} / {state.Service.Records.Count:N0}";

            ResizeAchievementCards(state.Results);
            state.Results.PerformLayout();
        }

        private Control CreatePreparedAchievementCard(
            AchievementBrowseState state,
            XElement node)
        {
            uint questId = UInt(node, "s_nQuestID");
            uint iconId = UInt(node, "s_nIcon");
            uint buffId = UInt(node, "s_nBuffCode");

            string name = AchievementText(node, "s_szName");
            string titleText = AchievementText(node, "s_szTitle");
            XElement? quest = state.Service.Quest(questId);

            var card = new Panel
            {
                Height = 104,
                Width = Math.Max(560, state.Results.ClientSize.Width - 26),
                BackColor = Color.FromArgb(29, 29, 29),
                Margin = new Padding(0, 0, 0, 8),
                Tag = node
            };

            card.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(70, 70, 70));
                e.Graphics.DrawRectangle(
                    pen,
                    0,
                    0,
                    card.Width - 1,
                    card.Height - 1);
            };

            var icon = new PictureBox
            {
                Location = new Point(12, 12),
                Size = new Size(78, 78),
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = AchievementIconAtlasCache.TryLoad(iconId),
                Tag = iconId
            };

            var main = new Label
            {
                Text = string.IsNullOrWhiteSpace(titleText) ? name : titleText,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(104, 10),
                Size = new Size(430, 24),
                AutoEllipsis = true
            };

            var info = new Label
            {
                Text =
                    $"Quest {questId} • Icon {iconId} • Type {AchievementText(node, "s_nType")} • " +
                    $"Group {AchievementText(node, "s_nGroup")}/{AchievementText(node, "s_nSubGroup")}",
                ForeColor = Color.FromArgb(120, 220, 145),
                Location = new Point(104, 36),
                Size = new Size(480, 20),
                AutoEllipsis = true
            };

            var refs = new Label
            {
                Text =
                    $"{state.Service.BuffSummary(buffId)} • Quest: " +
                    (quest == null ? "missing" : AchievementText(quest, "TitleText")),
                ForeColor = CMuted,
                Location = new Point(104, 58),
                Size = new Size(500, 20),
                AutoEllipsis = true
            };

            var desc = new Label
            {
                Text = AchievementText(node, "s_szComment"),
                ForeColor = CMuted,
                Location = new Point(104, 79),
                Size = new Size(500, 18),
                AutoEllipsis = true
            };

            var edit = CreateEditorActionButton("EDIT");
            edit.Size = new Size(88, 30);
            edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var clone = CreateEditorActionButton("CLONE");
            clone.Size = new Size(88, 30);
            clone.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            void LayoutCard()
            {
                clone.Location = new Point(
                    card.ClientSize.Width - clone.Width - 12,
                    54);

                edit.Location = new Point(
                    card.ClientSize.Width - edit.Width - 12,
                    16);

                int width = Math.Max(
                    140,
                    edit.Left - main.Left - 12);

                main.Width = width;
                info.Width = width;
                refs.Width = width;
                desc.Width = width;
            }

            card.Resize += (_, _) => LayoutCard();

            edit.Click += (_, _) =>
                OpenAchievementEditTab(
                    state.Service,
                    new XElement(node),
                    node,
                    false);

            clone.Click += (_, _) =>
            {
                XElement copy = new XElement(node);
                uint next = state.Service.SuggestAvailableId(questId + 1);

                XElement? id = copy.Element("s_nQuestID");
                if (id != null)
                    id.Value = next.ToString(CultureInfo.InvariantCulture);

                XElement? cloneName = copy.Element("s_szName");
                if (cloneName != null)
                    cloneName.Value += " [Clone]";

                OpenAchievementEditTab(
                    state.Service,
                    copy,
                    null,
                    true);
            };

            card.Controls.AddRange(new Control[]
            {
                icon,
                main,
                info,
                refs,
                desc,
                edit,
                clone
            });

            LayoutCard();
            return card;
        }

        private void ApplyPreparedAchievementIcons(AchievementBrowseState state)
        {
            if (state.Results.IsDisposed)
                return;

            foreach (Control card in state.Results.Controls)
            {
                if (card.Tag is not XElement node)
                    continue;

                PictureBox? picture = card.Controls
                    .OfType<PictureBox>()
                    .FirstOrDefault();

                if (picture == null || picture.IsDisposed)
                    continue;

                uint iconId = UInt(node, "s_nIcon");

                if (picture.Image != null &&
                    picture.Tag is uint loaded &&
                    loaded == iconId)
                {
                    continue;
                }

                Image? old = picture.Image;
                picture.Image = AchievementIconAtlasCache.TryLoad(iconId);
                picture.Tag = iconId;

                if (!ReferenceEquals(old, picture.Image))
                    old?.Dispose();
            }
        }

        private static void DisposeAchievementCardImages(Control root)
        {
            foreach (Control card in root.Controls)
            {
                foreach (PictureBox picture in card.Controls.OfType<PictureBox>())
                {
                    picture.Image?.Dispose();
                    picture.Image = null;
                }
            }
        }

        private static bool ContainsAchievementLoadingView(Control root)
        {
            if (root is EditorLoadingView)
                return true;

            foreach (Control child in root.Controls)
            {
                if (ContainsAchievementLoadingView(child))
                    return true;
            }

            return false;
        }

        private void EnsureAchievementImportButton(TabPage page)
        {
            Panel? header = page.Controls
                .Cast<Control>()
                .SelectMany(EnumerateAchievementIntegrationControls)
                .OfType<Panel>()
                .FirstOrDefault(x => x.Name == "EntityEditorHeader");

            if (header == null ||
                header.Controls["AchievementDatabaseImportHost"] != null)
            {
                return;
            }

            var host = new Panel
            {
                Name = "AchievementDatabaseImportHost",
                Dock = DockStyle.Right,
                Width = 224,
                BackColor = Color.Transparent,
                Padding = new Padding(10, 18, 18, 18)
            };

            var button = CreateEditorActionButton("IMPORT TO DATABASE");
            button.Name = "btnImportAchievementToDatabase";
            button.Dock = DockStyle.Fill;
            button.Font = new Font(
                "Segoe UI Semibold",
                8.5F,
                FontStyle.Bold);

            editorToolTip.SetToolTip(
                button,
                "Imports Achieve.xml into Asset.Achievement using QuestId=s_nQuestID and BuffId=s_nBuffCode. " +
                "Runs in one SQL transaction with rollback on failure.");

            button.Click += async (_, _) =>
                await OpenAchievementDatabaseImportTabAndRunAsync(
                    Path.Combine(AppPaths.Xml, "Achieve"));

            host.Controls.Add(button);
            header.Controls.Add(host);
            host.BringToFront();
        }

        private static System.Collections.Generic.IEnumerable<Control>
            EnumerateAchievementIntegrationControls(Control root)
        {
            yield return root;

            foreach (Control child in root.Controls)
            {
                foreach (Control nested in
                    EnumerateAchievementIntegrationControls(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
