using System;
using System.Drawing;
using System.Windows.Forms;

namespace DRW_Work_Tool.Core
{
    public enum DarkScrollOrientation
    {
        Vertical,
        Horizontal
    }

    /// <summary>
    /// Fully custom scrollbar. No native Win32 scrollbar is created.
    /// </summary>
    public sealed class DarkScrollBar : Control
    {
        private int _minimum;
        private int _maximum;
        private int _largeChange = 1;
        private int _value;

        private bool _dragging;
        private int _dragOffset;

        public event EventHandler? ValueChanged;

        public DarkScrollOrientation Orientation { get; set; } =
            DarkScrollOrientation.Vertical;

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                Normalize();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(
                    Minimum,
                    value);

                Normalize();
            }
        }

        public int LargeChange
        {
            get => _largeChange;
            set
            {
                _largeChange =
                    Math.Max(
                        1,
                        value);

                Normalize();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                int next =
                    Math.Max(
                        Minimum,
                        Math.Min(
                            EffectiveMaximum,
                            value));

                if (_value == next)
                    return;

                _value = next;
                Invalidate();

                ValueChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            }
        }

        public int EffectiveMaximum =>
            Math.Max(
                Minimum,
                Maximum -
                LargeChange +
                1);

        public DarkScrollBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor =
                Color.FromArgb(
                    17,
                    17,
                    17);

            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.Clear(
                BackColor);

            Rectangle track =
                GetTrackRectangle();

            using (var trackBrush =
                   new SolidBrush(
                       Color.FromArgb(
                           24,
                           24,
                           24)))
            {
                e.Graphics.FillRectangle(
                    trackBrush,
                    track);
            }

            Rectangle thumb =
                GetThumbRectangle();

            using var thumbBrush =
                new SolidBrush(
                    Enabled
                        ? Color.FromArgb(
                            84,
                            84,
                            84)
                        : Color.FromArgb(
                            50,
                            50,
                            50));

            e.Graphics.FillRectangle(
                thumbBrush,
                thumb);
        }

        protected override void OnMouseDown(
            MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (!Enabled ||
                e.Button != MouseButtons.Left)
            {
                return;
            }

            Rectangle thumb =
                GetThumbRectangle();

            int position =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? e.Y
                    : e.X;

            int thumbStart =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? thumb.Top
                    : thumb.Left;

            int thumbEnd =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? thumb.Bottom
                    : thumb.Right;

            if (position >= thumbStart &&
                position <= thumbEnd)
            {
                _dragging = true;

                _dragOffset =
                    position -
                    thumbStart;

                Capture = true;
            }
            else
            {
                int page =
                    Math.Max(
                        1,
                        LargeChange);

                Value +=
                    position < thumbStart
                        ? -page
                        : page;
            }
        }

        protected override void OnMouseMove(
            MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!_dragging)
                return;

            Rectangle track =
                GetTrackRectangle();

            Rectangle thumb =
                GetThumbRectangle();

            int trackStart =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? track.Top
                    : track.Left;

            int trackLength =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? track.Height
                    : track.Width;

            int thumbLength =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? thumb.Height
                    : thumb.Width;

            int mousePosition =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? e.Y
                    : e.X;

            int available =
                Math.Max(
                    1,
                    trackLength -
                    thumbLength);

            int pixel =
                Math.Max(
                    0,
                    Math.Min(
                        available,
                        mousePosition -
                        _dragOffset -
                        trackStart));

            int range =
                Math.Max(
                    0,
                    EffectiveMaximum -
                    Minimum);

            Value =
                range == 0
                    ? Minimum
                    : Minimum +
                      (int)Math.Round(
                          pixel /
                          (double)available *
                          range);
        }

        protected override void OnMouseUp(
            MouseEventArgs e)
        {
            base.OnMouseUp(e);

            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseWheel(
            MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            Value +=
                e.Delta > 0
                    ? -Math.Max(
                        1,
                        LargeChange / 5)
                    : Math.Max(
                        1,
                        LargeChange / 5);
        }

        private Rectangle GetTrackRectangle()
        {
            return new Rectangle(
                1,
                1,
                Math.Max(
                    1,
                    Width - 2),
                Math.Max(
                    1,
                    Height - 2));
        }

        private Rectangle GetThumbRectangle()
        {
            Rectangle track =
                GetTrackRectangle();

            int contentRange =
                Math.Max(
                    1,
                    Maximum -
                    Minimum +
                    1);

            int trackLength =
                Orientation ==
                DarkScrollOrientation.Vertical
                    ? track.Height
                    : track.Width;

            int minThumb = 24;

            int thumbLength =
                Math.Max(
                    minThumb,
                    Math.Min(
                        trackLength,
                        (int)Math.Round(
                            trackLength *
                            Math.Min(
                                1.0,
                                LargeChange /
                                (double)contentRange))));

            int available =
                Math.Max(
                    0,
                    trackLength -
                    thumbLength);

            int valueRange =
                Math.Max(
                    0,
                    EffectiveMaximum -
                    Minimum);

            int offset =
                valueRange == 0
                    ? 0
                    : (int)Math.Round(
                        available *
                        (Value - Minimum) /
                        (double)valueRange);

            return Orientation ==
                   DarkScrollOrientation.Vertical
                ? new Rectangle(
                    track.Left,
                    track.Top + offset,
                    track.Width,
                    thumbLength)
                : new Rectangle(
                    track.Left + offset,
                    track.Top,
                    thumbLength,
                    track.Height);
        }

        private void Normalize()
        {
            _value =
                Math.Max(
                    Minimum,
                    Math.Min(
                        EffectiveMaximum,
                        _value));

            Invalidate();
        }
    }
}
