using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DRW_Work_Tool
{
    public sealed class LoadingForm : Form
    {
        private readonly Label _title;
        private readonly Label _status;
        private readonly Panel _progressTrack;
        private readonly Panel _progressFill;
        private readonly Label _percent;

        private bool _started;

        public bool StartupFinished { get; private set; }

        public LoadingForm()
        {
            Text =
                "Digimon Reboot World Work Tool";

            FormBorderStyle =
                FormBorderStyle.None;

            StartPosition =
                FormStartPosition.CenterScreen;

            ClientSize =
                new Size(
                    520,
                    198);

            BackColor =
                Color.FromArgb(
                    17,
                    17,
                    17);

            ForeColor =
                Color.FromArgb(
                    245,
                    245,
                    245);

            ShowInTaskbar = true;

            _title =
                new Label
                {
                    Text =
                        "Digimon Reboot World Work Tool",
                    ForeColor =
                        Color.FromArgb(
                            245,
                            245,
                            245),
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            13F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            24,
                            22),
                    Size =
                        new Size(
                            470,
                            32),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            var subtitle =
                new Label
                {
                    Text =
                        "Preparing editor database and visual indexes...",
                    ForeColor =
                        Color.FromArgb(
                            175,
                            175,
                            175),
                    Font =
                        new Font(
                            "Segoe UI",
                            8.5F),
                    Location =
                        new Point(
                            26,
                            55),
                    Size =
                        new Size(
                            455,
                            24),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            _status =
                new Label
                {
                    Text =
                        "Starting...",
                    ForeColor =
                        Color.FromArgb(
                            220,
                            220,
                            220),
                    Font =
                        new Font(
                            "Segoe UI",
                            8.8F),
                    Location =
                        new Point(
                            26,
                            92),
                    Size =
                        new Size(
                            405,
                            22),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                };

            _percent =
                new Label
                {
                    Text = "0%",
                    ForeColor =
                        Color.FromArgb(
                            125,
                            220,
                            140),
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.8F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            440,
                            92),
                    Size =
                        new Size(
                            54,
                            22),
                    TextAlign =
                        ContentAlignment.MiddleRight
                };

            _progressTrack =
                new Panel
                {
                    Location =
                        new Point(
                            26,
                            124),
                    Size =
                        new Size(
                            468,
                            10),
                    BackColor =
                        Color.FromArgb(
                            42,
                            42,
                            42)
                };

            _progressFill =
                new Panel
                {
                    Location =
                        new Point(
                            0,
                            0),
                    Size =
                        new Size(
                            0,
                            10),
                    BackColor =
                        Color.FromArgb(
                            105,
                            200,
                            125)
                };

            _progressTrack.Controls.Add(
                _progressFill);

            var hint =
                new Label
                {
                    Text =
                        "Please wait. The main window will open automatically.",
                    ForeColor =
                        Color.FromArgb(
                            125,
                            125,
                            125),
                    Font =
                        new Font(
                            "Segoe UI",
                            7.7F),
                    Location =
                        new Point(
                            26,
                            150),
                    Size =
                        new Size(
                            468,
                            22),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            Controls.Add(
                _title);

            Controls.Add(
                subtitle);

            Controls.Add(
                _status);

            Controls.Add(
                _percent);

            Controls.Add(
                _progressTrack);

            Controls.Add(
                hint);

            Paint +=
                (_, e) =>
                {
                    using var pen =
                        new Pen(
                            Color.FromArgb(
                                58,
                                58,
                                58));

                    e.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        ClientSize.Width - 1,
                        ClientSize.Height - 1);
                };

            Shown +=
                LoadingForm_Shown;
        }

        private async void LoadingForm_Shown(
            object? sender,
            EventArgs e)
        {
            if (_started)
                return;

            _started = true;

            // Give Windows one paint/message cycle before any preload begins.
            await Task.Yield();

            var progress =
                new Progress<StartupPreloadProgress>(
                    UpdateProgress);

            try
            {
                await EditorPreloadService
                    .StartAsync(
                        progress);

                UpdateProgress(
                    new StartupPreloadProgress(
                        100,
                        "Ready. Opening Work Tool..."));

                StartupFinished = true;

                await Task.Delay(
                    180);

                Close();
            }
            catch (Exception ex)
            {
                // The old application behavior allowed the UI to continue even
                // when an optional editor preload failed. Preserve that behavior,
                // but make the failure visible on the startup window.
                _status.Text =
                    "Preload warning: " +
                    ex.Message;

                _status.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);

                _percent.Text =
                    "WARN";

                _percent.ForeColor =
                    Color.FromArgb(
                        255,
                        190,
                        90);

                StartupFinished = true;

                await Task.Delay(
                    1400);

                Close();
            }
        }

        private void UpdateProgress(
            StartupPreloadProgress progress)
        {
            if (IsDisposed)
                return;

            int percent =
                Math.Clamp(
                    progress.Percent,
                    0,
                    100);

            _status.Text =
                progress.Message;

            _percent.Text =
                percent +
                "%";

            int width =
                (int)Math.Round(
                    _progressTrack.ClientSize.Width *
                    (percent / 100.0));

            _progressFill.Width =
                Math.Max(
                    0,
                    Math.Min(
                        _progressTrack.ClientSize.Width,
                        width));

            _progressFill.Height =
                _progressTrack.ClientSize.Height;

            _progressFill.Invalidate();
            _progressTrack.Invalidate();
        }
    }
}
