using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    /// <summary>
    /// Resolves Digimon preview icons with Digimon_List.xml + Model.xml-aware fallbacks.
    ///
    /// IMPORTANT: every Bitmap returned by TryLoad is owned by the caller.
    /// Cached/preloaded images are cloned before being returned so UI controls
    /// may safely Dispose their previous preview without corrupting the global cache.
    /// </summary>
    public static class DigimonEvoIconResolver
    {
        public static Bitmap? TryLoad(uint digimonId, uint modelId)
        {
            if (digimonId == 0 && modelId == 0)
                return null;

            // Many older callers only know the Digimon ID and historically passed
            // it as the ModelID too. Resolve the real ModelID from Digimon_List.xml
            // before walking Model.xml -> Data\Digimon\<folder>.
            if (digimonId != 0 && (modelId == 0 || modelId == digimonId))
            {
                try
                {
                    if (DigimonBookDigimonCatalog.TryGet(digimonId, out DigimonBookDigimonEntry entry) && entry.ModelId != 0)
                        modelId = entry.ModelId;
                }
                catch
                {
                    // Keep all existing fallbacks usable even when Digimon_List.xml
                    // is not present in a reduced workspace.
                }
            }

            Bitmap? image = digimonId == 0 ? null : EditorPreloadService.TryGetDigimonIcon(digimonId);
            if (image != null)
                return CloneOwned(image);

            if (modelId != 0 && modelId != digimonId)
            {
                image = EditorPreloadService.TryGetDigimonIcon(modelId);
                if (image != null)
                    return CloneOwned(image);
            }

            // The following loaders create new Bitmap instances, so ownership can
            // be transferred directly to the caller.
            if (digimonId != 0)
            {
                image = DigimonListEditorService.TryLoadIconFromDatabase(digimonId);
                if (image != null)
                    return image;
            }

            if (modelId != 0 && modelId != digimonId)
            {
                image = DigimonListEditorService.TryLoadIconFromDatabase(modelId);
                if (image != null)
                    return image;
            }

            return TryLoadFromModelFolder(digimonId, modelId);
        }

        private static Bitmap? CloneOwned(Image source)
        {
            try
            {
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap? TryLoadFromModelFolder(uint digimonId, uint modelId)
        {
            if (modelId == 0)
                return null;

            DigimonModelReferenceService? models = EditorPreloadService.TryGetDigimonModels();
            if (models == null || !models.TryGet(modelId, out DigimonModelReference model))
                return null;

            string rawFolder = ExtractRawDigimonFolder(model.KfmPath);
            if (string.IsNullOrWhiteSpace(rawFolder))
                return null;

            string databaseRoot = Path.Combine(AppContext.BaseDirectory, "ImgDatabase", "Digimon");
            if (!Directory.Exists(databaseRoot))
                return null;

            string? folder = ResolveFolder(databaseRoot, rawFolder);
            if (folder == null)
                return null;

            foreach (uint wanted in new[] { digimonId, modelId }.Where(x => x != 0).Distinct())
            {
                foreach (string extension in new[] { ".tga", ".png", ".bmp", ".jpg", ".jpeg" })
                {
                    string exact = Path.Combine(folder, wanted.ToString(CultureInfo.InvariantCulture) + extension);
                    if (!File.Exists(exact))
                        continue;

                    Bitmap? exactImage = LoadBitmap(exact);
                    if (exactImage != null)
                        return exactImage;
                }
            }

            string[] candidates = Directory
                .EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedImage)
                .ToArray();

            if (candidates.Length == 1)
                return LoadBitmap(candidates[0]);

            return null;
        }

        private static string? ResolveFolder(string root, string rawFolder)
        {
            string direct = Path.Combine(root, rawFolder);
            if (Directory.Exists(direct))
                return direct;

            try
            {
                return Directory
                    .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(x => Path.GetFileName(x).Equals(rawFolder, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractRawDigimonFolder(string? kfmPath)
        {
            string normalized = (kfmPath ?? string.Empty).Replace('/', '\\').Trim();
            const string prefix = @"Data\Digimon\";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            string relative = normalized.Substring(prefix.Length);
            int slash = relative.IndexOf('\\');
            return slash < 0 ? Path.GetFileNameWithoutExtension(relative) : relative.Substring(0, slash);
        }

        private static bool IsSupportedImage(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        private static Bitmap? LoadBitmap(string path)
        {
            try
            {
                if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
                    return TgaImageLoader.LoadBitmap(path);

                using Image source = Image.FromFile(path);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }
    }
}
