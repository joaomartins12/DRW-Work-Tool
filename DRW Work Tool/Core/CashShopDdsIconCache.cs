using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Cash Shop icon loader that deliberately uses DDS atlas variants.
    /// CashShop atlases are physically 15x15 (480x480), while nIconID uses
    /// a logical 10x10 address in the final two digits (00..99).
    /// </summary>
    public static class CashShopDdsIconCache
    {
        private static readonly object Sync = new();
        private static ImageDatabaseIndexService? _database;
        private static readonly Dictionary<string, Bitmap> AtlasCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<uint, Bitmap?> IconCache = new();

        public static Bitmap? TryLoad(uint iconId)
        {
            if (iconId == 0)
                return null;

            lock (Sync)
            {
                if (IconCache.TryGetValue(iconId, out Bitmap? cached))
                    return cached == null ? null : new Bitmap(cached);
            }

            try
            {
                ImageDatabaseIndexService database = GetDatabase();
                string normalized = iconId.ToString();

                InterfaceIconMapEntry? mapping = database.InterfaceMap.Icons
                    .Where(x => NormalizeId(x.Id) == normalized)
                    .Where(x =>
                        x.Category.Equals("CashShop", StringComparison.OrdinalIgnoreCase) ||
                        x.Atlas.StartsWith("cashshop", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Category.Equals("CashShop", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (mapping == null)
                {
                    Cache(iconId, null);
                    return null;
                }

                InterfaceAtlasEntry? atlas = database.GetAtlas(mapping.Atlas);
                if (atlas == null)
                {
                    Cache(iconId, null);
                    return null;
                }

                string? ddsPath = atlas.Files
                    .Select(x => ResolvePath(database.DatabaseRoot, x))
                    .FirstOrDefault(x =>
                        File.Exists(x) &&
                        Path.GetExtension(x).Equals(".dds", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(ddsPath))
                {
                    Cache(iconId, null);
                    return null;
                }

                Bitmap atlasBitmap = GetAtlas(ddsPath);

                int sourceWidth = atlas.TileWidth > 0 ? atlas.TileWidth : mapping.Width;
                int sourceHeight = atlas.TileHeight > 0 ? atlas.TileHeight : mapping.Height;
                int sourceX = mapping.X;
                int sourceY = mapping.Y;

                // IMPORTANT:
                // cashshopG_PPP.dds is 480x480 / 15x15 physically, but the CashShop ID
                // namespace is GPPP00..GPPP99. Therefore the two trailing digits are a
                // logical 10x10 slot, NOT a row-major index using atlas.Columns (15).
                // Recalculate here so even an old InterfaceIconMap.json remains usable.
                if (mapping.Category.Equals("CashShop", StringComparison.OrdinalIgnoreCase) ||
                    mapping.Atlas.StartsWith("cashshop", StringComparison.OrdinalIgnoreCase))
                {
                    int logicalSlot = (int)(iconId % 100u);
                    sourceX = (logicalSlot % 10) * sourceWidth;
                    sourceY = (logicalSlot / 10) * sourceHeight;
                }

                var source = new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);

                if (source.Width <= 0 ||
                    source.Height <= 0 ||
                    source.X < 0 ||
                    source.Y < 0 ||
                    source.Right > atlasBitmap.Width ||
                    source.Bottom > atlasBitmap.Height)
                {
                    AppLogger.Warning(
                        $"Cash Shop DDS icon {iconId} has invalid source rectangle " +
                        $"{source.X},{source.Y},{source.Width},{source.Height} in {mapping.Atlas} " +
                        $"({atlasBitmap.Width}x{atlasBitmap.Height}).");
                    Cache(iconId, null);
                    return null;
                }

                var icon = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(icon))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.DrawImage(
                        atlasBitmap,
                        new Rectangle(0, 0, icon.Width, icon.Height),
                        source,
                        GraphicsUnit.Pixel);
                }

                Cache(iconId, icon);
                return new Bitmap(icon);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Cash Shop DDS icon {iconId} could not be loaded: {ex.Message}");
                Cache(iconId, null);
                return null;
            }
        }

        public static void Reset()
        {
            lock (Sync)
            {
                foreach (Bitmap atlas in AtlasCache.Values)
                    atlas.Dispose();
                AtlasCache.Clear();

                foreach (Bitmap? icon in IconCache.Values)
                    icon?.Dispose();
                IconCache.Clear();
                _database = null;
            }
        }

        private static ImageDatabaseIndexService GetDatabase()
        {
            lock (Sync)
            {
                if (_database != null)
                    return _database;

                var database = new ImageDatabaseIndexService();
                database.Load(rebuildIndexIfMissing: true);
                _database = database;
                return database;
            }
        }

        private static Bitmap GetAtlas(string path)
        {
            lock (Sync)
            {
                if (AtlasCache.TryGetValue(path, out Bitmap? cached))
                    return cached;
            }

            Bitmap loaded = DdsImageLoader.LoadBitmap(path);

            lock (Sync)
            {
                if (AtlasCache.TryGetValue(path, out Bitmap? raced))
                {
                    loaded.Dispose();
                    return raced;
                }

                AtlasCache[path] = loaded;
                return loaded;
            }
        }

        private static void Cache(uint iconId, Bitmap? icon)
        {
            lock (Sync)
            {
                if (IconCache.TryGetValue(iconId, out Bitmap? old))
                    old?.Dispose();

                IconCache[iconId] = icon == null ? null : new Bitmap(icon);
            }
        }

        private static string ResolvePath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string NormalizeId(string value)
        {
            value = (value ?? string.Empty).Trim();
            return ulong.TryParse(value, out ulong numeric) ? numeric.ToString() : value;
        }
    }
}
