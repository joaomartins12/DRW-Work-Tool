using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Deterministic loader for Achievement / Title icons.
    ///
    /// Unlike the generic interface-preview resolver, this class always opens
    /// the real DDS files for the three title atlases:
    ///   achieve_icon.dds     -> ids 0..255
    ///   achieve_icon_02.dds  -> ids 300..555
    ///   achieve_icon_03.dds  -> ids 556..811
    ///
    /// The atlas grid/tile information comes from ImageDatabase.json, while the
    /// pixel decoding is delegated to DdsImageLoader. The decoded atlases stay in
    /// memory for the lifetime of the process so scrolling cards never re-decodes
    /// a DDS file.
    /// </summary>
    public static class AchievementIconAtlasCache
    {
        private sealed class CachedAtlas
        {
            public required string Name { get; init; }
            public required uint BaseId { get; init; }
            public required uint MaxId { get; init; }
            public required int TileWidth { get; init; }
            public required int TileHeight { get; init; }
            public required int Columns { get; init; }
            public required int Capacity { get; init; }
            public required string SourcePath { get; init; }
            public required Bitmap Bitmap { get; init; }
        }

        private static readonly object Sync = new();
        private static Dictionary<string, CachedAtlas>? _atlases;

        public static void Preload()
        {
            lock (Sync)
            {
                if (_atlases != null)
                    return;
            }

            var database = new ImageDatabaseIndexService();
            database.Load(rebuildIndexIfMissing: true);

            var loaded = new Dictionary<string, CachedAtlas>(
                StringComparer.OrdinalIgnoreCase);

            try
            {
                AddAtlas(database, loaded, "achieve_icon", 0, 255);
                AddAtlas(database, loaded, "achieve_icon_02", 300, 555);
                AddAtlas(database, loaded, "achieve_icon_03", 556, 811);
            }
            catch
            {
                foreach (CachedAtlas atlas in loaded.Values)
                    atlas.Bitmap.Dispose();

                throw;
            }

            lock (Sync)
            {
                if (_atlases != null)
                {
                    foreach (CachedAtlas atlas in loaded.Values)
                        atlas.Bitmap.Dispose();

                    return;
                }

                _atlases = loaded;
            }
        }

        public static Bitmap? TryLoad(uint iconId)
        {
            try
            {
                Preload();

                CachedAtlas? atlas;

                lock (Sync)
                {
                    atlas = ResolveAtlas(iconId);
                }

                if (atlas == null)
                    return null;

                uint slotValue = iconId - atlas.BaseId;
                if (slotValue >= atlas.Capacity)
                    return null;

                int slot = checked((int)slotValue);
                int column = slot % atlas.Columns;
                int row = slot / atlas.Columns;

                var source = new Rectangle(
                    column * atlas.TileWidth,
                    row * atlas.TileHeight,
                    atlas.TileWidth,
                    atlas.TileHeight);

                if (source.X < 0 ||
                    source.Y < 0 ||
                    source.Right > atlas.Bitmap.Width ||
                    source.Bottom > atlas.Bitmap.Height)
                {
                    return null;
                }

                var icon = new Bitmap(
                    atlas.TileWidth,
                    atlas.TileHeight,
                    PixelFormat.Format32bppArgb);

                using Graphics graphics = Graphics.FromImage(icon);
                graphics.Clear(Color.Transparent);
                graphics.DrawImage(
                    atlas.Bitmap,
                    new Rectangle(0, 0, icon.Width, icon.Height),
                    source,
                    GraphicsUnit.Pixel);

                return icon;
            }
            catch (Exception ex)
            {
                AppLogger.Warning(
                    $"Achievement icon {iconId} could not be loaded: {ex.Message}");

                return null;
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                if (_atlases == null)
                    return;

                foreach (CachedAtlas atlas in _atlases.Values)
                    atlas.Bitmap.Dispose();

                _atlases = null;
            }
        }

        private static CachedAtlas? ResolveAtlas(uint iconId)
        {
            if (_atlases == null)
                return null;

            if (iconId <= 255)
                return _atlases.GetValueOrDefault("achieve_icon");

            if (iconId >= 300 && iconId <= 555)
                return _atlases.GetValueOrDefault("achieve_icon_02");

            if (iconId >= 556 && iconId <= 811)
                return _atlases.GetValueOrDefault("achieve_icon_03");

            return null;
        }

        private static void AddAtlas(
            ImageDatabaseIndexService database,
            IDictionary<string, CachedAtlas> target,
            string atlasName,
            uint baseId,
            uint maxId)
        {
            InterfaceAtlasEntry atlas = database.GetAtlas(atlasName)
                ?? throw new FileNotFoundException(
                    $"ImgDatabase does not contain atlas '{atlasName}'. " +
                    "Run SETTINGS -> Synchronize ImageDatabase first.");

            string expectedFileName = atlasName + ".dds";

            string? source = atlas.Files
                .Select(path => ResolvePath(database.DatabaseRoot, path))
                .FirstOrDefault(path =>
                    File.Exists(path) &&
                    Path.GetFileName(path).Equals(
                        expectedFileName,
                        StringComparison.OrdinalIgnoreCase));

            source ??= atlas.Files
                .Select(path => ResolvePath(database.DatabaseRoot, path))
                .FirstOrDefault(path =>
                    File.Exists(path) &&
                    Path.GetExtension(path).Equals(
                        ".dds",
                        StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                throw new FileNotFoundException(
                    $"The required title atlas '{expectedFileName}' was not found in ImgDatabase. " +
                    "The Achievement editor intentionally requires the DDS source, not a BMP/TGA preview.");
            }

            Bitmap bitmap = DdsImageLoader.LoadBitmap(source);

            int tileWidth = atlas.TileWidth > 0 ? atlas.TileWidth : 32;
            int tileHeight = atlas.TileHeight > 0 ? atlas.TileHeight : 32;
            int columns = atlas.Columns > 0
                ? atlas.Columns
                : Math.Max(1, bitmap.Width / tileWidth);

            int rows = atlas.Rows > 0
                ? atlas.Rows
                : Math.Max(1, bitmap.Height / tileHeight);

            int physicalCapacity = Math.Max(1, columns * rows);
            int requestedCapacity = checked((int)(maxId - baseId + 1));
            int capacity = Math.Min(
                atlas.Capacity > 0 ? atlas.Capacity : physicalCapacity,
                Math.Min(physicalCapacity, requestedCapacity));

            target[atlasName] = new CachedAtlas
            {
                Name = atlasName,
                BaseId = baseId,
                MaxId = maxId,
                TileWidth = tileWidth,
                TileHeight = tileHeight,
                Columns = columns,
                Capacity = capacity,
                SourcePath = source,
                Bitmap = bitmap
            };

            AppLogger.Success(
                $"Achievement atlas ready: {expectedFileName} | " +
                $"{bitmap.Width}x{bitmap.Height} | Grid {columns}x{rows} | " +
                $"Slots={capacity} | IDs {baseId}..{baseId + (uint)Math.Max(0, capacity - 1)}");
        }

        private static string ResolvePath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));
        }
    }
}
