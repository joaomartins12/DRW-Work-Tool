using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private bool _skillBuffIconBridgeInitialized;

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            InitializeEditorPolish();
            InitializeSkillBuffIconBridge();
            InstallDigimonBookRuntimeHooks();
            RefreshEditorPolish();
            RefreshSkillBuffIconBridge();
        }

        private void InitializeSkillBuffIconBridge()
        {
            if (_skillBuffIconBridgeInitialized || editorTabs == null)
                return;

            _skillBuffIconBridgeInitialized = true;
            editorTabs.SelectedIndexChanged += (_, _) => RefreshSkillBuffIconBridge();
            editorTabs.ControlAdded += (_, _) => BeginInvoke(new Action(RefreshSkillBuffIconBridge));
        }

        private void RefreshSkillBuffIconBridge()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            TabPage? page = editorTabs.SelectedTab;
            if (page == null || page.IsDisposed || page.Tag == null)
                return;

            string stateName = page.Tag.GetType().Name;

            if (stateName.Contains("SkillEditState", StringComparison.Ordinal))
                ReplaceIconActionButton(page, "SELECT ICON", "s_nIcon", "Select Skill Icon");
            else if (stateName.Contains("BuffEditState", StringComparison.Ordinal))
                ReplaceIconActionButton(page, "REFRESH ICON", "s_nBuffIcon", "Select Buff Icon");
        }

        private void ReplaceIconActionButton(TabPage page, string originalText, string xmlElement, string pickerTitle)
        {
            string bridgeName = "SharedAtlasPicker_" + xmlElement;
            if (FindControlRecursive(page, bridgeName) is Button existingBridge)
            {
                existingBridge.BringToFront();
                return;
            }

            Button? original = EnumerateControlsForIconBridge(page)
                .OfType<Button>()
                .FirstOrDefault(x => x.Text.Equals(originalText, StringComparison.OrdinalIgnoreCase));

            if (original == null || original.Parent == null)
                return;

            Control parent = original.Parent;
            var browse = CreateEditorActionButton("SELECT ICON");
            browse.Name = bridgeName;
            browse.Location = original.Location;
            browse.Size = original.Size;
            browse.Anchor = original.Anchor;
            browse.TabIndex = original.TabIndex;

            editorToolTip.SetToolTip(
                browse,
                "Open the sicon01-sicon07 skill atlases. Click a mapped slot and CONFIRM to apply its Icon ID.");

            browse.Click += async (_, _) =>
            {
                object? state = page.Tag;
                if (state == null)
                    return;

                XElement? working = ReadBridgeStateMember(state, "Working") as XElement;
                if (working == null)
                    return;

                uint current = 0;
                uint.TryParse(
                    working.Element(xmlElement)?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out current);

                uint? selected = await OpenSkillAtlasIconBrowserAsync(current, pickerTitle);
                if (!selected.HasValue || page.IsDisposed)
                    return;

                ApplySharedAtlasIconToEditor(page, state, working, xmlElement, selected.Value);
            };

            original.Visible = false;
            parent.Controls.Add(browse);
            browse.BringToFront();
        }

        private void ApplySharedAtlasIconToEditor(TabPage page, object state, XElement working, string xmlElement, uint iconId)
        {
            XElement? element = working.Element(xmlElement);
            if (element == null)
            {
                element = new XElement(xmlElement, iconId);
                working.Add(element);
            }
            else
            {
                element.Value = iconId.ToString(CultureInfo.InvariantCulture);
            }

            object? fieldsObject = ReadBridgeStateMember(state, "Fields");
            if (fieldsObject is IDictionary<string, TextBox> fields &&
                fields.TryGetValue(xmlElement, out TextBox? box) &&
                !box.IsDisposed)
            {
                box.Text = iconId.ToString(CultureInfo.InvariantCulture);
            }

            if (ReadBridgeStateMember(state, "IconIdLabel") is Label iconLabel && !iconLabel.IsDisposed)
                iconLabel.Text = $"Icon ID: {iconId}";

            if (ReadBridgeStateMember(state, "Icon") is PictureBox iconBox && !iconBox.IsDisposed)
            {
                Image? old = iconBox.Image;
                iconBox.Image = ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Skill");
                old?.Dispose();
            }

            SetBridgeStateMember(state, "Dirty", true);

            Label? dirtyLabel = EnumerateControlsForIconBridge(page)
                .OfType<Label>()
                .FirstOrDefault(x =>
                    x.Text.Equals("Saved", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Equals("SAVED", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Contains("UNSAVED", StringComparison.OrdinalIgnoreCase));

            if (dirtyLabel != null)
            {
                dirtyLabel.Text = "UNSAVED CHANGES";
                dirtyLabel.ForeColor = Color.FromArgb(255, 190, 90);
            }
        }

        private static object? ReadBridgeStateMember(object state, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = state.GetType();
            FieldInfo? field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(state);
            PropertyInfo? property = type.GetProperty(name, flags);
            return property?.GetValue(state);
        }

        private static void SetBridgeStateMember(object state, string name, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = state.GetType();
            FieldInfo? field = type.GetField(name, flags);
            if (field != null)
            {
                field.SetValue(state, value);
                return;
            }
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property?.CanWrite == true)
                property.SetValue(state, value);
        }

        private static Control? FindControlRecursive(Control root, string name)
        {
            foreach (Control child in root.Controls)
            {
                if (child.Name.Equals(name, StringComparison.Ordinal))
                    return child;
                Control? nested = FindControlRecursive(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static IEnumerable<Control> EnumerateControlsForIconBridge(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in EnumerateControlsForIconBridge(child))
                    yield return nested;
            }
        }

        private sealed class SharedSkillAtlasInfo
        {
            public string Name { get; init; } = string.Empty;
            public string SourcePath { get; init; } = string.Empty;
            public List<ItemIconSlotInfo> Slots { get; init; } = new();
        }

        private sealed class SharedSkillAtlasBrowserState
        {
            public required IReadOnlyList<SharedSkillAtlasInfo> Atlases { get; init; }
            public required Dictionary<string, Bitmap> BitmapCache { get; init; }
            public required PictureBox Picture { get; init; }
            public required Panel Scroll { get; init; }
            public required Label AtlasLabel { get; init; }
            public required Label SelectionLabel { get; init; }
            public required Label ZoomLabel { get; init; }
            public required Button Confirm { get; init; }
            public required TaskCompletionSource<uint?> Completion { get; init; }
            public int AtlasIndex { get; set; }
            public float Zoom { get; set; } = 1F;
            public ItemIconSlotInfo? Selected { get; set; }
            public bool Dragging { get; set; }
            public bool DragMoved { get; set; }
            public Point DragStart { get; set; }
            public Point ScrollStart { get; set; }
        }

        private async Task<uint?> OpenSkillAtlasIconBrowserAsync(uint currentIcon, string title)
        {
            var completion = new TaskCompletionSource<uint?>();
            var page = CreateDarkTab(title);
            page.Name = $"shared-skill-atlas:{Guid.NewGuid():N}";

            var loading = new EditorLoadingView(
                "Loading Skill Icon Atlases",
                "Preparing sicon01-sicon07 skill atlases and their mapped Icon IDs.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            List<SharedSkillAtlasInfo> atlases;
            try
            {
                atlases = await Task.Run(LoadSharedSkillAtlases);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                    loading.SetError("Skill icon atlases could not be loaded", ex.Message);
                return null;
            }

            if (page.IsDisposed)
                return null;

            if (atlases.Count == 0)
            {
                loading.SetError(
                    "No sicon atlases found",
                    "Run SETTINGS → Synchronize ImageDatabase / Reajuste Analyse Icons first. Expected atlases are sicon01 through sicon07.");
                return null;
            }

            page.Controls.Clear();

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = CPanel };
            var previous = CreateEditorActionButton("◀ PREVIOUS");
            previous.Location = new Point(14, 12); previous.Size = new Size(110, 34);
            var next = CreateEditorActionButton("NEXT ▶");
            next.Location = new Point(132, 12); next.Size = new Size(110, 34);
            var zoomOut = CreateEditorActionButton("−");
            zoomOut.Location = new Point(258, 12); zoomOut.Size = new Size(36, 34);
            var zoomIn = CreateEditorActionButton("+");
            zoomIn.Location = new Point(298, 12); zoomIn.Size = new Size(36, 34);
            var reset = CreateEditorActionButton("RESET");
            reset.Location = new Point(338, 12); reset.Size = new Size(68, 34);

            var zoomLabel = new Label
            {
                ForeColor = CMuted,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Location = new Point(416, 12),
                Size = new Size(70, 34),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var atlasLabel = new Label
            {
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Location = new Point(14, 57),
                Size = new Size(420, 24),
                AutoEllipsis = true
            };

            var selectionLabel = new Label
            {
                ForeColor = Color.FromArgb(125, 220, 140),
                Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                Location = new Point(440, 57),
                Size = new Size(220, 24),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var confirmHost = new Panel
            {
                Dock = DockStyle.Right,
                Width = 150,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 11, 14, 11)
            };
            var confirm = CreateEditorActionButton("CONFIRM");
            confirm.Dock = DockStyle.Fill;
            confirm.Enabled = false;
            confirmHost.Controls.Add(confirm);

            toolbar.Controls.Add(confirmHost);
            toolbar.Controls.Add(previous); toolbar.Controls.Add(next);
            toolbar.Controls.Add(zoomOut); toolbar.Controls.Add(zoomIn); toolbar.Controls.Add(reset);
            toolbar.Controls.Add(zoomLabel); toolbar.Controls.Add(atlasLabel); toolbar.Controls.Add(selectionLabel);
            confirmHost.BringToFront();

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(13, 13, 13),
                Padding = new Padding(24)
            };
            DarkUi.ApplyDarkScrollBar(scroll);

            var picture = new PictureBox
            {
                Location = new Point(24, 24),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.FromArgb(9, 9, 9),
                Cursor = Cursors.Cross
            };

            scroll.Controls.Add(picture);
            page.Controls.Add(scroll);
            page.Controls.Add(toolbar);

            int initialAtlas = 0;
            for (int i = 0; i < atlases.Count; i++)
            {
                if (atlases[i].Slots.Any(x => x.Id == currentIcon))
                {
                    initialAtlas = i;
                    break;
                }
            }

            var state = new SharedSkillAtlasBrowserState
            {
                Atlases = atlases,
                BitmapCache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase),
                Picture = picture,
                Scroll = scroll,
                AtlasLabel = atlasLabel,
                SelectionLabel = selectionLabel,
                ZoomLabel = zoomLabel,
                Confirm = confirm,
                Completion = completion,
                AtlasIndex = initialAtlas
            };

            void CloseWith(uint? value)
            {
                if (!completion.Task.IsCompleted)
                    completion.TrySetResult(value);
                if (editorTabs.TabPages.Contains(page))
                    editorTabs.TabPages.Remove(page);
                page.Dispose();
            }

            void UpdateSelection()
            {
                if (state.Selected == null)
                {
                    selectionLabel.Text = "1 click = select • CONFIRM = apply";
                    confirm.Enabled = false;
                }
                else
                {
                    selectionLabel.Text = $"Selected Icon ID: {state.Selected.Id}";
                    confirm.Enabled = true;
                }
                picture.Invalidate();
            }

            void ResizeAtlas()
            {
                if (picture.Image == null)
                    return;
                picture.Size = new Size(
                    Math.Max(1, (int)Math.Round(picture.Image.Width * state.Zoom)),
                    Math.Max(1, (int)Math.Round(picture.Image.Height * state.Zoom)));
                zoomLabel.Text = $"{state.Zoom * 100F:0}%";
                picture.Invalidate();
            }

            void LoadAtlas(uint preferred = 0)
            {
                SharedSkillAtlasInfo atlas = state.Atlases[state.AtlasIndex];
                if (!state.BitmapCache.TryGetValue(atlas.SourcePath, out Bitmap? bitmap))
                {
                    bitmap = LoadSharedSkillAtlasBitmap(atlas.SourcePath);
                    state.BitmapCache[atlas.SourcePath] = bitmap;
                }
                picture.Image = bitmap;
                atlasLabel.Text = $"{atlas.Name}   •   {bitmap.Width}×{bitmap.Height}   •   {atlas.Slots.Count:N0} mapped slots";
                state.Selected = preferred == 0 ? null : atlas.Slots.FirstOrDefault(x => x.Id == preferred);
                UpdateSelection();
                ResizeAtlas();
            }

            previous.Click += (_, _) => { state.AtlasIndex = (state.AtlasIndex - 1 + state.Atlases.Count) % state.Atlases.Count; LoadAtlas(); };
            next.Click += (_, _) => { state.AtlasIndex = (state.AtlasIndex + 1) % state.Atlases.Count; LoadAtlas(); };
            zoomOut.Click += (_, _) => { state.Zoom = Math.Max(0.5F, state.Zoom - 0.25F); ResizeAtlas(); };
            zoomIn.Click += (_, _) => { state.Zoom = Math.Min(6F, state.Zoom + 0.25F); ResizeAtlas(); };
            reset.Click += (_, _) => { state.Zoom = 1F; state.Scroll.AutoScrollPosition = Point.Empty; ResizeAtlas(); };
            confirm.Click += (_, _) => { if (state.Selected != null) CloseWith(state.Selected.Id); };

            picture.Paint += (_, e) =>
            {
                if (state.Selected == null) return;
                Rectangle b = state.Selected.Bounds;
                Rectangle scaled = new Rectangle(
                    (int)Math.Round(b.X * state.Zoom),
                    (int)Math.Round(b.Y * state.Zoom),
                    Math.Max(1, (int)Math.Round(b.Width * state.Zoom)),
                    Math.Max(1, (int)Math.Round(b.Height * state.Zoom)));
                using var fill = new SolidBrush(Color.FromArgb(58, 125, 220, 140));
                using var border = new Pen(Color.FromArgb(125, 220, 140), 2F);
                e.Graphics.FillRectangle(fill, scaled);
                e.Graphics.DrawRectangle(border, scaled);
            };

            picture.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                state.Dragging = true; state.DragMoved = false; state.DragStart = e.Location;
                state.ScrollStart = new Point(-state.Scroll.AutoScrollPosition.X, -state.Scroll.AutoScrollPosition.Y);
                picture.Cursor = Cursors.SizeAll;
            };

            picture.MouseMove += (_, e) =>
            {
                if (!state.Dragging) return;
                int dx = e.X - state.DragStart.X; int dy = e.Y - state.DragStart.Y;
                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) state.DragMoved = true;
                if (!state.DragMoved) return;
                state.Scroll.AutoScrollPosition = new Point(
                    Math.Max(0, state.ScrollStart.X - dx),
                    Math.Max(0, state.ScrollStart.Y - dy));
            };

            picture.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                bool moved = state.DragMoved;
                state.Dragging = false; state.DragMoved = false; picture.Cursor = Cursors.Cross;
                if (moved) return;
                Point imagePoint = new Point((int)Math.Floor(e.X / state.Zoom), (int)Math.Floor(e.Y / state.Zoom));
                SharedSkillAtlasInfo atlas = state.Atlases[state.AtlasIndex];
                state.Selected = atlas.Slots.FirstOrDefault(x => x.Bounds.Contains(imagePoint));
                UpdateSelection();
            };

            picture.MouseDoubleClick += (_, e) =>
            {
                Point imagePoint = new Point((int)Math.Floor(e.X / state.Zoom), (int)Math.Floor(e.Y / state.Zoom));
                SharedSkillAtlasInfo atlas = state.Atlases[state.AtlasIndex];
                ItemIconSlotInfo? slot = atlas.Slots.FirstOrDefault(x => x.Bounds.Contains(imagePoint));
                if (slot != null) CloseWith(slot.Id);
            };

            page.Disposed += (_, _) =>
            {
                foreach (Bitmap bitmap in state.BitmapCache.Values) bitmap.Dispose();
                state.BitmapCache.Clear();
                if (!completion.Task.IsCompleted) completion.TrySetResult(null);
            };

            LoadAtlas(currentIcon);
            return await completion.Task;
        }

        private static Bitmap LoadSharedSkillAtlasBitmap(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Skill atlas path is empty.");

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "Skill atlas file was not found.",
                    path);

            string extension =
                Path.GetExtension(path);

            if (extension.Equals(
                    ".dds",
                    StringComparison.OrdinalIgnoreCase))
            {
                return DdsImageLoader.LoadBitmap(path);
            }

            if (extension.Equals(
                    ".tga",
                    StringComparison.OrdinalIgnoreCase))
            {
                return TgaImageLoader.LoadBitmap(path);
            }

            if (extension.Equals(
                    ".bmp",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(
                    ".png",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(
                    ".jpg",
                    StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(
                    ".jpeg",
                    StringComparison.OrdinalIgnoreCase))
            {
                using var source = new Bitmap(path);
                return new Bitmap(source);
            }

            using (var stream = File.OpenRead(path))
            {
                if (stream.Length >= 4)
                {
                    int d0 = stream.ReadByte();
                    int d1 = stream.ReadByte();
                    int d2 = stream.ReadByte();
                    int d3 = stream.ReadByte();

                    if (d0 == (byte)'D' &&
                        d1 == (byte)'D' &&
                        d2 == (byte)'S' &&
                        d3 == (byte)' ')
                    {
                        return DdsImageLoader.LoadBitmap(path);
                    }
                }
            }

            try
            {
                using var source = new Bitmap(path);
                return new Bitmap(source);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"Unsupported or invalid skill atlas '{Path.GetFileName(path)}'. " +
                    "Expected sicon01-sicon07 in DDS, TGA or BMP format.",
                    ex);
            }
        }

        private static List<SharedSkillAtlasInfo> LoadSharedSkillAtlases()
        {
            var database = new ImageDatabaseIndexService();
            database.Load(rebuildIndexIfMissing: true);

            HashSet<string> allowedAtlases =
                Enumerable.Range(1, 7)
                    .Select(i => $"sicon{i:00}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            IEnumerable<InterfaceIconMapEntry> mappings = database.InterfaceMap.Icons
                .Where(x =>
                    x.Category.Equals("Skill", StringComparison.OrdinalIgnoreCase) &&
                    allowedAtlases.Contains(x.Atlas));

            var result = new List<SharedSkillAtlasInfo>();
            foreach (IGrouping<string, InterfaceIconMapEntry> group in mappings
                .GroupBy(x => x.Atlas, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                string sourcePath = string.Empty;
                var slots = new List<ItemIconSlotInfo>();
                foreach (InterfaceIconMapEntry mapping in group)
                {
                    if (!uint.TryParse(mapping.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
                        continue;
                    if (!database.TryGetInterfaceIcon(mapping.Id, out ResolvedImageReference resolved, "Skill"))
                        continue;
                    if (sourcePath.Length == 0) sourcePath = resolved.SourcePath;
                    slots.Add(new ItemIconSlotInfo
                    {
                        Id = id,
                        AtlasName = group.Key,
                        Bounds = new Rectangle(mapping.X, mapping.Y, mapping.Width, mapping.Height)
                    });
                }
                if (sourcePath.Length == 0 || slots.Count == 0) continue;
                result.Add(new SharedSkillAtlasInfo
                {
                    Name = group.Key,
                    SourcePath = sourcePath,
                    Slots = slots.OrderBy(x => x.Id).ToList()
                });
            }
            return result;
        }
    }
}
