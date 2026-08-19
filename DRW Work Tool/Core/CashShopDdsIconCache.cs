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
    /// It resolves only Cash Shop icon mappings and never substitutes ItemList icons.
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
                        x.Atlas.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
                        x.Atlas.Contains("shop", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.Category.Equals("CashShop", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x =>
                        x.Atlas.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
                        x.Atlas.Contains("shop", StringComparison.OrdinalIgnoreCase))
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
                var source = new Rectangle(mapping.X, mapping.Y, mapping.Width, mapping.Height);

                if (source.Width <= 0 ||
                    source.Height <= 0 ||
                    source.X < 0 ||
                    source.Y < 0 ||
                    source.Right > atlasBitmap.Width ||
                    source.Bottom > atlasBitmap.Height)
                {
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
