using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private Panel databaseIndicator = null!;
        private Panel databaseIndicatorDot = null!;
        private Label databaseIndicatorText = null!;

        private TextBox txtDatabaseConnection = null!;
        private Label lblDatabaseSettingsStatus = null!;
        private Button btnDatabaseSaveTest = null!;
        private Button btnDatabaseClear = null!;
        private Button btnDatabaseShowHide = null!;

        private DatabaseConnectionState databaseConnectionState =
            DatabaseConnectionState.NotConfigured;

        private string databaseConnectionMessage =
            "Not configured";

        private CancellationTokenSource? databaseImportCancellation;

        private void InitializeDatabaseFeatures()
        {
            BuildDatabaseTopIndicator();
            BuildDatabaseSettingsCard();

            FormClosed +=
                (_, _) =>
                {
                    databaseImportCancellation?.Cancel();
                    databaseImportCancellation?.Dispose();
                };

            string stored =
                string.Empty;

            try
            {
                stored =
                    DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Encrypted settings error");

                lblDatabaseSettingsStatus.Text =
                    "Não foi possível desencriptar a connection string: " +
                    ex.Message;

                return;
            }

            txtDatabaseConnection.Text =
                stored;

            if (string.IsNullOrWhiteSpace(
                stored))
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.NotConfigured,
                    "Not configured");

                lblDatabaseSettingsStatus.Text =
                    "Connection string ainda não configurada.";

                return;
            }

            lblDatabaseSettingsStatus.Text =
                "Stored securely: " +
                DatabaseConnectionStore.GetSafeDescription(
                    stored);

            _ =
                TestStoredDatabaseConnectionAsync();
        }

        private void BuildDatabaseTopIndicator()
        {
            databaseIndicator =
                new Panel
                {
                    Size =
                        new Size(
                            154,
                            46),
                    BackColor =
                        Color.Transparent,
                    Cursor =
                        Cursors.Hand,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Right
                };

            databaseIndicatorDot =
                new Panel
                {
                    Size =
                        new Size(
                            10,
                            10),
                    Location =
                        new Point(
                            5,
                            18),
                    BackColor =
                        Color.FromArgb(
                            210,
                            70,
                            70)
                };

            databaseIndicatorText =
                new Label
                {
                    Text = "DATABASE",
                    ForeColor = CMuted,
                    BackColor =
                        Color.Transparent,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            7.8F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            22,
                            0),
                    Size =
                        new Size(
                            127,
                            46),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };

            databaseIndicator.Controls.Add(
                databaseIndicatorDot);

            databaseIndicator.Controls.Add(
                databaseIndicatorText);

            databaseIndicator.Click +=
                (_, _) =>
                    ShowSettings(true);

            databaseIndicatorText.Click +=
                (_, _) =>
                    ShowSettings(true);

            topBar.Controls.Add(
                databaseIndicator);

            topBar.Resize +=
                (_, _) =>
                    LayoutDatabaseTopIndicator();

            LayoutDatabaseTopIndicator();
            SetDatabaseConnectionState(
                DatabaseConnectionState.NotConfigured,
                "Not configured");
        }

        private void LayoutDatabaseTopIndicator()
        {
            if (databaseIndicator == null ||
                topBar == null)
            {
                return;
            }

            // Always center the DATABASE status against the full application
            // width. It is intentionally independent from SETTINGS / _ / X.
            int left =
                Math.Max(
                    0,
                    (topBar.ClientSize.Width -
                     databaseIndicator.Width) / 2);

            databaseIndicator.Location =
                new Point(
                    left,
                    0);

            databaseIndicator.BringToFront();
        }

        private void BuildDatabaseSettingsCard()
        {
            var card =
                new Panel
                {
                    Location =
                        new Point(
                            30,
                            410),
                    Size =
                        new Size(
                            720,
                            250),
                    BackColor = CPanel
                };

            card.Paint +=
                (_, e) =>
                {
                    using var p =
                        new Pen(CBorder);

                    e.Graphics.DrawRectangle(
                        p,
                        0,
                        0,
                        card.Width - 1,
                        card.Height - 1);
                };

            var title =
                new Label
                {
                    Text =
                        "SQL Server Database",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            13F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            22,
                            16),
                    Size =
                        new Size(
                            360,
                            30)
                };

            var description =
                new Label
                {
                    Text =
                        "Connection string usada pelo IMPORT TO DATABASE. " +
                        "É guardada cifrada com Windows DPAPI (CurrentUser).",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8.8F),
                    Location =
                        new Point(
                            24,
                            48),
                    Size =
                        new Size(
                            660,
                            38)
                };

            txtDatabaseConnection =
                new TextBox
                {
                    Location =
                        new Point(
                            24,
                            92),
                    Size =
                        new Size(
                            540,
                            30),
                    BackColor =
                        Color.FromArgb(
                            13,
                            13,
                            13),
                    ForeColor = CText,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Font =
                        new Font(
                            "Consolas",
                            8.5F),
                    UseSystemPasswordChar = true
                };

            btnDatabaseShowHide =
                CreateBottomButton(
                    "SHOW");

            btnDatabaseShowHide.Location =
                new Point(
                    574,
                    91);

            btnDatabaseShowHide.Size =
                new Size(
                    110,
                    32);

            btnDatabaseShowHide.Click +=
                (_, _) =>
                {
                    txtDatabaseConnection.UseSystemPasswordChar =
                        !txtDatabaseConnection.UseSystemPasswordChar;

                    btnDatabaseShowHide.Text =
                        txtDatabaseConnection.UseSystemPasswordChar
                            ? "SHOW"
                            : "HIDE";
                };

            btnDatabaseSaveTest =
                CreateBottomButton(
                    "SAVE + TEST");

            btnDatabaseSaveTest.Location =
                new Point(
                    24,
                    138);

            btnDatabaseSaveTest.Size =
                new Size(
                    150,
                    36);

            btnDatabaseSaveTest.Click +=
                async (_, _) =>
                    await SaveAndTestDatabaseConnectionAsync();

            btnDatabaseClear =
                CreateBottomButton(
                    "CLEAR");

            btnDatabaseClear.Location =
                new Point(
                    184,
                    138);

            btnDatabaseClear.Size =
                new Size(
                    110,
                    36);

            btnDatabaseClear.Click +=
                (_, _) =>
                {
                    DatabaseConnectionStore.Delete();

                    txtDatabaseConnection.Clear();

                    lblDatabaseSettingsStatus.Text =
                        "Stored connection removed.";

                    SetDatabaseConnectionState(
                        DatabaseConnectionState.NotConfigured,
                        "Not configured");
                };

            lblDatabaseSettingsStatus =
                new Label
                {
                    Text =
                        "Connection string ainda não configurada.",
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI",
                            8.5F),
                    Location =
                        new Point(
                            24,
                            184),
                    Size =
                        new Size(
                            660,
                            44),
                    AutoEllipsis = true
                };

            card.Controls.Add(title);
            card.Controls.Add(description);
            card.Controls.Add(txtDatabaseConnection);
            card.Controls.Add(btnDatabaseShowHide);
            card.Controls.Add(btnDatabaseSaveTest);
            card.Controls.Add(btnDatabaseClear);
            card.Controls.Add(lblDatabaseSettingsStatus);

            settingsPanel.Controls.Add(
                card);

            card.BringToFront();
        }

        private async Task SaveAndTestDatabaseConnectionAsync()
        {
            string connection =
                txtDatabaseConnection.Text.Trim();

            if (connection.Length == 0)
            {
                MessageBox.Show(
                    "Introduz primeiro a connection string.",
                    "Database Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            btnDatabaseSaveTest.Enabled = false;

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Testing...");

            lblDatabaseSettingsStatus.Text =
                "A testar ligação ao SQL Server...";

            try
            {
                var service =
                    new DatabaseImportService();

                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(20));

                await service.TestConnectionAsync(
                    connection,
                    cts.Token);

                DatabaseConnectionStore.Save(
                    connection);

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");

                lblDatabaseSettingsStatus.Text =
                    "Connected + encrypted storage OK: " +
                    DatabaseConnectionStore.GetSafeDescription(
                        connection);
            }
            catch (Exception ex)
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Connection failed");

                lblDatabaseSettingsStatus.Text =
                    ex.Message;

                MessageBox.Show(
                    "A ligação à database falhou.\r\n\r\n" +
                    ex.Message,
                    "Database Connection",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnDatabaseSaveTest.Enabled = true;
            }
        }

        private async Task TestStoredDatabaseConnectionAsync()
        {
            string connection;

            try
            {
                connection =
                    DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Decrypt failed");

                lblDatabaseSettingsStatus.Text =
                    ex.Message;

                return;
            }

            if (string.IsNullOrWhiteSpace(
                connection))
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.NotConfigured,
                    "Not configured");

                return;
            }

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Connecting...");

            try
            {
                var service =
                    new DatabaseImportService();

                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(15));

                await service.TestConnectionAsync(
                    connection,
                    cts.Token);

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");
            }
            catch (OperationCanceledException)
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.Checking,
                    "Slow / timeout");
            }
            catch
            {
                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Connection failed");
            }
        }

        private void SetDatabaseConnectionState(
            DatabaseConnectionState state,
            string message)
        {
            databaseConnectionState =
                state;

            databaseConnectionMessage =
                message;

            if (databaseIndicatorDot == null ||
                databaseIndicatorText == null)
            {
                return;
            }

            databaseIndicatorDot.BackColor =
                state switch
                {
                    DatabaseConnectionState.Connected =>
                        Color.FromArgb(
                            75,
                            205,
                            105),

                    DatabaseConnectionState.Checking =>
                        Color.FromArgb(
                            235,
                            170,
                            65),

                    DatabaseConnectionState.Failed =>
                        Color.FromArgb(
                            225,
                            75,
                            75),

                    _ =>
                        Color.FromArgb(
                            225,
                            75,
                            75)
                };

            databaseIndicatorText.Text =
                state switch
                {
                    DatabaseConnectionState.Connected =>
                        "DB CONNECTED",

                    DatabaseConnectionState.Checking =>
                        "DB CHECKING",

                    DatabaseConnectionState.Failed =>
                        "DB FAILED",

                    _ =>
                        "DB NOT SET"
                };

            databaseIndicatorText.ForeColor =
                state ==
                DatabaseConnectionState.Connected
                    ? Color.FromArgb(
                        180,
                        235,
                        190)
                    : CMuted;
        }

        private void AddDatabaseImportButtonIfSupported(
            Control header,
            string entity,
            string folder)
        {
            string folderName =
                Path.GetFileName(
                    folder.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar));

            bool isItemList =
                entity.Equals(
                    "ItemList",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "ItemList",
                    StringComparison.OrdinalIgnoreCase);

            bool isNpc =
                entity.Equals(
                    "Npc",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "Npc",
                    StringComparison.OrdinalIgnoreCase);

            bool isDigimonCore =
                entity.Equals(
                    "Digimon_List",
                    StringComparison.OrdinalIgnoreCase) ||
                entity.Equals(
                    "DigimonEvo",
                    StringComparison.OrdinalIgnoreCase) ||
                entity.Equals(
                    "Skill",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "Digimon_List",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "DigimonEvo",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "Skill",
                    StringComparison.OrdinalIgnoreCase);

            bool isMonsterCore =
                entity.Equals(
                    "Monster",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "Monster",
                    StringComparison.OrdinalIgnoreCase);

            bool isBuff =
                entity.Equals(
                    "Buff",
                    StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals(
                    "Buff",
                    StringComparison.OrdinalIgnoreCase);

            if (!isItemList &&
                !isNpc &&
                !isDigimonCore &&
                !isMonsterCore &&
                !isBuff)
            {
                return;
            }

            // Use a right-docked host so the button never disappears because
            // the entity header still has an initial width close to zero.
            var importHost =
                new Panel
                {
                    Name =
                        "DatabaseImportButtonHost",
                    Dock =
                        DockStyle.Right,
                    Width = 224,
                    BackColor =
                        Color.Transparent,
                    Padding =
                        new Padding(
                            10,
                            18,
                            18,
                            18)
                };

            var button =
                CreateEditorActionButton(
                    "IMPORT TO DATABASE");

            button.Name =
                isDigimonCore
                    ? "btnImportDigimonCoreToDatabase"
                    : isMonsterCore
                        ? "btnImportMonsterCoreToDatabase"
                        : isBuff
                            ? "btnImportBuffToDatabase"
                            : isNpc
                                ? "btnImportNpcToDatabase"
                                : "btnImportItemListToDatabase";

            button.Dock =
                DockStyle.Fill;

            button.Font =
                new Font(
                    "Segoe UI Semibold",
                    8.5F,
                    FontStyle.Bold);

            button.FlatAppearance.BorderColor =
                databaseConnectionState ==
                DatabaseConnectionState.Connected
                    ? Color.FromArgb(
                        72,
                        130,
                        82)
                    : Color.FromArgb(
                        72,
                        72,
                        72);

            if (isDigimonCore)
            {
                editorToolTip.SetToolTip(
                    button,
                    "Importer core partilhado por Digimon_List, DigimonEvo e Skill. " +
                    "Valida primeiro os três XMLs e depois importa pela ordem: " +
                    "Digimon_List.xml -> DigimonEvo.xml -> Skill.xml.");

                button.Click +=
                    async (_, _) =>
                        await OpenDigimonCoreDatabaseImportTabAndRunAsync();
            }
            else if (isMonsterCore)
            {
                editorToolTip.SetToolTip(
                    button,
                    "Importa Monster.xml + MonstersSkill.xml para " +
                    "MonsterBaseInfo, MonsterSkill e MonsterSkillInfo. " +
                    "Os dois XMLs são validados por completo antes de qualquer DELETE. " +
                    "A operação usa uma única transação SQL com ROLLBACK em caso de erro.");

                button.Click +=
                    async (_, _) =>
                        await OpenMonsterDatabaseImportTabAndRunAsync(
                            folder);
            }
            else if (isBuff)
            {
                editorToolTip.SetToolTip(
                    button,
                    "Importa Buff.xml para Asset.Buff. " +
                    "O XML é validado completamente antes de qualquer DELETE. " +
                    "A operação usa uma única transação SQL e executa ROLLBACK em caso de erro.");

                button.Click +=
                    async (_, _) =>
                        await OpenBuffDatabaseImportTabAndRunAsync(
                            folder);
            }
            else if (isNpc)
            {
                editorToolTip.SetToolTip(
                    button,
                    "Importa Npc.xml para as tabelas relacionadas: " +
                    "Npc, NpcItem, NpcPortal, NpcPortalsAmount, " +
                    "NpcPortals e NpcColiseum. " +
                    "A operação usa uma única transação SQL.");

                button.Click +=
                    async (_, _) =>
                    {
                        string npcXml =
                            Path.Combine(
                                folder,
                                "Npc.xml");

                        if (!File.Exists(
                            npcXml))
                        {
                            MessageBox.Show(
                                "Npc.xml não foi encontrado:\r\n\r\n" +
                                npcXml,
                                "NPC Database Import",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);

                            return;
                        }

                        await OpenNpcDatabaseImportTabAndRunAsync(
                            npcXml);
                    };
            }
            else
            {
                editorToolTip.SetToolTip(
                    button,
                    "Importa ItemList.xml + ItemAcessorys.xml + ItemMaking.xml para " +
                    "ItemInfo, AccessoryRoll, AccessoryRollStatus, ItemCraft e ItemCraftMaterial. " +
                    "Cada grupo de tabelas é limpo antes da respetiva importação.");

                button.Click +=
                    async (_, _) =>
                        await OpenDatabaseImportTabAndRunAsync(
                            folder);
            }

            importHost.Controls.Add(
                button);

            header.Controls.Add(
                importHost);

            importHost.BringToFront();
        }

        private async Task OpenBuffDatabaseImportTabAndRunAsync(
            string buffFolder)
        {
            string connection;

            try
            {
                connection =
                    DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível ler a connection string cifrada.\r\n\r\n" +
                    ex.Message,
                    "Buff Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(
                    "Configura primeiro a connection string em SETTINGS → SQL Server Database.",
                    "Buff Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                ShowSettings(true);
                return;
            }

            string buffXml =
                Path.Combine(
                    buffFolder,
                    "Buff.xml");

            if (!File.Exists(buffXml))
            {
                MessageBox.Show(
                    "Buff.xml não foi encontrado:\r\n\r\n" +
                    buffXml,
                    "Buff Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (MessageBox.Show(
                    "BUFF -> DATABASE\r\n\r\n" +
                    "A operação valida TODO o Buff.xml antes de tocar na database.\r\n" +
                    "Só depois será iniciada uma única transação SQL.\r\n\r\n" +
                    "Tabela substituída:\r\n" +
                    "• Asset.Buff\r\n\r\n" +
                    "Mapping principal:\r\n" +
                    "• BuffId = s_dwID\r\n" +
                    "• Name = s_szName\r\n" +
                    "• DigimonSkillCode = s_dwDigimonSkillCode\r\n" +
                    "• SkillCode = s_dwSkillCode\r\n" +
                    "• MinLevel = s_nMinLv\r\n" +
                    "• ConditionLevel = s_nConditionLv\r\n" +
                    "• Class = s_nBuffClass\r\n" +
                    "• Type = s_nBuffType\r\n" +
                    "• LifeType = s_nBuffLifeType\r\n" +
                    "• TimeType = s_nBuffTimeType\r\n\r\n" +
                    "Campos sem coluna equivalente (Comment/Icon/Effect/Delete/unknown) permanecem apenas no XML.\r\n\r\n" +
                    "Qualquer erro provoca ROLLBACK.\r\n\r\n" +
                    "Continuar?",
                    "Buff Database Importer",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var page =
                CreateDarkTab(
                    "Buff DB Import [Running]");

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    BackColor = CPanel
                };

            var status =
                new Label
                {
                    Text =
                        "VALIDATING — Buff.xml",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            14,
                            0),
                    Size =
                        new Size(
                            650,
                            72),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };

            var cancel =
                CreateEditorActionButton(
                    "CANCEL");

            cancel.Size =
                new Size(
                    96,
                    34);

            cancel.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            void LayoutHeader()
            {
                cancel.Location =
                    new Point(
                        header.ClientSize.Width -
                        cancel.Width -
                        14,
                        19);

                status.Width =
                    Math.Max(
                        260,
                        cancel.Left -
                        status.Left -
                        14);
            }

            header.Resize +=
                (_, _) =>
                    LayoutHeader();

            var log =
                new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle =
                        BorderStyle.None,
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor =
                        Color.FromArgb(
                            225,
                            225,
                            225),
                    Font =
                        new Font(
                            "Consolas",
                            8.5F),
                    WordWrap = false
                };

            DarkUi.ApplyDarkScrollBar(
                log);

            header.Controls.Add(
                status);

            header.Controls.Add(
                cancel);

            page.Controls.Add(
                log);

            page.Controls.Add(
                header);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            LayoutHeader();

            databaseImportCancellation?.Cancel();
            databaseImportCancellation?.Dispose();

            databaseImportCancellation =
                new CancellationTokenSource();

            cancel.Click +=
                (_, _) =>
                    databaseImportCancellation.Cancel();

            void AppendProgressLine(
                string line)
            {
                if (log.IsDisposed)
                    return;

                int start =
                    log.TextLength;

                log.AppendText(
                    line +
                    Environment.NewLine);

                log.Select(
                    start,
                    line.Length);

                if (line.Contains(
                        "WARNING",
                        StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            255,
                            190,
                            90);
                }
                else if (line.Contains(
                             "VERIFY OK",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "concluíd",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "SUCCESS",
                             StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            125,
                            220,
                            140);
                }
                else if (line.Contains(
                             "ERRO",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "FALH",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "ROLLBACK",
                             StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);
                }
                else
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            225,
                            225,
                            225);
                }

                log.SelectionStart =
                    log.TextLength;

                log.SelectionColor =
                    Color.FromArgb(
                        225,
                        225,
                        225);

                log.ScrollToCaret();
            }

            IProgress<string> progress =
                new Progress<string>(
                    AppendProgressLine);

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Importing...");

            try
            {
                var service =
                    new BuffDatabaseImportService();

                BuffDatabaseImportSummary summary =
                    await service.ImportAsync(
                        connection,
                        buffXml,
                        progress,
                        databaseImportCancellation.Token);

                page.Text =
                    "Buff DB Import [Success]";

                status.Text =
                    $"SUCCESS — Buff {summary.BuffRows:N0} rows";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");

                MessageBox.Show(
                    "Importação Buff concluída com sucesso.\r\n\r\n" +
                    $"Buff rows: {summary.BuffRows:N0}\r\n" +
                    $"BuffId duplicados preservados: {summary.DuplicateBuffIds:N0}\r\n" +
                    $"Tempo: {summary.Elapsed.TotalSeconds:N1}s\r\n\r\n" +
                    $"Log:\r\n{summary.LogFile}",
                    "Buff Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                page.Text =
                    "Buff DB Import [Cancelled]";

                status.Text =
                    "IMPORT CANCELLED — transaction rolled back.";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Checking,
                    "Cancelled");
            }
            catch (Exception ex)
            {
                page.Text =
                    "Buff DB Import [Failed]";

                status.Text =
                    "IMPORT FAILED — see log below.";

                AppendProgressLine(
                    "[ERROR] " +
                    ex);

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Import failed");

                MessageBox.Show(
                    "A importação Buff falhou.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nSe a transação SQL já tinha começado, foi executado ROLLBACK. " +
                    "Consulta o separador de log para os detalhes.",
                    "Buff Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                cancel.Enabled = false;
            }
        }

        private async Task OpenMonsterDatabaseImportTabAndRunAsync(
            string monsterFolder)
        {
            string connection;

            try
            {
                connection =
                    DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível ler a connection string cifrada.\r\n\r\n" +
                    ex.Message,
                    "Monster Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(
                    "Configura primeiro a connection string em SETTINGS → SQL Server Database.",
                    "Monster Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                ShowSettings(true);
                return;
            }

            string monsterXml =
                Path.Combine(
                    monsterFolder,
                    "Monster.xml");

            string monsterSkillXml =
                Path.Combine(
                    monsterFolder,
                    "MonstersSkill.xml");

            string[] required =
            {
                monsterXml,
                monsterSkillXml
            };

            string[] missing =
                required
                    .Where(x => !File.Exists(x))
                    .ToArray();

            if (missing.Length > 0)
            {
                MessageBox.Show(
                    "O Monster importer precisa dos dois XMLs:\r\n\r\n" +
                    string.Join("\r\n", required) +
                    "\r\n\r\nEm falta:\r\n" +
                    string.Join("\r\n", missing),
                    "Monster Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (MessageBox.Show(
                    "MONSTER -> DATABASE\r\n\r\n" +
                    "A operação vai validar PRIMEIRO Monster.xml e MonstersSkill.xml.\r\n" +
                    "Só depois da validação completa será iniciada UMA transação SQL.\r\n\r\n" +
                    "Tabelas substituídas:\r\n" +
                    "• Asset.MonsterBaseInfo\r\n" +
                    "• Asset.MonsterSkill\r\n" +
                    "• Asset.MonsterSkillInfo\r\n\r\n" +
                    "Se qualquer validação, INSERT ou verificação falhar, será executado ROLLBACK.\r\n\r\n" +
                    "Campos XML que não possuem coluna equivalente nestas três tabelas NÃO serão inventados nem gravados noutra coluna.\r\n\r\n" +
                    "Continuar?",
                    "Monster Database Importer",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var page =
                CreateDarkTab(
                    "Monster DB Import [Running]");

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    BackColor = CPanel
                };

            var status =
                new Label
                {
                    Text =
                        "VALIDATING — Monster.xml -> MonstersSkill.xml",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location = new Point(14, 0),
                    Size = new Size(650, 72),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };

            var cancel =
                CreateEditorActionButton(
                    "CANCEL");

            cancel.Size =
                new Size(
                    96,
                    34);

            cancel.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            void LayoutHeader()
            {
                cancel.Location =
                    new Point(
                        header.ClientSize.Width -
                        cancel.Width -
                        14,
                        19);

                status.Width =
                    Math.Max(
                        260,
                        cancel.Left -
                        status.Left -
                        14);
            }

            header.Resize +=
                (_, _) =>
                    LayoutHeader();

            var log =
                new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle =
                        BorderStyle.None,
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor =
                        Color.FromArgb(
                            225,
                            225,
                            225),
                    Font =
                        new Font(
                            "Consolas",
                            8.5F),
                    WordWrap = false
                };

            DarkUi.ApplyDarkScrollBar(
                log);

            header.Controls.Add(status);
            header.Controls.Add(cancel);

            page.Controls.Add(log);
            page.Controls.Add(header);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            LayoutHeader();

            databaseImportCancellation?.Cancel();
            databaseImportCancellation?.Dispose();

            databaseImportCancellation =
                new CancellationTokenSource();

            cancel.Click +=
                (_, _) =>
                    databaseImportCancellation.Cancel();

            void AppendProgressLine(string line)
            {
                if (log.IsDisposed)
                    return;

                int start =
                    log.TextLength;

                log.AppendText(
                    line +
                    Environment.NewLine);

                log.Select(
                    start,
                    line.Length);

                if (line.Contains(
                        "WARNING",
                        StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            255,
                            190,
                            90);
                }
                else if (line.Contains(
                             "SUCESSO",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "VERIFY OK",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "concluíd",
                             StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            125,
                            220,
                            140);
                }
                else if (line.Contains(
                             "ERRO",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "FALH",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "ROLLBACK",
                             StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);
                }
                else
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            225,
                            225,
                            225);
                }

                log.SelectionStart =
                    log.TextLength;

                log.SelectionColor =
                    Color.FromArgb(
                        225,
                        225,
                        225);

                log.ScrollToCaret();
            }

            IProgress<string> progress =
                new Progress<string>(
                    AppendProgressLine);

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Importing...");

            try
            {
                var service =
                    new MonsterDatabaseImportService();

                MonsterDatabaseImportSummary summary =
                    await service.ImportAsync(
                        connection,
                        monsterXml,
                        monsterSkillXml,
                        progress,
                        databaseImportCancellation.Token);

                page.Text =
                    "Monster DB Import [Success]";

                status.Text =
                    $"SUCCESS — MonsterBaseInfo {summary.MonsterBaseInfoRows:N0} | " +
                    $"MonsterSkill {summary.MonsterSkillRows:N0} | " +
                    $"MonsterSkillInfo {summary.MonsterSkillInfoRows:N0}";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");

                MessageBox.Show(
                    "Importação Monster concluída com sucesso.\r\n\r\n" +
                    $"MonsterBaseInfo: {summary.MonsterBaseInfoRows:N0}\r\n" +
                    $"MonsterSkill: {summary.MonsterSkillRows:N0}\r\n" +
                    $"MonsterSkillInfo: {summary.MonsterSkillInfoRows:N0}\r\n" +
                    $"Monster refs apenas no MonstersSkill.xml: {summary.MissingMonsterReferences:N0}\r\n\r\n" +
                    $"Tempo: {summary.Elapsed.TotalSeconds:N1}s\r\n\r\n" +
                    $"Log:\r\n{summary.LogFile}",
                    "Monster Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                page.Text =
                    "Monster DB Import [Cancelled]";

                status.Text =
                    "IMPORT CANCELLED — transaction rolled back.";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Checking,
                    "Cancelled");
            }
            catch (Exception ex)
            {
                page.Text =
                    "Monster DB Import [Failed]";

                status.Text =
                    "IMPORT FAILED — see log below.";

                AppendProgressLine(
                    "[ERROR] " +
                    ex);

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Import failed");

                MessageBox.Show(
                    "A importação Monster falhou.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nSe a transação SQL já tinha começado, foi executado ROLLBACK. " +
                    "Consulta o separador de log para os detalhes.",
                    "Monster Database Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                cancel.Enabled = false;
            }
        }

        private async Task OpenDatabaseImportTabAndRunAsync(
            string itemListFolder)
        {
            string connection;

            try
            {
                connection =
                    DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível ler a connection string cifrada.\r\n\r\n" +
                    ex.Message,
                    "Import To Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (string.IsNullOrWhiteSpace(
                connection))
            {
                MessageBox.Show(
                    "Configura primeiro a connection string em SETTINGS → SQL Server Database.",
                    "Import To Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                ShowSettings(true);

                return;
            }

            string itemListXml =
                Path.Combine(
                    itemListFolder,
                    "ItemList.xml");

            string accessoryXml =
                Path.Combine(
                    itemListFolder,
                    "ItemAcessorys.xml");

            string itemMakingXml =
                Path.Combine(
                    itemListFolder,
                    "ItemMaking.xml");

            if (!File.Exists(itemListXml) ||
                !File.Exists(accessoryXml) ||
                !File.Exists(itemMakingXml))
            {
                MessageBox.Show(
                    "Para importar o ItemList completo são necessários estes três ficheiros:\r\n\r\n" +
                    itemListXml + "\r\n" +
                    accessoryXml + "\r\n" +
                    itemMakingXml,
                    "Import To Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            var page =
                CreateDarkTab(
                    "Database Import [Running]");

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 58,
                    BackColor = CPanel
                };

            var status =
                new Label
                {
                    Text =
                        "IMPORT TO DATABASE — ItemInfo + AccessoryRoll + AccessoryRollStatus + ItemCraft + ItemCraftMaterial",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            14,
                            0),
                    Size =
                        new Size(
                            620,
                            58),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            var cancel =
                CreateEditorActionButton(
                    "CANCEL");

            cancel.Size =
                new Size(
                    90,
                    32);

            cancel.Location =
                new Point(
                    650,
                    13);

            cancel.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            var log =
                new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle =
                        BorderStyle.None,
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor =
                        Color.FromArgb(
                            225,
                            225,
                            225),
                    Font =
                        new Font(
                            "Consolas",
                            8.5F),
                    WordWrap = false
                };

            DarkUi.ApplyDarkScrollBar(
                log);

            header.Controls.Add(status);
            header.Controls.Add(cancel);

            page.Controls.Add(log);
            page.Controls.Add(header);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            databaseImportCancellation?.Cancel();
            databaseImportCancellation?.Dispose();

            databaseImportCancellation =
                new CancellationTokenSource();

            cancel.Click +=
                (_, _) =>
                    databaseImportCancellation.Cancel();

            IProgress<string> progress =
                new Progress<string>(
                    line =>
                    {
                        if (log.IsDisposed)
                            return;

                        log.AppendText(
                            line +
                            Environment.NewLine);

                        log.SelectionStart =
                            log.TextLength;

                        log.ScrollToCaret();
                    });

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Importing...");

            try
            {
                var service =
                    new DatabaseImportService();

                DatabaseImportSummary summary =
                    await service.ImportAllAsync(
                        connection,
                        itemListXml,
                        accessoryXml,
                        progress,
                        databaseImportCancellation.Token);

                progress.Report(
                    "[ItemList Import] ItemList + Accessory concluído. A iniciar ItemMaking...");

                var itemMakingService =
                    new NpcItemMakingDatabaseImportService();

                ItemMakingDatabaseImportSummary itemMakingSummary =
                    await itemMakingService.ImportItemMakingAsync(
                        connection,
                        itemMakingXml,
                        progress,
                        databaseImportCancellation.Token);

                page.Text =
                    "Database Import [Success]";

                status.Text =
                    $"SUCCESS — ItemInfo {summary.ItemInfoRows:N0} | " +
                    $"AccessoryRoll {summary.AccessoryRollRows:N0} | " +
                    $"Status {summary.AccessoryStatusRows:N0} | " +
                    $"Crafts {itemMakingSummary.CraftRows:N0} | " +
                    $"Materials {itemMakingSummary.MaterialRows:N0}";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");

                MessageBox.Show(
                    "Importação completa do ItemList concluída com sucesso.\r\n\r\n" +
                    $"ItemInfo: {summary.ItemInfoRows:N0}\r\n" +
                    $"AccessoryRoll: {summary.AccessoryRollRows:N0}\r\n" +
                    $"AccessoryRollStatus: {summary.AccessoryStatusRows:N0}\r\n" +
                    $"ItemCraft: {itemMakingSummary.CraftRows:N0}\r\n" +
                    $"ItemCraftMaterial: {itemMakingSummary.MaterialRows:N0}\r\n\r\n" +
                    $"Tempo ItemList/Accessory: {summary.Elapsed.TotalSeconds:N1}s\r\n" +
                    $"Tempo ItemMaking: {itemMakingSummary.Elapsed.TotalSeconds:N1}s\r\n\r\n" +
                    $"Log ItemList/Accessory:\r\n{summary.LogFile}\r\n\r\n" +
                    $"Log ItemMaking:\r\n{itemMakingSummary.LogFile}",
                    "Import To Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                page.Text =
                    "Database Import [Cancelled]";

                status.Text =
                    "IMPORT CANCELLED — transaction rolled back.";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Checking,
                    "Cancelled");
            }
            catch (Exception ex)
            {
                page.Text =
                    "Database Import [Failed]";

                status.Text =
                    "IMPORT FAILED — see log below.";

                log.AppendText(
                    Environment.NewLine +
                    "[ERROR] " +
                    ex +
                    Environment.NewLine);

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Import failed");

                MessageBox.Show(
                    "A importação falhou.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nA transação foi revertida; consulta o separador de logs.",
                    "Import To Database",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                cancel.Enabled = false;
            }
        }
        private async Task OpenDigimonCoreDatabaseImportTabAndRunAsync()
        {
            string connection;

            try
            {
                connection =
                    DatabaseConnectionStore.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Não foi possível ler a connection string cifrada.\r\n\r\n" +
                    ex.Message,
                    "Digimon Core Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(
                    "Configura primeiro a connection string em SETTINGS → SQL Server Database.",
                    "Digimon Core Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                ShowSettings(true);
                return;
            }

            string digimonListXml =
                Path.Combine(
                    AppPaths.Xml,
                    "Digimon_List",
                    "Digimon_List.xml");

            string digimonEvoXml =
                Path.Combine(
                    AppPaths.Xml,
                    "DigimonEvo",
                    "DigimonEvo.xml");

            string skillXml =
                Path.Combine(
                    AppPaths.Xml,
                    "Skill",
                    "Skill.xml");

            string[] required =
            {
                digimonListXml,
                digimonEvoXml,
                skillXml
            };

            string[] missing =
                required
                    .Where(x => !File.Exists(x))
                    .ToArray();

            if (missing.Length > 0)
            {
                MessageBox.Show(
                    "O importer core precisa dos três XMLs canónicos:\r\n\r\n" +
                    string.Join("\r\n", required) +
                    "\r\n\r\nEm falta:\r\n" +
                    string.Join("\r\n", missing),
                    "Digimon Core Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            if (MessageBox.Show(
                    "IMPORTER CORE DIGIMON\r\n\r\n" +
                    "A operação vai validar PRIMEIRO os três XMLs e só depois iniciar a transação SQL.\r\n\r\n" +
                    "Ordem:\r\n" +
                    "1. Digimon_List.xml\r\n" +
                    "2. DigimonEvo.xml\r\n" +
                    "3. Skill.xml\r\n\r\n" +
                    "As tabelas core correspondentes serão substituídas dentro de UMA transação. " +
                    "Se algo falhar, é executado ROLLBACK.\r\n\r\n" +
                    "EvolutionArmor será preservada porque os três XMLs não contêm ItemId/Chance/Amount equivalentes.\r\n\r\n" +
                    "Continuar?",
                    "Digimon Core Importer",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            var page =
                CreateDarkTab(
                    "Digimon Core Import [Running]");

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    BackColor = CPanel
                };

            var status =
                new Label
                {
                    Text =
                        "VALIDATING — Digimon_List.xml -> DigimonEvo.xml -> Skill.xml",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location = new Point(14, 0),
                    Size = new Size(650, 72),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };

            var cancel =
                CreateEditorActionButton(
                    "CANCEL");

            cancel.Size =
                new Size(
                    96,
                    34);

            cancel.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            void LayoutHeader()
            {
                cancel.Location =
                    new Point(
                        header.ClientSize.Width -
                        cancel.Width -
                        14,
                        19);

                status.Width =
                    Math.Max(
                        260,
                        cancel.Left -
                        status.Left -
                        14);
            }

            header.Resize +=
                (_, _) =>
                    LayoutHeader();

            var log =
                new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    BorderStyle =
                        BorderStyle.None,
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor =
                        Color.FromArgb(
                            225,
                            225,
                            225),
                    Font =
                        new Font(
                            "Consolas",
                            8.5F),
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

            databaseImportCancellation =
                new CancellationTokenSource();

            cancel.Click +=
                (_, _) =>
                    databaseImportCancellation.Cancel();

            void AppendProgressLine(string line)
            {
                if (log.IsDisposed)
                    return;

                int start =
                    log.TextLength;

                log.AppendText(
                    line +
                    Environment.NewLine);

                log.Select(
                    start,
                    line.Length);

                if (line.Contains(
                        "WARNING",
                        StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            255,
                            190,
                            90);
                }
                else if (line.Contains(
                             "SUCESSO",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "VERIFY OK",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "concluíd",
                             StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            125,
                            220,
                            140);
                }
                else if (line.Contains(
                             "ERRO",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "FALH",
                             StringComparison.OrdinalIgnoreCase) ||
                         line.Contains(
                             "ROLLBACK",
                             StringComparison.OrdinalIgnoreCase))
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            255,
                            95,
                            95);
                }
                else
                {
                    log.SelectionColor =
                        Color.FromArgb(
                            225,
                            225,
                            225);
                }

                log.SelectionStart =
                    log.TextLength;

                log.SelectionLength = 0;
                log.SelectionColor =
                    Color.FromArgb(
                        225,
                        225,
                        225);

                log.ScrollToCaret();

                if (line.Contains(
                        "FASE 1/3",
                        StringComparison.OrdinalIgnoreCase))
                {
                    status.Text =
                        "IMPORTING 1/3 — Digimon_List.xml -> DigimonBaseInfo";
                }
                else if (line.Contains(
                             "FASE 2/3",
                             StringComparison.OrdinalIgnoreCase))
                {
                    status.Text =
                        "IMPORTING 2/3 — DigimonEvo.xml -> Evolution tables";
                }
                else if (line.Contains(
                             "FASE 3/3",
                             StringComparison.OrdinalIgnoreCase))
                {
                    status.Text =
                        "IMPORTING 3/3 — Skill.xml -> Skill tables + DigimonSkill";
                }
            }

            IProgress<string> progress =
                new Progress<string>(
                    AppendProgressLine);

            SetDatabaseConnectionState(
                DatabaseConnectionState.Checking,
                "Importing Digimon core...");

            try
            {
                var service =
                    new DigimonCoreDatabaseImportService();

                DigimonCoreDatabaseImportSummary summary =
                    await service.ImportAsync(
                        connection,
                        digimonListXml,
                        digimonEvoXml,
                        skillXml,
                        progress,
                        databaseImportCancellation.Token);

                page.Text =
                    "Digimon Core Import [Success]";

                status.Text =
                    $"SUCCESS — Digimon {summary.DigimonBaseInfoRows:N0} | " +
                    $"Evolution {summary.EvolutionRows:N0}/{summary.EvolutionLineRows:N0}/{summary.EvolutionStageRows:N0} | " +
                    $"Skills {summary.SkillCodeRows:N0} | DigimonSkill {summary.DigimonSkillRows:N0}";

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Connected,
                    "Connected");

                MessageBox.Show(
                    "Importação core concluída com sucesso.\r\n\r\n" +
                    $"DigimonBaseInfo: {summary.DigimonBaseInfoRows:N0}\r\n" +
                    $"Evolution: {summary.EvolutionRows:N0}\r\n" +
                    $"EvolutionLine: {summary.EvolutionLineRows:N0}\r\n" +
                    $"EvolutionStage: {summary.EvolutionStageRows:N0}\r\n" +
                    $"SkillCode: {summary.SkillCodeRows:N0}\r\n" +
                    $"SkillCodeApply: {summary.SkillCodeApplyRows:N0}\r\n" +
                    $"SkillInfo: {summary.SkillInfoRows:N0}\r\n" +
                    $"DigimonSkill: {summary.DigimonSkillRows:N0}\r\n\r\n" +
                    $"Skill IDs duplicados colapsados: {summary.DuplicateSkillIdsCollapsed:N0}\r\n" +
                    $"Skill refs do DigimonList ausentes no Skill.xml: {summary.MissingSkillReferences:N0}\r\n" +
                    $"Associações partilhadas adicionais: {summary.SharedSkillAssociations:N0}\r\n\r\n" +
                    $"Tempo: {summary.Elapsed.TotalSeconds:N1}s\r\n\r\n" +
                    $"Log:\r\n{summary.LogFile}",
                    "Digimon Core Importer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                page.Text =
                    "Digimon Core Import [Cancelled]";

                status.Text =
                    "CANCELLED — validation/import stopped; transaction rolled back if it had started.";

                AppendProgressLine(
                    "[CANCELLED] Operação cancelada pelo utilizador.");

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Checking,
                    "Cancelled");
            }
            catch (Exception ex)
            {
                page.Text =
                    "Digimon Core Import [Failed]";

                status.Text =
                    "FAILED — see validation/import log.";

                AppendProgressLine(
                    "[ERROR] " +
                    ex);

                SetDatabaseConnectionState(
                    DatabaseConnectionState.Failed,
                    "Core import failed");

                MessageBox.Show(
                    "A importação core falhou.\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\nConsulta o separador de log. Se a transação SQL já tinha começado, foi executado ROLLBACK.",
                    "Digimon Core Importer",
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
