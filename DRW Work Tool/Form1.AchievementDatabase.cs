using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private async Task OpenAchievementDatabaseImportTabAndRunAsync(string folder)
        {
            string xml = Path.Combine(folder, "Achieve.xml");
            if (!File.Exists(xml))
            {
                MessageBox.Show("Achieve.xml was not found:\r\n\r\n" + xml,
                    "Achievement Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Achievement Import", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show("Configure and test the SQL Server connection in SETTINGS first.",
                    "Achievement Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "This will replace [dmo].[Asset].[Achievement] using Achieve.xml.\r\n\r\n" +
                "Mapping: QuestId=s_nQuestID, BuffId=s_nBuffCode, Type=0 (matching the supplied working DB sample).\r\n" +
                "The operation runs inside one SQL transaction and rolls back on failure.\r\n\r\nContinue?",
                "Import Achievement to Database", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            var page = CreateDarkTab("Achievement DB Import");
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var title = new Label { Text = "Achievement → Asset.Achievement", ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold), Location = new Point(8,8), AutoSize = true };
            var status = new Label { Text = "Preparing...", ForeColor = CMuted, Location = new Point(10,44), Size = new Size(720,24), AutoEllipsis = true };
            var log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom, Height = 430, BackColor = Color.FromArgb(10,10,10), ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 8.5F) };
            root.Controls.AddRange(new Control[] { title, status, log }); page.Controls.Add(root);
            editorTabs.TabPages.Add(page); editorTabs.SelectedTab = page;

            databaseImportCancellation?.Cancel();
            databaseImportCancellation?.Dispose();
            databaseImportCancellation = new CancellationTokenSource();
            CancellationToken token = databaseImportCancellation.Token;

            var progress = new Progress<string>(line =>
            {
                if (page.IsDisposed) return;
                status.Text = line;
                log.AppendText(line + Environment.NewLine);
            });

            try
            {
                var service = new AchievementDatabaseImportService();
                AchievementDatabaseImportSummary summary = await service.ImportAsync(connection, xml, progress, token);
                status.Text = $"DONE • {summary.Rows:N0} rows • {summary.Elapsed.TotalSeconds:N1}s";
                status.ForeColor = Color.FromArgb(120,220,145);
                MessageBox.Show($"Achievement import completed.\r\n\r\nRows: {summary.Rows:N0}\r\nLog: {summary.LogFile}",
                    "Achievement Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                status.Text = "Cancelled — transaction rolled back.";
                status.ForeColor = Color.FromArgb(255,190,90);
            }
            catch (Exception ex)
            {
                status.Text = "FAILED — transaction rolled back.";
                status.ForeColor = Color.FromArgb(255,100,110);
                log.AppendText(Environment.NewLine + ex + Environment.NewLine);
                ShowEditorError("Achievement Database Import", ex);
            }
        }
    }
}
