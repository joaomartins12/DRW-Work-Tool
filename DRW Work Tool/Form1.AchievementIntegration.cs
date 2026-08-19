using DRW_Work_Tool.Core;
using System;
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
        private bool _achievementIntegrationReady;
        private bool _achievementRefreshPending;
        private System.Windows.Forms.Timer? _achievementStateTimer;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (_achievementIntegrationReady)
                return;

            _achievementIntegrationReady = true;

            _achievementStateTimer = new System.Windows.Forms.Timer
            {
                Interval = 120
            };

            _achievementStateTimer.Tick += (_, _) =>
                RefreshAchievementIntegration();

            if (editorTabs != null)
            {
                editorTabs.SelectedIndexChanged += (_, _) =>
                {
                    QueueAchievementIntegrationRefresh();
                    StartAchievementStateTimer();
                };

                editorTabs.ControlAdded += (_, _) =>
                {
                    QueueAchievementIntegrationRefresh();
                    StartAchievementStateTimer();
                };
            }

            QueueAchievementIntegrationRefresh();
        }

        private void StartAchievementStateTimer()
        {
            if (_achievementStateTimer == null || _achievementStateTimer.Enabled)
                return;

            _achievementStateTimer.Start();
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

            bool waitingForAchievementBrowser = false;

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

                if (page.Tag is AchievementBrowseState browseState)
                {
                    EnsureAchievementIconBinding(browseState);
                    ApplyVisibleAchievementIconPreviews(browseState);
                    continue;
                }

                if (ContainsAchievementLoadingView(page))
                    waitingForAchievementBrowser = true;
            }

            if (!waitingForAchievementBrowser)
                _achievementStateTimer?.Stop();
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
                "The loading screen remains visible until Achieve.xml, Quest.xml, Buff.xml and the title icon atlases are ready.");

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
                    StartAchievementStateTimer();
                    return;
                }

                StartAchievementStateTimer();
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
                "Loading Achieve.xml, Quest.xml, Buff.xml and title icons from achieve_icon.dds, achieve_icon_02.dds and achieve_icon_03.dds...");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            // Give WinForms enough time to perform an actual paint before the
            // heavier XML/ImageDatabase work starts.
            await Task.Delay(40);

            try
            {
                AchievementService service = await Task.Run(() =>
                {
                    var loaded = new AchievementService(full);

                    // Warm the title icon mappings while the loading view is still
                    // visible. Category Achieve resolves the three achieve_icon atlases.
                    foreach (uint iconId in loaded.Records
                        .Select(x => UInt(x, "s_nIcon"))
                        .Where(x => x > 0)
                        .Distinct())
                    {
                        using Bitmap? preview =
                            ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Achieve");
                    }

                    return loaded;
                });

                if (page.IsDisposed)
                    return;

                BuildAchievementBrowser(page, service);

                // Let card layout complete before applying visible previews.
                await Task.Yield();

                if (!page.IsDisposed && page.Tag is AchievementBrowseState state)
                {
                    EnsureAchievementIconBinding(state);
                    ApplyVisibleAchievementIconPreviews(state);
                }
            }
            catch (Exception ex)
            {
                if (page.IsDisposed)
                    return;

                page.Controls.Clear();
                page.Controls.Add(CreateInfoLabel(
                    "Achieve.xml could not be loaded.\r\n\r\n" + ex.Message));

                AppLogger.ErrorDetailed(
                    "Achievement Editor",
                    ex.Message,
                    "Verify Achieve.xml, Quest.xml, Buff.xml and achieve_icon atlas mappings in ImgDatabase.");
            }
        }

        private void EnsureAchievementIconBinding(AchievementBrowseState state)
        {
            const string marker = "AchievementIconBinding";

            if (state.Results.Tag is string tag && tag == marker)
                return;

            state.Results.Tag = marker;

            state.Results.Scroll += (_, _) =>
                BeginInvoke(new Action(() =>
                    ApplyVisibleAchievementIconPreviews(state)));

            state.Results.MouseWheel += (_, _) =>
                BeginInvoke(new Action(() =>
                    ApplyVisibleAchievementIconPreviews(state)));

            state.Search.TextChanged += (_, _) =>
                BeginInvoke(new Action(() =>
                    ApplyVisibleAchievementIconPreviews(state)));
        }

        private void ApplyVisibleAchievementIconPreviews(AchievementBrowseState state)
        {
            if (state.Results.IsDisposed)
                return;

            Rectangle viewport = state.Results.ClientRectangle;
            viewport.Inflate(0, 120);

            int index = 0;

            foreach (Control card in state.Results.Controls)
            {
                if (index >= state.Filtered.Count)
                    break;

                XElement node = state.Filtered[index++];

                Rectangle bounds = new Rectangle(
                    card.Left + state.Results.AutoScrollPosition.X,
                    card.Top + state.Results.AutoScrollPosition.Y,
                    card.Width,
                    card.Height);

                if (!viewport.IntersectsWith(bounds))
                    continue;

                PictureBox? picture = card.Controls
                    .OfType<PictureBox>()
                    .FirstOrDefault();

                if (picture == null || picture.IsDisposed)
                    continue;

                uint iconId = UInt(node, "s_nIcon");

                if (picture.Tag is uint loaded &&
                    loaded == iconId &&
                    picture.Image != null)
                {
                    continue;
                }

                Image? previous = picture.Image;

                // Achievement/title icons are stored in achieve_icon.dds,
                // achieve_icon_02.dds and achieve_icon_03.dds.
                picture.Image = ImageDatabasePreview.TryLoadInterfaceIcon(
                    iconId,
                    "Achieve");

                picture.Tag = iconId;

                if (!ReferenceEquals(previous, picture.Image))
                    previous?.Dispose();
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
