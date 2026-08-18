using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DRW_Work_Tool.Core
{
    public static class DarkUi
    {
        private const int DefaultEndSpacing = 36;

        private static readonly ConditionalWeakTable<ScrollableControl, ScrollSpacingState>
            ScrollStates = new();

        [DllImport(
            "uxtheme.dll",
            CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr hWnd,
            string? pszSubAppName,
            string? pszSubIdList);

        /// <summary>
        /// Explicit dark scrollbar request.
        ///
        /// IMPORTANT:
        /// Native SetWindowTheme is called ONLY for the control that actually
        /// needs scrollbar theming. The global scroll policy must never theme
        /// every Label/Panel/Button/PictureBox created by an editor.
        /// </summary>
        public static void ApplyDarkScrollBar(
            Control control)
        {
            ApplyDarkThemeOnly(
                control);

            if (control is ScrollableControl scrollable)
            {
                ApplyScrollableEndSpacing(
                    scrollable,
                    DefaultEndSpacing);
            }
        }

        /// <summary>
        /// Global end-spacing policy.
        ///
        /// This deliberately DOES NOT call SetWindowTheme while traversing the
        /// tree. It only watches for ScrollableControl instances.
        /// </summary>
        public static void InstallGlobalScrollPolicy(
            Control root,
            int endSpacing = DefaultEndSpacing)
        {
            if (root == null)
            {
                throw new ArgumentNullException(
                    nameof(root));
            }

            InstallScrollPolicyTree(
                root,
                Math.Max(
                    16,
                    endSpacing));
        }

        private static void InstallScrollPolicyTree(
            Control control,
            int endSpacing)
        {
            if (control is ScrollableControl scrollable &&
                scrollable.AutoScroll)
            {
                ApplyScrollableEndSpacing(
                    scrollable,
                    endSpacing);
            }

            control.ControlAdded -=
                DynamicControlAdded;

            control.ControlAdded +=
                DynamicControlAdded;

            foreach (Control child
                     in control.Controls)
            {
                InstallScrollPolicyTree(
                    child,
                    endSpacing);
            }
        }

        private static void DynamicControlAdded(
            object? sender,
            ControlEventArgs e)
        {
            if (e.Control == null)
                return;

            // No native theming here. This path can run hundreds of times while
            // an XML editor is creating its cards.
            InstallScrollPolicyTree(
                e.Control,
                DefaultEndSpacing);

            if (sender is ScrollableControl scrollable &&
                scrollable.AutoScroll)
            {
                QueueRefresh(
                    scrollable);
            }
        }

        private static void ApplyDarkThemeOnly(
            Control control)
        {
            void Apply()
            {
                try
                {
                    if (control.IsHandleCreated)
                    {
                        SetWindowTheme(
                            control.Handle,
                            "DarkMode_Explorer",
                            null);
                    }
                }
                catch
                {
                    // Cosmetic only.
                }
            }

            if (control.IsHandleCreated)
                Apply();

            control.HandleCreated -=
                DarkHandleCreated;

            control.HandleCreated +=
                DarkHandleCreated;
        }

        private static void DarkHandleCreated(
            object? sender,
            EventArgs e)
        {
            if (sender is not Control control)
                return;

            try
            {
                if (control.IsHandleCreated)
                {
                    SetWindowTheme(
                        control.Handle,
                        "DarkMode_Explorer",
                        null);
                }
            }
            catch
            {
                // Cosmetic only.
            }
        }

        public static void ApplyScrollableEndSpacing(
            ScrollableControl scrollable,
            int endSpacing = DefaultEndSpacing)
        {
            if (scrollable == null)
            {
                throw new ArgumentNullException(
                    nameof(scrollable));
            }

            if (!scrollable.AutoScroll)
                return;

            ScrollSpacingState state =
                ScrollStates.GetValue(
                    scrollable,
                    _ =>
                        new ScrollSpacingState());

            state.EndSpacing =
                Math.Max(
                    state.EndSpacing,
                    Math.Max(
                        16,
                        endSpacing));

            if (scrollable is FlowLayoutPanel flow)
            {
                EnsureFlowEndSpacer(
                    flow,
                    state);
            }

            if (!state.EventsInstalled)
            {
                state.EventsInstalled = true;

                scrollable.ControlAdded +=
                    (_, e) =>
                    {
                        if (state.Updating)
                            return;

                        if (scrollable is FlowLayoutPanel flow &&
                            !ReferenceEquals(
                                e.Control,
                                state.FlowEndSpacer))
                        {
                            QueueRefresh(
                                flow);
                        }
                        else
                        {
                            QueueRefresh(
                                scrollable);
                        }
                    };

                scrollable.ControlRemoved +=
                    (_, _) =>
                        QueueRefresh(
                            scrollable);

                scrollable.SizeChanged +=
                    (_, _) =>
                        QueueRefresh(
                            scrollable);

                // Do NOT subscribe to Layout.
                // Layout fires repeatedly while dozens of editor controls are
                // being created and was another source of UI stalls.
            }

            EnsureEndSpacing(
                scrollable,
                state);
        }

        private static void EnsureFlowEndSpacer(
            FlowLayoutPanel flow,
            ScrollSpacingState state)
        {
            if (flow.IsDisposed ||
                state.Updating)
            {
                return;
            }

            try
            {
                state.Updating = true;

                Panel spacer;

                if (state.FlowEndSpacer == null ||
                    state.FlowEndSpacer.IsDisposed)
                {
                    spacer =
                        new Panel
                        {
                            Name =
                                "__GlobalScrollEndSpacer",
                            Height =
                                state.EndSpacing,
                            BackColor =
                                flow.BackColor,
                            Margin =
                                Padding.Empty,
                            TabStop = false
                        };

                    state.FlowEndSpacer =
                        spacer;
                }
                else
                {
                    spacer =
                        state.FlowEndSpacer;
                }

                spacer.Height =
                    state.EndSpacing;

                spacer.BackColor =
                    flow.BackColor;

                spacer.Width =
                    Math.Max(
                        1,
                        flow.ClientSize.Width -
                        flow.Padding.Horizontal -
                        SystemInformation.VerticalScrollBarWidth -
                        8);

                if (spacer.Parent != flow)
                {
                    flow.Controls.Add(
                        spacer);
                }

                int lastIndex =
                    Math.Max(
                        0,
                        flow.Controls.Count -
                        1);

                if (flow.Controls.GetChildIndex(
                        spacer) !=
                    lastIndex)
                {
                    flow.Controls.SetChildIndex(
                        spacer,
                        lastIndex);
                }
            }
            finally
            {
                state.Updating = false;
            }
        }

        private static void QueueRefresh(
            ScrollableControl scrollable)
        {
            if (scrollable.IsDisposed ||
                scrollable.Disposing ||
                !scrollable.IsHandleCreated)
            {
                return;
            }

            if (!ScrollStates.TryGetValue(
                scrollable,
                out ScrollSpacingState? state))
            {
                return;
            }

            if (state.RefreshQueued)
                return;

            state.RefreshQueued = true;

            try
            {
                scrollable.BeginInvoke(
                    new Action(
                        () =>
                        {
                            state.RefreshQueued = false;

                            if (scrollable.IsDisposed)
                                return;

                            EnsureEndSpacing(
                                scrollable,
                                state);
                        }));
            }
            catch
            {
                state.RefreshQueued = false;
            }
        }

        private static void EnsureEndSpacing(
            ScrollableControl scrollable,
            ScrollSpacingState state)
        {
            if (scrollable.IsDisposed ||
                !scrollable.AutoScroll ||
                state.Updating)
            {
                return;
            }

            if (scrollable is FlowLayoutPanel flow)
            {
                EnsureFlowEndSpacer(
                    flow,
                    state);

                return;
            }

            try
            {
                state.Updating = true;

                int lowestBottom = 0;
                int furthestRight = 0;

                foreach (Control child
                         in scrollable.Controls)
                {
                    if (!child.Visible)
                        continue;

                    lowestBottom =
                        Math.Max(
                            lowestBottom,
                            child.Bottom +
                            child.Margin.Bottom);

                    furthestRight =
                        Math.Max(
                            furthestRight,
                            child.Right +
                            child.Margin.Right);
                }

                if (lowestBottom <= 0)
                    return;

                Size current =
                    scrollable.AutoScrollMinSize;

                int wantedHeight =
                    lowestBottom +
                    state.EndSpacing;

                int wantedWidth =
                    Math.Max(
                        current.Width,
                        furthestRight);

                if (current.Height <
                    wantedHeight)
                {
                    scrollable.AutoScrollMinSize =
                        new Size(
                            wantedWidth,
                            wantedHeight);
                }
            }
            finally
            {
                state.Updating = false;
            }
        }

        private sealed class ScrollSpacingState
        {
            public int EndSpacing { get; set; } =
                DefaultEndSpacing;

            public bool EventsInstalled { get; set; }

            public bool Updating { get; set; }

            public bool RefreshQueued { get; set; }

            public Panel? FlowEndSpacer { get; set; }
        }
    }
}
