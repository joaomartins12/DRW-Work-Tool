using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DRW_Work_Tool.Core
{
    public sealed class EditorTabClosingEventArgs : EventArgs
    {
        public EditorTabClosingEventArgs(
            TabPage page)
        {
            Page = page;
        }

        public TabPage Page { get; }
        public bool Cancel { get; set; }
    }

    /// <summary>
    /// Custom dark tab UI.
    ///
    /// Visible:
    /// - custom tab header viewport
    /// - custom dark horizontal scrollbar when tabs overflow
    ///
    /// Technical:
    /// - NativePageHost is still a real TabControl because WinForms TabPage
    ///   requires one as parent
    /// - native tab headers are hidden by intercepting TCM_ADJUSTRECT
    /// </summary>
    public sealed class EditorTabControl : Panel
    {
        // WinForms TabControl keeps a few native frame pixels around the
        // selected TabPage even when TCM_ADJUSTRECT is overridden.
        // Reserve this amount INSIDE every TabPage so DockStyle.Top controls
        // never end up underneath that native frame.
        private const int PageContentTopInset = 35;

        private readonly Panel _tabArea;
        private readonly Panel _tabViewport;
        private readonly FlowLayoutPanel _tabStrip;
        private readonly DarkScrollBar _tabScroll;

        private readonly NativePageHost _pageHost;
        private readonly Panel _transitionLayer;

        private readonly Dictionary<TabPage, Panel> _headers =
            new();

        private readonly EditorTabPageCollection _pages;

        private readonly List<TabPage> _navigationHistory =
            new();

        private TabPage? _selectedTab;
        private int _transitionVersion;

        public event EventHandler<EditorTabClosingEventArgs>? TabClosing;
        public event EventHandler? SelectedIndexChanged;

        public EditorTabControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            UpdateStyles();

            BackColor =
                Color.FromArgb(
                    22,
                    22,
                    22);

            ForeColor =
                Color.FromArgb(
                    245,
                    245,
                    245);

            ItemSize =
                new Size(
                    190,
                    36);

            _tabArea =
                new Panel
                {
                    Dock = DockStyle.None,
                    Height = 49,
                    BackColor =
                        Color.FromArgb(
                            25,
                            25,
                            25)
                };

            _tabViewport =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 36,
                    BackColor =
                        Color.FromArgb(
                            25,
                            25,
                            25)
                };

            _tabStrip =
                new FlowLayoutPanel
                {
                    Location = Point.Empty,
                    Height = 36,
                    Width = 10,
                    AutoSize = true,
                    AutoSizeMode =
                        AutoSizeMode.GrowAndShrink,
                    FlowDirection =
                        FlowDirection.LeftToRight,
                    WrapContents = false,
                    AutoScroll = false,
                    BackColor =
                        Color.FromArgb(
                            25,
                            25,
                            25),
                    Padding = Padding.Empty,
                    Margin = Padding.Empty
                };

            _tabScroll =
                new DarkScrollBar
                {
                    Dock = DockStyle.Bottom,
                    Height = 12,
                    Orientation =
                        DarkScrollOrientation.Horizontal,
                    BackColor =
                        Color.FromArgb(
                            15,
                            15,
                            15),
                    Minimum = 0,
                    Maximum = 0,
                    LargeChange = 1,
                    Visible = false
                };

            _tabViewport.Controls.Add(
                _tabStrip);

            _tabArea.Controls.Add(
                _tabViewport);

            _tabArea.Controls.Add(
                _tabScroll);

            _pageHost =
                new NativePageHost
                {
                    Dock = DockStyle.None,
                    BackColor =
                        Color.FromArgb(
                            22,
                            22,
                            22),
                    ForeColor =
                        Color.FromArgb(
                            245,
                            245,
                            245),
                    Appearance =
                        TabAppearance.FlatButtons,
                    SizeMode =
                        TabSizeMode.Fixed,
                    ItemSize =
                        new Size(
                            1,
                            1),
                    Multiline = true,
                    Padding =
                        new Point(
                            0,
                            0)
                };

            _transitionLayer =
                new Panel
                {
                    Dock = DockStyle.None,
                    Visible = false,
                    BackColor =
                        Color.FromArgb(
                            22,
                            22,
                            22),
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };

            Controls.Add(
                _pageHost);

            Controls.Add(
                _transitionLayer);

            Controls.Add(
                _tabArea);

            _pages =
                new EditorTabPageCollection(
                    this);

            _tabScroll.ValueChanged +=
                (_, _) =>
                {
                    _tabStrip.Left =
                        -_tabScroll.Value;
                };

            _tabViewport.Resize +=
                (_, _) =>
                    RefreshTabScroll();

            _tabStrip.SizeChanged +=
                (_, _) =>
                    RefreshTabScroll();

            _tabViewport.MouseWheel +=
                (_, e) =>
                {
                    if (!_tabScroll.Visible)
                        return;

                    _tabScroll.Value +=
                        e.Delta > 0
                            ? -80
                            : 80;
                };

            _pageHost.SelectedIndexChanged +=
                (_, _) =>
                {
                    if (_pageHost.SelectedTab == null ||
                        ReferenceEquals(
                            _selectedTab,
                            _pageHost.SelectedTab))
                    {
                        return;
                    }

                    TabPage destination =
                        _pageHost.SelectedTab;

                    if (_selectedTab != null &&
                        _pages.Contains(
                            _selectedTab))
                    {
                        _navigationHistory.RemoveAll(
                            x =>
                                ReferenceEquals(
                                    x,
                                    _selectedTab));

                        _navigationHistory.Add(
                            _selectedTab);
                    }

                    _navigationHistory.RemoveAll(
                        x =>
                            ReferenceEquals(
                                x,
                                destination));

                    _selectedTab =
                        destination;

                    UpdateHeaders();

                    SelectedIndexChanged?.Invoke(
                        this,
                        EventArgs.Empty);
                };

            Resize +=
                (_, _) =>
                {
                    RefreshTabScroll();
                    LayoutEditorRegions();

                    if (_transitionLayer.Visible)
                        _transitionLayer.BringToFront();

                    _tabArea.BringToFront();
                };

            LayoutEditorRegions();
        }

        protected override void OnLayout(
            LayoutEventArgs levent)
        {
            base.OnLayout(
                levent);

            if (_tabArea != null &&
                _pageHost != null &&
                _transitionLayer != null)
            {
                LayoutEditorRegions();
            }
        }

        public Size ItemSize { get; set; }

        public EditorTabPageCollection TabPages =>
            _pages;

        public TabPage? SelectedTab
        {
            get => _selectedTab;
            set => SelectPage(value);
        }

        public int SelectedIndex =>
            _selectedTab == null
                ? -1
                : _pages.IndexOf(
                    _selectedTab);

        public new Rectangle DisplayRectangle =>
            new Rectangle(
                0,
                _tabArea.Bottom +
                PageContentTopInset,
                ClientSize.Width,
                Math.Max(
                    0,
                    ClientSize.Height -
                    _tabArea.Height -
                    PageContentTopInset));

        public Rectangle GetTabRect(
            int index)
        {
            if (index < 0 ||
                index >=
                _pages.Count)
            {
                return Rectangle.Empty;
            }

            TabPage page =
                _pages[index];

            if (!_headers.TryGetValue(
                page,
                out Panel? header))
            {
                return Rectangle.Empty;
            }

            Point location =
                PointToClient(
                    header.PointToScreen(
                        Point.Empty));

            return new Rectangle(
                location,
                header.Size);
        }

        internal void AddPage(
            TabPage page)
        {
            if (_headers.ContainsKey(
                page))
            {
                return;
            }

            page.UseVisualStyleBackColor =
                false;

            page.BackColor =
                Color.FromArgb(
                    22,
                    22,
                    22);

            page.ForeColor =
                Color.FromArgb(
                    245,
                    245,
                    245);

            page.Padding =
                new Padding(
                    0,
                    PageContentTopInset,
                    0,
                    0);

            _pageHost.TabPages.Add(
                page);

            Panel header =
                CreateHeader(
                    page);

            _headers[
                page] =
                header;

            _tabStrip.Controls.Add(
                header);

            page.TextChanged +=
                Page_TextChanged;

            RefreshTabScroll();

            if (_selectedTab == null)
            {
                SelectPage(
                    page);
            }
            else
            {
                UpdateHeaders();
            }
        }

        internal void RemovePage(
            TabPage page)
        {
            int removedIndex =
                _pages.IndexOf(
                    page);

            page.TextChanged -=
                Page_TextChanged;

            if (_headers.TryGetValue(
                page,
                out Panel? header))
            {
                _tabStrip.Controls.Remove(
                    header);

                header.Dispose();

                _headers.Remove(
                    page);
            }

            bool wasSelected =
                ReferenceEquals(
                    _selectedTab,
                    page);

            _navigationHistory.RemoveAll(
                historyPage =>
                    ReferenceEquals(
                        historyPage,
                        page));

            TabPage? destination = null;

            if (wasSelected)
            {
                destination =
                    PopLastAvailableHistoryPage();

                if (destination == null &&
                    _pages.Count > 0)
                {
                    int next =
                        Math.Min(
                            Math.Max(
                                0,
                                removedIndex),
                            _pages.Count - 1);

                    destination =
                        _pages[next];
                }

                ShowTabTransition(
                    destination,
                    destination == null
                        ? "Closing editor..."
                        : $"Returning to {destination.Text}...");
            }

            if (_pageHost.TabPages.Contains(
                page))
            {
                _pageHost.TabPages.Remove(
                    page);
            }

            RefreshTabScroll();

            if (wasSelected)
            {
                _selectedTab = null;

                if (destination != null &&
                    _pages.Contains(destination))
                {
                    SelectPage(
                        destination,
                        addCurrentToHistory:
                            false);
                }
                else
                {
                    SelectedIndexChanged?.Invoke(
                        this,
                        EventArgs.Empty);
                }

                CompleteTabTransition(
                    destination);
            }

            UpdateHeaders();
        }

        private void LayoutEditorRegions()
        {
            int width =
                Math.Max(
                    1,
                    ClientSize.Width);

            int height =
                Math.Max(
                    1,
                    ClientSize.Height);

            int tabHeight =
                Math.Max(
                    36,
                    _tabArea.Height);

            // Reserve real physical space for the custom tab strip.
            // The native TabControl and loading transition layer start only
            // BELOW this rectangle, so no editor content can ever be painted
            // underneath the tabs.
            _tabArea.SetBounds(
                0,
                0,
                width,
                tabHeight);

            int contentTop =
                tabHeight;

            int contentHeight =
                Math.Max(
                    1,
                    height -
                    contentTop);

            // Native TabControl still paints side/bottom frame pixels even
            // though its native headers are suppressed. We clip only those
            // safe edges. The TOP edge must remain aligned with contentTop:
            // otherwise the real TabPage content is pulled underneath the
            // custom tab strip.
            // IMPORTANT:
            // Never move the native host upward into the custom tab strip.
            // Doing so also moves the selected TabPage upward and makes the
            // first 30-40 px of every editor appear underneath the tabs.
            //
            // We only oversize horizontally and at the BOTTOM, where the
            // unwanted native frame can safely be clipped by this control.
            // The TOP of the selected page starts exactly after _tabArea.
            _pageHost.SetBounds(
                -5,
                contentTop,
                width + 10,
                contentHeight + 8);

            // The transition layer is NOT a native TabControl, therefore it
            // uses the exact real content rectangle.
            _transitionLayer.SetBounds(
                0,
                contentTop + PageContentTopInset,
                width,
                Math.Max(
                    1,
                    contentHeight - PageContentTopInset));

            _pageHost.SendToBack();

            if (_transitionLayer.Visible)
                _transitionLayer.BringToFront();

            _tabArea.BringToFront();
        }

        private void RefreshTabScroll()
        {
            int viewportWidth =
                Math.Max(
                    1,
                    _tabViewport.ClientSize.Width);

            int contentWidth =
                Math.Max(
                    0,
                    _tabStrip.PreferredSize.Width);

            bool overflow =
                contentWidth >
                viewportWidth;

            _tabScroll.Visible =
                overflow;

            _tabArea.Height =
                overflow
                    ? 49
                    : 37;

            _tabViewport.Height =
                36;

            LayoutEditorRegions();

            if (!overflow)
            {
                _tabScroll.Value = 0;
                _tabStrip.Left = 0;
                return;
            }

            _tabScroll.Minimum = 0;
            _tabScroll.Maximum =
                contentWidth;

            _tabScroll.LargeChange =
                viewportWidth;

            _tabScroll.Value =
                Math.Min(
                    _tabScroll.Value,
                    _tabScroll.EffectiveMaximum);

            _tabStrip.Left =
                -_tabScroll.Value;
        }

        private Panel CreateHeader(
            TabPage page)
        {
            var header =
                new Panel
                {
                    Width =
                        ItemSize.Width,
                    Height =
                        ItemSize.Height,
                    BackColor =
                        Color.FromArgb(
                            31,
                            31,
                            31),
                    Margin =
                        new Padding(
                            0,
                            0,
                            1,
                            0),
                    Cursor =
                        Cursors.Hand
                };

            var label =
                new Label
                {
                    Name =
                        "TabTitle",
                    Text =
                        page.Text,
                    ForeColor =
                        Color.FromArgb(
                            235,
                            235,
                            235),
                    BackColor =
                        Color.Transparent,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            12,
                            0),
                    Size =
                        new Size(
                            Math.Max(
                                20,
                                ItemSize.Width -
                                48),
                            ItemSize.Height),
                    TextAlign =
                        ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Cursor =
                        Cursors.Hand
                };

            var close =
                new Button
                {
                    Text = "×",
                    Size =
                        new Size(
                            30,
                            ItemSize.Height),
                    Location =
                        new Point(
                            ItemSize.Width -
                            32,
                            0),
                    FlatStyle =
                        FlatStyle.Flat,
                    BackColor =
                        Color.Transparent,
                    ForeColor =
                        Color.FromArgb(
                            190,
                            190,
                            190),
                    Font =
                        new Font(
                            "Segoe UI",
                            10F,
                            FontStyle.Bold),
                    TabStop = false,
                    Cursor =
                        Cursors.Hand
                };

            close.FlatAppearance.BorderSize =
                0;

            close.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(
                    82,
                    40,
                    40);

            close.FlatAppearance.MouseDownBackColor =
                Color.FromArgb(
                    105,
                    45,
                    45);

            header.Paint +=
                (_, e) =>
                {
                    bool selected =
                        ReferenceEquals(
                            _selectedTab,
                            page);

                    using var pen =
                        new Pen(
                            selected
                                ? Color.FromArgb(
                                    158,
                                    158,
                                    158)
                                : Color.FromArgb(
                                    48,
                                    48,
                                    48));

                    e.Graphics.DrawLine(
                        pen,
                        0,
                        header.Height - 1,
                        header.Width,
                        header.Height - 1);
                };

            void select(
                object? sender,
                EventArgs e)
            {
                SelectPageWithTransition(
                    page);
            }

            header.Click += select;
            label.Click += select;

            header.MouseEnter +=
                (_, _) =>
                {
                    if (!ReferenceEquals(
                        _selectedTab,
                        page))
                    {
                        header.BackColor =
                            Color.FromArgb(
                                39,
                                39,
                                39);
                    }
                };

            header.MouseLeave +=
                (_, _) =>
                {
                    if (!ReferenceEquals(
                        _selectedTab,
                        page))
                    {
                        header.BackColor =
                            Color.FromArgb(
                                31,
                                31,
                                31);
                    }
                };

            close.Click +=
                (_, _) =>
                {
                    var args =
                        new EditorTabClosingEventArgs(
                            page);

                    TabClosing?.Invoke(
                        this,
                        args);

                    if (args.Cancel)
                        return;

                    TabPages.Remove(
                        page);

                    page.Dispose();
                };

            header.Controls.Add(
                label);

            header.Controls.Add(
                close);

            return header;
        }

        private void EnsureHeaderVisible(
            TabPage page)
        {
            if (!_tabScroll.Visible ||
                !_headers.TryGetValue(
                    page,
                    out Panel? header))
            {
                return;
            }

            int left =
                header.Left -
                _tabScroll.Value;

            int right =
                left +
                header.Width;

            if (left < 0)
            {
                _tabScroll.Value =
                    Math.Max(
                        0,
                        header.Left);
            }
            else if (right >
                     _tabViewport.ClientSize.Width)
            {
                _tabScroll.Value =
                    Math.Min(
                        _tabScroll.EffectiveMaximum,
                        header.Right -
                        _tabViewport.ClientSize.Width);
            }
        }

        private void SelectPageWithTransition(
            TabPage page)
        {
            if (!_pages.Contains(page) ||
                ReferenceEquals(
                    _selectedTab,
                    page))
            {
                return;
            }

            EnsureHeaderVisible(
                page);

            int version =
                ShowTabTransition(
                    page,
                    $"Loading {page.Text}...");

            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (IsDisposed ||
                            version != _transitionVersion ||
                            !_pages.Contains(page))
                        {
                            return;
                        }

                        SelectPage(
                            page);

                        CompleteTabTransition(
                            page,
                            version);
                    }));
        }

        private int ShowTabTransition(
            TabPage? destination,
            string message)
        {
            int version =
                ++_transitionVersion;

            _transitionLayer.SuspendLayout();

            foreach (Control control
                     in _transitionLayer.Controls
                         .Cast<Control>()
                         .ToArray())
            {
                _transitionLayer.Controls.Remove(
                    control);

                control.Dispose();
            }

            string title =
                destination == null
                    ? "Updating Editor"
                    : destination.Text;

            var loading =
                new EditorLoadingView(
                    title,
                    message);

            _transitionLayer.Controls.Add(
                loading);

            LayoutEditorRegions();

            _transitionLayer.Visible = true;
            _transitionLayer.BringToFront();
            _tabArea.BringToFront();

            _transitionLayer.ResumeLayout(true);
            _transitionLayer.PerformLayout();
            _transitionLayer.Invalidate(true);
            _transitionLayer.Update();

            return version;
        }

        private void CompleteTabTransition(
            TabPage? destination,
            int? expectedVersion = null)
        {
            int version =
                expectedVersion ??
                _transitionVersion;

            BeginInvoke(
                new Action(
                    () =>
                    {
                        if (IsDisposed ||
                            version != _transitionVersion)
                        {
                            return;
                        }

                        if (destination != null &&
                            !destination.IsDisposed)
                        {
                            destination.PerformLayout();
                            destination.Invalidate(true);
                            destination.Update();
                        }

                        BeginInvoke(
                            new Action(
                                () =>
                                {
                                    if (IsDisposed ||
                                        version != _transitionVersion)
                                    {
                                        return;
                                    }

                                    HideTabTransition(
                                        version);
                                }));
                    }));
        }

        private void HideTabTransition(
            int version)
        {
            if (version != _transitionVersion)
                return;

            _transitionLayer.Visible = false;

            foreach (Control control
                     in _transitionLayer.Controls
                         .Cast<Control>()
                         .ToArray())
            {
                _transitionLayer.Controls.Remove(
                    control);

                control.Dispose();
            }

            _pageHost.SendToBack();
            _tabArea.BringToFront();

            Invalidate(true);
            Update();
        }

        private void SelectPage(
            TabPage? page,
            bool addCurrentToHistory = true)
        {
            if (page == null ||
                !_pages.Contains(
                    page) ||
                ReferenceEquals(
                    _selectedTab,
                    page))
            {
                return;
            }

            if (addCurrentToHistory &&
                _selectedTab != null &&
                _pages.Contains(
                    _selectedTab))
            {
                _navigationHistory.RemoveAll(
                    x =>
                        ReferenceEquals(
                            x,
                            _selectedTab));

                _navigationHistory.Add(
                    _selectedTab);
            }

            _navigationHistory.RemoveAll(
                x =>
                    ReferenceEquals(
                        x,
                        page));

            _selectedTab =
                page;

            _pageHost.SelectedTab =
                page;

            page.BackColor =
                Color.FromArgb(
                    22,
                    22,
                    22);

            page.ForeColor =
                Color.FromArgb(
                    245,
                    245,
                    245);

            UpdateHeaders();
            EnsureHeaderVisible(
                page);

            SelectedIndexChanged?.Invoke(
                this,
                EventArgs.Empty);
        }

        private TabPage? PopLastAvailableHistoryPage()
        {
            while (_navigationHistory.Count > 0)
            {
                int lastIndex =
                    _navigationHistory.Count - 1;

                TabPage candidate =
                    _navigationHistory[
                        lastIndex];

                _navigationHistory.RemoveAt(
                    lastIndex);

                if (_pages.Contains(
                    candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void UpdateHeaders()
        {
            foreach (KeyValuePair<TabPage, Panel> pair
                     in _headers)
            {
                bool selected =
                    ReferenceEquals(
                        pair.Key,
                        _selectedTab);

                pair.Value.BackColor =
                    selected
                        ? Color.FromArgb(
                            49,
                            49,
                            49)
                        : Color.FromArgb(
                            31,
                            31,
                            31);

                pair.Value.Invalidate();
            }
        }

        private void Page_TextChanged(
            object? sender,
            EventArgs e)
        {
            if (sender is not
                TabPage page)
            {
                return;
            }

            if (!_headers.TryGetValue(
                page,
                out Panel? header))
            {
                return;
            }

            Label? title =
                header.Controls
                    .OfType<Label>()
                    .FirstOrDefault(
                        x =>
                            x.Name ==
                            "TabTitle");

            if (title != null)
                title.Text = page.Text;
        }

        private void RefreshHeaderWidths()
        {
            foreach (Panel header
                     in _headers.Values)
            {
                header.Width =
                    ItemSize.Width;

                Button? close =
                    header.Controls
                        .OfType<Button>()
                        .FirstOrDefault();

                Label? label =
                    header.Controls
                        .OfType<Label>()
                        .FirstOrDefault();

                if (close != null)
                {
                    close.Left =
                        header.Width -
                        close.Width;
                }

                if (label != null)
                {
                    label.Width =
                        Math.Max(
                            20,
                            header.Width -
                            48);
                }
            }

            RefreshTabScroll();
        }

        private sealed class NativePageHost : TabControl
        {
            private const int TcmAdjustRect =
                0x1328;

            public NativePageHost()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint,
                    true);

                UpdateStyles();

                Appearance =
                    TabAppearance.FlatButtons;

                SizeMode =
                    TabSizeMode.Fixed;

                ItemSize =
                    new Size(
                        1,
                        1);

                Multiline = true;
            }

            protected override void WndProc(
                ref Message m)
            {
                if (m.Msg ==
                    TcmAdjustRect &&
                    !DesignMode)
                {
                    // Return the full client rectangle as the tab-page area.
                    // This prevents Windows from reserving native tab-header
                    // height above the actual TabPage.
                    if (m.LParam !=
                        IntPtr.Zero)
                    {
                        RECT rect =
                            Marshal.PtrToStructure<RECT>(
                                m.LParam);

                        rect.Left = 0;
                        rect.Top = 0;
                        rect.Right =
                            ClientSize.Width;
                        rect.Bottom =
                            ClientSize.Height;

                        Marshal.StructureToPtr(
                            rect,
                            m.LParam,
                            false);
                    }

                    m.Result =
                        IntPtr.Zero;

                    return;
                }

                base.WndProc(
                    ref m);
            }

            protected override void OnResize(
                EventArgs e)
            {
                base.OnResize(e);

                Invalidate(
                    invalidateChildren: false);
            }

            protected override void OnPaintBackground(
                PaintEventArgs pevent)
            {
                pevent.Graphics.Clear(
                    Color.FromArgb(
                        22,
                        22,
                        22));
            }

            [StructLayout(
                LayoutKind.Sequential)]
            private struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }
        }

        public sealed class EditorTabPageCollection :
            IEnumerable
        {
            private readonly EditorTabControl _owner;

            private readonly List<TabPage> _items =
                new();

            internal EditorTabPageCollection(
                EditorTabControl owner)
            {
                _owner = owner;
            }

            public int Count =>
                _items.Count;

            public TabPage this[int index] =>
                _items[index];

            public int IndexOf(
                TabPage page) =>
                _items.IndexOf(
                    page);

            public bool Contains(
                TabPage page) =>
                _items.Contains(
                    page);

            public void Add(
                TabPage page)
            {
                if (_items.Contains(
                    page))
                {
                    return;
                }

                _items.Add(
                    page);

                _owner.AddPage(
                    page);
            }

            public void Remove(
                TabPage page)
            {
                if (!_items.Contains(
                    page))
                {
                    return;
                }

                _items.Remove(
                    page);

                _owner.RemovePage(
                    page);
            }

            public IEnumerator GetEnumerator() =>
                _items.GetEnumerator();
        }
    }
}
