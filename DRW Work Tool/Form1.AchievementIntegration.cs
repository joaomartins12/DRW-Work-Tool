using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private bool _achievementIntegrationReady;
        private bool _achievementRedirectPending;
        private bool _achievementRedirecting;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (_achievementIntegrationReady)
                return;

            _achievementIntegrationReady = true;

            if (editorTabs != null)
            {
                editorTabs.SelectedIndexChanged += (_, _) => QueueAchievementIntegrationRefresh();
                editorTabs.ControlAdded += (_, _) => QueueAchievementIntegrationRefresh();
            }

            QueueAchievementIntegrationRefresh();
        }

        private void QueueAchievementIntegrationRefresh()
        {
            if (_achievementRedirectPending || IsDisposed || !IsHandleCreated)
                return;

            _achievementRedirectPending = true;
            BeginInvoke(new Action(() =>
            {
                _achievementRedirectPending = false;
                RefreshAchievementIntegration();
            }));
        }

        private void RefreshAchievementIntegration()
        {
            if (_achievementRedirecting || editorTabs == null || editorTabs.IsDisposed)
                return;

            // Keep the database import action available on the Achieve landing tab.
            foreach (TabPage page in editorTabs.TabPages)
            {
                if (page.Tag is EntityTabState entityState &&
                    entityState.Entity.Equals("Achieve", StringComparison.OrdinalIgnoreCase))
                {
                    EnsureAchievementImportButton(page);
                }
            }

            // Scan every tab instead of only SelectedTab. The generic XML browser
            // does not always keep the full XML path in TabPage.Name, so detection
            // also uses the visible tab caption. This is why Achieve.xml could stay
            // stuck in "Block Browser" even though the integration event fired.
            TabPage? staleGeneric = editorTabs.TabPages
                .Cast<TabPage>()
                .FirstOrDefault(IsGenericAchievementTab);

            if (staleGeneric == null)
                return;

            string path = ResolveAchievementPath(staleGeneric);
            if (!File.Exists(path))
                return;

            _achievementRedirecting = true;
            try
            {
                editorTabs.TabPages.Remove(staleGeneric);
                staleGeneric.Dispose();
                OpenAchievementBrowser(path);
            }
            finally
            {
                _achievementRedirecting = false;
            }
        }

        private bool IsGenericAchievementTab(TabPage page)
        {
            if (page.IsDisposed)
                return false;

            // Never touch our own dedicated editor/loading/edit tabs.
            if (page.Tag is AchievementBrowseState ||
                page.Tag is AchievementEditState ||
                ContainsAchievementLoadingView(page))
            {
                return false;
            }

            string byName = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(page.Name))
                    byName = Path.GetFileName(page.Name);
            }
            catch
            {
                byName = string.Empty;
            }

            bool looksLikeAchievement =
                byName.Equals("Achieve.xml", StringComparison.OrdinalIgnoreCase) ||
                page.Text.Equals("Achieve.xml", StringComparison.OrdinalIgnoreCase) ||
                page.Text.StartsWith("Achieve.xml ", StringComparison.OrdinalIgnoreCase);

            if (!looksLikeAchievement)
                return false;

            string stateType = page.Tag?.GetType().Name ?? string.Empty;

            return stateType.Contains("GenericBrowseState", StringComparison.Ordinal) ||
                   ContainsBlockBrowserUi(page);
        }

        private static bool ContainsBlockBrowserUi(Control root)
        {
            foreach (Control control in EnumerateAchievementIntegrationControls(root))
            {
                if (control is Label label &&
                    label.Text.Contains("Block Browser", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveAchievementPath(TabPage page)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(page.Name) && File.Exists(page.Name))
                    return Path.GetFullPath(page.Name);
            }
            catch
            {
            }

            return Path.Combine(AppPaths.Xml, "Achieve", "Achieve.xml");
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

            if (header == null || header.Controls["AchievementDatabaseImportHost"] != null)
                return;

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
            button.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);

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

        private static System.Collections.Generic.IEnumerable<Control> EnumerateAchievementIntegrationControls(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (Control nested in EnumerateAchievementIntegrationControls(child))
                    yield return nested;
        }
    }
}
