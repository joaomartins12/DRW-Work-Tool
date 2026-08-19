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
        private async Task OpenSkillDatabaseDiagnosticTabAndRunAsync()
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
                    "Skill DB Diagnostic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(
                    "Configura primeiro a connection string em SETTINGS → SQL Server Database.",
                    "Skill DB Diagnostic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                ShowSettings(true);
                return;
            }

            string skillXml = Path.Combine(AppPaths.Xml, "Skill", "Skill.xml");
            string digimonListXml = Path.Combine(AppPaths.Xml, "Digimon_List", "Digimon_List.xml");

            if (!File.Exists(skillXml) || !File.Exists(digimonListXml))
            {
                MessageBox.Show(
                    "O diagnóstico precisa de:\r\n\r\n" + skillXml + "\r\n" + digimonListXml,
                    "Skill DB Diagnostic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (MessageBox.Show(
                    "SKILL XML ↔ DATABASE DIAGNOSTIC\r\n\r\n" +
                    "Executa uma comparação READ-ONLY entre o Skill.xml / Digimon_List.xml e a database atual.\r\n\r\n" +
                    "NÃO executa INSERT, UPDATE, DELETE, TRUNCATE ou reseed.\r\n" +
                    "A database deve estar restaurada para o estado que funciona corretamente no jogo.\r\n\r\n" +
                    "Serão analisadas:\r\n" +
                    "• Asset.SkillInfo\r\n" +
                    "• Asset.SkillCode\r\n" +
                    "• Asset.SkillCodeApply\r\n" +
                    "• Asset.DigimonSkill\r\n\r\n" +
                    "O tool vai procurar automaticamente quais campos do XML apresentam maior correspondência com cada coluna SQL e gerar CSVs detalhados.\r\n\r\n" +
                    "Continuar?",
                    "Skill DB Diagnostic",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
            {
                return;
            }

            var page = CreateDarkTab("Skill DB Compare [Running]");
            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = CPanel };
            var status = new Label
            {
                Text = "READ-ONLY — Skill.xml ↔ current database",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(14, 0),
                Size = new Size(650, 72),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var cancel = CreateEditorActionButton("CANCEL");
            cancel.Size = new Size(96, 34);
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            void LayoutHeader()
            {
                cancel.Location = new Point(header.ClientSize.Width - cancel.Width - 14, 19);
                status.Width = Math.Max(260, cancel.Left - status.Left - 14);
            }
            header.Resize += (_, _) => LayoutHeader();

            var log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = Color.FromArgb(225, 225, 225),
                Font = new Font("Consolas", 8.5F),
                WordWrap = false
            };
            DarkUi.ApplyDarkScrollBar(log);

            header.Controls.Add(status);
            header.Controls.Add(cancel);
            page.Controls.Add(log);
            page.Controls.Add(header);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            LayoutHeader();

            databaseImportCancellation?.Cancel();
            databaseImportCancellation?.Dispose();
            databaseImportCancellation = new CancellationTokenSource();
            cancel.Click += (_, _) => databaseImportCancellation.Cancel();

            void Append(string line)
            {
                if (log.IsDisposed) return;
                int start = log.TextLength;
                log.AppendText(line + Environment.NewLine);
                log.Select(start, line.Length);
                log.SelectionColor = line.Contains("READ-ONLY", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains("Gerado", StringComparison.OrdinalIgnoreCase) ||
                                     line.Contains("concluído", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(125, 220, 140)
                    : line.Contains("WARNING", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromArgb(255, 190, 90)
                        : line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || line.Contains("ERRO", StringComparison.OrdinalIgnoreCase)
                            ? Color.FromArgb(255, 95, 95)
                            : Color.FromArgb(225, 225, 225);
                log.SelectionStart = log.TextLength;
                log.SelectionLength = 0;
                log.SelectionColor = Color.FromArgb(225, 225, 225);
                log.ScrollToCaret();
            }

            IProgress<string> progress = new Progress<string>(Append);
            SetDatabaseConnectionState(DatabaseConnectionState.Checking, "Comparing Skill DB...");

            try
            {
                var service = new SkillDatabaseDiagnosticService();
                SkillDatabaseDiagnosticSummary summary = await service.CompareAsync(
                    connection,
                    skillXml,
                    digimonListXml,
                    progress,
                    databaseImportCancellation.Token);

                page.Text = "Skill DB Compare [Success]";
                status.Text = $"READ-ONLY SUCCESS — XML {summary.XmlSkills:N0} | SkillInfo {summary.DatabaseSkillInfoRows:N0} | Apply {summary.DatabaseSkillCodeApplyRows:N0}";
                SetDatabaseConnectionState(DatabaseConnectionState.Connected, "Connected");

                MessageBox.Show(
                    "Diagnóstico concluído sem alterar a database.\r\n\r\n" +
                    $"Skill.xml únicos: {summary.XmlSkills:N0}\r\n" +
                    $"SkillInfo: {summary.DatabaseSkillInfoRows:N0}\r\n" +
                    $"SkillCode: {summary.DatabaseSkillCodeRows:N0}\r\n" +
                    $"SkillCodeApply: {summary.DatabaseSkillCodeApplyRows:N0}\r\n" +
                    $"DigimonSkill: {summary.DatabaseDigimonSkillRows:N0}\r\n" +
                    $"XML skills sem DB SkillInfo: {summary.MissingXmlSkillsInDatabase:N0}\r\n" +
                    $"DB SkillInfo sem XML: {summary.MissingDatabaseSkillsInXml:N0}\r\n\r\n" +
                    "Envia-me principalmente estes ficheiros:\r\n" +
                    "• HIGH_SIGNAL_REPORT.txt\r\n" +
                    "• SkillInfo_FieldMatchSummary.csv\r\n" +
                    "• SkillCodeApply_FieldMatchSummary.csv\r\n" +
                    "• DigimonSkill_Comparison.csv\r\n\r\n" +
                    $"Pasta:\r\n{summary.OutputFolder}",
                    "Skill DB Diagnostic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                page.Text = "Skill DB Compare [Cancelled]";
                status.Text = "READ-ONLY diagnostic cancelled.";
                Append("[CANCELLED] Diagnóstico cancelado. Nenhuma alteração foi feita na database.");
                SetDatabaseConnectionState(DatabaseConnectionState.Connected, "Connected");
            }
            catch (Exception ex)
            {
                page.Text = "Skill DB Compare [Failed]";
                status.Text = "DIAGNOSTIC FAILED — database was not modified.";
                Append("[ERROR] " + ex);
                SetDatabaseConnectionState(DatabaseConnectionState.Failed, "Diagnostic failed");
                MessageBox.Show(
                    "O diagnóstico falhou, mas a database NÃO foi alterada.\r\n\r\n" + ex.Message,
                    "Skill DB Diagnostic",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                cancel.Enabled = false;
            }
        }
    }
}
