using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class DigimonBookDigimonEntry
    {
        public uint Id { get; init; }
        public uint ModelId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Display => $"{Id} — {Name}";
    }

    public static class DigimonBookDigimonCatalog
    {
        private static readonly object Sync = new();
        private static string? _loadedPath;
        private static DateTime _loadedWriteUtc;
        private static List<DigimonBookDigimonEntry> _entries = new();
        private static Dictionary<uint, DigimonBookDigimonEntry> _byId = new();

        public static IReadOnlyList<DigimonBookDigimonEntry> Load()
        {
            string path = ResolveDigimonListPath();
            DateTime write = File.GetLastWriteTimeUtc(path);

            lock (Sync)
            {
                if (string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase) &&
                    _loadedWriteUtc == write && _entries.Count > 0)
                    return _entries;

                XDocument doc = XDocument.Load(path, LoadOptions.None);
                XElement root = doc.Root ?? throw new InvalidDataException("Digimon_List.xml has no root element.");

                var rows = new List<DigimonBookDigimonEntry>();
                foreach (XElement digimon in root.Elements("Digimon"))
                {
                    uint id = ReadUInt(digimon.Attribute("ID")?.Value);
                    if (id == 0) continue;

                    string name = digimon.Attribute("Name")?.Value ?? string.Empty;
                    uint modelId = ReadUInt(digimon.Element("ModelID")?.Value);
                    rows.Add(new DigimonBookDigimonEntry
                    {
                        Id = id,
                        ModelId = modelId,
                        Name = string.IsNullOrWhiteSpace(name) ? $"Digimon {id}" : name
                    });
                }

                _entries = rows.OrderBy(x => x.Id).ToList();
                _byId = _entries.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
                _loadedPath = path;
                _loadedWriteUtc = write;
                return _entries;
            }
        }

        public static bool TryGet(uint id, out DigimonBookDigimonEntry entry)
        {
            Load();
            lock (Sync)
                return _byId.TryGetValue(id, out entry!);
        }

        public static Bitmap? TryLoadIcon(uint id)
        {
            if (id == 0) return null;
            if (TryGet(id, out DigimonBookDigimonEntry entry))
                return DigimonEvoIconResolver.TryLoad(entry.Id, entry.ModelId);
            return DigimonEvoIconResolver.TryLoad(id, id);
        }

        public static IReadOnlyList<DigimonBookDigimonEntry> Search(string? query, int limit = 250)
        {
            string value = (query ?? string.Empty).Trim();
            IEnumerable<DigimonBookDigimonEntry> rows = Load();
            if (value.Length > 0)
            {
                rows = rows.Where(x =>
                    x.Id.ToString(CultureInfo.InvariantCulture).Contains(value, StringComparison.OrdinalIgnoreCase) ||
                    x.ModelId.ToString(CultureInfo.InvariantCulture).Contains(value, StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
            }
            return rows.Take(Math.Max(1, limit)).ToList();
        }

        private static string ResolveDigimonListPath()
        {
            string[] candidates =
            {
                Path.Combine(AppPaths.Xml, "Digimon_List", "Digimon_List.xml"),
                Path.Combine(AppPaths.Xml, "DigimonList", "DigimonList.xml"),
                Path.Combine(AppPaths.Xml, "Digimon_List.xml"),
                Path.Combine(AppPaths.Xml, "DigimonList.xml")
            };

            string? direct = candidates.FirstOrDefault(File.Exists);
            if (direct != null) return direct;

            string? discovered = Directory.Exists(AppPaths.Xml)
                ? Directory.EnumerateFiles(AppPaths.Xml, "*.xml", SearchOption.AllDirectories)
                    .FirstOrDefault(x =>
                        Path.GetFileName(x).Equals("Digimon_List.xml", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(x).Equals("DigimonList.xml", StringComparison.OrdinalIgnoreCase))
                : null;

            if (discovered != null) return discovered;
            throw new FileNotFoundException("Digimon_List.xml was not found in the XML workspace.");
        }

        private static uint ReadUInt(string? value) =>
            uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result) ? result : 0;
    }
}
