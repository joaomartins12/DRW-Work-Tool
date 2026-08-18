using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class ImageDatabaseSyncResult
    {
        public string DatabaseRoot { get; internal set; } = string.Empty;
        public int FoldersScanned { get; internal set; }
        public int FilesScanned { get; internal set; }
        public int InterfaceAtlases { get; internal set; }
        public int SkillAtlases { get; internal set; }
        public int AtlasVariants { get; internal set; }
        public int DigimonIcons { get; internal set; }
        public int TamerIcons { get; internal set; }
        public int NpcIcons { get; internal set; }
        public int InvalidDirectIconDimensions { get; internal set; }

        public int TotalDirectIcons =>
            DigimonIcons +
            TamerIcons +
            NpcIcons;
    }

    public static class ImageDatabaseIndexBuilder
    {
        private static readonly string[] ImageExtensions =
        {
            ".bmp",
            ".tga",
            ".dds"
        };

        public static ImageDatabaseSyncResult Synchronize(
            string? databaseRoot = null,
            IProgress<string>? progress = null)
        {
            string root =
                string.IsNullOrWhiteSpace(databaseRoot)
                    ? Path.Combine(AppContext.BaseDirectory, "ImgDatabase")
                    : Path.GetFullPath(databaseRoot);

            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"A ImgDatabase ainda não existe: {root}. " +
                    "Executa primeiro IMAGE DATABASE.");
            }

            progress?.Report("Synchronize: a verificar folders da ImgDatabase...");

            var result = new ImageDatabaseSyncResult
            {
                DatabaseRoot = root
            };

            foreach (string folder in
                     Directory.EnumerateDirectories(
                         root,
                         "*",
                         SearchOption.AllDirectories))
            {
                result.FoldersScanned++;
            }

            progress?.Report("Synchronize: a ler dimensões dos ficheiros...");

            ImageDatabaseIndexDocument document = Rebuild(root);

            result.InterfaceAtlases = document.InterfaceAtlases.Count;
            result.SkillAtlases =
                document.InterfaceAtlases.Count(
                    x => x.Kind.Equals(
                        "SkillAtlas",
                        StringComparison.OrdinalIgnoreCase));

            result.AtlasVariants =
                document.InterfaceAtlases.Sum(x => x.Files.Count);

            result.DigimonIcons = document.DigimonIcons.Count;
            result.TamerIcons = document.TamerIcons.Count;
            result.NpcIcons = document.NpcIcons.Count;

            result.InvalidDirectIconDimensions =
                document.DigimonIcons.Count(x => !x.IsExpected32x32) +
                document.TamerIcons.Count(x => !x.IsExpected32x32);

            string[] imageExtensions =
            {
                ".bmp",
                ".tga",
                ".dds"
            };

            result.FilesScanned =
                Directory
                    .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Count(
                        x => imageExtensions.Contains(
                            Path.GetExtension(x),
                            StringComparer.OrdinalIgnoreCase));

            progress?.Report(
                $"Synchronize concluído: " +
                $"Atlases={result.InterfaceAtlases:N0}, " +
                $"SkillAtlases={result.SkillAtlases:N0}, " +
                $"Digimon={result.DigimonIcons:N0}, " +
                $"Tamer={result.TamerIcons:N0}, " +
                $"NPC={result.NpcIcons:N0}.");

            return result;
        }

        public static ImageDatabaseIndexDocument Rebuild(
            string? databaseRoot = null)
        {
            string root =
                string.IsNullOrWhiteSpace(databaseRoot)
                    ? Path.Combine(AppContext.BaseDirectory, "ImgDatabase")
                    : Path.GetFullPath(databaseRoot);

            Directory.CreateDirectory(root);

            var document = new ImageDatabaseIndexDocument
            {
                Version = 1,
                GeneratedUtc = DateTime.UtcNow,
                InterfaceAtlases = BuildInterfaceAtlases(root),
                DigimonIcons = BuildDirectIcons(root, "Digimon"),
                TamerIcons = BuildDirectIcons(root, "Tamer"),
                NpcIcons = BuildDirectIcons(root, "Npc")
            };

            string output = Path.Combine(root, "ImageDatabase.json");
            ImageDatabaseIndexService.Serialize(output, document);

            string mapPath = Path.Combine(root, "InterfaceIconMap.json");

            if (!File.Exists(mapPath))
            {
                ImageDatabaseIndexService.Serialize(
                    mapPath,
                    new InterfaceIconMapDocument());
            }

            return document;
        }

        private static List<InterfaceAtlasEntry> BuildInterfaceAtlases(string root)
        {
            string folder = Path.Combine(root, "interface", "icon");

            if (!Directory.Exists(folder))
                return new List<InterfaceAtlasEntry>();

            List<string> files =
                Directory
                    .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(IsSupportedImage)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var result = new List<InterfaceAtlasEntry>();

            foreach (IGrouping<string, string> group in
                     files.GroupBy(
                         x => Path.GetFileNameWithoutExtension(x),
                         StringComparer.OrdinalIgnoreCase))
            {
                List<string> variants = group.ToList();

                string preferred =
                    variants.FirstOrDefault(
                        x => Path.GetExtension(x).Equals(
                            ".bmp",
                            StringComparison.OrdinalIgnoreCase))
                    ?? variants.FirstOrDefault(
                        x => Path.GetExtension(x).Equals(
                            ".tga",
                            StringComparison.OrdinalIgnoreCase))
                    ?? variants.First();

                if (!TryReadDimensions(preferred, out int width, out int height))
                {
                    foreach (string variant in variants)
                    {
                        if (TryReadDimensions(variant, out width, out height))
                        {
                            preferred = variant;
                            break;
                        }
                    }
                }

                int columns = width > 0 ? width / 32 : 0;
                int rows = height > 0 ? height / 32 : 0;

                result.Add(
                    new InterfaceAtlasEntry
                    {
                        Name = group.Key,
                        Width = width,
                        Height = height,
                        TileWidth = 32,
                        TileHeight = 32,
                        Columns = columns,
                        Rows = rows,
                        Capacity = columns * rows,
                        Kind =
                            group.Key.StartsWith(
                                "sicon",
                                StringComparison.OrdinalIgnoreCase)
                                ? "SkillAtlas"
                                : "InterfaceAtlas",
                        PreferredPreviewPath = Relative(root, preferred),
                        Files = variants
                            .Select(x => Relative(root, x))
                            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    });
            }

            return result
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<DirectImageEntry> BuildDirectIcons(
            string root,
            string category)
        {
            string folder = Path.Combine(root, category);

            if (!Directory.Exists(folder))
                return new List<DirectImageEntry>();

            var result = new List<DirectImageEntry>();

            foreach (string file in
                     Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!IsSupportedImage(file))
                    continue;

                string id = Path.GetFileNameWithoutExtension(file);

                if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsDigit))
                    continue;

                string containingFolder =
                    Path.GetFileName(
                        Path.GetDirectoryName(file) ?? string.Empty);

                TryReadDimensions(
                    file,
                    out int width,
                    out int height);

                result.Add(
                    new DirectImageEntry
                    {
                        Id = id,
                        FolderName = containingFolder,
                        RelativePath = Relative(root, file),
                        Extension = Path.GetExtension(file),
                        Width = width,
                        Height = height,
                        IsExpected32x32 =
                            width == 32 &&
                            height == 32
                    });
            }

            return result
                .OrderBy(x => ParseSortableId(x.Id))
                .ThenBy(x => x.FolderName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool TryReadDimensions(
            string path,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;

            try
            {
                string extension = Path.GetExtension(path);

                if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                    return ReadBmp(path, out width, out height);

                if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
                    return ReadTga(path, out width, out height);

                if (extension.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    return ReadDds(path, out width, out height);
            }
            catch
            {
                width = 0;
                height = 0;
            }

            return false;
        }

        private static bool ReadBmp(
            string path,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;

            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new(fs);

            if (fs.Length < 26 || br.ReadUInt16() != 0x4D42)
                return false;

            fs.Position = 18;
            width = Math.Abs(br.ReadInt32());
            height = Math.Abs(br.ReadInt32());

            return width > 0 && height > 0;
        }

        private static bool ReadTga(
            string path,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;

            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new(fs);

            if (fs.Length < 18)
                return false;

            fs.Position = 12;
            width = br.ReadUInt16();
            height = br.ReadUInt16();

            return width > 0 && height > 0;
        }

        private static bool ReadDds(
            string path,
            out int width,
            out int height)
        {
            width = 0;
            height = 0;

            using FileStream fs = File.OpenRead(path);
            using BinaryReader br = new(fs);

            if (fs.Length < 20)
                return false;

            byte[] magic = br.ReadBytes(4);

            if (magic.Length != 4 ||
                magic[0] != (byte)'D' ||
                magic[1] != (byte)'D' ||
                magic[2] != (byte)'S' ||
                magic[3] != (byte)' ')
            {
                return false;
            }

            fs.Position = 12;
            height = checked((int)br.ReadUInt32());
            width = checked((int)br.ReadUInt32());

            return width > 0 && height > 0;
        }

        private static bool IsSupportedImage(string path) =>
            ImageExtensions.Contains(
                Path.GetExtension(path),
                StringComparer.OrdinalIgnoreCase);

        private static string Relative(string root, string path) =>
            Path.GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/');

        private static ulong ParseSortableId(string id) =>
            ulong.TryParse(id, out ulong value)
                ? value
                : ulong.MaxValue;
    }
}
