using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// 100% custom dark dropdown.
    /// No native ComboBox and no native ListBox scrollbar.
    /// </summary>
    public sealed class DarkComboBox : UserControl
    {
        private const int RowHeight = 29;
        private const int MaxVisibleRows = 10;

        private readonly List<object> _items = new();

        private object? _selectedItem;
        private int _selectedIndex = -1;
        private bool _hover;

        private ToolStripDropDown? _dropDown;

        public event EventHandler? SelectedIndexChanged;

        public DarkComboBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor =
                Color.FromArgb(
                    13,
                    13,
                    13);

            ForeColor =
                Color.FromArgb(
                    240,
                    240,
                    240);

            Font =
                new Font(
                    "Segoe UI",
                    9F);

            Height = 29;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public IList<object> Items =>
            _items;

        public object? SelectedItem
        {
            get => _selectedItem;
            set =>
                SetSelectedIndex(
                    value == null
                        ? -1
                        : _items.IndexOf(value));
        }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set => SetSelectedIndex(value);
        }

        protected override void OnPaint(
            PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode =
                SmoothingMode.AntiAlias;

            Color background =
                Enabled
                    ? _hover
                        ? Color.FromArgb(
                            20,
                            20,
                            20)
                        : BackColor
                    : Color.FromArgb(
                        29,
                        29,
                        29);

            using (var brush =
                   new SolidBrush(background))
            {
                e.Graphics.FillRectangle(
                    brush,
                    ClientRectangle);
            }

            using (var border =
                   new Pen(
                       Focused
                           ? Color.FromArgb(
                               90,
                               125,
                               155)
                           : Color.FromArgb(
                               76,
                               76,
                               76)))
            {
                e.Graphics.DrawRectangle(
                    border,
                    0,
                    0,
                    Width - 1,
                    Height - 1);
            }

            const int arrowWidth = 30;

            using (var arrowBackground =
                   new SolidBrush(
                       Color.FromArgb(
                           34,
                           34,
                           34)))
            {
                e.Graphics.FillRectangle(
                    arrowBackground,
                    Width - arrowWidth,
                    1,
                    arrowWidth - 1,
                    Height - 2);
            }

            string text =
                _selectedItem == null
                    ? string.Empty
                    : GetItemText(
                        _selectedItem);

            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(
                    9,
                    1,
                    Math.Max(
                        10,
                        Width -
                        arrowWidth -
                        14),
                    Height - 2),
                Enabled
                    ? ForeColor
                    : Color.FromArgb(
                        125,
                        125,
                        125),
                TextFormatFlags.Left |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            Point center =
                new Point(
                    Width -
                    arrowWidth / 2,
                    Height / 2 + 1);

            Point[] triangle =
            {
                new(
                    center.X - 4,
                    center.Y - 2),
                new(
                    center.X + 4,
                    center.Y - 2),
                new(
                    center.X,
                    center.Y + 3)
            };

            using var arrow =
                new SolidBrush(
                    Enabled
                        ? Color.FromArgb(
                            220,
                            220,
                            220)
                        : Color.FromArgb(
                            105,
                            105,
                            105));

            e.Graphics.FillPolygon(
                arrow,
                triangle);
        }

        protected override void OnMouseEnter(
            EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(
            EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(
            MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (Enabled &&
                e.Button ==
                MouseButtons.Left)
            {
                Focus();
                ShowDropDown();
            }
        }

        protected override void OnKeyDown(
            KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!Enabled)
                return;

            if (e.KeyCode == Keys.Space ||
                e.KeyCode == Keys.Enter ||
                e.KeyCode == Keys.Down)
            {
                ShowDropDown();
                e.Handled = true;
            }
        }

        private void ShowDropDown()
        {
            if (_items.Count == 0)
                return;

            _dropDown?.Close();
            _dropDown?.Dispose();

            int popupWidth =
                Math.Max(
                    Width,
                    300);

            int visibleRows =
                Math.Min(
                    MaxVisibleRows,
                    _items.Count);

            int popupHeight =
                Math.Max(
                    RowHeight + 2,
                    visibleRows *
                    RowHeight + 2);

            bool needsScroll =
                _items.Count >
                visibleRows;

            int scrollWidth =
                needsScroll
                    ? 13
                    : 0;

            var surface =
                new Panel
                {
                    Size =
                        new Size(
                            popupWidth,
                            popupHeight),
                    BackColor =
                        Color.FromArgb(
                            16,
                            16,
                            16)
                };

            var viewport =
                new Panel
                {
                    Location =
                        new Point(
                            1,
                            1),
                    Size =
                        new Size(
                            popupWidth -
                            scrollWidth -
                            2,
                            popupHeight -
                            2),
                    BackColor =
                        Color.FromArgb(
                            16,
                            16,
                            16)
                };

            var rows =
                new Panel
                {
                    Location =
                        Point.Empty,
                    Size =
                        new Size(
                            viewport.Width,
                            _items.Count *
                            RowHeight),
                    BackColor =
                        Color.FromArgb(
                            16,
                            16,
                            16)
                };

            viewport.Controls.Add(rows);
            surface.Controls.Add(viewport);

            var rowControls =
                new List<Panel>();

            for (int i = 0;
                 i < _items.Count;
                 i++)
            {
                int itemIndex = i;

                var row =
                    new Panel
                    {
                        Location =
                            new Point(
                                0,
                                i *
                                RowHeight),
                        Size =
                            new Size(
                                viewport.Width,
                                RowHeight),
                        BackColor =
                            i ==
                            _selectedIndex
                                ? Color.FromArgb(
                                    58,
                                    58,
                                    58)
                                : Color.FromArgb(
                                    16,
                                    16,
                                    16),
                        Cursor =
                            Cursors.Hand
                    };

                var label =
                    new Label
                    {
                        Dock = DockStyle.Fill,
                        Padding =
                            new Padding(
                                9,
                                0,
                                6,
                                0),
                        Text =
                            GetItemText(
                                _items[i]),
                        ForeColor =
                            Color.FromArgb(
                                240,
                                240,
                                240),
                        BackColor =
                            Color.Transparent,
                        Font = Font,
                        TextAlign =
                            ContentAlignment.MiddleLeft,
                        AutoEllipsis = true,
                        Cursor =
                            Cursors.Hand
                    };

                void choose()
                {
                    SetSelectedIndex(
                        itemIndex);

                    _dropDown?.Close();
                }

                void enter()
                {
                    row.BackColor =
                        Color.FromArgb(
                            46,
                            46,
                            46);
                }

                void leave()
                {
                    row.BackColor =
                        itemIndex ==
                        _selectedIndex
                            ? Color.FromArgb(
                                58,
                                58,
                                58)
                            : Color.FromArgb(
                                16,
                                16,
                                16);
                }

                row.Click +=
                    (_, _) =>
                        choose();

                label.Click +=
                    (_, _) =>
                        choose();

                row.MouseEnter +=
                    (_, _) =>
                        enter();

                label.MouseEnter +=
                    (_, _) =>
                        enter();

                row.MouseLeave +=
                    (_, _) =>
                        leave();

                label.MouseLeave +=
                    (_, _) =>
                        leave();

                row.Controls.Add(label);
                rows.Controls.Add(row);
                rowControls.Add(row);
            }

            DarkScrollBar? scrollbar = null;

            if (needsScroll)
            {
                scrollbar =
                    new DarkScrollBar
                    {
                        Orientation =
                            DarkScrollOrientation.Vertical,
                        Location =
                            new Point(
                                popupWidth -
                                scrollWidth -
                                1,
                                1),
                        Size =
                            new Size(
                                scrollWidth,
                                popupHeight -
                                2),
                        Minimum = 0,
                        Maximum =
                            _items.Count,
                        LargeChange =
                            visibleRows,
                        BackColor =
                            Color.FromArgb(
                                14,
                                14,
                                14)
                    };

                scrollbar.ValueChanged +=
                    (_, _) =>
                    {
                        rows.Top =
                            -scrollbar.Value *
                            RowHeight;
                    };

                surface.Controls.Add(
                    scrollbar);

                surface.MouseWheel +=
                    (_, e) =>
                    {
                        scrollbar.Value +=
                            e.Delta > 0
                                ? -1
                                : 1;
                    };

                viewport.MouseWheel +=
                    (_, e) =>
                    {
                        scrollbar.Value +=
                            e.Delta > 0
                                ? -1
                                : 1;
                    };

                rows.MouseWheel +=
                    (_, e) =>
                    {
                        scrollbar.Value +=
                            e.Delta > 0
                                ? -1
                                : 1;
                    };

                if (_selectedIndex >=
                    visibleRows)
                {
                    scrollbar.Value =
                        Math.Min(
                            scrollbar.EffectiveMaximum,
                            _selectedIndex -
                            visibleRows / 2);
                }
            }

            var host =
                new ToolStripControlHost(
                    surface)
                {
                    AutoSize = false,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty,
                    Size = surface.Size
                };

            _dropDown =
                new ToolStripDropDown
                {
                    AutoSize = false,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty,
                    BackColor =
                        Color.FromArgb(
                            58,
                            58,
                            58),
                    Size =
                        new Size(
                            popupWidth,
                            popupHeight)
                };

            _dropDown.Items.Add(host);

            _dropDown.Closed +=
                (_, _) =>
                    Invalidate();

            _dropDown.Show(
                this,
                new Point(
                    0,
                    Height));
        }

        private void SetSelectedIndex(
            int index)
        {
            if (index < -1 ||
                index >= _items.Count)
            {
                index = -1;
            }

            if (_selectedIndex == index)
                return;

            _selectedIndex = index;

            _selectedItem =
                index >= 0
                    ? _items[index]
                    : null;

            Invalidate();

            SelectedIndexChanged?.Invoke(
                this,
                EventArgs.Empty);
        }

        private static string GetItemText(
            object item) =>
            item?.ToString()
            ?? string.Empty;
    }

    public sealed class DarkComboOption
    {
        public string Value { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;

        public override string ToString() =>
            string.IsNullOrWhiteSpace(Label)
                ? Value
                : $"{Value}  —  {Label}";
    }
}
