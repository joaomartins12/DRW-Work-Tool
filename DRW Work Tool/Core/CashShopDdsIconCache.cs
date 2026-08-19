using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Loads Cash Shop icons directly from cashshopG_PPP DDS atlases.
    /// Verified from the original TGA guide atlases:
    /// - atlas size: 480x480
    /// - cell size: 80x80
    /// - grid: 6 columns x 6 rows
    /// - 36 sequential IDs per atlas (suffix 00..35)
    /// </summary>
    public static class CashShopDdsIconCache
    {
        private const int CashShopTileSize = 80;
        private const int CashShopColumns = 6;
        private const int CashShopRows = 6;
        private const int CashShopSlots = CashShopColumns * CashShopRows;

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
                if (!TryResolveAtlasAndSlot(iconId, out string atlasName, out int slot))
                {
                    Cache(iconId, null);
                    return null;
                }

                ImageDatabaseIndexService database = GetDatabase();
                InterfaceAtlasEntry? atlas = database.GetAtlas(atlasName);

                if (atlas == null)
                {
                    AppLogger.Warning($"Cash Shop atlas '{atlasName}' was not found for nIconID {iconId}.");
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
                    AppLogger.Warning($"Cash Shop DDS variant was not found for atlas '{atlasName}'.");
                    Cache(iconId, null);
                    return null;
                }

                Bitmap atlasBitmap = GetAtlas(ddsPath);

                if (atlasBitmap.Width != 480 || atlasBitmap.Height != 480)
                {
                    AppLogger.Warning(
                        $"Cash Shop atlas '{atlasName}' has unexpected dimensions " +
                        $"{atlasBitmap.Width}x{atlasBitmap.Height}; expected 480x480.");
                }

                int column = slot % CashShopColumns;
                int row = slot / CashShopColumns;
                var source = new Rectangle(
                    column * CashShopTileSize,
                    row * CashShopTileSize,
                    CashShopTileSize,
                    CashShopTileSize);

                if (source.Right > atlasBitmap.Width || source.Bottom > atlasBitmap.Height)
                {
                    AppLogger.Warning(
                        $"Cash Shop nIconID {iconId} resolved outside '{atlasName}': " +
                        $"slot={slot}, rect={source.X},{source.Y},{source.Width},{source.Height}, " +
                        $"atlas={atlasBitmap.Width}x{atlasBitmap.Height}.");
                    Cache(iconId, null);
                    return null;
                }

                var icon = new Bitmap(CashShopTileSize, CashShopTileSize, PixelFormat.Format32bppArgb);
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

        /// <summary>
        /// Converts an nIconID into its atlas name and physical slot.
        /// Example: 210210 => cashshop2_102, slot 10.
        /// Example: 510423 => cashshop5_104, slot 23.
        /// </summary>
        private static bool TryResolveAtlasAndSlot(uint iconId, out string atlasName, out int slot)
        {
            atlasName = string.Empty;
            slot = (int)(iconId % 100u);

            if (slot < 0 || slot >= CashShopSlots)
                return false;

            uint atlasCode = iconId / 100u;
            string code = atlasCode.ToString();

            // Current DMO Cash Shop atlases use one group digit plus a three-digit page:
            // 2102 => group 2, page 102; 5104 => group 5, page 104.
            if (code.Length < 4)
                return false;

            string group = code.Substring(0, code.Length - 3);
            string page = code.Substring(code.Length - 3);

            if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(page))
                return false;

            atlasName = $"cashshop{group}_{page}";
            return true;
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
    }
}
