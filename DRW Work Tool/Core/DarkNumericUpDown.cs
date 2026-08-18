using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Dark integer numeric editor used instead of native NumericUpDown,
    /// avoiding the white Win32 spinner buttons.
    /// </summary>
    public sealed class DarkNumericUpDown : UserControl
    {
        private readonly TextBox _text;
        private readonly Button _up;
        private readonly Button _down;

        private decimal _minimum;
        private decimal _maximum = 100;
        private decimal _value;

        public event EventHandler? ValueChanged;

        public DarkNumericUpDown()
        {
            Height = 29;
            BackColor =
                Color.FromArgb(
                    13,
                    13,
                    13);

            _text =
                new TextBox
                {
                    BorderStyle =
                        BorderStyle.None,
                    BackColor =
                        Color.FromArgb(
                            13,
                            13,
                            13),
                    ForeColor =
                        Color.FromArgb(
                            240,
                            240,
                            240),
                    Font =
                        new Font(
                            "Segoe UI",
                            9.4F),
                    TextAlign =
                        HorizontalAlignment.Left
                };

            _up =
                CreateArrowButton("▲");

            _down =
                CreateArrowButton("▼");

            Controls.Add(_text);
            Controls.Add(_up);
            Controls.Add(_down);

            Resize +=
                (_, _) =>
                    LayoutControls();

            _up.Click +=
                (_, _) =>
                    Value =
                        Math.Min(
                            Maximum,
                            Value + 1);

            _down.Click +=
                (_, _) =>
                    Value =
                        Math.Max(
                            Minimum,
                            Value - 1);

            _text.Validating +=
                (_, _) =>
                    CommitText();

            _text.KeyDown +=
                (_, e) =>
                {
                    if (e.KeyCode == Keys.Up)
                    {
                        Value =
                            Math.Min(
                                Maximum,
                                Value + 1);

                        e.Handled = true;
                    }
                    else if (e.KeyCode == Keys.Down)
                    {
                        Value =
                            Math.Max(
                                Minimum,
                                Value - 1);

                        e.Handled = true;
                    }
                    else if (e.KeyCode == Keys.Enter)
                    {
                        CommitText();
                        e.Handled = true;
                    }
                };

            Paint +=
                (_, e) =>
                {
                    using var p =
                        new Pen(
                            Color.FromArgb(
                                76,
                                76,
                                76));

                    e.Graphics.DrawRectangle(
                        p,
                        0,
                        0,
                        Width - 1,
                        Height - 1);
                };

            LayoutControls();
            UpdateText();
        }

        public decimal Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;

                if (_value < _minimum)
                    Value = _minimum;
            }
        }

        public decimal Maximum
        {
            get => _maximum;
            set
            {
                _maximum = value;

                if (_value > _maximum)
                    Value = _maximum;
            }
        }

        public decimal Value
        {
            get => _value;
            set
            {
                decimal next =
                    Math.Max(
                        Minimum,
                        Math.Min(
                            Maximum,
                            value));

                if (_value == next)
                {
                    UpdateText();
                    return;
                }

                _value = next;
                UpdateText();

                ValueChanged?.Invoke(
                    this,
                    EventArgs.Empty);
            }
        }

        private Button CreateArrowButton(
            string text)
        {
            var button =
                new Button
                {
                    Text = text,
                    FlatStyle =
                        FlatStyle.Flat,
                    BackColor =
                        Color.FromArgb(
                            34,
                            34,
                            34),
                    ForeColor =
                        Color.FromArgb(
                            210,
                            210,
                            210),
                    Font =
                        new Font(
                            "Segoe UI",
                            6F,
                            FontStyle.Bold),
                    TabStop = false,
                    Padding = Padding.Empty,
                    Margin = Padding.Empty
                };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(
                    52,
                    52,
                    52);

            button.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(
                    68,
                    68,
                    68);

            return button;
        }

        private void LayoutControls()
        {
            int spinnerWidth = 24;

            _text.Location =
                new Point(
                    8,
                    6);

            _text.Size =
                new Size(
                    Math.Max(
                        20,
                        Width -
                        spinnerWidth -
                        14),
                    Math.Max(
                        18,
                        Height - 10));

            int half =
                Math.Max(
                    12,
                    Height / 2);

            _up.Bounds =
                new Rectangle(
                    Width - spinnerWidth - 1,
                    1,
                    spinnerWidth,
                    half - 1);

            _down.Bounds =
                new Rectangle(
                    Width - spinnerWidth - 1,
                    half,
                    spinnerWidth,
                    Height - half - 1);
        }

        private void CommitText()
        {
            if (!decimal.TryParse(
                _text.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out decimal parsed))
            {
                UpdateText();
                return;
            }

            Value = parsed;
        }

        private void UpdateText()
        {
            _text.Text =
                decimal.Truncate(
                    _value)
                    .ToString(
                        CultureInfo.InvariantCulture);
        }
    }
}
