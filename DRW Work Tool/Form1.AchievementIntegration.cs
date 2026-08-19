using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
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
                    EnsureAchievementSiconBinding(browseState);
                    ApplyVisibleAchievementSiconPreviews(browseState);
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
                "Open Achieve.xml directly in the visual Achievement / Title Editor.");

            open.Click += (_, _) =>
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

                // Delay one message-loop turn so the entity page can repaint
                // before the loading tab becomes active.
                BeginInvoke(new Action(() => OpenAchievementBrowser(path)));
            };

            parent.Controls.Remove(oldOpen);
            oldOpen.Dispose();
            parent.Controls.Add(open);
            open.BringToFront();
        }

        private void EnsureAchievementSiconBinding(AchievementBrowseState state)
        {
            const string marker = "AchievementSiconBinding";

            if (state.Results.Tag is string tag && tag == marker)
                return;

            state.Results.Tag = marker;

            state.Results.Scroll += (_, _) =>
                BeginInvoke(new Action(() =>
                    ApplyVisibleAchievementSiconPreviews(state)));

            state.Results.MouseWheel += (_, _) =>
                BeginInvoke(new Action(() =>
                    ApplyVisibleAchievementSiconPreviews(state)));

            state.Search.TextChanged += (_, _) =>
                BeginInvoke(new Action(() =>
                    ApplyVisibleAchievementSiconPreviews(state)));
        }

        private void ApplyVisibleAchievementSiconPreviews(AchievementBrowseState state)
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

                if (picture.Tag is uint loaded && loaded == iconId && picture.Image != null)
                    continue;

                Image? previous = picture.Image;

                // Title icons use the same sicon01-sicon07 family used by
                // Skill/Buff previews, so resolve them through category Skill.
                picture.Image = ImageDatabasePreview.TryLoadInterfaceIcon(
                    iconId,
                    "Skill");

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
