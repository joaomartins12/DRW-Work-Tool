using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DRW_Work_Tool.Core
{
    public sealed record InterfaceIconPreloadResult(
        int TotalMappings,
        int LoadedIcons,
        int MissingIcons,
        int FailedIcons,
        int AtlasCount);

    public static class ImageDatabasePreview
    {
        private static readonly object Sync = new();

        private static ImageDatabaseIndexService? _database;

        // Temporary decoded atlas cache.
        // During startup all needed atlases are decoded once. After individual
        // icon thumbnails are generated the atlases are disposed to release RAM.
        private static readonly Dictionary<string, Bitmap> AtlasCache =
            new(StringComparer.OrdinalIgnoreCase);

        // Permanent startup memory cache.
        // Key = Category|NormalizedIconId
        // A master 32x32/slot Bitmap stays in memory for the whole application.
        // Callers always receive a clone, so PictureBox.Dispose is safe.
        private static readonly Dictionary<string, Bitmap?> IconCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _allInterfaceIconsPreloaded;

        public static bool AllInterfaceIconsPreloaded
        {
            get
            {
                lock (Sync)
                    return _allInterfaceIconsPreloaded;
            }
        }

        public static int CachedInterfaceIconCount
        {
            get
            {
                lock (Sync)
                    return IconCache.Count;
            }
        }

        public static void Preload()
        {
            lock (Sync)
            {
                if (_database != null)
                    return;

                var db =
                    new ImageDatabaseIndexService();

                db.Load(
                    rebuildIndexIfMissing: true);

                _database = db;
            }
        }

        /// <summary>
        /// Decodes every mapped interface atlas and pre-renders every mapped
        /// interface icon into RAM before Form1 is shown.
        ///
        /// This is intentionally done from LoadingForm/EditorPreloadService.
        /// ItemList/Skill/Accessory screens then perform only a Dictionary lookup
        /// + tiny Bitmap clone and never open DDS/BMP files while the UI is active.
        /// </summary>
        public static InterfaceIconPreloadResult PreloadAllInterfaceIcons(
            Action<int, int, string>? progress = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            ImageDatabaseIndexService db =
                GetDatabase();

            List<InterfaceIconMapEntry> mappings =
                db.InterfaceMap
                    .Icons
                    .Where(
                        x =>
                            !string.IsNullOrWhiteSpace(
                                x.Id))
                    .GroupBy(
                        x =>
                            CacheKey(
                                x.Id,
                                x.Category),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        x => x.First())
                    .ToList();

            int total =
                mappings.Count;

            int loaded = 0;
            int missing = 0;
            int failed = 0;

            var atlasPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            progress?.Invoke(
                0,
                total,
                "Preparing interface icons...");

            for (int i = 0;
                 i < mappings.Count;
                 i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                InterfaceIconMapEntry mapping =
                    mappings[i];

                string key =
                    CacheKey(
                        mapping.Id,
                        mapping.Category);

                bool alreadyCached;

                lock (Sync)
                {
                    alreadyCached =
                        IconCache.ContainsKey(
                            key);
                }

                if (alreadyCached)
                {
                    loaded++;

                    ReportProgress(
                        progress,
                        i + 1,
                        total,
                        mapping);

                    continue;
                }

                try
                {
                    if (!db.TryGetInterfaceIcon(
                        mapping.Id,
                        out ResolvedImageReference image,
                        mapping.Category))
                    {
                        lock (Sync)
                            IconCache[key] = null;

                        missing++;

                        ReportProgress(
                            progress,
                            i + 1,
                            total,
                            mapping);

                        continue;
                    }

                    if (!File.Exists(
                        image.SourcePath))
                    {
                        lock (Sync)
                            IconCache[key] = null;

                        missing++;

                        ReportProgress(
                            progress,
                            i + 1,
                            total,
                            mapping);

                        continue;
                    }

                    atlasPaths.Add(
                        image.SourcePath);

                    Bitmap? icon =
                        CreateIconFromResolved(
                            image);

                    lock (Sync)
                    {
                        IconCache[key] =
                            icon;
                    }

                    if (icon == null)
                        failed++;
                    else
                        loaded++;
                }
                catch
                {
                    lock (Sync)
                        IconCache[key] = null;

                    failed++;
                }

                ReportProgress(
                    progress,
                    i + 1,
                    total,
                    mapping);
            }

            // After every slot is rendered, keeping the large decoded atlases in
            // memory is unnecessary. The small icon masters remain cached.
            DisposeAtlasCache();

            lock (Sync)
                _allInterfaceIconsPreloaded = true;

            progress?.Invoke(
                total,
                total,
                $"Interface icons ready: {loaded:N0} cached.");

            return new InterfaceIconPreloadResult(
                total,
                loaded,
                missing,
                failed,
                atlasPaths.Count);
        }

        public static Bitmap? TryLoadInterfaceIcon(
            uint iconId,
            string category = "Item") =>
            TryLoadInterfaceIcon(
                iconId.ToString(),
                category);

        public static Bitmap? TryLoadInterfaceIcon(
            string iconId,
            string category = "Item")
        {
            string key =
                CacheKey(
                    iconId,
                    category);

            lock (Sync)
            {
                if (IconCache.TryGetValue(
                    key,
                    out Bitmap? cached))
                {
                    return cached == null
                        ? null
                        : new Bitmap(
                            cached);
                }
            }

            // Compatibility fallback for:
            // - a newly generated mapping after startup
            // - an editor category not present during preload
            // It is cached after the first request.
            try
            {
                ImageDatabaseIndexService db =
                    GetDatabase();

                if (!db.TryGetInterfaceIcon(
                    iconId,
                    out ResolvedImageReference image,
                    category))
                {
                    lock (Sync)
                        IconCache[key] = null;

                    return null;
                }

                if (!File.Exists(
                    image.SourcePath))
                {
                    lock (Sync)
                        IconCache[key] = null;

                    return null;
                }

                Bitmap? created =
                    CreateIconFromResolved(
                        image);

                lock (Sync)
                {
                    IconCache[key] =
                        created == null
                            ? null
                            : new Bitmap(
                                created);
                }

                return created;
            }
            catch
            {
                lock (Sync)
                    IconCache[key] = null;

                return null;
            }
        }

        public static void ReloadInterfaceMapAndPreload(
            Action<int, int, string>? progress = null)
        {
            ImageDatabaseIndexService db =
                GetDatabase();

            db.ReloadInterfaceMap();

            ClearInterfaceIconCacheOnly();

            PreloadAllInterfaceIcons(
                progress);
        }

        public static void ClearCache()
        {
            lock (Sync)
            {
                foreach (Bitmap atlas
                         in AtlasCache.Values)
                {
                    atlas.Dispose();
                }

                AtlasCache.Clear();

                foreach (Bitmap? icon
                         in IconCache.Values)
                {
                    icon?.Dispose();
                }

                IconCache.Clear();

                _database = null;
                _allInterfaceIconsPreloaded = false;
            }
        }

        public static void ClearInterfaceIconCacheOnly()
        {
            lock (Sync)
            {
                foreach (Bitmap? icon
                         in IconCache.Values)
                {
                    icon?.Dispose();
                }

                IconCache.Clear();

                _allInterfaceIconsPreloaded = false;
            }
        }

        private static void ReportProgress(
            Action<int, int, string>? progress,
            int current,
            int total,
            InterfaceIconMapEntry mapping)
        {
            if (progress == null)
                return;

            // Avoid dispatching thousands of UI messages.
            if (current != total &&
                current % 64 != 0)
            {
                return;
            }

            string category =
                string.IsNullOrWhiteSpace(
                    mapping.Category)
                    ? "Interface"
                    : mapping.Category;

            progress(
                current,
                total,
                $"Caching {category} icons... {current:N0}/{total:N0}");
        }

        private static string CacheKey(
            string id,
            string? category)
        {
            string normalizedId =
                NormalizeId(
                    id);

            string normalizedCategory =
                string.IsNullOrWhiteSpace(
                    category)
                    ? string.Empty
                    : category.Trim();

            return
                normalizedCategory +
                "|" +
                normalizedId;
        }

        private static string NormalizeId(
            string id)
        {
            id =
                (id ?? string.Empty)
                    .Trim();

            if (ulong.TryParse(
                id,
                out ulong numeric))
            {
                return numeric.ToString();
            }

            return id;
        }

        private static Bitmap? CreateIconFromResolved(
            ResolvedImageReference image)
        {
            Bitmap atlas =
                GetAtlas(
                    image.SourcePath);

            Rectangle source =
                new(
                    image.X,
                    image.Y,
                    image.Width,
                    image.Height);

            if (source.X < 0 ||
                source.Y < 0 ||
                source.Width <= 0 ||
                source.Height <= 0 ||
                source.Right > atlas.Width ||
                source.Bottom > atlas.Height)
            {
                return null;
            }

            var icon =
                new Bitmap(
                    image.Width,
                    image.Height,
                    PixelFormat.Format32bppArgb);

            using Graphics g =
                Graphics.FromImage(
                    icon);

            g.Clear(
                Color.Transparent);

            g.DrawImage(
                atlas,
                new Rectangle(
                    0,
                    0,
                    image.Width,
                    image.Height),
                source,
                GraphicsUnit.Pixel);

            return icon;
        }

        private static ImageDatabaseIndexService GetDatabase()
        {
            lock (Sync)
            {
                if (_database != null)
                    return _database;

                var db =
                    new ImageDatabaseIndexService();

                db.Load(
                    rebuildIndexIfMissing: true);

                _database =
                    db;

                return db;
            }
        }

        private static Bitmap GetAtlas(
            string sourcePath)
        {
            lock (Sync)
            {
                if (AtlasCache.TryGetValue(
                    sourcePath,
                    out Bitmap? cached))
                {
                    return cached;
                }
            }

            // Decode outside Sync so long DDS work does not block unrelated
            // cache lookups.
            Bitmap atlas;

            string extension =
                Path.GetExtension(
                    sourcePath);

            if (extension.Equals(
                ".dds",
                StringComparison.OrdinalIgnoreCase))
            {
                atlas =
                    DdsImageLoader.LoadBitmap(
                        sourcePath);
            }
            else
            {
                atlas =
                    new Bitmap(
                        sourcePath);
            }

            lock (Sync)
            {
                if (AtlasCache.TryGetValue(
                    sourcePath,
                    out Bitmap? raced))
                {
                    atlas.Dispose();

                    return raced;
                }

                AtlasCache[sourcePath] =
                    atlas;

                return atlas;
            }
        }

        private static void DisposeAtlasCache()
        {
            lock (Sync)
            {
                foreach (Bitmap atlas
                         in AtlasCache.Values)
                {
                    atlas.Dispose();
                }

                AtlasCache.Clear();
            }
        }
    }
}
