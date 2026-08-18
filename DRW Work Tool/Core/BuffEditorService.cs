using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class BuffEditorRecord
    {
        public XElement Node { get; init; } = null!;
        public int PhysicalIndex { get; init; }
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public int BuffType { get; init; }
        public int LifeType { get; init; }
        public int TimeType { get; init; }
        public int MinLevel { get; init; }
        public int ConditionLevel { get; init; }
        public int BuffClass { get; init; }
        public uint SkillCode { get; init; }
        public uint DigimonSkillCode { get; init; }
        public int DeleteFlag { get; init; }
        public string EffectFile { get; init; } = string.Empty;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name)
                ? $"Unnamed Buff {Id}"
                : Name;
    }

    public sealed class BuffEditorService
    {
        private XDocument _document;
        private List<BuffEditorRecord> _records = new();
        private Dictionary<uint, List<BuffEditorRecord>> _byId = new();

        private BuffEditorService(
            string filePath,
            XDocument document)
        {
            FilePath = Path.GetFullPath(filePath);
            _document = document;
            Reindex();
        }

        public string FilePath { get; }

        public XElement Root =>
            _document.Root ??
            throw new InvalidDataException(
                "Buff.xml has no root element.");

        public IReadOnlyList<BuffEditorRecord> Records => _records;
        public int Count => _records.Count;

        public IReadOnlyList<int> BuffTypes =>
            _records.Select(x => x.BuffType).Distinct().OrderBy(x => x).ToArray();

        public IReadOnlyList<int> LifeTypes =>
            _records.Select(x => x.LifeType).Distinct().OrderBy(x => x).ToArray();

        public IReadOnlyList<int> TimeTypes =>
            _records.Select(x => x.TimeType).Distinct().OrderBy(x => x).ToArray();

        public static BuffEditorService Load(string filePath)
        {
            string full = Path.GetFullPath(filePath);

            if (!File.Exists(full))
            {
                throw new FileNotFoundException(
                    "Buff.xml was not found.",
                    full);
            }

            XDocument document =
                XDocument.Load(
                    full,
                    LoadOptions.PreserveWhitespace);

            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Buff.xml has no root element.");

            if (!root.Name.LocalName.Equals(
                    "BuffDataArray",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unexpected Buff.xml root <{root.Name.LocalName}>. Expected <BuffDataArray>.");
            }

            return new BuffEditorService(
                full,
                document);
        }

        public BuffEditorRecord? FindByNode(
            XElement node) =>
            _records.FirstOrDefault(
                x => ReferenceEquals(x.Node, node));

        public int CountId(uint id) =>
            _byId.TryGetValue(
                id,
                out List<BuffEditorRecord>? rows)
                    ? rows.Count
                    : 0;

        public bool IsIdAvailable(
            uint id,
            int? exceptPhysicalIndex = null)
        {
            if (id == 0)
                return false;

            if (!_byId.TryGetValue(
                    id,
                    out List<BuffEditorRecord>? records))
            {
                return true;
            }

            return records.All(
                x =>
                    exceptPhysicalIndex.HasValue &&
                    x.PhysicalIndex ==
                    exceptPhysicalIndex.Value);
        }

        public uint SuggestAvailableId(
            uint start = 1)
        {
            uint candidate =
                Math.Max(
                    1u,
                    start);

            HashSet<uint> ids =
                _records
                    .Select(x => x.Id)
                    .ToHashSet();

            while (candidate != uint.MaxValue &&
                   ids.Contains(candidate))
            {
                candidate++;
            }

            return candidate;
        }

        public IReadOnlyList<BuffEditorRecord> Search(
            string? query,
            int? buffType,
            int? lifeType,
            int? timeType)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            IEnumerable<BuffEditorRecord> result =
                _records;

            if (q.Length != 0)
            {
                result =
                    result.Where(
                        x =>
                            x.Id.ToString(
                                    CultureInfo.InvariantCulture)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase) ||
                            x.IconId.ToString(
                                    CultureInfo.InvariantCulture)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase) ||
                            x.SkillCode.ToString(
                                    CultureInfo.InvariantCulture)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase) ||
                            x.DigimonSkillCode.ToString(
                                    CultureInfo.InvariantCulture)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase) ||
                            x.DisplayName.Contains(
                                q,
                                StringComparison.CurrentCultureIgnoreCase) ||
                            x.Comment.Contains(
                                q,
                                StringComparison.CurrentCultureIgnoreCase) ||
                            x.EffectFile.Contains(
                                q,
                                StringComparison.CurrentCultureIgnoreCase));
            }

            if (buffType.HasValue)
                result = result.Where(x => x.BuffType == buffType.Value);

            if (lifeType.HasValue)
                result = result.Where(x => x.LifeType == lifeType.Value);

            if (timeType.HasValue)
                result = result.Where(x => x.TimeType == timeType.Value);

            return result
                .OrderBy(
                    x => x.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Id)
                .ThenBy(x => x.PhysicalIndex)
                .ToList();
        }

        public XElement CreateNewNode()
        {
            uint suggested =
                SuggestAvailableId(
                    _records.Count == 0
                        ? 1
                        : _records.Max(x => x.Id) + 1);

            return new XElement(
                "BuffData",
                new XElement("s_dwID", suggested),
                new XElement("s_szName", string.Empty),
                new XElement("s_szComment", string.Empty),
                new XElement("s_nBuffIcon", 0),
                new XElement("s_nBuffType", 1),
                new XElement("s_nBuffLifeType", 1),
                new XElement("s_nBuffTimeType", 1),
                new XElement("s_nMinLv", 1),
                new XElement("s_nBuffClass", 0),
                new XElement("unknow", 0),
                new XElement("s_dwSkillCode", 0),
                new XElement("s_dwDigimonSkillCode", 0),
                new XElement("s_bDelete", 0),
                new XElement("s_szEffectFile", string.Empty),
                new XElement("s_nConditionLv", 0),
                new XElement("u", 0));
        }

        public void CommitNew(
            XElement node)
        {
            Root.Add(
                new XElement(node));

            Save();
        }

        public void CommitEdit(
            int physicalIndex,
            XElement working)
        {
            XElement? target =
                Root
                    .Elements("BuffData")
                    .ElementAtOrDefault(
                        physicalIndex - 1);

            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Buff physical row {physicalIndex} no longer exists.");
            }

            target.ReplaceWith(
                new XElement(working));

            Save();
        }

        public void Delete(
            int physicalIndex)
        {
            XElement? target =
                Root
                    .Elements("BuffData")
                    .ElementAtOrDefault(
                        physicalIndex - 1);

            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Buff physical row {physicalIndex} no longer exists.");
            }

            target.Remove();
            Save();
        }

        public Bitmap? TryLoadIcon(uint iconId)
        {
            if (iconId == 0)
                return null;

            // Buff s_nBuffIcon uses the same sicon atlas family as Skill.xml.
            return ImageDatabasePreview.TryLoadInterfaceIcon(
                iconId,
                "Skill");
        }

        public void Save()
        {
            string backup =
                FilePath + ".editor.bak";

            if (File.Exists(FilePath))
                File.Copy(FilePath, backup, true);

            _document.Save(
                FilePath,
                SaveOptions.DisableFormatting);

            // Reload to guarantee that every editor holds nodes belonging to
            // the current document after a replace/delete.
            _document =
                XDocument.Load(
                    FilePath,
                    LoadOptions.PreserveWhitespace);

            Reindex();
        }

        private void Reindex()
        {
            _records =
                Root
                    .Elements("BuffData")
                    .Select(
                        (node, index) =>
                            new BuffEditorRecord
                            {
                                Node = node,
                                PhysicalIndex = index + 1,
                                Id = U(node, "s_dwID"),
                                Name = S(node, "s_szName"),
                                Comment =
                                    Normalize(
                                        S(node, "s_szComment")),
                                IconId = U(node, "s_nBuffIcon"),
                                BuffType = I(node, "s_nBuffType"),
                                LifeType = I(node, "s_nBuffLifeType"),
                                TimeType = I(node, "s_nBuffTimeType"),
                                MinLevel = I(node, "s_nMinLv"),
                                ConditionLevel = I(node, "s_nConditionLv"),
                                BuffClass = I(node, "s_nBuffClass"),
                                SkillCode = U(node, "s_dwSkillCode"),
                                DigimonSkillCode = U(node, "s_dwDigimonSkillCode"),
                                DeleteFlag = I(node, "s_bDelete"),
                                EffectFile = S(node, "s_szEffectFile")
                            })
                    .ToList();

            _byId =
                _records
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToList());
        }

        private static uint U(
            XElement node,
            string name) =>
            uint.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value)
                    ? value
                    : 0;

        private static int I(
            XElement node,
            string name) =>
            int.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                    ? value
                    : 0;

        private static string S(
            XElement node,
            string name) =>
            node.Element(name)?.Value
            ?? string.Empty;

        private static string Normalize(
            string value) =>
            (value ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
    }
}
