using System;
using DRW_Work_Tool.Core;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public partial class Form1 : Form
    {
        private Panel topBar = null!;
        private Panel leftPanel = null!;
        private Panel rightPanel = null!;
        private Label lblLogTitle = null!;
        private RichTextBox txtConverterLog = null!;
        private Button btnClearLog = null!;

        private Panel listViewport = null!;
        private Panel listContent = null!;
        private Panel scrollTrack = null!;
        private Panel scrollThumb = null!;

        private Panel modeBar = null!;
        private Button btnConverterMode = null!;
        private Button btnEditorMode = null!;

        private Panel bottomLeft = null!;
        private Button btnExportAll = null!;
        private Button btnPackAll = null!;

        private bool editorMode = false;
        private bool settingsMode = false;

        private Button btnSettings = null!;
        private Button btnMinimize = null!;
        private Button btnClose = null!;

        private Panel settingsPanel = null!;
        private Button btnSettingsBack = null!;
        private Button btnImageDatabase = null!;
        private Button btnSynchronizeImageDatabase = null!;
        private Button btnReajusteAnalyzeIcons = null!;
        private Label lblImageDatabaseStatus = null!;

        private bool dragging;
        private Point dragStartCursor;
        private Point dragStartForm;

        private bool thumbDragging;
        private int thumbDragOffset;
        private int scrollOffset;
        private readonly string[] binNames = BinCatalog.Names.ToArray();

        // Tema: branco + cinzentos + preto
        private static readonly Color CWindow = Color.FromArgb(18, 18, 18);
        private static readonly Color CTop = Color.FromArgb(12, 12, 12);
        private static readonly Color CPanel = Color.FromArgb(28, 28, 28);
        private static readonly Color CEditor = Color.FromArgb(22, 22, 22);
        private static readonly Color CHeader = Color.FromArgb(45, 45, 45);
        private static readonly Color CRow1 = Color.FromArgb(36, 36, 36);
        private static readonly Color CRow2 = Color.FromArgb(29, 29, 29);
        private static readonly Color CRowHover = Color.FromArgb(55, 55, 55);
        private static readonly Color CBorder = Color.FromArgb(72, 72, 72);
        private static readonly Color CText = Color.FromArgb(245, 245, 245);
        private static readonly Color CMuted = Color.FromArgb(185, 185, 185);

        private static readonly Color CScrollTrack = Color.FromArgb(24, 24, 24);
        private static readonly Color CScrollThumb = Color.FromArgb(90, 90, 90);
        private static readonly Color CScrollHover = Color.FromArgb(120, 120, 120);

        public Form1()
        {
            InitializeComponent();
            BuildUi();

            // Applies to every current and future AutoScroll control.
            // Gives the final card/row safe space below it at maximum scroll.
            DarkUi.InstallGlobalScrollPolicy(
                this,
                endSpacing: 36);

            AppPaths.EnsureWorkspace();

            InitializeDatabaseFeatures();

            AppLogger.EntryLogged += AppLogger_EntryLogged;

            LoadExistingLog();
            AppLogger.Log("Interface pronta.");

            // Normal startup now preloads everything in LoadingForm before
            // Form1 becomes accessible. Keep this fallback only for cases where
            // Form1 is launched directly by a designer/test harness.
            if (!EditorPreloadService.IsCompleted)
            {
                _ =
                    PreloadEditorDataAsync();
            }
            else if (EditorPreloadService.LastError == null)
            {
                AppLogger.Success(
                    "Editor preload já concluído durante o arranque.");
            }
            else
            {
                AppLogger.Warning(
                    "O preload de arranque terminou com aviso: " +
                    EditorPreloadService.LastError.Message);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            AppLogger.EntryLogged -= AppLogger_EntryLogged;
            DisposeEditorResources();
            ImageDatabasePreview.ClearCache();
            base.OnFormClosed(e);
        }

        private async Task PreloadEditorDataAsync()
        {
            try
            {
                AppLogger.Log(
                    "Editor preload: a carregar XMLs, referências e índice de previews em background...");

                await EditorPreloadService.StartAsync();

                if (EditorPreloadService.IsReady)
                {
                    AppLogger.Success(
                        "Editor preload concluído: referências principais do editor e ImageDatabase prontas em memória.");
                }
                else
                {
                    AppLogger.Log(
                        "Editor preload concluído. ItemList.xml ainda não existe; será carregado quando for aberto.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    "Editor preload não pôde ser concluído: " +
                    ex.Message +
                    ". O editor continuará a carregar os dados normalmente quando necessário.");
            }
        }

        private void BuildUi()
        {
            Controls.Clear();

            SuspendLayout();

            Text = "Digimon Reboot World Work Tool";
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(1000, 650);
            BackColor = CWindow;
            ForeColor = CText;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;

            BuildTopBar();
            BuildLeftPanel();
            BuildRightPanel();
            BuildSettingsPanel();

            Controls.Add(settingsPanel);
            Controls.Add(rightPanel);
            Controls.Add(leftPanel);
            Controls.Add(topBar);

            Resize += (_, _) => LayoutUi();
            LayoutUi();

            ResumeLayout(false);
        }

        private void BuildTopBar()
        {
            topBar = new Panel
            {
                BackColor = CTop,
                Height = 46,
                Dock = DockStyle.Top
            };

            topBar.MouseDown += Drag_MouseDown;
            topBar.MouseMove += Drag_MouseMove;
            topBar.MouseUp += Drag_MouseUp;

            var title = new Label
            {
                Text = "Digimon Reboot World Work Tool",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
                Location = new Point(18, 0),
                Size = new Size(410, 46),
                TextAlign = ContentAlignment.MiddleLeft
            };

            title.MouseDown += Drag_MouseDown;
            title.MouseMove += Drag_MouseMove;
            title.MouseUp += Drag_MouseUp;

            btnSettings = CreateTopTextButton("SETTINGS");
            btnSettings.Click += (_, _) => ShowSettings(true);

            btnMinimize = CreateWindowButton("_");
            btnMinimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

            btnClose = CreateWindowButton("X");
            btnClose.Click += (_, _) => Close();

            topBar.Controls.Add(title);
            topBar.Controls.Add(btnSettings);
            topBar.Controls.Add(btnMinimize);
            topBar.Controls.Add(btnClose);
        }

        private void BuildLeftPanel()
        {
            leftPanel = new Panel
            {
                BackColor = CPanel
            };

            var title = new Label
            {
                Text = "BIN / DAT / XML",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(12, 44),
                Size = new Size(320, 40),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var header = new Panel
            {
                Location = new Point(8, 84),
                Size = new Size(344, 34),
                BackColor = CHeader
            };

            header.Paint += (_, e) =>
            {
                using var p = new Pen(CBorder);
                e.Graphics.DrawRectangle(p, 0, 0, header.Width - 1, header.Height - 1);
            };

            header.Controls.Add(CreateHeaderLabel("Entity Type", 10, 0, 150, 34, ContentAlignment.MiddleLeft));
            header.Controls.Add(CreateHeaderLabel("XML", 164, 0, 82, 34, ContentAlignment.MiddleCenter));
            header.Controls.Add(CreateHeaderLabel("BIN / DAT", 246, 0, 82, 34, ContentAlignment.MiddleCenter));

            modeBar = new Panel
            {
                Location = new Point(8, 0),
                Width = 344,
                Height = 44,
                BackColor = CPanel
            };

            btnConverterMode = CreateModeButton("CONVERTER");
            btnConverterMode.Location = new Point(0, 5);
            btnConverterMode.Size = new Size(168, 34);
            btnConverterMode.Click += (_, _) => SetMode(false);

            btnEditorMode = CreateModeButton("EDITOR");
            btnEditorMode.Location = new Point(176, 5);
            btnEditorMode.Size = new Size(168, 34);
            btnEditorMode.Click += (_, _) => SetMode(true);

            modeBar.Controls.Add(btnConverterMode);
            modeBar.Controls.Add(btnEditorMode);

            listViewport = new Panel
            {
                Location = new Point(8, 118),
                Width = 344,
                BackColor = CPanel
            };

            listViewport.MouseWheel += ListViewport_MouseWheel;

            listContent = new Panel
            {
                Location = new Point(0, 0),
                Width = 330,
                BackColor = CPanel
            };

            int rowHeight = 34;
            for (int i = 0; i < binNames.Length; i++)
            {
                var row = CreateBinRow(binNames[i], i);
                row.Location = new Point(0, i * rowHeight);
                listContent.Controls.Add(row);
            }
            listContent.Height = binNames.Length * rowHeight;

            scrollTrack = new Panel
            {
                Width = 10,
                BackColor = CScrollTrack,
                Cursor = Cursors.Hand
            };

            scrollTrack.MouseDown += ScrollTrack_MouseDown;

            scrollThumb = new Panel
            {
                Width = 6,
                BackColor = CScrollThumb,
                Cursor = Cursors.Hand
            };

            scrollThumb.MouseEnter += (_, _) => scrollThumb.BackColor = CScrollHover;
            scrollThumb.MouseLeave += (_, _) =>
            {
                if (!thumbDragging)
                    scrollThumb.BackColor = CScrollThumb;
            };
            scrollThumb.MouseDown += ScrollThumb_MouseDown;
            scrollThumb.MouseMove += ScrollThumb_MouseMove;
            scrollThumb.MouseUp += ScrollThumb_MouseUp;

            scrollTrack.Controls.Add(scrollThumb);

            listViewport.Controls.Add(listContent);
            listViewport.Controls.Add(scrollTrack);

            bottomLeft = new Panel
            {
                BackColor = CPanel
            };

            btnExportAll = CreateBottomButton("CONVERT ALL TO XML");
            btnPackAll = CreateBottomButton("CONVERT ALL TO BIN / DAT");

            btnExportAll.Click += (_, _) => ConverterManager.ConvertAllBinToXml();
            btnPackAll.Click += (_, _) => ConverterManager.ConvertAllXmlToBin();

            bottomLeft.Controls.Add(btnExportAll);
            bottomLeft.Controls.Add(btnPackAll);

            leftPanel.Controls.Add(title);
            leftPanel.Controls.Add(header);
            leftPanel.Controls.Add(modeBar);
            leftPanel.Controls.Add(listViewport);
            leftPanel.Controls.Add(bottomLeft);

            SetMode(false);
        }

        private void BuildRightPanel()
        {
            rightPanel = new Panel
            {
                BackColor = CEditor
            };

            lblLogTitle = new Label
            {
                Text = "CONVERTER LOG",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Location = new Point(16, 12),
                Size = new Size(300, 26),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnClearLog = new Button
            {
                Text = "LIMPAR LOG",
                Size = new Size(110, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            btnClearLog.FlatAppearance.BorderColor = CBorder;
            btnClearLog.FlatAppearance.BorderSize = 1;
            btnClearLog.FlatAppearance.MouseOverBackColor = CHeader;
            btnClearLog.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 60, 60);
            btnClearLog.Click += btnClearLog_Click;

            txtConverterLog = new RichTextBox
            {
                Location = new Point(16, 46),
                Multiline = true,
                ReadOnly = true,
                DetectUrls = false,

                WordWrap = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(16, 16, 16),
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Consolas", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            rightPanel.Controls.Add(lblLogTitle);
            rightPanel.Controls.Add(btnClearLog);
            rightPanel.Controls.Add(txtConverterLog);

            BuildEditorWorkspace();

            rightPanel.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(38, 38, 38));
                e.Graphics.DrawRectangle(p, 0, 0, rightPanel.Width - 1, rightPanel.Height - 1);
            };
        }


        private void BuildSettingsPanel()
        {
            settingsPanel = new Panel
            {
                BackColor = CEditor,
                Visible = false
            };

            var title = new Label
            {
                Text = "SETTINGS",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
                Location = new Point(28, 22),
                Size = new Size(420, 42),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var subtitle = new Label
            {
                Text = "Ferramentas adicionais do Work Tool.",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 10F),
                Location = new Point(31, 65),
                Size = new Size(620, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnSettingsBack = CreateModeButton("VOLTAR");
            btnSettingsBack.Size = new Size(110, 34);
            btnSettingsBack.Click += (_, _) => ShowSettings(false);

            var card = new Panel
            {
                Location = new Point(30, 120),
                Size = new Size(720, 272),
                BackColor = CPanel
            };

            card.Paint += (_, e) =>
            {
                using var p = new Pen(CBorder);
                e.Graphics.DrawRectangle(
                    p,
                    0,
                    0,
                    card.Width - 1,
                    card.Height - 1);
            };

            var imageDbTitle = new Label
            {
                Text = "ImageDatabase",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Location = new Point(22, 18),
                Size = new Size(300, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var imageDbDescription = new Label
            {
                Text =
                    "Seleciona a pasta do client. O Work Tool procura os icons em " +
                    @"data\interface\icon, data\digimon, data\tamer e data\npc. " +
                    @"Para NPC procura ficheiros que terminem em l.tga e cria a database em ImgDatabase.",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(24, 54),
                Size = new Size(660, 48),
                TextAlign = ContentAlignment.TopLeft
            };

            btnImageDatabase = CreateBottomButton("IMAGE DATABASE");
            btnImageDatabase.Location = new Point(24, 116);
            btnImageDatabase.Size = new Size(180, 38);
            btnImageDatabase.Click += ImageDatabase_Click;

            btnSynchronizeImageDatabase =
                CreateBottomButton("SYNCHRONIZE DATABASE");
            btnSynchronizeImageDatabase.Location = new Point(216, 116);
            btnSynchronizeImageDatabase.Size = new Size(205, 38);
            btnSynchronizeImageDatabase.Click +=
                SynchronizeImageDatabase_Click;

            btnReajusteAnalyzeIcons =
                CreateBottomButton("REAJUSTE / ANALYZE ICON MAP");
            btnReajusteAnalyzeIcons.Location = new Point(432, 116);
            btnReajusteAnalyzeIcons.Size = new Size(252, 38);
            btnReajusteAnalyzeIcons.Click +=
                ReajusteAnalyzeIcons_Click;

            lblImageDatabaseStatus = new Label
            {
                Text =
                    "IMAGE DATABASE copia do client. " +
                    "SYNCHRONIZE apenas verifica/reindexa os ficheiros já existentes.",
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(24, 200),
                Size = new Size(660, 48),
                TextAlign = ContentAlignment.MiddleLeft
            };

            card.Controls.Add(imageDbTitle);
            card.Controls.Add(imageDbDescription);
            card.Controls.Add(btnImageDatabase);
            card.Controls.Add(btnSynchronizeImageDatabase);
            card.Controls.Add(btnReajusteAnalyzeIcons);
            card.Controls.Add(lblImageDatabaseStatus);

            settingsPanel.Controls.Add(title);
            settingsPanel.Controls.Add(subtitle);
            settingsPanel.Controls.Add(btnSettingsBack);
            settingsPanel.Controls.Add(card);
        }

        private void ShowSettings(bool show)
        {
            settingsMode = show;

            leftPanel.Visible = !show;
            rightPanel.Visible = !show;
            settingsPanel.Visible = show;

            btnSettings.BackColor =
                show
                    ? Color.FromArgb(70, 70, 70)
                    : Color.Transparent;

            LayoutUi();
        }


        private async void SynchronizeImageDatabase_Click(
            object? sender,
            EventArgs e)
        {
            btnImageDatabase.Enabled = false;
            btnSynchronizeImageDatabase.Enabled = false;
            btnReajusteAnalyzeIcons.Enabled = false;
            btnSettingsBack.Enabled = false;

            lblImageDatabaseStatus.Text =
                "A sincronizar a ImgDatabase e a recalcular dimensões...";

            AppLogger.Log(
                "ImageDatabase: SYNCHRONIZE iniciado.");

            try
            {
                var progress = new Progress<string>(
                    message =>
                    {
                        if (!IsDisposed &&
                            lblImageDatabaseStatus != null)
                        {
                            lblImageDatabaseStatus.Text = message;
                        }
                    });

                ImageDatabaseSyncResult result =
                    await Task.Run(
                        () => ImageDatabaseIndexBuilder.Synchronize(
                            null,
                            progress));

                lblImageDatabaseStatus.Text =
                    $"Sincronizado: {result.InterfaceAtlases:N0} atlas, " +
                    $"{result.TotalDirectIcons:N0} icons diretos.";

                string warning =
                    result.InvalidDirectIconDimensions > 0
                        ? "\n\nATENÇÃO: " +
                          $"{result.InvalidDirectIconDimensions:N0} Digimon/Tamer icon(s) " +
                          "não possuem dimensão 32x32."
                        : "\n\nTodos os icons diretos de Digimon/Tamer encontrados " +
                          "têm dimensão 32x32.";

                AppLogger.Success(
                    $"ImageDatabase sincronizada. " +
                    $"Folders={result.FoldersScanned:N0}, " +
                    $"Ficheiros de imagem={result.FilesScanned:N0}, " +
                    $"Atlases={result.InterfaceAtlases:N0}, " +
                    $"SkillAtlases={result.SkillAtlases:N0}, " +
                    $"Variantes={result.AtlasVariants:N0}, " +
                    $"Digimon={result.DigimonIcons:N0}, " +
                    $"Tamer={result.TamerIcons:N0}, " +
                    $"NPC={result.NpcIcons:N0}, " +
                    $"Dimensões Digimon/Tamer inválidas={result.InvalidDirectIconDimensions:N0}.");

                MessageBox.Show(
                    "ImageDatabase sincronizada com sucesso.\n\n" +
                    $"Folders verificadas: {result.FoldersScanned:N0}\n" +
                    $"Ficheiros de imagem: {result.FilesScanned:N0}\n" +
                    $"Atlases Interface: {result.InterfaceAtlases:N0}\n" +
                    $"Skill atlases (sicon): {result.SkillAtlases:N0}\n" +
                    $"Variantes BMP/TGA/DDS: {result.AtlasVariants:N0}\n" +
                    $"Digimon icons: {result.DigimonIcons:N0}\n" +
                    $"Tamer icons: {result.TamerIcons:N0}\n" +
                    $"NPC portraits/icons: {result.NpcIcons:N0}" +
                    warning +
                    "\n\nO ImageDatabase.json foi atualizado.",
                    "Synchronize ImageDatabase",
                    MessageBoxButtons.OK,
                    result.InvalidDirectIconDimensions > 0
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                lblImageDatabaseStatus.Text =
                    "Synchronize falhou. Consulta o log.";

                AppLogger.ErrorDetailed(
                    "ImageDatabase Synchronize",
                    ex.Message,
                    "Executa primeiro IMAGE DATABASE ou confirma que a pasta " +
                    "ImgDatabase existe ao lado do executável.");

                MessageBox.Show(
                    "Não foi possível sincronizar a ImageDatabase.\n\n" +
                    ex.Message,
                    "Synchronize ImageDatabase - Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnImageDatabase.Enabled = true;
                btnSynchronizeImageDatabase.Enabled = true;
                btnReajusteAnalyzeIcons.Enabled = true;
                btnSettingsBack.Enabled = true;
            }
        }

        private async void ReajusteAnalyzeIcons_Click(
            object? sender,
            EventArgs e)
        {
            btnImageDatabase.Enabled = false;
            btnSynchronizeImageDatabase.Enabled = false;
            btnReajusteAnalyzeIcons.Enabled = false;
            btnSettingsBack.Enabled = false;

            lblImageDatabaseStatus.Text =
                "A gerar e a analisar o InterfaceIconMap...";

            AppLogger.Log(
                "ImageDatabase: REAJUSTE / ANALYZE ICON MAP iniciado.");

            try
            {
                var progress = new Progress<string>(
                    message =>
                    {
                        if (!IsDisposed &&
                            lblImageDatabaseStatus != null)
                        {
                            lblImageDatabaseStatus.Text = message;
                        }
                    });

                InterfaceIconMapBuildResult result =
                    await Task.Run(
                        () => InterfaceIconMapBuilder.BuildAndAnalyze(
                            null,
                            progress));

                lblImageDatabaseStatus.Text =
                    $"Mapeado: {result.TotalMappedIcons:N0} icons em {result.MappedAtlases:N0} atlas.";

                string warning =
                    result.WarningsCount > 0
                        ? $"\n\nATENÇÃO: {result.WarningsCount:N0} aviso(s). Consulta o ficheiro de análise:\n{result.AnalysisPath}"
                        : "\n\nSem avisos. Todas as regras conhecidas bateram certo.";

                AppLogger.Success(
                    $"InterfaceIconMap concluído. " +
                    $"Atlases mapeados={result.MappedAtlases:N0}, " +
                    $"Atlases sem regra={result.UnmappedAtlases:N0}, " +
                    $"Total icons={result.TotalMappedIcons:N0}, " +
                    $"Warnings={result.WarningsCount:N0}. " +
                    $"Map='{result.MapPath}', Analysis='{result.AnalysisPath}'.");

                MessageBox.Show(
                    "Reajuste / análise do mapa de icons concluído.\n\n" +
                    $"Atlases mapeados: {result.MappedAtlases:N0}\n" +
                    $"Atlases sem regra: {result.UnmappedAtlases:N0}\n" +
                    $"Icons mapeados: {result.TotalMappedIcons:N0}\n" +
                    $"Warnings: {result.WarningsCount:N0}\n\n" +
                    $"Map JSON:\n{result.MapPath}\n\n" +
                    $"Analysis TXT:\n{result.AnalysisPath}" +
                    warning,
                    "Reajuste / Analyze Icon Map",
                    MessageBoxButtons.OK,
                    result.WarningsCount > 0
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                lblImageDatabaseStatus.Text =
                    "Reajuste / análise falhou. Consulta o log.";

                AppLogger.ErrorDetailed(
                    "InterfaceIconMap",
                    ex.Message,
                    "Executa primeiro IMAGE DATABASE e depois SYNCHRONIZE DATABASE para garantir que a ImgDatabase e o ImageDatabase.json existem.");

                MessageBox.Show(
                    "Não foi possível gerar/analisar o mapa de icons.\n\n" +
                    ex.Message,
                    "Reajuste / Analyze Icon Map - Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnImageDatabase.Enabled = true;
                btnSynchronizeImageDatabase.Enabled = true;
                btnReajusteAnalyzeIcons.Enabled = true;
                btnSettingsBack.Enabled = true;
            }
        }

        private async void ImageDatabase_Click(object? sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description =
                    "Seleciona a pasta root do client DMO (a pasta que contém Data/data) " +
                    "ou seleciona diretamente a própria pasta Data.",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            string selectedFolder = dialog.SelectedPath;

            btnImageDatabase.Enabled = false;
            btnSynchronizeImageDatabase.Enabled = false;
            btnReajusteAnalyzeIcons.Enabled = false;
            btnSettingsBack.Enabled = false;
            lblImageDatabaseStatus.Text = "A verificar folders e a construir ImgDatabase...";

            AppLogger.Log(
                $"ImageDatabase: pesquisa iniciada em '{selectedFolder}'.");

            try
            {
                var progress = new Progress<string>(
                    message =>
                    {
                        if (!IsDisposed && lblImageDatabaseStatus != null)
                            lblImageDatabaseStatus.Text = message;
                    });

                ImageDatabaseBuildResult result =
                    await Task.Run(
                        () => ImageDatabaseBuilder.Build(
                            selectedFolder,
                            progress));

                lblImageDatabaseStatus.Text =
                    $"Concluído: {result.TotalFilesCopied:N0} imagens copiadas.";

                AppLogger.Success(
                    $"ImageDatabase concluída. " +
                    $"Folders verificadas={result.FoldersScanned:N0}, " +
                    $"Interface={result.InterfaceIconsCopied:N0}, " +
                    $"Digimon={result.DigimonIconsCopied:N0}, " +
                    $"Tamer={result.TamerIconsCopied:N0}, " +
                    $"NPC={result.NpcIconsCopied:N0}, " +
                    $"Total={result.TotalFilesCopied:N0}. " +
                    $"Destino='{result.DatabaseRoot}'.");

                MessageBox.Show(
                    "ImageDatabase concluída com sucesso.\n\n" +
                    $"Folders verificadas: {result.FoldersScanned:N0}\n" +
                    $"Interface icons: {result.InterfaceIconsCopied:N0}\n" +
                    $"Digimon icons: {result.DigimonIconsCopied:N0}\n" +
                    $"Tamer icons: {result.TamerIconsCopied:N0}\n" +
                    $"NPC portraits/icons: {result.NpcIconsCopied:N0}\n" +
                    $"Total na operação: {result.TotalFilesCopied:N0}\n\n" +
                    $"Database:\n{result.DatabaseRoot}",
                    "ImageDatabase",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                lblImageDatabaseStatus.Text = "Falhou. Consulta o motivo no log.";

                AppLogger.ErrorDetailed(
                    "ImageDatabase",
                    ex.Message,
                    "Confirma que selecionaste a root do client ou a pasta Data e que " +
                    @"existe data\interface\icon, data\digimon, data\tamer ou data\npc.");

                MessageBox.Show(
                    "Não foi possível construir a ImageDatabase.\n\n" +
                    ex.Message,
                    "ImageDatabase - Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                btnImageDatabase.Enabled = true;
                btnSynchronizeImageDatabase.Enabled = true;
                btnReajusteAnalyzeIcons.Enabled = true;
                btnSettingsBack.Enabled = true;
            }
        }

        private Label CreateHeaderLabel(string text, int x, int y, int w, int h, ContentAlignment align)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                TextAlign = align,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };
        }

        private Panel CreateBinRow(string name, int index)
        {
            var baseColor = index % 2 == 0 ? CRow1 : CRow2;

            var row = new Panel
            {
                Size = new Size(330, 34),
                BackColor = baseColor
            };

            var label = new Label
            {
                Text = name,
                Location = new Point(10, 0),
                Size = new Size(150, 34),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold)
            };

            var export = CreateRowButton("EXPORT", name);
            export.Location = new Point(164, 2);
            export.Click += Export_Click;

            var pack = CreateRowButton(name == "Model" ? "PACK DAT" : "PACK", name);
            pack.Location = new Point(246, 2);
            pack.Click += Pack_Click;

            void enter(object? s, EventArgs e) => row.BackColor = CRowHover;
            void leave(object? s, EventArgs e) => row.BackColor = baseColor;

            row.MouseEnter += enter;
            row.MouseLeave += leave;
            label.MouseEnter += enter;
            label.MouseLeave += leave;

            row.Controls.Add(label);
            row.Controls.Add(export);
            row.Controls.Add(pack);

            return row;
        }

        private Button CreateRowButton(string text, string entity)
        {
            var button = new Button
            {
                Text = text,
                Tag = entity,
                Size = new Size(78, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = CRowHover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(72, 72, 72);

            return button;
        }

        private Button CreateModeButton(string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderColor = CBorder;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = CHeader;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 60, 60);

            return button;
        }

        private void SetMode(bool useEditor)
        {
            editorMode = useEditor;

            btnConverterMode.BackColor = editorMode ? Color.FromArgb(24, 24, 24) : Color.FromArgb(70, 70, 70);
            btnEditorMode.BackColor = editorMode ? Color.FromArgb(70, 70, 70) : Color.FromArgb(24, 24, 24);

            btnConverterMode.ForeColor = CText;
            btnEditorMode.ForeColor = CText;

            foreach (Control control in listContent.Controls)
            {
                if (control is not Panel row)
                    continue;

                foreach (Control child in row.Controls)
                {
                    if (child is not Button button)
                        continue;

                    if (editorMode)
                    {
                        button.Text = "EDIT";
                        button.Visible = button.Left < 200;
                    }
                    else
                    {
                        button.Visible = true;

                        if (button.Left < 200)
                            button.Text = "EXPORT";
                        else
                            button.Text = (button.Tag as string) == "Model" ? "PACK DAT" : "PACK";
                    }
                }
            }

            btnExportAll.Visible = !editorMode;
            btnPackAll.Visible = !editorMode;

            if (lblLogTitle != null)
                lblLogTitle.Visible = !editorMode;

            if (txtConverterLog != null)
                txtConverterLog.Visible = !editorMode;

            if (btnClearLog != null)
                btnClearLog.Visible = !editorMode;

            SetEditorWorkspaceVisible(editorMode);
        }

        private Button CreateBottomButton(string text)
        {
            var button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderColor = CBorder;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = CHeader;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 60, 60);

            return button;
        }


        private Button CreateTopTextButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(98, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = CHeader;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 60, 60);

            return button;
        }

        private Button CreateWindowButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(46, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                TabStop = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = CHeader;

            return button;
        }

        private void LayoutUi()
        {
            if (leftPanel == null || rightPanel == null || settingsPanel == null)
                return;

            const int margin = 8;
            const int leftWidth = 360;

            int top = topBar.Height + margin;
            int height = ClientSize.Height - top - margin;

            settingsPanel.Location = new Point(margin, top);
            settingsPanel.Size = new Size(
                Math.Max(0, ClientSize.Width - (margin * 2)),
                Math.Max(0, height));

            if (btnSettingsBack != null)
            {
                btnSettingsBack.Location = new Point(
                    Math.Max(30, settingsPanel.Width - btnSettingsBack.Width - 30),
                    28);
            }

            leftPanel.Location = new Point(margin, top);
            leftPanel.Size = new Size(leftWidth, height);

            rightPanel.Location = new Point(leftPanel.Right + margin, top);
            rightPanel.Size = new Size(
                Math.Max(0, ClientSize.Width - rightPanel.Left - margin),
                height);

            if (txtConverterLog != null)
            {
                txtConverterLog.Size = new Size(
                    Math.Max(100, rightPanel.Width - 32),
                    Math.Max(100, rightPanel.Height - 62));
            }

            if (btnClearLog != null)
            {
                btnClearLog.Location = new Point(
                    rightPanel.Width - btnClearLog.Width - 16,
                    10);
            }

            LayoutEditorWorkspace();

            modeBar.Width = leftPanel.Width - 16;
            btnConverterMode.Width = (modeBar.Width - 8) / 2;
            btnEditorMode.Left = btnConverterMode.Width + 8;
            btnEditorMode.Width = btnConverterMode.Width;

            listViewport.Top = 118;
            listViewport.Height = Math.Max(100, height - 118 - 66);

            scrollTrack.Location = new Point(listViewport.Width - 10, 0);
            scrollTrack.Height = listViewport.Height;

            listContent.Width = listViewport.Width - 14;

            bottomLeft.Location = new Point(8, height - 58);
            bottomLeft.Size = new Size(leftWidth - 16, 50);

            int gap = 8;
            int w = (bottomLeft.Width - gap) / 2;

            btnExportAll.Location = new Point(0, 7);
            btnExportAll.Size = new Size(w, 36);

            btnPackAll.Location = new Point(w + gap, 7);
            btnPackAll.Size = new Size(w, 36);

            btnClose.Location = new Point(ClientSize.Width - 46, 0);
            btnMinimize.Location = new Point(ClientSize.Width - 92, 0);
            btnSettings.Location = new Point(ClientSize.Width - 190, 0);

            // Keep DB status centered after SETTINGS / window controls move.
            LayoutDatabaseTopIndicator();

            UpdateScrollThumb();
            ApplyScrollOffset();
        }

        private int MaxScroll
        {
            get
            {
                if (listContent == null || listViewport == null)
                    return 0;

                return Math.Max(0, listContent.Height - listViewport.Height);
            }
        }

        private void UpdateScrollThumb()
        {
            if (scrollTrack == null || scrollThumb == null)
                return;

            if (MaxScroll <= 0)
            {
                scrollThumb.Visible = false;
                scrollOffset = 0;
                return;
            }

            scrollThumb.Visible = true;

            double ratio = (double)listViewport.Height / listContent.Height;
            int thumbHeight = Math.Max(44, (int)(scrollTrack.Height * ratio));
            thumbHeight = Math.Min(scrollTrack.Height, thumbHeight);

            scrollThumb.Height = thumbHeight;
            scrollThumb.Left = (scrollTrack.Width - scrollThumb.Width) / 2;

            int travel = Math.Max(1, scrollTrack.Height - scrollThumb.Height);
            scrollThumb.Top = (int)Math.Round((double)scrollOffset / MaxScroll * travel);
        }

        private void ApplyScrollOffset()
        {
            scrollOffset = Math.Max(0, Math.Min(MaxScroll, scrollOffset));
            listContent.Top = -scrollOffset;
            UpdateScrollThumb();
        }

        private void ListViewport_MouseWheel(object? sender, MouseEventArgs e)
        {
            int step = 3 * 34;

            if (e.Delta > 0)
                scrollOffset -= step;
            else if (e.Delta < 0)
                scrollOffset += step;

            ApplyScrollOffset();
        }

        private void ScrollTrack_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !scrollThumb.Visible)
                return;

            if (e.Y < scrollThumb.Top)
                scrollOffset -= listViewport.Height;
            else if (e.Y > scrollThumb.Bottom)
                scrollOffset += listViewport.Height;

            ApplyScrollOffset();
        }

        private void ScrollThumb_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            thumbDragging = true;
            thumbDragOffset = e.Y;
            scrollThumb.Capture = true;
            scrollThumb.BackColor = CScrollHover;
        }

        private void ScrollThumb_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!thumbDragging)
                return;

            Point mouseOnTrack = scrollTrack.PointToClient(Cursor.Position);

            int newTop = mouseOnTrack.Y - thumbDragOffset;
            int maxTop = Math.Max(0, scrollTrack.Height - scrollThumb.Height);
            newTop = Math.Max(0, Math.Min(maxTop, newTop));

            if (maxTop > 0)
                scrollOffset = (int)Math.Round((double)newTop / maxTop * MaxScroll);
            else
                scrollOffset = 0;

            ApplyScrollOffset();
        }

        private void ScrollThumb_MouseUp(object? sender, MouseEventArgs e)
        {
            thumbDragging = false;
            scrollThumb.Capture = false;
            scrollThumb.BackColor = CScrollThumb;
        }

        private void Export_Click(object? sender, EventArgs e)
        {
            if (sender is not Button button || button.Tag is not string entity)
                return;

            if (editorMode)
            {
                OpenEntityEditor(entity);
                return;
            }

            ConverterManager.ConvertEntityBinToXml(entity);
        }

        private void Pack_Click(object? sender, EventArgs e)
        {
            if (editorMode)
                return;

            if (sender is Button button && button.Tag is string entity)
                ConverterManager.ConvertEntityXmlToBin(entity);
        }

        private void btnClearLog_Click(object? sender, EventArgs e)
        {
            try
            {
                AppPaths.EnsureWorkspace();

                System.IO.File.WriteAllText(
                    AppPaths.LogFile,
                    string.Empty);

                txtConverterLog.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Não foi possível limpar o histórico de logs.\n\n{ex.Message}",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void LoadExistingLog()
        {
            try
            {
                if (txtConverterLog == null)
                    return;

                if (System.IO.File.Exists(AppPaths.LogFile))
                    txtConverterLog.Text = System.IO.File.ReadAllText(AppPaths.LogFile);

                txtConverterLog.SelectionStart = txtConverterLog.TextLength;
                txtConverterLog.ScrollToCaret();
            }
            catch
            {
                // O log em ficheiro continua funcional mesmo que o painel não consiga carregar o histórico.
            }
        }

        private void AppLogger_EntryLogged(LogEntry entry)
        {
            if (IsDisposed || txtConverterLog == null)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<LogEntry>(AppLogger_EntryLogged), entry);
                return;
            }

            bool isByteInfo =
                entry.Text.Contains(
                    "tamanho BIN",
                    StringComparison.OrdinalIgnoreCase) ||
                entry.Text.Contains(
                    "bytes (OK)",
                    StringComparison.OrdinalIgnoreCase) ||
                entry.Text.Contains(
                    "bytes. Esperado=",
                    StringComparison.OrdinalIgnoreCase);

            Color color = isByteInfo
                ? Color.FromArgb(100, 200, 255)
                : entry.Level switch
                {
                    LogLevel.Error => Color.FromArgb(255, 95, 95),
                    LogLevel.Warning => Color.FromArgb(255, 190, 90),
                    LogLevel.Success => Color.FromArgb(125, 220, 140),
                    _ => Color.FromArgb(230, 230, 230)
                };

            string prefix =
                $"[{entry.Time:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"[{entry.Level.ToString().ToUpperInvariant()}] ";

            txtConverterLog.SelectionStart = txtConverterLog.TextLength;
            txtConverterLog.SelectionLength = 0;
            txtConverterLog.SelectionColor = color;
            txtConverterLog.AppendText(prefix + entry.Text + Environment.NewLine);
            txtConverterLog.SelectionColor = txtConverterLog.ForeColor;

            txtConverterLog.SelectionStart = txtConverterLog.TextLength;
            txtConverterLog.ScrollToCaret();
        }

        private void Drag_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            dragging = true;
            dragStartCursor = Cursor.Position;
            dragStartForm = Location;
        }

        private void Drag_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!dragging)
                return;

            Point diff = Point.Subtract(Cursor.Position, new Size(dragStartCursor));
            Location = Point.Add(dragStartForm, new Size(diff));
        }

        private void Drag_MouseUp(object? sender, MouseEventArgs e)
        {
            dragging = false;
        }
    }
}
