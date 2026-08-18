using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Resolves Digimon preview icons with Model.xml-aware fallbacks.
    ///
    /// Order:
    /// 1) preloaded Digimon icon by Digimon ID
    /// 2) preloaded Digimon icon by ModelID
    /// 3) ImgDatabase/Digimon direct ID lookup
    /// 4) ImgDatabase/Digimon direct ModelID lookup
    /// 5) Model.xml -> Data\Digimon\<folder>\... -> matching ImgDatabase folder
    ///
    /// The final Model.xml folder fallback only auto-picks an image when the
    /// match is unambiguous or when an exact DigimonID/ModelID filename exists.
    /// </summary>
    public static class DigimonEvoIconResolver
    {
        public static Bitmap? TryLoad(
            uint digimonId,
            uint modelId)
        {
            if (digimonId == 0 && modelId == 0)
                return null;

            Bitmap? image =
                digimonId == 0
                    ? null
                    : EditorPreloadService.TryGetDigimonIcon(digimonId);

            if (image != null)
                return image;

            if (modelId != 0 && modelId != digimonId)
            {
                image =
                    EditorPreloadService.TryGetDigimonIcon(modelId);

                if (image != null)
                    return image;
            }

            if (digimonId != 0)
            {
                image =
                    DigimonListEditorService.TryLoadIconFromDatabase(
                        digimonId);

                if (image != null)
                    return image;
            }

            if (modelId != 0 && modelId != digimonId)
            {
                image =
                    DigimonListEditorService.TryLoadIconFromDatabase(
                        modelId);

                if (image != null)
                    return image;
            }

            return TryLoadFromModelFolder(
                digimonId,
                modelId);
        }

        private static Bitmap? TryLoadFromModelFolder(
            uint digimonId,
            uint modelId)
        {
            if (modelId == 0)
                return null;

            DigimonModelReferenceService? models =
                EditorPreloadService.TryGetDigimonModels();

            if (models == null ||
                !models.TryGet(
                    modelId,
                    out DigimonModelReference model))
            {
                return null;
            }

            string rawFolder =
                ExtractRawDigimonFolder(
                    model.KfmPath);

            if (string.IsNullOrWhiteSpace(rawFolder))
                return null;

            string databaseRoot =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "ImgDatabase",
                    "Digimon");

            if (!Directory.Exists(databaseRoot))
                return null;

            string? folder =
                ResolveFolder(
                    databaseRoot,
                    rawFolder);

            if (folder == null)
                return null;

            // Exact numeric files first. ImageDatabaseBuilder renames Digimon
            // TGA files to "<id>.tga", preserving the source Digimon folder.
            foreach (uint wanted in new[] { digimonId, modelId }
                         .Where(x => x != 0)
                         .Distinct())
            {
                foreach (string extension in new[]
                         {
                             ".tga", ".png", ".bmp", ".jpg", ".jpeg"
                         })
                {
                    string exact =
                        Path.Combine(
                            folder,
                            wanted.ToString(
                                CultureInfo.InvariantCulture) +
                            extension);

                    if (!File.Exists(exact))
                        continue;

                    Bitmap? exactImage =
                        LoadBitmap(exact);

                    if (exactImage != null)
                        return exactImage;
                }
            }

            string[] candidates =
                Directory
                    .EnumerateFiles(
                        folder,
                        "*.*",
                        SearchOption.TopDirectoryOnly)
                    .Where(IsSupportedImage)
                    .ToArray();

            // Never guess between multiple unrelated icons in the same model
            // folder. If the Model.xml folder contains a single icon, it is an
            // unambiguous fallback and can safely be used.
            if (candidates.Length == 1)
                return LoadBitmap(candidates[0]);

            return null;
        }

        private static string? ResolveFolder(
            string root,
            string rawFolder)
        {
            string direct =
                Path.Combine(
                    root,
                    rawFolder);

            if (Directory.Exists(direct))
                return direct;

            // Windows is normally case-insensitive, but keep this robust when
            // development/build output is inspected on another filesystem.
            try
            {
                return Directory
                    .EnumerateDirectories(
                        root,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(
                        x =>
                            Path.GetFileName(x)
                                .Equals(
                                    rawFolder,
                                    StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractRawDigimonFolder(
            string? kfmPath)
        {
            string normalized =
                (kfmPath ?? string.Empty)
                    .Replace('/', '\\')
                    .Trim();

            const string prefix = @"Data\Digimon\";

            if (!normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            string relative =
                normalized.Substring(
                    prefix.Length);

            int slash =
                relative.IndexOf('\\');

            return slash < 0
                ? Path.GetFileNameWithoutExtension(relative)
                : relative.Substring(0, slash);
        }

        private static bool IsSupportedImage(
            string path)
        {
            string extension =
                Path.GetExtension(path);

            return extension.Equals(
                       ".tga",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".png",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".bmp",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".jpg",
                       StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(
                       ".jpeg",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static Bitmap? LoadBitmap(
            string path)
        {
            try
            {
                if (Path.GetExtension(path).Equals(
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
