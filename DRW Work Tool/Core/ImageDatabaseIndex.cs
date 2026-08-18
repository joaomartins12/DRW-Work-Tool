using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DRW_Work_Tool.Core
{
    public sealed class ImageDatabaseIndexDocument
    {
        public int Version { get; set; } = 1;
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
        public List<InterfaceAtlasEntry> InterfaceAtlases { get; set; } = new();
        public List<DirectImageEntry> DigimonIcons { get; set; } = new();
        public List<DirectImageEntry> TamerIcons { get; set; } = new();
        public List<DirectImageEntry> NpcIcons { get; set; } = new();
    }

    public sealed class InterfaceAtlasEntry
    {
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int TileWidth { get; set; } = 32;
        public int TileHeight { get; set; } = 32;
        public int Columns { get; set; }
        public int Rows { get; set; }
        public int Capacity { get; set; }

        // Classificação automática da família do atlas.
        // Exemplos: SkillAtlas, InterfaceAtlas.
        public string Kind { get; set; } = "InterfaceAtlas";

        public string PreferredPreviewPath { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
    }

    public sealed class DirectImageEntry
    {
        public string Id { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;

        // Os Digimon/Tamer icons são imagens diretas; não são atlas.
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsExpected32x32 { get; set; }
    }

    public sealed class InterfaceIconMapDocument
    {
        public int Version { get; set; } = 1;
        public List<InterfaceIconMapEntry> Icons { get; set; } = new();
    }

    public sealed class InterfaceIconMapEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Atlas { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = 32;
        public int Height { get; set; } = 32;

        // Opcional. Exemplos futuros: Item, Skill, Achieve, CashShop.
        public string Category { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    public sealed class ResolvedImageReference
    {
        public string Id { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public bool IsAtlas { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string Category { get; init; } = string.Empty;
        public string AtlasName { get; init; } = string.Empty;
    }

    public sealed class ImageDatabaseIndexService
    {
        private readonly string _databaseRoot;
        private ImageDatabaseIndexDocument _index = new();
        private InterfaceIconMapDocument _interfaceMap = new();

        private Dictionary<string, DirectImageEntry> _digimonById =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, DirectImageEntry> _tamerById =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, DirectImageEntry> _npcById =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, InterfaceAtlasEntry> _atlasByName =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, List<InterfaceIconMapEntry>> _interfaceById =
            new(StringComparer.OrdinalIgnoreCase);

        public ImageDatabaseIndexService(string? databaseRoot = null)
        {
            _databaseRoot =
                string.IsNullOrWhiteSpace(databaseRoot)
                    ? Path.Combine(AppContext.BaseDirectory, "ImgDatabase")
                    : Path.GetFullPath(databaseRoot);
        }

        public string DatabaseRoot => _databaseRoot;
        public string IndexPath => Path.Combine(_databaseRoot, "ImageDatabase.json");
        public string InterfaceMapPath => Path.Combine(_databaseRoot, "InterfaceIconMap.json");

        public ImageDatabaseIndexDocument Index => _index;
        public InterfaceIconMapDocument InterfaceMap => _interfaceMap;

        public void Load(bool rebuildIndexIfMissing = true)
        {
            Directory.CreateDirectory(_databaseRoot);

            if (!File.Exists(IndexPath) && rebuildIndexIfMissing)
                ImageDatabaseIndexBuilder.Rebuild(_databaseRoot);

            _index =
                File.Exists(IndexPath)
                    ? Deserialize<ImageDatabaseIndexDocument>(IndexPath)
                    : new ImageDatabaseIndexDocument();

            if (!File.Exists(InterfaceMapPath))
                CreateEmptyInterfaceMap();

            _interfaceMap =
                File.Exists(InterfaceMapPath)
                    ? Deserialize<InterfaceIconMapDocument>(InterfaceMapPath)
                    : new InterfaceIconMapDocument();

            BuildLookups();
        }

        public void ReloadInterfaceMap()
        {
            _interfaceMap =
                File.Exists(InterfaceMapPath)
                    ? Deserialize<InterfaceIconMapDocument>(InterfaceMapPath)
                    : new InterfaceIconMapDocument();

            BuildInterfaceLookup();
        }

        public bool TryGetDigimonIcon(
            uint id,
            out ResolvedImageReference image) =>
            TryGetDigimonIcon(id.ToString(), out image);

        public bool TryGetDigimonIcon(
            string id,
            out ResolvedImageReference image)
        {
            if (_digimonById.TryGetValue(NormalizeId(id), out DirectImageEntry? entry))
            {
                image = Direct(entry);
                return File.Exists(image.SourcePath);
            }

            image = new ResolvedImageReference();
            return false;
        }

        public bool TryGetTamerIcon(
            uint id,
            out ResolvedImageReference image) =>
            TryGetTamerIcon(id.ToString(), out image);

        public bool TryGetTamerIcon(
            string id,
            out ResolvedImageReference image)
        {
            if (_tamerById.TryGetValue(NormalizeId(id), out DirectImageEntry? entry))
            {
                image = Direct(entry);
                return File.Exists(image.SourcePath);
            }

            image = new ResolvedImageReference();
            return false;
        }

        public bool TryGetNpcIcon(
            uint id,
            out ResolvedImageReference image) =>
            TryGetNpcIcon(
                id.ToString(),
                out image);

        public bool TryGetNpcIcon(
            string id,
            out ResolvedImageReference image)
        {
            if (_npcById.TryGetValue(
                NormalizeId(id),
                out DirectImageEntry? entry))
            {
                image = Direct(entry);
                return File.Exists(
                    image.SourcePath);
            }

            image = new ResolvedImageReference();
            return false;
        }

        public bool TryGetInterfaceIcon(
            uint id,
            out ResolvedImageReference image,
            string? category = null) =>
            TryGetInterfaceIcon(id.ToString(), out image, category);

        public bool TryGetInterfaceIcon(
            string id,
            out ResolvedImageReference image,
            string? category = null)
        {
            string key = NormalizeId(id);

            if (!_interfaceById.TryGetValue(key, out List<InterfaceIconMapEntry>? entries))
            {
                image = new ResolvedImageReference();
                return false;
            }

            InterfaceIconMapEntry? mapping = null;

            if (!string.IsNullOrWhiteSpace(category))
            {
                mapping = entries.FirstOrDefault(
                    x => x.Category.Equals(
                        category,
                        StringComparison.OrdinalIgnoreCase));
            }

            mapping ??= entries.FirstOrDefault();

            if (mapping == null ||
                !_atlasByName.TryGetValue(mapping.Atlas, out InterfaceAtlasEntry? atlas))
            {
                image = new ResolvedImageReference();
                return false;
            }

            string source = ResolveRelativePath(atlas.PreferredPreviewPath);

            image = new ResolvedImageReference
            {
                Id = mapping.Id,
                SourcePath = source,
                IsAtlas = true,
                X = mapping.X,
                Y = mapping.Y,
                Width = mapping.Width,
                Height = mapping.Height,
                Category = mapping.Category,
                AtlasName = mapping.Atlas
            };

            return File.Exists(source);
        }

        public IReadOnlyList<ResolvedImageReference> GetAllInterfaceIcons(string id)
        {
            string key = NormalizeId(id);

            if (!_interfaceById.TryGetValue(key, out List<InterfaceIconMapEntry>? entries))
                return Array.Empty<ResolvedImageReference>();

            var result = new List<ResolvedImageReference>();

            foreach (InterfaceIconMapEntry mapping in entries)
            {
                if (!_atlasByName.TryGetValue(mapping.Atlas, out InterfaceAtlasEntry? atlas))
                    continue;

                result.Add(
                    new ResolvedImageReference
                    {
                        Id = mapping.Id,
                        SourcePath = ResolveRelativePath(atlas.PreferredPreviewPath),
                        IsAtlas = true,
                        X = mapping.X,
                        Y = mapping.Y,
                        Width = mapping.Width,
                        Height = mapping.Height,
                        Category = mapping.Category,
                        AtlasName = mapping.Atlas
                    });
            }

            return result;
        }

        public InterfaceAtlasEntry? GetAtlas(string name) =>
            _atlasByName.TryGetValue(name, out InterfaceAtlasEntry? atlas)
                ? atlas
                : null;

        private void BuildLookups()
        {
            _digimonById =
                _index.DigimonIcons
                    .GroupBy(x => NormalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First(),
                        StringComparer.OrdinalIgnoreCase);

            _tamerById =
                _index.TamerIcons
                    .GroupBy(x => NormalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First(),
                        StringComparer.OrdinalIgnoreCase);

            _npcById =
                _index.NpcIcons
                    .GroupBy(x => NormalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First(),
                        StringComparer.OrdinalIgnoreCase);

            _atlasByName =
                _index.InterfaceAtlases
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First(),
                        StringComparer.OrdinalIgnoreCase);

            BuildInterfaceLookup();
        }

        private void BuildInterfaceLookup()
        {
            _interfaceById =
                _interfaceMap.Icons
                    .GroupBy(x => NormalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToList(),
                        StringComparer.OrdinalIgnoreCase);
        }

        private ResolvedImageReference Direct(DirectImageEntry entry) =>
            new()
            {
                Id = entry.Id,
                SourcePath = ResolveRelativePath(entry.RelativePath),
                IsAtlas = false,
                X = 0,
                Y = 0,
                Width = 0,
                Height = 0,
                Category = string.Empty,
                AtlasName = string.Empty
            };

        private string ResolveRelativePath(string relativePath) =>
            Path.GetFullPath(
                Path.Combine(
                    _databaseRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private void CreateEmptyInterfaceMap()
        {
            var empty = new InterfaceIconMapDocument();
            Serialize(InterfaceMapPath, empty);
        }

        private static string NormalizeId(string id)
        {
            string value = (id ?? string.Empty).Trim();

            if (value.Length == 0)
                return value;

            // Permite procurar "0069009" e 69009 pelo mesmo icon sem perder
            // o ID original guardado no ficheiro JSON.
            string normalized = value.TrimStart('0');
            return normalized.Length == 0 ? "0" : normalized;
        }

        private static T Deserialize<T>(string path)
            where T : new()
        {
            string json = File.ReadAllText(path);

            return JsonSerializer.Deserialize<T>(json, JsonOptions())
                   ?? new T();
        }

        internal static void Serialize<T>(string path, T value)
        {
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            string json = JsonSerializer.Serialize(value, JsonOptions());
            File.WriteAllText(path, json);
        }

        private static JsonSerializerOptions JsonOptions() =>
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
    }
}
