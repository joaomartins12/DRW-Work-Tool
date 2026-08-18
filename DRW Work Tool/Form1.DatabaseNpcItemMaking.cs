using DRW_Work_Tool.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private async Task OpenItemMakingDatabaseImportTabAndRunAsync(
            string itemMakingXml)
        {
            await RunNpcOrMakingImportAsync(
                title: "ItemMaking Database Import",
                description: "ItemCraft + ItemCraftMaterial",
                importer: async (connection, progress, token) =>
                {
                    var service = new NpcItemMakingDatabaseImportService();
                    ItemMakingDatabaseImportSummary summary =
                        await service.ImportItemMakingAsync(
                            connection,
                            itemMakingXml,
                            progress,
                            token);

                    return
                        $"ItemCraft {summary.CraftRows:N0} | " +
                        $"Materials {summary.MaterialRows:N0}";
                });
        }

        private async Task OpenNpcDatabaseImportTabAndRunAsync(
            string npcXml)
        {
            await RunNpcOrMakingImportAsync(
                title: "NPC Database Import",
                description:
                    "Npc + NpcItem + NpcPortal + NpcPortalsAmount + NpcPortals + NpcColiseum",
                importer: async (connection, progress, token) =>
                {
                    var service = new NpcItemMakingDatabaseImportService();
                    NpcDatabaseImportSummary summary =
                        await service.ImportNpcAsync(
                            connection,
                            npcXml,
                            progress,
                            token);

                    return
                        $"Npc {summary.NpcRows:N0} | Items {summary.NpcItemRows:N0} | " +
                        $"Portal {summary.PortalRows:N0} | PortalType {summary.PortalAmountRows:N0} | " +
                        $"Req {summary.PortalRequirementRows:N0} | Coliseum {summary.ColiseumRows:N0}";
                });
        }

        private async Task RunNpcOrMakingImportAsync(
            string title,
            string description,
            Func<string, IProgress<string>, CancellationToken, Task<string>> importer)
        {
            string connection;

            try
            {
                connection = DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível ler a connection string cifrada.\r\n\r\n" + ex.Message,
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(
                    "Configura primeiro a connection string em SETTINGS → SQL Server Database.",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                ShowSettings(true);
                return;
            }

            var page = CreateDarkTab(title + " [Running]");

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                BackColor = CPanel
            };

            var status = new Label
            {
                Text = "IMPORT TO DATABASE — " + description,
                ForeColor = CText,
                Font = new System.Drawing.Font(
                    "Segoe UI Semibold",
                    9F,
                    System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(14, 0),
                Size = new System.Drawing.Size(670, 58),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var cancel = CreateEditorActionButton("CANCEL");
            cancel.Size = new System.Drawing.Size(90, 32);
            cancel.Location = new System.Drawing.Point(700, 13);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.Color.FromArgb(10, 10, 10),
                ForeColor = System.Drawing.Color.FromArgb(225, 225, 225),
                Font = new System.Drawing.Font("Consolas", 8.5F),
                WordWrap = false
            };

            DarkUi.ApplyDarkScrollBar(log);

            header.Controls.Add(status);
            header.Controls.Add(cancel);
            page.Controls.Add(log);
            page.Controls.Add(header);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            databaseImportCancellation?.Cancel();
            databaseImportCancellation?.Dispose();
            databaseImportCancellation = new CancellationTokenSource();

            cancel.Click += (_, _) =>
                databaseImportCancellation.Cancel();

            var progress = new Progress<string>(line =>
            {
                if (log.IsDisposed)
                    return;

                log.AppendText(line + Environment.NewLine);
                log.SelectionStart = log.TextLength;
                log.ScrollToCaret();
            });

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Importing...");

            try
            {
                string summary = await importer(
                    connection,
                    progress,
                    databaseImportCancellation.Token);

                page.Text = title + " [Success]";
                status.Text = "SUCCESS — " + summary;

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");

                MessageBox.Show(
                    "Importação concluída com sucesso.\r\n\r\n" + summary,
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                page.Text = title + " [Cancelled]";
                status.Text = "CANCELLED — transaction rolled back";
                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");
            }
            catch (Exception ex)
            {
                page.Text = title + " [Failed]";
                status.Text = "FAILED — " + ex.Message;

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Import failed");

                log.AppendText(
                    Environment.NewLine +
                    "[ERROR] " +
                    ex +
                    Environment.NewLine);

                MessageBox.Show(
                    ex.Message,
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
