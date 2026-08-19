using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        // Runtime bridge intentionally avoids another Form override. Several editor partials
        // already use WinForms lifecycle overrides, so a lightweight UI timer is safer and
        // keeps the DMBase additions isolated.
        private readonly System.Windows.Forms.Timer _dmBaseLevelToolsRuntimeTimer = DMBaseCreateLevelToolsRuntimeTimer();

        private static System.Windows.Forms.Timer DMBaseCreateLevelToolsRuntimeTimer()
        {
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (_, _) =>
            {
                foreach (Form1 form in Application.OpenForms.OfType<Form1>().ToArray())
                {
                    if (!form.IsDisposed && form.IsHandleCreated)
                        form.DMBaseEnhanceRuntimeUi();
                }
            };
            timer.Start();
            return timer;
        }

        private static bool DMBaseIsLevelCurveFile(string file) =>
            file.Equals("DigimonBase.xml", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("DigimonBaseInfo.xml", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("TamerBase.xml", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("TamerBaseInfo.xml", StringComparison.OrdinalIgnoreCase);

        private void DMBaseEnhanceRuntimeUi()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            TabPage? selected = editorTabs.SelectedTab;
            if (selected == null || selected.IsDisposed)
                return;

            if (selected.Tag is DMBaseVisualState state && DMBaseIsLevelCurveFile(state.FileName))
                DMBaseInstallLevelToolbar(state);

            if ((selected.Name ?? string.Empty).StartsWith("dmbase-edit:", StringComparison.OrdinalIgnoreCase))
                DMBaseCompactRecordEditor(selected);
        }

        private void DMBaseInstallLevelToolbar(DMBaseVisualState state)
        {
            if (state.Page.IsDisposed)
                return;

            Panel? root = state.Page.Controls.OfType<Panel>().FirstOrDefault(x => x.Dock == DockStyle.Fill);
            Panel? header = root?.Controls.OfType<Panel>().FirstOrDefault(x => x.Dock == DockStyle.Top);
            if (header == null)
                return;

            header.Height = Math.Max(header.Height, 154);

            Button generate = DMBaseEnsureHeaderToolButton(header, "DMBaseGenerateLevels", "GENERATE LEVELS");
            Button compare = DMBaseEnsureHeaderToolButton(header, "DMBaseCompareLevelDb", "COMPARE DB");
            Button import = DMBaseEnsureHeaderToolButton(header, "DMBaseImportLevelDb", "IMPORT DB");

            generate.Size = new Size(126, 32);
            compare.Size = new Size(108, 32);
            import.Size = new Size(108, 32);

            void Layout()
            {
                generate.Location = new Point(4, 112);
                compare.Location = new Point(generate.Right + 8, 112);
                import.Location = new Point(compare.Right + 8, 112);
            }

            Layout();
            header.Resize -= DMBaseDummyResizeHandler;
            header.Resize += (_, _) => Layout();

            if (generate.Tag == null)
            {
                generate.Tag = "wired";
                generate.Click += (_, _) => DMBaseOpenLevelGenerator(state);
            }
            if (compare.Tag == null)
            {
                compare.Tag = "wired";
                compare.Click += async (_, _) => await RunDMBaseLevelCompareAsync(state);
            }
            if (import.Tag == null)
            {
                import.Tag = "wired";
                import.Click += (_, _) => DMBaseShowLevelImportGate(state);
            }
        }

        private static void DMBaseDummyResizeHandler(object? sender, EventArgs e)
        {
        }

        private Button DMBaseEnsureHeaderToolButton(Panel header, string name, string text)
        {
            if (header.Controls[name] is Button existing)
                return existing;

            Button button = CreateEditorActionButton(text);
            button.Name = name;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            header.Controls.Add(button);
            button.BringToFront();
            return button;
        }

        private void DMBaseCompactRecordEditor(TabPage page)
        {
            Panel? scroll = DMBaseDescendants(page)
                .OfType<Panel>()
                .FirstOrDefault(x => x.AutoScroll && x.Dock == DockStyle.Fill);
            if (scroll == null)
                return;

            foreach (TextBox box in scroll.Controls.OfType<TextBox>())
            {
                if (box.Multiline)
                    continue;

                box.Width = Math.Min(270, Math.Max(180, scroll.ClientSize.Width - box.Left - 130));
                box.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                Button? select = scroll.Controls.OfType<Button>()
                    .FirstOrDefault(x => x.Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
                                         Math.Abs(x.Top - (box.Top - 3)) <= 7);
                if (select != null)
                {
                    select.Location = new Point(box.Right + 10, box.Top - 3);
                    select.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                }
            }

            foreach (Panel group in scroll.Controls.OfType<Panel>())
            {
                DataGridView? grid = group.Controls.OfType<DataGridView>().FirstOrDefault();
                if (grid == null)
                    continue;

                group.Width = Math.Max(430, scroll.ClientSize.Width - 24);
                group.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                grid.Width = Math.Max(260, group.ClientSize.Width - 160);
                grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

                int right = group.ClientSize.Width - 92;
                foreach (Button action in group.Controls.OfType<Button>())
                {
                    action.Left = right;
                    action.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                }
            }
        }

        private static IEnumerable<Control> DMBaseDescendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in DMBaseDescendants(child))
                    yield return nested;
            }
        }

        private sealed class DMBaseCurveInfo
        {
            public long CurveKey { get; init; }
            public List<XElement> Records { get; init; } = new();
            public int MinLevel => Records.Count == 0 ? 0 : Records.Min(x => DMBaseInt64(x, "Level") > int.MaxValue ? int.MaxValue : (int)DMBaseInt64(x, "Level"));
            public int MaxLevel => Records.Count == 0 ? 0 : Records.Max(x => DMBaseInt64(x, "Level") > int.MaxValue ? int.MaxValue : (int)DMBaseInt64(x, "Level"));
            public override string ToString() => $"Curve {CurveKey}  •  Levels {MinLevel}-{MaxLevel}  •  {Records.Count} rows";
        }

        private void DMBaseOpenLevelGenerator(DMBaseVisualState state)
        {
            string key = "dmbase-level-generator:" + state.XmlPath;
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Name == key);
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            List<DMBaseCurveInfo> curves = DMBaseDetectCurves(state);
            if (curves.Count == 0)
            {
                MessageBox.Show(this, "No valid Id/Level curves were detected in this XML.", "DMBase Level Generator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var page = CreateDarkTab("Level Generator");
            page.Name = key;
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var header = new Panel { Dock = DockStyle.Top, Height = 148, BackColor = CEditor };

            var title = new Label
            {
                Text = $"{Path.GetFileNameWithoutExtension(state.FileName)} — Adaptive Level Generator",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                Location = new Point(4, 2),
                Size = new Size(650, 30),
                AutoEllipsis = true
            };
            var subtitle = new Label
            {
                Text = "Learns each curve from the existing 1–120 data. EXP uses robust log-growth; combat stats use recent linear trend. Nothing is written until APPLY.",
                ForeColor = CMuted,
                Location = new Point(6, 36),
                Size = new Size(800, 36),
                AutoEllipsis = true
            };
            var scopeLabel = new Label { Text = "Scope", ForeColor = CText, Location = new Point(6, 80), Size = new Size(48, 24), TextAlign = ContentAlignment.MiddleLeft };
            var scope = new ComboBox
            {
                Location = new Point(58, 80),
                Size = new Size(300, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = CText
            };
            scope.Items.Add("ALL CURVES");
            foreach (DMBaseCurveInfo curve in curves) scope.Items.Add(curve);
            scope.SelectedIndex = 0;

            var amountLabel = new Label { Text = "+ Levels", ForeColor = CText, Location = new Point(374, 80), Size = new Size(62, 24), TextAlign = ContentAlignment.MiddleLeft };
            var amount = new NumericUpDown
            {
                Location = new Point(440, 80),
                Size = new Size(72, 26),
                Minimum = 1,
                Maximum = 50,
                Value = 10,
                BackColor = Color.FromArgb(18, 18, 18),
                ForeColor = CText
            };
            var previewButton = CreateEditorActionButton("PREVIEW"); previewButton.Location = new Point(530, 78); previewButton.Size = new Size(100, 32);
            var applyButton = CreateEditorActionButton("APPLY TO XML"); applyButton.Location = new Point(638, 78); applyButton.Size = new Size(120, 32);
            var analysis = new Label
            {
                Text = $"Detected {curves.Count:N0} independent curves • current maximum level {curves.Max(x => x.MaxLevel)} • default projection +10 levels",
                ForeColor = Color.FromArgb(105, 220, 145),
                Location = new Point(6, 116),
                Size = new Size(820, 24),
                AutoEllipsis = true
            };

            header.Controls.AddRange(new Control[] { title, subtitle, scopeLabel, scope, amountLabel, amount, previewButton, applyButton, analysis });

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(18, 18, 18),
                ForeColor = Color.Black,
                RowHeadersVisible = false,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            foreach (string column in new[] { "Level", "Id", "Exp", "Hp", "Ds", "At", "De", "Ct", "Ev", "Ht", "Ms" })
                grid.Columns.Add(column, column);

            root.Controls.Add(grid);
            root.Controls.Add(header);
            page.Controls.Add(root);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            DMBaseCurveInfo PreviewCurve() => scope.SelectedIndex <= 0 ? curves[0] : (DMBaseCurveInfo)scope.SelectedItem!;

            void RenderPreview()
            {
                grid.Rows.Clear();
                DMBaseCurveInfo curve = PreviewCurve();
                List<XElement> projected = DMBaseProjectCurve(curve, (int)amount.Value);
                foreach (XElement row in projected)
                {
                    grid.Rows.Add(
                        DMBaseValue(row, "Level"), DMBaseValue(row, "Id"), DMBaseValue(row, "Exp"),
                        DMBaseValue(row, "Hp"), DMBaseValue(row, "Ds"), DMBaseValue(row, "At"),
                        DMBaseValue(row, "De"), DMBaseValue(row, "Ct"), DMBaseValue(row, "Ev"),
                        DMBaseValue(row, "Ht"), DMBaseValue(row, "Ms"));
                }
                string scopeText = scope.SelectedIndex == 0 ? $"ALL {curves.Count:N0} curves will be extended" : $"Only curve {curve.CurveKey} will be extended";
                analysis.Text = $"Preview: Level {curve.MaxLevel + 1} → {curve.MaxLevel + (int)amount.Value} • {scopeText} • EXP model uses the last up to 20 positive samples.";
            }

            previewButton.Click += (_, _) => RenderPreview();
            scope.SelectedIndexChanged += (_, _) => RenderPreview();
            amount.ValueChanged += (_, _) => RenderPreview();
            applyButton.Click += (_, _) =>
            {
                List<DMBaseCurveInfo> targets = scope.SelectedIndex == 0
                    ? curves
                    : new List<DMBaseCurveInfo> { (DMBaseCurveInfo)scope.SelectedItem! };

                int add = (int)amount.Value;
                int total = targets.Count * add;
                DialogResult confirm = MessageBox.Show(
                    this,
                    $"Generate {add} additional levels for {targets.Count:N0} curve(s)?\r\n\r\n" +
                    $"This will append {total:N0} XML records. A .editor.bak backup is created first.\r\n\r\n" +
                    "EXP is extrapolated from the recent log-growth trend and the remaining stats from the recent linear trend.",
                    "Apply generated levels",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                foreach (DMBaseCurveInfo target in targets)
                {
                    XElement? insertion = target.Records.OrderBy(x => DMBaseInt64(x, "Level")).LastOrDefault();
                    if (insertion?.Parent == null) continue;
                    foreach (XElement generated in DMBaseProjectCurve(target, add))
                    {
                        insertion.AddAfterSelf(generated);
                        insertion = generated;
                    }
                }

                DMBaseSaveState(state);
                DMBaseReloadState(state);
                DMBaseRenderCards(state);
                curves = DMBaseDetectCurves(state);
                MessageBox.Show(this, $"Generated and saved {total:N0} new level records successfully.", "DMBase Level Generator", MessageBoxButtons.OK, MessageBoxIcon.Information);
                editorTabs.SelectedTab = state.Page;
            };

            RenderPreview();
        }

        private static List<DMBaseCurveInfo> DMBaseDetectCurves(DMBaseVisualState state)
        {
            return state.Records
                .Select(x => new { Node = x, Id = DMBaseInt64(x, "Id"), Level = DMBaseInt64(x, "Level") })
                .Where(x => x.Id > 0 && x.Level > 0)
                .GroupBy(x => x.Id - x.Level)
                .Select(g => new DMBaseCurveInfo
                {
                    CurveKey = g.Key,
                    Records = g.Select(x => x.Node).OrderBy(x => DMBaseInt64(x, "Level")).ToList()
                })
                .OrderBy(x => x.CurveKey)
                .ToList();
        }

        private static List<XElement> DMBaseProjectCurve(DMBaseCurveInfo curve, int extraLevels)
        {
            var result = new List<XElement>();
            if (curve.Records.Count == 0 || extraLevels <= 0)
                return result;

            XElement template = curve.Records.OrderBy(x => DMBaseInt64(x, "Level")).Last();
            int maxLevel = (int)DMBaseInt64(template, "Level");
            long previousExp = DMBaseInt64(template, "Exp");

            for (int i = 1; i <= extraLevels; i++)
            {
                int level = maxLevel + i;
                XElement row = new XElement(template);
                DMBaseSet(row, "Level", level);
                DMBaseSet(row, "Id", curve.CurveKey + level);

                long exp = DMBasePredictExp(curve.Records, level, previousExp);
                DMBaseSet(row, "Exp", exp);
                previousExp = exp;

                foreach (string field in new[] { "Hp", "Ds", "At", "De", "Ct", "Ev", "Ht", "Ms" })
                {
                    if (row.Element(field) != null)
                        DMBaseSet(row, field, DMBasePredictLinear(curve.Records, field, level));
                }

                result.Add(row);
            }
            return result;
        }

        private static long DMBasePredictLinear(List<XElement> records, string field, int targetLevel)
        {
            var points = records
                .Select(x => (Level: (double)DMBaseInt64(x, "Level"), Value: (double)DMBaseInt64(x, field)))
                .Where(x => x.Level > 0)
                .OrderBy(x => x.Level)
                .TakeLast(Math.Min(24, records.Count))
                .ToList();
            if (points.Count == 0) return 0;
            if (points.Count == 1) return (long)Math.Round(points[0].Value, MidpointRounding.AwayFromZero);

            (double intercept, double slope) = DMBaseLinearRegression(points);
            double predicted = intercept + slope * targetLevel;
            if (double.IsNaN(predicted) || double.IsInfinity(predicted)) predicted = points[^1].Value;
            return Math.Max(0, (long)Math.Round(predicted, MidpointRounding.AwayFromZero));
        }

        private static long DMBasePredictExp(List<XElement> records, int targetLevel, long previousExp)
        {
            var points = records
                .Select(x => (Level: (double)DMBaseInt64(x, "Level"), Value: (double)DMBaseInt64(x, "Exp")))
                .Where(x => x.Level > 0 && x.Value > 0)
                .OrderBy(x => x.Level)
                .TakeLast(Math.Min(20, records.Count))
                .Select(x => (x.Level, Value: Math.Log(x.Value)))
                .ToList();

            double predicted;
            if (points.Count >= 3)
            {
                (double intercept, double slope) = DMBaseLinearRegression(points);
                // Guard against a malformed tail exploding the next levels.
                slope = Math.Max(0.001, Math.Min(0.20, slope));
                predicted = Math.Exp(intercept + slope * targetLevel);
            }
            else
            {
                predicted = previousExp * 1.05;
            }

            long fallbackIncrement = DMBaseMedianRecentExpIncrement(records);
            long minimum = previousExp + Math.Max(1, fallbackIncrement / 3);
            if (double.IsNaN(predicted) || double.IsInfinity(predicted) || predicted > long.MaxValue)
                return minimum;

            long rounded = (long)Math.Round(predicted, MidpointRounding.AwayFromZero);
            return Math.Max(minimum, rounded);
        }

        private static long DMBaseMedianRecentExpIncrement(List<XElement> records)
        {
            List<long> values = records.OrderBy(x => DMBaseInt64(x, "Level")).TakeLast(Math.Min(12, records.Count)).Select(x => DMBaseInt64(x, "Exp")).ToList();
            var diffs = new List<long>();
            for (int i = 1; i < values.Count; i++)
                if (values[i] > values[i - 1]) diffs.Add(values[i] - values[i - 1]);
            if (diffs.Count == 0) return Math.Max(1, values.LastOrDefault() / 20);
            diffs.Sort();
            return diffs[diffs.Count / 2];
        }

        private static (double Intercept, double Slope) DMBaseLinearRegression(List<(double Level, double Value)> points)
        {
            double meanX = points.Average(x => x.Level);
            double meanY = points.Average(x => x.Value);
            double numerator = points.Sum(x => (x.Level - meanX) * (x.Value - meanY));
            double denominator = points.Sum(x => (x.Level - meanX) * (x.Level - meanX));
            double slope = Math.Abs(denominator) < 0.0000001 ? 0 : numerator / denominator;
            return (meanY - slope * meanX, slope);
        }

        private static long DMBaseInt64(XElement node, string name)
        {
            return long.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
        }

        private static string DMBaseValue(XElement node, string name) => node.Element(name)?.Value?.Trim() ?? string.Empty;

        private static void DMBaseSet(XElement node, string name, long value)
        {
            XElement? element = node.Element(name);
            if (element == null) node.Add(new XElement(name, value.ToString(CultureInfo.InvariantCulture)));
            else element.Value = value.ToString(CultureInfo.InvariantCulture);
        }

        private async Task RunDMBaseLevelCompareAsync(DMBaseVisualState state)
        {
            string connection;
            try { connection = DatabaseConnectionStore.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "DMBase Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(connection))
            {
                MessageBox.Show(this, "Configure and test the SQL Server connection in SETTINGS first.", "DMBase Compare DB", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string tabName = Path.GetFileNameWithoutExtension(state.FileName) + " DB Compare";
            var page = CreateDarkTab(tabName);
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var title = new Label
            {
                Text = state.FileName.StartsWith("Digimon", StringComparison.OrdinalIgnoreCase)
                    ? "DMBase XML ↔ [Asset].[DigimonLevelStatus]"
                    : "DMBase XML ↔ [Asset].[CharacterLevelStatus]",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Location = new Point(8, 8),
                Size = new Size(780, 28),
                AutoEllipsis = true
            };
            var subtitle = new Label
            {
                Text = "READ-ONLY diagnostic • discovers field mapping, curve → Type mapping, StatusId and ScaleType rules • database is never modified",
                ForeColor = CMuted,
                Location = new Point(10, 40),
                Size = new Size(850, 28),
                AutoEllipsis = true
            };
            var status = new Label { Text = "Preparing...", ForeColor = CMuted, Location = new Point(10, 72), Size = new Size(850, 24), AutoEllipsis = true };
            var log = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(10, 104),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Size = new Size(820, 430),
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5F)
            };
            root.Controls.AddRange(new Control[] { title, subtitle, status, log });
            root.Resize += (_, _) => log.Size = new Size(Math.Max(300, root.ClientSize.Width - 36), Math.Max(180, root.ClientSize.Height - 130));
            page.Controls.Add(root);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            var cts = new CancellationTokenSource();
            EventHandler disposed = (_, _) => { if (!cts.IsCancellationRequested) cts.Cancel(); };
            page.Disposed += disposed;
            var progress = new Progress<string>(line =>
            {
                if (page.IsDisposed) return;
                status.Text = line;
                log.AppendText(line + Environment.NewLine);
            });

            try
            {
                var service = new DMBaseLevelDatabaseDiagnosticService();
                DMBaseLevelDatabaseDiagnosticSummary summary = await service.CompareAsync(connection, state.XmlPath, progress, cts.Token);
                if (page.IsDisposed) return;
                status.Text = $"DONE • XML {summary.XmlRows:N0} • DB {summary.DbRows:N0} • curves {summary.CurveCount:N0} • strong matches {summary.StrongMatches:N0}";
                status.ForeColor = Color.FromArgb(120, 220, 145);
                log.AppendText(Environment.NewLine + "HIGH SIGNAL REPORT: " + summary.HighSignalReport + Environment.NewLine);

                DialogResult open = MessageBox.Show(
                    this,
                    "DMBase level comparison completed.\r\n\r\n" +
                    $"XML rows: {summary.XmlRows:N0}\r\n" +
                    $"DB rows: {summary.DbRows:N0}\r\n" +
                    $"Detected curves: {summary.CurveCount:N0}\r\n" +
                    $"Strong matches: {summary.StrongMatches:N0}\r\n\r\n" +
                    "The folder contains raw XML/DB snapshots, field mapping percentages, curve→Type candidates and the high-signal report.\r\n\r\nOpen the diagnostic folder?",
                    "DMBase Compare DB",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (open == DialogResult.Yes && Directory.Exists(summary.OutputFolder))
                    Process.Start(new ProcessStartInfo { FileName = summary.OutputFolder, UseShellExecute = true });
            }
            catch (OperationCanceledException)
            {
                if (!page.IsDisposed) { status.Text = "Cancelled."; status.ForeColor = Color.FromArgb(255, 190, 90); }
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    status.Text = "FAILED — database was not modified.";
                    status.ForeColor = Color.FromArgb(255, 100, 110);
                    log.AppendText(Environment.NewLine + ex + Environment.NewLine);
                    ShowEditorError("DMBase Compare DB", ex);
                }
            }
            finally
            {
                if (!page.IsDisposed) page.Disposed -= disposed;
                cts.Dispose();
            }
        }

        private void DMBaseShowLevelImportGate(DMBaseVisualState state)
        {
            string table = state.FileName.StartsWith("Digimon", StringComparison.OrdinalIgnoreCase)
                ? "[dmo].[Asset].[DigimonLevelStatus]"
                : "[dmo].[Asset].[CharacterLevelStatus]";
            MessageBox.Show(
                this,
                "The IMPORT DB button is prepared but intentionally locked for this first pass.\r\n\r\n" +
                $"Target table: {table}\r\n\r\n" +
                "Run COMPARE DB first and send me the generated DMBaseLevelDatabaseDiagnostic folder. " +
                "That report establishes the exact Type mapping and, for Digimon, StatusId/ScaleType rules before any destructive database write is enabled.",
                "DMBase Import DB — mapping confirmation required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
