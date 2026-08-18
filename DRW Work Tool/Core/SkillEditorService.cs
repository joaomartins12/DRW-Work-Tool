using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class SkillEditorRecord
    {
        public XElement Node { get; init; } = null!;
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public int MaxLevel { get; init; }
        public int LevelupPoint { get; init; }
        public int AttributeType { get; init; }
        public int NatureType { get; init; }
        public int FamilyType { get; init; }
        public int SkillType { get; init; }
        public int Target { get; init; }
        public int AttackType { get; init; }
        public int UseHp { get; init; }
        public int UseDs { get; init; }
        public float Cooldown { get; init; }
        public int LimitLevel { get; init; }
        public int SkillRank { get; init; }
        public int MemorySkill { get; init; }
        public int RequiredItem { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name)
                ? $"Unnamed Skill {Id}"
                : Name;
    }

    /// <summary>
    /// Editable Skill.xml document + indexes used by the visual Skill editor.
    /// Unknown fields and XML ordering are preserved because edits are made
    /// directly against cloned SkillData XElement nodes.
    /// </summary>
    public sealed class SkillEditorService
    {
        private XDocument _document;
        private List<SkillEditorRecord> _records = new();
        private Dictionary<uint, List<SkillEditorRecord>> _byId = new();

        private SkillEditorService(
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
                "Skill.xml has no root element.");

        public IReadOnlyList<SkillEditorRecord> Records => _records;

        public int Count => _records.Count;

        public static SkillEditorService Load(string filePath)
        {
            string full = Path.GetFullPath(filePath);

            if (!File.Exists(full))
            {
                throw new FileNotFoundException(
                    "Skill.xml was not found.",
                    full);
            }

            XDocument document =
                XDocument.Load(
                    full,
                    LoadOptions.PreserveWhitespace);

            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Skill.xml has no root element.");

            if (!root.Name.LocalName.Equals(
                    "SkillDataArray",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unexpected Skill.xml root <{root.Name.LocalName}>. Expected <SkillDataArray>.");
            }

            return new SkillEditorService(
                full,
                document);
        }

        public bool IsIdAvailable(
            uint id,
            XElement? exceptNode = null)
        {
            if (id == 0)
                return false;

            if (!_byId.TryGetValue(id, out List<SkillEditorRecord>? records))
                return true;

            return records.All(
                x => ReferenceEquals(x.Node, exceptNode));
        }

        public int CountId(uint id) =>
            _byId.TryGetValue(
                id,
                out List<SkillEditorRecord>? records)
                ? records.Count
                : 0;

        public SkillEditorRecord? FindByNode(XElement node) =>
            _records.FirstOrDefault(
                x => ReferenceEquals(x.Node, node));

        public IReadOnlyList<SkillEditorRecord> Search(
            string? query,
            int? skillType,
            int? target,
            int? attribute)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            IEnumerable<SkillEditorRecord> result =
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
                            x.DisplayName.Contains(
                                q,
                                StringComparison.CurrentCultureIgnoreCase) ||
                            x.Comment.Contains(
                                q,
                                StringComparison.CurrentCultureIgnoreCase) ||
                            x.IconId.ToString(
                                    CultureInfo.InvariantCulture)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase));
            }

            if (skillType.HasValue)
                result = result.Where(x => x.SkillType == skillType.Value);

            if (target.HasValue)
                result = result.Where(x => x.Target == target.Value);

            if (attribute.HasValue)
                result = result.Where(x => x.AttributeType == attribute.Value);

            return result
                .OrderBy(
                    x => x.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Id)
                .ToList();
        }

        public IReadOnlyList<int> SkillTypes =>
            _records
                .Select(x => x.SkillType)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

        public IReadOnlyList<int> Targets =>
            _records
                .Select(x => x.Target)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

        public IReadOnlyList<int> Attributes =>
            _records
                .Select(x => x.AttributeType)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

        public IReadOnlyList<uint> IconIds =>
            _records
                .Select(x => x.IconId)
                .Where(x => x != 0)
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

        public Bitmap? TryLoadIcon(uint iconId)
        {
            if (iconId == 0)
                return null;

            return ImageDatabasePreview
                .TryLoadInterfaceIcon(
                    iconId,
                    "Skill");
        }

        public XElement CreateDefaultSkill()
        {
            uint nextId =
                _records.Count == 0
                    ? 1
                    : _records.Max(x => x.Id) + 1;

            var node =
                new XElement(
                    "SkillData",
                    new XElement("s_dwID", nextId),
                    new XElement("s_szName", $"New Skill {nextId}"),
                    new XElement("s_szComment", string.Empty),
                    new XElement(
                        "SkillApply",
                        CreateDefaultIncreaseApply(),
                        CreateDefaultIncreaseApply(),
                        CreateDefaultIncreaseApply()),
                    new XElement("s_nLevelupPoint", 0),
                    new XElement("s_nMaxLevel", 0),
                    new XElement("s_nAttributeType", 0),
                    new XElement("s_nNatureType", 0),
                    new XElement("s_nFamilyType", 0),
                    new XElement("s_nUseHP", 0),
                    new XElement("s_nUseDS", 0),
                    new XElement("s_nIcon", 0),
                    new XElement("s_nTarget", 0),
                    new XElement("s_nAttType", 0),
                    new XElement("s_fAttRange", 0),
                    new XElement("s_fAttRange_MinDmg", 0),
                    new XElement("s_fAttRange_NorDmg", 0),
                    new XElement("s_fAttRange_MaxDmg", 0),
                    new XElement("s_nAttSphere", 0),
                    new XElement("s_fCastingTime", 0),
                    new XElement("s_fDamageTime", 0),
                    new XElement("s_nDamageDay", 0),
                    new XElement("ink", 0),
                    new XElement("s_nDistanceTime", 0),
                    new XElement("s_fCooldownTime", 0),
                    new XElement("s_nCooldownDay", 0),
                    new XElement("unk", 0),
                    new XElement("s_fSkill_Velocity", 0),
                    new XElement("s_fSkill_Accel", 0),
                    new XElement("s_nSkillType", 0),
                    new XElement("s_nLimitLevel", 0),
                    new XElement("s_nSkillGroup", 0),
                    new XElement("s_nSkillRank", 0),
                    new XElement("s_nMemorySkill", 0),
                    new XElement("s_nReq_Item", 0),
                    new XElement("unk2", 0));

            Root.Add(node);
            Reindex();
            return node;
        }

        public void Remove(XElement node)
        {
            node.Remove();
            Reindex();
        }

        public void Reindex()
        {
            _records =
                Root
                    .Elements("SkillData")
                    .Select(
                        node =>
                            new SkillEditorRecord
                            {
                                Node = node,
                                Id = U(node, "s_dwID"),
                                Name = S(node, "s_szName"),
                                Comment = S(node, "s_szComment"),
                                IconId = U(node, "s_nIcon"),
                                MaxLevel = I(node, "s_nMaxLevel"),
                                LevelupPoint = I(node, "s_nLevelupPoint"),
                                AttributeType = I(node, "s_nAttributeType"),
                                NatureType = I(node, "s_nNatureType"),
                                FamilyType = I(node, "s_nFamilyType"),
                                SkillType = I(node, "s_nSkillType"),
                                Target = I(node, "s_nTarget"),
                                AttackType = I(node, "s_nAttType"),
                                UseHp = I(node, "s_nUseHP"),
                                UseDs = I(node, "s_nUseDS"),
                                Cooldown = F(node, "s_fCooldownTime"),
                                LimitLevel = I(node, "s_nLimitLevel"),
                                SkillRank = I(node, "s_nSkillRank"),
                                MemorySkill = I(node, "s_nMemorySkill"),
                                RequiredItem = I(node, "s_nReq_Item")
                            })
                    .ToList();

            _byId =
                _records
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToList());
        }

        public void Save()
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(FilePath)!);

            _document.Save(
                FilePath,
                SaveOptions.DisableFormatting);

            Reindex();
        }

        public static XElement CloneNode(XElement node) =>
            new XElement(node);

        public static void CopyInto(
            XElement destination,
            XElement source)
        {
            destination.ReplaceWith(
                new XElement(source));
        }

        public static uint U(
            XElement node,
            string name) =>
            uint.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value)
                ? value
                : 0;

        public static int I(
            XElement node,
            string name) =>
            int.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                ? value
                : 0;

        public static float F(
            XElement node,
            string name) =>
            float.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value)
                ? value
                : 0F;

        public static string S(
            XElement node,
            string name) =>
            node.Element(name)?.Value ?? string.Empty;

        public static void Set(
            XElement node,
            string name,
            string value)
        {
            XElement? element =
                node.Element(name);

            if (element == null)
            {
                node.Add(
                    new XElement(
                        name,
                        value));
            }
            else
            {
                element.Value = value;
            }
        }

        private static XElement CreateDefaultIncreaseApply() =>
            new XElement(
                "IncreaseApply",
                new XElement("s_nA", 0),
                new XElement("s_nInvoke_Rate", 0),
                new XElement("s_nB", 0),
                new XElement("s_nC", 0),
                new XElement("s_nBuffCode", 0),
                new XElement("s_nID", 0),
                new XElement("s_nIncrease_B_Point", 0));
    }
}
