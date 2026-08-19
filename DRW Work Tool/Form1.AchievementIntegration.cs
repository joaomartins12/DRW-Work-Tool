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
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            TabPage? selected = editorTabs.SelectedTab;
            if (selected == null || selected.IsDisposed)
                return;

            // Entity landing page: add the DB import button even on older
            // Form1.Database.cs versions that do not yet know the Achieve entity.
            if (selected.Tag is EntityTabState entityState &&
                entityState.Entity.Equals("Achieve", StringComparison.OrdinalIgnoreCase))
            {
                EnsureAchievementImportButton(selected);
                return;
            }

            string fileName = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(selected.Name))
                    fileName = Path.GetFileName(selected.Name);
            }
            catch
            {
                fileName = string.Empty;
            }

            if (!fileName.Equals("Achieve.xml", StringComparison.OrdinalIgnoreCase))
                return;

            if (selected.Tag is AchievementBrowseState)
                return;

            string path = selected.Name;
            if (!File.Exists(path))
                return;

            // OpenXmlEditor may have created a generic browser before this
            // integration handler runs. Replace that stale generic tab with the
            // dedicated Achievement editor immediately.
            editorTabs.TabPages.Remove(selected);
            selected.Dispose();
            OpenAchievementBrowser(path);
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
