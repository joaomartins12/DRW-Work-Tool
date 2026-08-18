using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DRW_Work_Tool.Core
{
    public static class NpcPreviewCache
    {
        private static readonly SemaphoreSlim DatabaseLock = new(1, 1);

        private static readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>>
            BitmapCache =
                new(StringComparer.OrdinalIgnoreCase);

        private static ImageDatabaseIndexService? _database;

        public static async Task PreloadAsync()
        {
            await EnsureDatabaseAsync()
                .ConfigureAwait(false);
        }

        public static async Task<Bitmap?> GetPreviewAsync(
            uint modelId,
            uint npcId,
            EditorReferenceCatalogService references)
        {
            ResolvedImageReference? resolved =
                await Task.Run(
                    () => ResolveImage(
                        modelId,
                        npcId,
                        references))
                    .ConfigureAwait(false);

            if (resolved == null ||
                string.IsNullOrWhiteSpace(resolved.SourcePath) ||
                !File.Exists(resolved.SourcePath))
            {
                return null;
            }

            Lazy<Task<Bitmap?>> lazy =
                BitmapCache.GetOrAdd(
                    resolved.SourcePath,
                    path =>
                        new Lazy<Task<Bitmap?>>(
                            () =>
                                Task.Run(
                                    () =>
                                        LoadMasterBitmap(path)),
                            LazyThreadSafetyMode.ExecutionAndPublication));

            Bitmap? master =
                await lazy.Value
                    .ConfigureAwait(false);

            if (master == null)
                return null;

            return await Task.Run(
                () => new Bitmap(master))
                .ConfigureAwait(false);
        }

        public static void Clear()
        {
            foreach (Lazy<Task<Bitmap?>> lazy in BitmapCache.Values)
            {
                if (!lazy.IsValueCreated ||
                    !lazy.Value.IsCompletedSuccessfully)
                {
                    continue;
                }

                lazy.Value.Result?.Dispose();
            }

            BitmapCache.Clear();
            _database = null;
        }

        private static async Task<ImageDatabaseIndexService> EnsureDatabaseAsync()
        {
            if (_database != null)
                return _database;

            await DatabaseLock.WaitAsync()
                .ConfigureAwait(false);

            try
            {
                if (_database != null)
                    return _database;

                _database =
                    await Task.Run(
                        () =>
                        {
                            var service =
                                new ImageDatabaseIndexService();

                            service.Load(
                                rebuildIndexIfMissing: true);

                            return service;
                        })
                        .ConfigureAwait(false);

                return _database;
            }
            finally
            {
                DatabaseLock.Release();
            }
        }

        private static ResolvedImageReference? ResolveImage(
            uint modelId,
            uint npcId,
            EditorReferenceCatalogService references)
        {
            ImageDatabaseIndexService database =
                EnsureDatabaseAsync()
                    .GetAwaiter()
                    .GetResult();

            ResolvedImageReference resolved;

            if (modelId > 0 &&
                references.TryGetModel(
                    modelId,
                    out EditorModelReference? model))
            {
                if (model.Kind.Equals(
                    "Digimon",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (database.TryGetDigimonIcon(
                        modelId,
                        out resolved))
                    {
                        return resolved;
                    }

                    if (database.TryGetNpcIcon(
                        modelId,
                        out resolved))
                    {
                        return resolved;
                    }
                }
                else if (model.Kind.Equals(
                    "Npc",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (database.TryGetNpcIcon(
                        modelId,
                        out resolved))
                    {
                        return resolved;
                    }

                    if (database.TryGetDigimonIcon(
                        modelId,
                        out resolved))
                    {
                        return resolved;
                    }
                }
                else
                {
                    if (database.TryGetNpcIcon(
                        modelId,
                        out resolved))
                    {
                        return resolved;
                    }

                    if (database.TryGetDigimonIcon(
                        modelId,
                        out resolved))
                    {
                        return resolved;
                    }
                }
            }
            else if (modelId > 0)
            {
                if (database.TryGetNpcIcon(
                    modelId,
                    out resolved))
                {
                    return resolved;
                }

                if (database.TryGetDigimonIcon(
                    modelId,
                    out resolved))
                {
                    return resolved;
                }
            }

            if (npcId > 0 &&
                database.TryGetNpcIcon(
                    npcId,
                    out resolved))
            {
                return resolved;
            }

            return null;
        }

        private static Bitmap? LoadMasterBitmap(
            string path)
        {
            try
            {
                string extension =
                    Path.GetExtension(path);

                if (extension.Equals(
                    ".tga",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return TgaImageLoader.LoadBitmap(path);
                }

                using Image source =
                    Image.FromFile(path);

                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }
    }
}
