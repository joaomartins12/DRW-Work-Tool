using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed record EditorItemReference(uint Id, string Name, uint IconId);
    public sealed record EditorMapReference(uint Id, string Name);
    public sealed record EditorNpcReference(uint Id, uint MapId, int Type, string Name, uint Model, string Tag);

    public sealed record EditorModelReference(
        uint Id,
        string Kind,
        string DisplayName,
        string KfmPath,
        string DigimonNames,
        string NpcNames);

    public sealed class EditorReferenceCatalogService
    {
        private readonly List<EditorItemReference> _items = new();
        private readonly List<EditorMapReference> _maps = new();
        private readonly List<EditorNpcReference> _npcs = new();
        private readonly List<EditorModelReference> _models = new();

        private readonly Dictionary<uint, EditorItemReference> _itemById = new();
        private readonly Dictionary<uint, EditorMapReference> _mapById = new();

        // MapName is frequently an internal resource name or only the numeric
        // ID. Keep it searchable, while the visible Name uses
        // MapDescription_Eng.
        private readonly Dictionary<uint, string> _mapInternalNames = new();

        private readonly Dictionary<uint, EditorNpcReference> _npcById = new();
        private readonly Dictionary<uint, EditorModelReference> _modelById = new();

        public IReadOnlyList<EditorItemReference> Items => _items;
        public IReadOnlyList<EditorMapReference> Maps => _maps;
        public IReadOnlyList<EditorNpcReference> Npcs => _npcs;
        public IReadOnlyList<EditorModelReference> Models => _models;

        public static string ResolveWorkspaceXml(
            string currentXmlPath,
            string entityFolder,
            string fileName)
        {
            string currentFolder =
                Path.GetDirectoryName(
                    Path.GetFullPath(currentXmlPath))
                ?? AppPaths.Xml;

            // 1) Same folder as the XML currently being edited.
            string sibling =
                Path.Combine(
                    currentFolder,
                    fileName);

            if (File.Exists(sibling))
                return sibling;

            // 2) Canonical Work Tool XML structure.
            string workspace =
                Path.Combine(
                    AppPaths.Xml,
                    entityFolder,
                    fileName);

            if (File.Exists(workspace))
                return workspace;

            // 3) Walk upwards looking for an XML root.
            DirectoryInfo? cursor =
                new DirectoryInfo(
                    currentFolder);

            while (cursor != null)
            {
                string candidate =
                    Path.Combine(
                        cursor.FullName,
                        "XML",
                        entityFolder,
                        fileName);

                if (File.Exists(candidate))
                    return candidate;

                cursor = cursor.Parent;
            }

            // 4) Robust fallback:
            // Search the Work Tool XML tree for the exact filename. This is
            // especially useful for Model.xml because older workspaces have
            // stored DMBase children under slightly different folders.
            if (Directory.Exists(
                AppPaths.Xml))
            {
                try
                {
                    string? exact =
                        Directory
                            .EnumerateFiles(
                                AppPaths.Xml,
                                fileName,
                                SearchOption.AllDirectories)
                            .OrderBy(
                                x =>
                                    x.Contains(
                                        Path.DirectorySeparatorChar +
                                        entityFolder +
                                        Path.DirectorySeparatorChar,
                                        StringComparison.OrdinalIgnoreCase)
                                        ? 0
                                        : 1)
                            .ThenBy(
                                x => x.Length)
                            .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(
                        exact))
                    {
                        return exact;
                    }
                }
                catch
                {
                    // Permission/transient IO error: keep canonical path below.
                }
            }

            return workspace;
        }

        public static EditorReferenceCatalogService Load(string currentXmlPath)
        {
            var service = new EditorReferenceCatalogService();

            string itemList = ResolveWorkspaceXml(currentXmlPath, "ItemList", "ItemList.xml");
            string npc = ResolveWorkspaceXml(currentXmlPath, "Npc", "Npc.xml");
            string mapList = ResolveWorkspaceXml(currentXmlPath, "MapList", "MapList.xml");
            string model = ResolveWorkspaceXml(currentXmlPath, "DMBase", "Model.xml");
            string digimonList = ResolveWorkspaceXml(currentXmlPath, "Digimon_List", "Digimon_List.xml");

            if (File.Exists(itemList))
                service.LoadItems(itemList);

            // Load NPC first so model cards can show all NPC names using a Model ID.
            if (File.Exists(npc))
                service.LoadNpcs(npc);

            if (File.Exists(mapList))
                service.LoadMaps(mapList);

            if (File.Exists(model))
                service.LoadModels(model, digimonList);

            return service;
        }

        public bool TryGetItem(uint id, out EditorItemReference item) => _itemById.TryGetValue(id, out item!);
        public bool TryGetMap(uint id, out EditorMapReference map) => _mapById.TryGetValue(id, out map!);
        public bool TryGetNpc(uint id, out EditorNpcReference npc) => _npcById.TryGetValue(id, out npc!);
        public bool TryGetModel(uint id, out EditorModelReference model) => _modelById.TryGetValue(id, out model!);

        public IReadOnlyList<EditorItemReference> SearchItems(string query, int take = 80)
        {
            query = (query ?? string.Empty).Trim();
            IEnumerable<EditorItemReference> source = _items;

            if (query.Length > 0)
            {
                source = source.Where(x =>
                    x.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(Math.Max(1, take)).ToArray();
        }

        public IReadOnlyList<EditorMapReference> SearchMaps(
            string query,
            int take = 60)
        {
            query =
                (query ?? string.Empty)
                    .Trim();

            IEnumerable<EditorMapReference> source =
                _maps;

            if (query.Length > 0)
            {
                source =
                    source.Where(
                        x =>
                            x.Id
                                .ToString(
                                    CultureInfo.InvariantCulture)
                                .Contains(
                                    query,
                                    StringComparison.OrdinalIgnoreCase) ||
                            x.Name.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase) ||
                            _mapInternalNames.TryGetValue(
                                x.Id,
                                out string? internalName) &&
                            internalName.Contains(
                                query,
                                StringComparison.OrdinalIgnoreCase));
            }

            return source
                .OrderBy(x => x.Id)
                .Take(
                    Math.Max(
                        1,
                        take))
                .ToArray();
        }

        public IReadOnlyList<EditorNpcReference> SearchNpcs(string query, int take = 80)
        {
            query = (query ?? string.Empty).Trim();
            IEnumerable<EditorNpcReference> source = _npcs;

            if (query.Length > 0)
            {
                source = source.Where(x =>
                    x.Id.ToString(CultureInfo.InvariantCulture)
                        .Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Tag.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Model.ToString(CultureInfo.InvariantCulture)
                        .Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Type.ToString(CultureInfo.InvariantCulture)
                        .Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    NpcTypeCatalog.GetName(x.Type)
                        .Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(Math.Max(1, take)).ToArray();
        }

        public IReadOnlyList<EditorModelReference> SearchModels(string query, int take = 100)
        {
            query = (query ?? string.Empty).Trim();
            IEnumerable<EditorModelReference> source = _models;

            if (query.Length > 0)
            {
                source = source.Where(x =>
                    x.Id.ToString(CultureInfo.InvariantCulture)
                        .Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.DigimonNames.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.NpcNames.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.KfmPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Kind.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return source
                .OrderBy(x => x.Id)
                .Take(Math.Max(1, take))
                .ToArray();
        }

        public void ReloadNpc(string currentXmlPath)
        {
            _npcs.Clear();
            _npcById.Clear();

            string npc = ResolveWorkspaceXml(currentXmlPath, "Npc", "Npc.xml");
            if (File.Exists(npc))
                LoadNpcs(npc);
        }

        private void LoadItems(string path)
        {
            XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            XElement? index = doc.Root?.Element("index");
            if (index == null)
                return;

            foreach (XElement node in index.Elements("sINFO"))
            {
                if (!uint.TryParse(node.Element("s_dwItemID")?.Value, out uint id))
                    continue;

                uint.TryParse(node.Element("s_nIcon")?.Value, out uint icon);
                string name = node.Element("s_szName")?.Value ?? string.Empty;

                var entry = new EditorItemReference(id, name, icon);
                if (_itemById.ContainsKey(id))
                    continue;

                _itemById[id] = entry;
                _items.Add(entry);
            }
        }

        private void LoadMaps(
            string path)
        {
            XDocument doc =
                XDocument.Load(
                    path,
                    LoadOptions.PreserveWhitespace);

            foreach (XElement node
                     in doc.Root?.Elements("Map")
                        ?? Enumerable.Empty<XElement>())
            {
                if (!uint.TryParse(
                    node.Element("MapID")?.Value,
                    out uint id))
                {
                    continue;
                }

                string internalName =
                    node.Element("MapName")?.Value
                    ?? string.Empty;

                string descriptionEng =
                    node.Element("MapDescription_Eng")?.Value
                    ?? string.Empty;

                // Human-readable editor label.
                // MapDescription_Eng is preferred because MapName is often
                // something like "3", "1300", "D_terminel_main01", etc.
                string displayName =
                    !string.IsNullOrWhiteSpace(
                        descriptionEng)
                        ? descriptionEng.Trim()
                        : !string.IsNullOrWhiteSpace(
                            internalName)
                            ? internalName.Trim()
                            : $"Map {id}";

                var entry =
                    new EditorMapReference(
                        id,
                        displayName);

                if (_mapById.ContainsKey(id))
                    continue;

                _mapById[id] =
                    entry;

                _mapInternalNames[id] =
                    internalName;

                _maps.Add(
                    entry);
            }
        }

        private void LoadNpcs(string path)
        {
            XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);

            foreach (XElement node in doc.Root?.Elements("NPC") ?? Enumerable.Empty<XElement>())
            {
                if (!uint.TryParse(node.Element("NpcID")?.Value, out uint id))
                    continue;

                uint.TryParse(node.Element("MapID")?.Value, out uint map);
                int.TryParse(node.Element("NPCType")?.Value, out int type);
                uint.TryParse(node.Element("Model")?.Value, out uint model);

                var entry = new EditorNpcReference(
                    id,
                    map,
                    type,
                    node.Element("NPCName")?.Value ?? string.Empty,
                    model,
                    node.Element("NPCTag")?.Value ?? string.Empty);

                if (_npcById.ContainsKey(id))
                    continue;

                _npcById[id] = entry;
                _npcs.Add(entry);
            }
        }

        private void LoadModels(string modelPath, string digimonListPath)
        {
            var digimonNamesByModel = new Dictionary<uint, List<string>>();

            if (File.Exists(digimonListPath))
            {
                XDocument digimonDoc =
                    XDocument.Load(digimonListPath, LoadOptions.PreserveWhitespace);

                foreach (XElement digimon
                         in digimonDoc.Root?.Elements("Digimon")
                            ?? Enumerable.Empty<XElement>())
                {
                    if (!uint.TryParse(
                        digimon.Element("ModelID")?.Value,
                        out uint modelId))
                    {
                        continue;
                    }

                    string name =
                        digimon.Attribute("Name")?.Value
                        ?? string.Empty;

                    if (!digimonNamesByModel.TryGetValue(
                        modelId,
                        out List<string>? names))
                    {
                        names = new List<string>();
                        digimonNamesByModel[modelId] = names;
                    }

                    if (!string.IsNullOrWhiteSpace(name) &&
                        !names.Any(x => x.Equals(
                            name,
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(name);
                    }
                }
            }

            var npcNamesByModel =
                _npcs
                    .Where(x => x.Model > 0)
                    .GroupBy(x => x.Model)
                    .ToDictionary(
                        x => x.Key,
                        x => x
                            .Select(n => n.Name)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList());

            XDocument modelDoc =
                XDocument.Load(modelPath, LoadOptions.PreserveWhitespace);

            foreach (XElement modelNode
                     in modelDoc.Root?.Elements("Model")
                        ?? Enumerable.Empty<XElement>())
            {
                if (!uint.TryParse(
                    modelNode.Element("s_dwID")?.Value,
                    out uint id))
                {
                    continue;
                }

                string kfm =
                    modelNode.Element("s_cKfmPath")?.Value
                    ?? string.Empty;

                string kind =
                    DetectModelKind(kfm);

                digimonNamesByModel.TryGetValue(
                    id,
                    out List<string>? digimonNames);

                npcNamesByModel.TryGetValue(
                    id,
                    out List<string>? npcNames);

                string digimonText =
                    string.Join(", ", digimonNames ?? new List<string>());

                string npcText =
                    string.Join(", ", npcNames ?? new List<string>());

                string displayName =
                    digimonNames?.FirstOrDefault()
                    ?? npcNames?.FirstOrDefault()
                    ?? GetModelPathName(kfm)
                    ?? $"Model {id}";

                var entry =
                    new EditorModelReference(
                        id,
                        kind,
                        displayName,
                        kfm,
                        digimonText,
                        npcText);

                if (!_modelById.ContainsKey(id))
                {
                    _modelById[id] = entry;
                    _models.Add(entry);
                }
            }
        }

        private static string DetectModelKind(string kfmPath)
        {
            string normalized =
                (kfmPath ?? string.Empty)
                    .Replace('/', '\\');

            if (normalized.Contains(
                "\\Digimon\\",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Digimon";
            }

            if (normalized.Contains(
                "\\Npc\\",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Npc";
            }

            if (normalized.Contains(
                "\\Tamer\\",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Tamer";
            }

            return "Other";
        }

        private static string? GetModelPathName(string kfmPath)
        {
            if (string.IsNullOrWhiteSpace(kfmPath))
                return null;

            string normalized =
                kfmPath.Replace('\\', '/');

            int lastSlash =
                normalized.LastIndexOf('/');

            if (lastSlash <= 0)
                return null;

            string parent =
                normalized[..lastSlash];

            int parentSlash =
                parent.LastIndexOf('/');

            return parentSlash >= 0
                ? parent[(parentSlash + 1)..]
                : parent;
        }
    }

    public static class NpcTypeCatalog
    {
        private static readonly Dictionary<int, string> Names = new()
        {
            [0] = "Standard / Quest NPC",
            [1] = "Shop / Sell NPC",
            [2] = "Return & Scan System",
            [3] = "Teleport / Portal System",
            [4] = "Digimon Hatch System",
            [5] = "Equipment Management",
            [6] = "Warehouse System",
            [7] = "Digimon Archive System",
            [8] = "Guild System",
            [9] = "DigiCore Merchant System",
            [10] = "Capsule Machine System",
            [12] = "Event Exchange System",
            [13] = "Clone / Reinforcement System",
            [14] = "Item-Gated Guide / Exchange System",
            [15] = "Event Machine System",
            [16] = "Master Match Event System",
            [18] = "Spirit Evolution System",
            [19] = "Card Game System",
            [20] = "Item Creator / Crafting System",
            [22] = "Arena Mode System",
            [23] = "Digimon Arena Guide System",
            [24] = "Special Evolution System"
        };

        public static IReadOnlyDictionary<int, string> All => Names;

        public static string GetName(int type) =>
            Names.TryGetValue(type, out string? name)
                ? name
                : $"Unknown / Preserved Type {type}";
    }
}
