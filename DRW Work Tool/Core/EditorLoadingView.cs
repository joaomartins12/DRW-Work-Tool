using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Dark animated loading surface for editor tabs.
    ///
    /// The real editor content should only replace this control after every
    /// required background load/index operation has completed. This prevents
    /// WinForms from exposing partially-created controls while a tab opens.
    /// </summary>
    public sealed class EditorLoadingView : UserControl
    {
        private readonly EditorLoadingSpinner _spinner;
        private readonly Label _title;
        private readonly Label _message;
        private readonly Label _status;

        public EditorLoadingView(
            string title,
            string message)
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
            UpdateStyles();

            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(20, 20, 20);

            var center =
                new Panel
                {
                    Size = new Size(520, 230),
                    BackColor = Color.FromArgb(24, 24, 24)
                };

            center.Paint +=
                (_, e) =>
                {
                    using var pen =
                        new Pen(
                            Color.FromArgb(52, 52, 52));

                    e.Graphics.DrawRectangle(
                        pen,
                        0,
                        0,
                        center.Width - 1,
                        center.Height - 1);
                };

            _spinner =
                new EditorLoadingSpinner
                {
                    Size = new Size(64, 64),
                    Location = new Point(228, 30)
                };

            _title =
                new Label
                {
                    Text = title,
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            12F,
                            FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(20, 108),
                    Size = new Size(480, 30)
                };

            _message =
                new Label
                {
                    Text = message,
                    ForeColor = Color.FromArgb(170, 170, 170),
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5F),
                    TextAlign = ContentAlignment.TopCenter,
                    Location = new Point(30, 143),
                    Size = new Size(460, 46),
                    AutoEllipsis = true
                };

            _status =
                new Label
                {
                    Text = "Loading...",
                    ForeColor = Color.FromArgb(125, 220, 140),
                    BackColor = Color.Transparent,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            7.5F,
                            FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Location = new Point(30, 194),
                    Size = new Size(460, 20)
                };

            center.Controls.Add(_spinner);
            center.Controls.Add(_title);
            center.Controls.Add(_message);
            center.Controls.Add(_status);

            Controls.Add(center);

            void PositionCenter()
            {
                center.Location =
                    new Point(
                        Math.Max(
                            8,
                            (ClientSize.Width - center.Width) / 2),
                        Math.Max(
                            8,
                            (ClientSize.Height - center.Height) / 2));
            }

            Resize +=
                (_, _) =>
                    PositionCenter();

            PositionCenter();
        }

        public void SetMessage(
            string message,
            string? status = null)
        {
            if (IsDisposed)
                return;

            _message.Text = message;

            if (!string.IsNullOrWhiteSpace(status))
                _status.Text = status;
        }

        public void SetError(
            string title,
            string message)
        {
            if (IsDisposed)
                return;

            _spinner.Stop();

            _spinner.ErrorMode = true;
            _spinner.Invalidate();

            _title.Text = title;
            _title.ForeColor =
                Color.FromArgb(
                    255,
                    110,
                    110);

            _message.Text = message;
            _message.ForeColor =
                Color.FromArgb(
                    210,
                    170,
                    170);

            _status.Text = "FAILED";
            _status.ForeColor =
                Color.FromArgb(
                    255,
                    95,
                    95);
        }

        public void SetCompleted(
            string status = "Ready")
        {
            if (IsDisposed)
                return;

            _spinner.Stop();

            _status.Text = status;
            _status.ForeColor =
                Color.FromArgb(
                    125,
                    220,
                    140);
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
                _spinner.Stop();

            base.Dispose(disposing);
        }
    }

    public sealed class EditorLoadingSpinner : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private int _frame;

        public EditorLoadingSpinner()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);
            UpdateStyles();

            DoubleBuffered = true;
            BackColor = Color.FromArgb(24, 24, 24);

            _timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 70
                };

            _timer.Tick +=
                (_, _) =>
                {
                    _frame =
                        (_frame + 1) %
                        12;

                    Invalidate();
                };

            _timer.Start();
        }

        public bool ErrorMode { get; set; }

        public void Stop()
        {
            if (_timer.Enabled)
                _timer.Stop();
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;

            float cx =
                ClientSize.Width / 2F;

            float cy =
                ClientSize.Height / 2F;

            float radius =
                Math.Min(
                    ClientSize.Width,
                    ClientSize.Height) *
                0.31F;

            float dot =
                Math.Max(
                    4F,
                    Math.Min(
                        ClientSize.Width,
                        ClientSize.Height) *
                    0.085F);

            if (ErrorMode)
            {
                using var pen =
                    new Pen(
                        Color.FromArgb(
                            255,
                            95,
                            95),
                        4F);

                float r = radius + 4F;

                e.Graphics.DrawEllipse(
                    pen,
                    cx - r,
                    cy - r,
                    r * 2,
                    r * 2);

                e.Graphics.DrawLine(
                    pen,
                    cx - r * 0.42F,
                    cy - r * 0.42F,
                    cx + r * 0.42F,
                    cy + r * 0.42F);

                e.Graphics.DrawLine(
                    pen,
                    cx + r * 0.42F,
                    cy - r * 0.42F,
                    cx - r * 0.42F,
                    cy + r * 0.42F);

                return;
            }

            for (int i = 0; i < 12; i++)
            {
                int distance =
                    (i - _frame + 12) %
                    12;

                int alpha =
                    Math.Max(
                        42,
                        255 -
                        distance * 18);

                double angle =
                    (Math.PI * 2D * i / 12D) -
                    Math.PI / 2D;

                float x =
                    cx +
                    (float)Math.Cos(angle) *
                    radius;

                float y =
                    cy +
                    (float)Math.Sin(angle) *
                    radius;

                using var brush =
                    new SolidBrush(
                        Color.FromArgb(
                            alpha,
                            125,
                            220,
                            140));

                e.Graphics.FillEllipse(
                    brush,
                    x - dot / 2F,
                    y - dot / 2F,
                    dot,
                    dot);
            }
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
                _timer.Dispose();

            base.Dispose(disposing);
        }
    }
}
