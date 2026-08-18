using DRW_Work_Tool.Core;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private sealed class ItemIconBrowserState
        {
            public required TabPage OwnerPage { get; init; }
            public required ItemEditState ItemState { get; init; }
            public required XElement IconNode { get; init; }
            public required ItemIconBrowserService Service { get; init; }

            public required Panel ScrollHost { get; init; }
            public required PictureBox Picture { get; init; }

            public required Label AtlasLabel { get; init; }
            public required Label SelectionLabel { get; init; }
            public required Label ZoomLabel { get; init; }
            public required Button ConfirmButton { get; init; }

            public int AtlasIndex { get; set; }
            public float Zoom { get; set; } = 1F;

            public ItemIconSlotInfo? SelectedSlot { get; set; }

            public bool Dragging { get; set; }
            public bool DragMoved { get; set; }
            public Point DragStart { get; set; }
            public Point ScrollStart { get; set; }
        }

        private async void OpenItemIconBrowser(
            TabPage ownerPage,
            ItemEditState itemState,
            XElement iconNode)
        {
            var page =
                CreateDarkTab(
                    "Select Item Icon");

            var opening =
                new EditorLoadingView(
                    "Loading Item Icon Browser",
                    "Preparing InterfaceIconMap, DDS atlases and mapped item icon slots.");
            page.Controls.Add(opening);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            var service = new ItemIconBrowserService();

            try
            {
                await System.Threading.Tasks.Task.Run(() => service.Load());
            }
            catch (Exception ex)
            {
                service.Dispose();
                if(!page.IsDisposed)opening.SetError("Item Icon Browser could not be loaded",ex.Message);
                return;
            }

            if (service.Atlases.Count == 0)
            {
                service.Dispose();
                if(!page.IsDisposed)opening.SetError(
                    "No Item icon atlases found",
                    "Run SETTINGS → Synchronize ImgDatabase and try again.");
                return;
            }

            if(page.IsDisposed)
            {
                service.Dispose();
                return;
            }

            page.SuspendLayout();

            uint currentIcon = 0;

            uint.TryParse(
                iconNode.Value,
                out currentIcon);

            var toolbar =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 88,
                    BackColor = CPanel
                };

            var previous =
                CreateEditorActionButton(
                    "◀ PREVIOUS");

            previous.Size =
                new Size(
                    110,
                    34);

            previous.Location =
                new Point(
                    14,
                    12);

            var next =
                CreateEditorActionButton(
                    "NEXT ▶");

            next.Size =
                new Size(
                    110,
                    34);

            next.Location =
                new Point(
                    132,
                    12);

            var zoomOut =
                CreateEditorActionButton(
                    "−");

            zoomOut.Size =
                new Size(
                    36,
                    34);

            zoomOut.Location =
                new Point(
                    258,
                    12);

            var zoomIn =
                CreateEditorActionButton(
                    "+");

            zoomIn.Size =
                new Size(
                    36,
                    34);

            zoomIn.Location =
                new Point(
                    298,
                    12);

            var reset =
                CreateEditorActionButton(
                    "RESET");

            reset.Size =
                new Size(
                    68,
                    34);

            reset.Location =
                new Point(
                    338,
                    12);

            var zoomLabel =
                new Label
                {
                    ForeColor = CMuted,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            8.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            416,
                            12),
                    Size =
                        new Size(
                            70,
                            34),
                    TextAlign =
                        ContentAlignment.MiddleLeft
                };

            var atlasLabel =
                new Label
                {
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            9.5F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            14,
                            57),
                    Size =
                        new Size(
                            405,
                            24),
                    AutoEllipsis = true
                };

            var selection =
                new Label
                {
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
                            430,
                            57),
                    Size =
                        new Size(
                            220,
                            24),
                    TextAlign =
                        ContentAlignment.MiddleRight,
                    AutoEllipsis = true,
                    Anchor =
                        AnchorStyles.Top |
                        AnchorStyles.Right
                };

            var confirmHost =
                new Panel
                {
                    Dock = DockStyle.Right,
                    Width = 150,
                    BackColor = Color.Transparent,
                    Padding =
                        new Padding(
                            8,
                            11,
                            14,
                            11)
                };

            var use =
                CreateEditorActionButton(
                    "CONFIRM");

            use.Dock = DockStyle.Fill;
            use.Enabled = false;

            confirmHost.Controls.Add(
                use);

            editorToolTip.SetToolTip(
                use,
                "Confirma o slot selecionado com 1 clique e aplica esse Icon ID ao item. " +
                "Duplo clique no icon continua a aplicar imediatamente.");

            toolbar.Controls.Add(confirmHost);

            toolbar.Controls.Add(previous);
            toolbar.Controls.Add(next);
            toolbar.Controls.Add(zoomOut);
            toolbar.Controls.Add(zoomIn);
            toolbar.Controls.Add(reset);
            toolbar.Controls.Add(zoomLabel);
            toolbar.Controls.Add(atlasLabel);
            toolbar.Controls.Add(selection);

            confirmHost.BringToFront();

            var scroll =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BackColor =
                        Color.FromArgb(
                            13,
                            13,
                            13),
                    Padding =
                        new Padding(
                            24)
                };

            DarkUi.ApplyDarkScrollBar(
                scroll);

            var picture =
                new PictureBox
                {
                    Location =
                        new Point(
                            24,
                            24),
                    SizeMode =
                        PictureBoxSizeMode.StretchImage,
                    BackColor =
                        Color.FromArgb(
                            9,
                            9,
                            9),
                    Cursor = Cursors.Cross
                };

            scroll.Controls.Add(
                picture);

            page.Controls.Add(scroll);
            page.Controls.Add(toolbar);

            var state =
                new ItemIconBrowserState
                {
                    OwnerPage = ownerPage,
                    ItemState = itemState,
                    IconNode = iconNode,
                    Service = service,
                    ScrollHost = scroll,
                    Picture = picture,
                    AtlasLabel = atlasLabel,
                    SelectionLabel = selection,
                    ZoomLabel = zoomLabel,
                    ConfirmButton = use,
                    AtlasIndex =
                        Math.Max(
                            0,
                            service.FindAtlasIndexForIcon(
                                currentIcon))
                };

            page.Tag = state;

            previous.Click +=
                (_, _) =>
                {
                    if (state.Service.Atlases.Count == 0)
                        return;

                    state.AtlasIndex =
                        (
                            state.AtlasIndex -
                            1 +
                            state.Service.Atlases.Count
                        ) %
                        state.Service.Atlases.Count;

                    state.SelectedSlot = null;

                    LoadItemIconAtlas(
                        state);
                };

            next.Click +=
                (_, _) =>
                {
                    if (state.Service.Atlases.Count == 0)
                        return;

                    state.AtlasIndex =
                        (
                            state.AtlasIndex +
                            1
                        ) %
                        state.Service.Atlases.Count;

                    state.SelectedSlot = null;

                    LoadItemIconAtlas(
                        state);
                };

            zoomOut.Click +=
                (_, _) =>
                {
                    state.Zoom =
                        Math.Max(
                            0.5F,
                            state.Zoom - 0.25F);

                    ResizeItemIconAtlas(
                        state);
                };

            zoomIn.Click +=
                (_, _) =>
                {
                    state.Zoom =
                        Math.Min(
                            6F,
                            state.Zoom + 0.25F);

                    ResizeItemIconAtlas(
                        state);
                };

            reset.Click +=
                (_, _) =>
                {
                    state.Zoom = 1F;
                    state.ScrollHost.AutoScrollPosition =
                        new Point(
                            0,
                            0);

                    ResizeItemIconAtlas(
                        state);
                };

            use.Click +=
                (_, _) =>
                    ApplySelectedItemIcon(
                        page,
                        state);

            picture.Paint +=
                (_, e) =>
                {
                    if (state.SelectedSlot == null)
                        return;

                    Rectangle slot =
                        state.SelectedSlot.Bounds;

                    Rectangle scaled =
                        new Rectangle(
                            (int)Math.Round(
                                slot.X *
                                state.Zoom),
                            (int)Math.Round(
                                slot.Y *
                                state.Zoom),
                            Math.Max(
                                1,
                                (int)Math.Round(
                                    slot.Width *
                                    state.Zoom)),
                            Math.Max(
                                1,
                                (int)Math.Round(
                                    slot.Height *
                                    state.Zoom)));

                    using var fill =
                        new SolidBrush(
                            Color.FromArgb(
                                58,
                                125,
                                220,
                                140));

                    using var border =
                        new Pen(
                            Color.FromArgb(
                                125,
                                220,
                                140),
                            2F);

                    e.Graphics.FillRectangle(
                        fill,
                        scaled);

                    e.Graphics.DrawRectangle(
                        border,
                        scaled);
                };

            picture.MouseDown +=
                (_, e) =>
                {
                    if (e.Button !=
                        MouseButtons.Left)
                    {
                        return;
                    }

                    state.Dragging = true;
                    state.DragMoved = false;
                    state.DragStart = e.Location;
                    state.ScrollStart =
                        new Point(
                            -state.ScrollHost.AutoScrollPosition.X,
                            -state.ScrollHost.AutoScrollPosition.Y);

                    picture.Cursor =
                        Cursors.SizeAll;
                };

            picture.MouseMove +=
                (_, e) =>
                {
                    if (!state.Dragging)
                        return;

                    int dx =
                        e.X -
                        state.DragStart.X;

                    int dy =
                        e.Y -
                        state.DragStart.Y;

                    if (Math.Abs(dx) > 3 ||
                        Math.Abs(dy) > 3)
                    {
                        state.DragMoved = true;
                    }

                    if (!state.DragMoved)
                        return;

                    state.ScrollHost.AutoScrollPosition =
                        new Point(
                            Math.Max(
                                0,
                                state.ScrollStart.X - dx),
                            Math.Max(
                                0,
                                state.ScrollStart.Y - dy));
                };

            picture.MouseUp +=
                (_, e) =>
                {
                    if (e.Button !=
                        MouseButtons.Left)
                    {
                        return;
                    }

                    bool wasDrag =
                        state.DragMoved;

                    state.Dragging = false;
                    state.DragMoved = false;
                    picture.Cursor =
                        Cursors.Cross;

                    if (wasDrag)
                        return;

                    SelectItemIconAtPoint(
                        state,
                        e.Location);
                };

            picture.MouseDoubleClick +=
                (_, e) =>
                {
                    SelectItemIconAtPoint(
                        state,
                        e.Location);

                    if (state.SelectedSlot != null)
                    {
                        ApplySelectedItemIcon(
                            page,
                            state);
                    }
                };

            page.Disposed +=
                (_, _) =>
                    service.Dispose();

            opening.BringToFront();
            page.ResumeLayout(true);
            opening.Refresh();

            LoadItemIconAtlas(
                state,
                currentIcon);

            page.Controls.Remove(opening);
            opening.Dispose();
            page.PerformLayout();
            page.Update();
        }

        private void LoadItemIconAtlas(
            ItemIconBrowserState state,
            uint preferredIcon = 0)
        {
            if (state.AtlasIndex < 0 ||
                state.AtlasIndex >=
                state.Service.Atlases.Count)
            {
                return;
            }

            ItemIconAtlasInfo atlas =
                state.Service.Atlases[
                    state.AtlasIndex];

            Bitmap bitmap =
                state.Service.GetAtlasBitmap(
                    atlas);

            state.Picture.Image =
                bitmap;

            state.AtlasLabel.Text =
                $"{atlas.Name}   •   {bitmap.Width}×{bitmap.Height}   •   {atlas.Slots.Count:N0} mapped slots";

            if (preferredIcon != 0)
            {
                state.SelectedSlot =
                    atlas.Slots
                        .FirstOrDefault(
                            x =>
                                x.Id ==
                                preferredIcon);
            }

            UpdateItemIconSelectionUi(
                state);

            ResizeItemIconAtlas(
                state);

            if (state.SelectedSlot != null)
            {
                ScrollSelectedItemIconIntoView(
                    state);
            }
        }

        private void ResizeItemIconAtlas(
            ItemIconBrowserState state)
        {
            if (state.Picture.Image == null)
                return;

            state.Picture.Size =
                new Size(
                    Math.Max(
                        1,
                        (int)Math.Round(
                            state.Picture.Image.Width *
                            state.Zoom)),
                    Math.Max(
                        1,
                        (int)Math.Round(
                            state.Picture.Image.Height *
                            state.Zoom)));

            state.ZoomLabel.Text =
                $"{state.Zoom * 100F:0}%";

            state.Picture.Invalidate();
        }

        private void SelectItemIconAtPoint(
            ItemIconBrowserState state,
            Point picturePoint)
        {
            if (state.AtlasIndex < 0 ||
                state.AtlasIndex >=
                state.Service.Atlases.Count)
            {
                return;
            }

            Point imagePoint =
                new Point(
                    (int)Math.Floor(
                        picturePoint.X /
                        state.Zoom),
                    (int)Math.Floor(
                        picturePoint.Y /
                        state.Zoom));

            ItemIconAtlasInfo atlas =
                state.Service.Atlases[
                    state.AtlasIndex];

            state.SelectedSlot =
                state.Service.FindSlotAt(
                    atlas,
                    imagePoint);

            UpdateItemIconSelectionUi(
                state);

            state.Picture.Invalidate();
        }

        private void UpdateItemIconSelectionUi(
            ItemIconBrowserState state)
        {
            if (state.SelectedSlot == null)
            {
                state.SelectionLabel.Text =
                    "1 click = select • CONFIRM = apply";

                state.ConfirmButton.Enabled =
                    false;

                return;
            }

            state.SelectionLabel.Text =
                $"Selected Icon ID: {state.SelectedSlot.Id}";

            state.ConfirmButton.Enabled =
                true;
        }

        private void ScrollSelectedItemIconIntoView(
            ItemIconBrowserState state)
        {
            if (state.SelectedSlot == null)
                return;

            Rectangle slot =
                state.SelectedSlot.Bounds;

            int centerX =
                (int)Math.Round(
                    (
                        slot.X +
                        slot.Width / 2F
                    ) *
                    state.Zoom);

            int centerY =
                (int)Math.Round(
                    (
                        slot.Y +
                        slot.Height / 2F
                    ) *
                    state.Zoom);

            state.ScrollHost.AutoScrollPosition =
                new Point(
                    Math.Max(
                        0,
                        centerX -
                        state.ScrollHost.ClientSize.Width / 2),
                    Math.Max(
                        0,
                        centerY -
                        state.ScrollHost.ClientSize.Height / 2));
        }

        private void ApplySelectedItemIcon(
            TabPage browserPage,
            ItemIconBrowserState state)
        {
            if (state.SelectedSlot == null)
                return;

            string id =
                state.SelectedSlot.Id.ToString();

            state.IconNode.Value =
                id;

            if (state.ItemState.Editors.TryGetValue(
                state.IconNode,
                out Control? control) &&
                control is TextBox iconText)
            {
                iconText.Text = id;
            }

            MarkItemDirty(
                state.OwnerPage,
                state.ItemState);

            UpdateItemIconPreview(
                state.ItemState);

            editorTabs.TabPages.Remove(
                browserPage);

            browserPage.Dispose();

            editorTabs.SelectedTab =
                state.OwnerPage;
        }
    }
}
