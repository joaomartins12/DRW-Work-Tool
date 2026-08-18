using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public enum LinkedItemReferenceSource
    {
        Skill,
        Accessory
    }

    public sealed class LinkedItemReferenceRecord
    {
        public LinkedItemReferenceSource Source { get; init; }
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public uint IconId { get; init; }

        public int GainOption { get; init; }
        public int ChangeableOptionNumber { get; init; }

        public string SearchText { get; init; } = string.Empty;
    }

    /// <summary>
    /// Resolves ItemList.s_dwSkill references against both:
    /// - XML\Skill\Skill.xml
    /// - XML\ItemList\ItemAcessorys.xml
    ///
    /// The supplied data strongly indicates:
    ///   s_nSkillCodeType 0/1 -> Skill-oriented reference
    ///   s_nSkillCodeType 2   -> Accessory-oriented reference
    ///
    /// However IDs can overlap between both datasets, so the UI always keeps
    /// the source explicit instead of assuming that the numeric ID alone is unique.
    /// </summary>
    public sealed class LinkedItemReferenceService
    {
        private static readonly object SharedLock = new();
        private static LinkedItemReferenceService? Shared;

        private readonly Dictionary<uint, LinkedItemReferenceRecord> _skills = new();
        private readonly Dictionary<uint, LinkedItemReferenceRecord> _accessories = new();

        private readonly List<LinkedItemReferenceRecord> _skillOrdered = new();
        private readonly List<LinkedItemReferenceRecord> _accessoryOrdered = new();

        public string SkillXmlPath { get; private set; } = string.Empty;
        public string AccessoryXmlPath { get; private set; } = string.Empty;

        public int SkillCount => _skillOrdered.Count;
        public int AccessoryCount => _accessoryOrdered.Count;

        public static LinkedItemReferenceService GetShared()
        {
            lock (SharedLock)
            {
                if (Shared != null)
                    return Shared;

                var service = new LinkedItemReferenceService();
                service.LoadDefaultWorkspace();
                Shared = service;

                return service;
            }
        }

        public static void Preload()
        {
            _ = GetShared();
        }

        public static void InvalidateShared()
        {
            lock (SharedLock)
                Shared = null;
        }

        public void LoadDefaultWorkspace()
        {
            string skillPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Skill",
                    "Skill.xml");

            string accessoryPath =
                Path.Combine(
                    AppPaths.Xml,
                    "ItemList",
                    "ItemAcessorys.xml");

            Load(skillPath, accessoryPath);
        }

        public void Load(
            string skillXmlPath,
            string accessoryXmlPath)
        {
            SkillXmlPath =
                Path.GetFullPath(skillXmlPath);

            AccessoryXmlPath =
                Path.GetFullPath(accessoryXmlPath);

            _skills.Clear();
            _accessories.Clear();
            _skillOrdered.Clear();
            _accessoryOrdered.Clear();

            if (File.Exists(SkillXmlPath))
                LoadSkills(SkillXmlPath);

            if (File.Exists(AccessoryXmlPath))
                LoadAccessories(AccessoryXmlPath);
        }

        public bool TryResolvePreferred(
            uint id,
            int skillCodeType,
            out LinkedItemReferenceRecord record)
        {
            // Type 2 is overwhelmingly an ItemAcessorys reference in the supplied data.
            if (skillCodeType == 2)
            {
                if (_accessories.TryGetValue(id, out record!))
                    return true;

                if (_skills.TryGetValue(id, out record!))
                    return true;
            }
            else
            {
                if (_skills.TryGetValue(id, out record!))
                    return true;

                if (_accessories.TryGetValue(id, out record!))
                    return true;
            }

            record = null!;
            return false;
        }

        public bool ExistsInSkill(uint id) =>
            _skills.ContainsKey(id);

        public bool ExistsInAccessory(uint id) =>
            _accessories.ContainsKey(id);

        public IReadOnlyList<LinkedItemReferenceRecord> Search(
            LinkedItemReferenceSource source,
            string? query,
            int maxResults = 80)
        {
            IReadOnlyList<LinkedItemReferenceRecord> sourceRows =
                source == LinkedItemReferenceSource.Skill
                    ? _skillOrdered
                    : _accessoryOrdered;

            string q =
                (query ?? string.Empty)
                    .Trim();

            int limit =
                Math.Max(
                    1,
                    maxResults);

            if (q.Length == 0)
                return sourceRows.Take(limit).ToArray();

            string upper =
                q.ToUpperInvariant();

            var result =
                new List<LinkedItemReferenceRecord>(
                    Math.Min(limit, 80));

            foreach (LinkedItemReferenceRecord row in sourceRows)
            {
                if (row.Id.ToString(
                        CultureInfo.InvariantCulture)
                        .Contains(
                            q,
                            StringComparison.OrdinalIgnoreCase) ||
                    row.SearchText.Contains(
                        upper,
                        StringComparison.Ordinal))
                {
                    result.Add(row);

                    if (result.Count >= limit)
                        break;
                }
            }

            return result;
        }

        public int CountSearch(
            LinkedItemReferenceSource source,
            string? query)
        {
            IReadOnlyList<LinkedItemReferenceRecord> sourceRows =
                source == LinkedItemReferenceSource.Skill
                    ? _skillOrdered
                    : _accessoryOrdered;

            string q =
                (query ?? string.Empty)
                    .Trim();

            if (q.Length == 0)
                return sourceRows.Count;

            string upper =
                q.ToUpperInvariant();

            int count = 0;

            foreach (LinkedItemReferenceRecord row in sourceRows)
            {
                if (row.Id.ToString(
                        CultureInfo.InvariantCulture)
                        .Contains(
                            q,
                            StringComparison.OrdinalIgnoreCase) ||
                    row.SearchText.Contains(
                        upper,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private void LoadSkills(
            string path)
        {
            XDocument document =
                XDocument.Load(
                    path,
                    LoadOptions.None);

            XElement root =
                document.Root
                ?? throw new InvalidDataException(
                    "Skill.xml não possui root.");

            foreach (XElement skill in root.Elements("SkillData"))
            {
                if (!TryReadUInt(
                    skill.Element("s_dwID"),
                    out uint id))
                {
                    continue;
                }

                string name =
                    skill.Element("s_szName")?.Value
                    ?? string.Empty;

                string comment =
                    skill.Element("s_szComment")?.Value
                    ?? string.Empty;

                uint iconId = 0;

                TryReadUInt(
                    skill.Element("s_nIcon"),
                    out iconId);

                string displayName =
                    !string.IsNullOrWhiteSpace(name)
                        ? name.Trim()
                        : !string.IsNullOrWhiteSpace(comment)
                            ? comment.Trim()
                            : $"Skill {id}";

                var record =
                    new LinkedItemReferenceRecord
                    {
                        Source =
                            LinkedItemReferenceSource.Skill,
                        Id = id,
                        Name = displayName,
                        Comment = comment,
                        IconId = iconId,
                        SearchText =
                            (
                                displayName + "\n" +
                                comment
                            ).ToUpperInvariant()
                    };

                if (!_skills.ContainsKey(id))
                {
                    _skills[id] = record;
                    _skillOrdered.Add(record);
                }
            }

            _skillOrdered.Sort(
                (a, b) =>
                    a.Id.CompareTo(b.Id));
        }

        private void LoadAccessories(
            string path)
        {
            XDocument document =
                XDocument.Load(
                    path,
                    LoadOptions.None);

            XElement root =
                document.Root
                ?? throw new InvalidDataException(
                    "ItemAcessorys.xml não possui root.");

            // The supplied ItemAcessorys.xml is structurally nested:
            // <Item><Item><Item>...
            // Descendants("Item") intentionally handles the full real file.
            foreach (XElement item in root.Descendants("Item"))
            {
                if (!TryReadUInt(
                    item.Element("index_Accessory"),
                    out uint id))
                {
                    continue;
                }

                int gainOption =
                    ReadInt(
                        item.Element("Gain_Option"));

                int changeable =
                    ReadInt(
                        item.Element("Changeable_Option_Number"));

                var record =
                    new LinkedItemReferenceRecord
                    {
                        Source =
                            LinkedItemReferenceSource.Accessory,
                        Id = id,
                        Name =
                            $"Accessory Definition {id}",
                        Comment =
                            $"Gain Options: {gainOption} | " +
                            $"Changeable Options: {changeable}",
                        GainOption = gainOption,
                        ChangeableOptionNumber = changeable,
                        SearchText =
                            (
                                id.ToString(
                                    CultureInfo.InvariantCulture) +
                                "\nACCESSORY " +
                                "\nGAIN OPTION " +
                                gainOption.ToString(
                                    CultureInfo.InvariantCulture) +
                                "\nCHANGEABLE OPTION " +
                                changeable.ToString(
                                    CultureInfo.InvariantCulture)
                            ).ToUpperInvariant()
                    };

                if (!_accessories.ContainsKey(id))
                {
                    _accessories[id] = record;
                    _accessoryOrdered.Add(record);
                }
            }

            _accessoryOrdered.Sort(
                (a, b) =>
                    a.Id.CompareTo(b.Id));
        }

        private static bool TryReadUInt(
            XElement? element,
            out uint value)
        {
            return uint.TryParse(
                element?.Value?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static int ReadInt(
            XElement? element)
        {
            return int.TryParse(
                element?.Value?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                    ? value
                    : 0;
        }
    }
}
