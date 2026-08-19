using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private void EnsureCashShopDatabaseButtons(CashShopBrowseState state)
        {
            Control? root = state.Cards.Parent;
            if (root == null)
                return;

            Button? newTemplate = EnumerateCashShopControls(root)
                .OfType<Button>()
                .FirstOrDefault(x => x.Text.Equals("NEW TEMPLATE", StringComparison.OrdinalIgnoreCase));

            if (newTemplate == null || newTemplate.Parent == null)
                return;

            Control host = newTemplate.Parent;

            Button compare;
            if (host.Controls.Find("CashShopCompareDbButton", false).FirstOrDefault() is Button existingCompare)
            {
                compare = existingCompare;
            }
            else
            {
                compare = CreateEditorActionButton("COMPARE DB");
                compare.Name = "CashShopCompareDbButton";
                compare.Size = new Size(116, 30);
                compare.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                compare.Click += async (_, _) => await OpenCashShopDatabaseCompareTabAsync();
                host.Controls.Add(compare);
                editorToolTip.SetToolTip(compare,
                    "READ-ONLY: compare canonical CashShop XML with dmo.Asset.CashShop and generate mapping/cardinality reports.");
            }

            Button import;
            if (host.Controls.Find("CashShopImportDbButton", false).FirstOrDefault() is Button existingImport)
            {
                import = existingImport;
            }
            else
            {
                import = CreateEditorActionButton("IMPORT DB");
                import.Name = "CashShopImportDbButton";
                import.Size = new Size(108, 30);
                import.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                import.Click += async (_, _) => await OpenCashShopDatabaseImportTabAsync();
                host.Controls.Add(import);
                editorToolTip.SetToolTip(import,
                    "Replace dmo.Asset.CashShop from the canonical CashShop XML set inside one SQL transaction.");
            }

            void Layout()
            {
                int gap = 8;
                import.Location = new Point(
                    Math.Max(8, newTemplate.Left - import.Width - gap),
                    newTemplate.Top);
                compare.Location = new Point(
                    Math.Max(8, import.Left - compare.Width - gap),
                    newTemplate.Top);
                compare.BringToFront();
                import.BringToFront();
                newTemplate.BringToFront();
            }

            host.Resize -= CashShopDatabaseHostResize;
            host.Resize += CashShopDatabaseHostResize;
            Layout();

            void CashShopDatabaseHostResize(object? sender, EventArgs e) => Layout();
        }

        private async Task OpenCashShopDatabaseCompareTabAsync()
        {
            string rootPath = Path.Combine(AppPaths.Xml, "CashShop");
            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show("CashShop folder was not found:\r\n\r\n" + rootPath,
                    "CashShop Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CashShop Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show("Configure and test the SQL Server connection in SETTINGS first.",
                    "CashShop Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var page = CreateDatabaseWorkTab(
                "CashShop DB Compare",
                "CashShop XML ↔ Asset.CashShop",
                "READ-ONLY • no database rows will be changed",
                out Label status,
                out TextBox log);

            var cts = new CancellationTokenSource();
            EventHandler onPageDisposed = (_, _) =>
            {
                if (!cts.IsCancellationRequested)
                    cts.Cancel();
            };
            page.Disposed += onPageDisposed;

            var progress = new Progress<string>(line =>
            {
                if (page.IsDisposed) return;
                status.Text = line;
                log.AppendText(line + Environment.NewLine);
            });

            try
            {
                var service = new CashShopDatabaseDiagnosticService();
                CashShopDatabaseDiagnosticSummary summary = await service.CompareAsync(
                    connection, rootPath, progress, cts.Token);

                if (page.IsDisposed)
                    return;

                status.Text = $"DONE • XML {summary.XmlFlattenedRows:N0} rows • DB {summary.DatabaseRows:N0} • matched {summary.MatchedRows:N0}";
                status.ForeColor = Color.FromArgb(120, 220, 145);
                log.AppendText(Environment.NewLine + "HIGH SIGNAL REPORT: " + summary.HighSignalReport + Environment.NewLine);

                DialogResult open = MessageBox.Show(
                    "Cash Shop database comparison completed.\r\n\r\n" +
                    $"XML groups: {summary.XmlContainers:N0}\r\n" +
                    $"XML purchase options: {summary.XmlOptions:N0}\r\n" +
                    $"XML DB-shaped rows: {summary.XmlFlattenedRows:N0}\r\n" +
                    $"DB rows: {summary.DatabaseRows:N0}\r\n" +
                    $"Matched rows: {summary.MatchedRows:N0}\r\n\r\n" +
                    "Open the diagnostic folder?",
                    "CashShop Compare DB",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (open == DialogResult.Yes && Directory.Exists(summary.OutputFolder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = summary.OutputFolder,
                        UseShellExecute = true
                    });
                }
            }
            catch (OperationCanceledException)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "Cancelled.";
                    status.ForeColor = Color.FromArgb(255, 190, 90);
                }
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "FAILED — database was not modified.";
                    status.ForeColor = Color.FromArgb(255, 100, 110);
                    log.AppendText(Environment.NewLine + ex + Environment.NewLine);
                    ShowEditorError("CashShop Compare DB", ex);
                }
            }
            finally
            {
                if (!page.IsDisposed)
                    page.Disposed -= onPageDisposed;
                cts.Dispose();
            }
        }

        private async Task OpenCashShopDatabaseImportTabAsync()
        {
            string rootPath = Path.Combine(AppPaths.Xml, "CashShop");
            if (!Directory.Exists(rootPath))
            {
                MessageBox.Show("CashShop folder was not found:\r\n\r\n" + rootPath,
                    "CashShop Import DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "CashShop Import DB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show("Configure and test the SQL Server connection in SETTINGS first.",
                    "CashShop Import DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult confirm = MessageBox.Show(
                "This will REPLACE [dmo].[Asset].[CashShop] using the canonical CashShop XML folders.\r\n\r\n" +
                "Numbered duplicate trees such as TamerInfo1 / DigimonInfo1 are ignored.\r\n" +
                "One DB row is generated per CASHINFO purchase option, using the first valid CashItems/Item entry.\r\n" +
                "Confirmed mapping: Quanty=first Amount, Price=nRealSellingPrice, Activated=Enabled, ItemName=Name.\r\n\r\n" +
                "The entire operation runs in ONE SQL transaction and rolls back on failure.\r\n\r\n" +
                "It is recommended to run COMPARE DB first. Continue?",
                "Import CashShop to Database",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            var page = CreateDatabaseWorkTab(
                "CashShop DB Import",
                "CashShop XML → Asset.CashShop",
                "Transactional import • confirmed CashShop mapping",
                out Label status,
                out TextBox log);

            var cts = new CancellationTokenSource();
            EventHandler onPageDisposed = (_, _) =>
            {
                if (!cts.IsCancellationRequested)
                    cts.Cancel();
            };
            page.Disposed += onPageDisposed;

            var progress = new Progress<string>(line =>
            {
                if (page.IsDisposed) return;
                status.Text = line;
                log.AppendText(line + Environment.NewLine);
            });

            try
            {
                var service = new CashShopDatabaseImportService();
                CashShopDatabaseImportSummary summary = await service.ImportAsync(
                    connection, rootPath, progress, cts.Token);

                if (page.IsDisposed)
                    return;

                status.Text = $"DONE • {summary.Rows:N0} rows • {summary.Elapsed.TotalSeconds:N1}s";
                status.ForeColor = Color.FromArgb(120, 220, 145);

                MessageBox.Show(
                    "Cash Shop import completed.\r\n\r\n" +
                    $"Rows: {summary.Rows:N0}\r\n" +
                    $"Quanty ← {summary.Mapping.QuantitySource}\r\n" +
                    $"Price ← {summary.Mapping.PriceSource}\r\n" +
                    $"Activated ← {summary.Mapping.ActivatedSource}\r\n" +
                    $"ItemName ← {summary.Mapping.ItemNameSource}\r\n\r\n" +
                    $"Log: {summary.LogFile}",
                    "CashShop Import DB",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "Cancelled — transaction rolled back.";
                    status.ForeColor = Color.FromArgb(255, 190, 90);
                }
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "FAILED — transaction rolled back.";
                    status.ForeColor = Color.FromArgb(255, 100, 110);
                    log.AppendText(Environment.NewLine + ex + Environment.NewLine);
                    ShowEditorError("CashShop Database Import", ex);
                }
            }
            finally
            {
                if (!page.IsDisposed)
                    page.Disposed -= onPageDisposed;
                cts.Dispose();
            }
        }

        private TabPage CreateDatabaseWorkTab(
            string tabTitle,
            string heading,
            string subheading,
            out Label status,
            out TextBox log)
        {
            var page = CreateDarkTab(tabTitle);
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CEditor,
                Padding = new Padding(18)
            };

            var title = new Label
            {
                Text = heading,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                Location = new Point(8, 8),
                AutoSize = true
            };

            var subtitle = new Label
            {
                Text = subheading,
                ForeColor = CMuted,
                Location = new Point(10, 39),
                AutoSize = true
            };

            status = new Label
            {
                Text = "Preparing...",
                ForeColor = CMuted,
                Location = new Point(10, 68),
                Size = new Size(850, 24),
                AutoEllipsis = true
            };

            log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Bottom,
                Height = 440,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5F)
            };

            root.Controls.AddRange(new Control[] { title, subtitle, status, log });
            page.Controls.Add(root);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            return page;
        }
    }
}
