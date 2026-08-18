using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class MonsterRecord
    {
        public required XElement Node { get; init; }
        public uint MonsterId { get; init; }
        public uint ModelDigimon { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public int Level { get; init; }
        public long HP { get; init; }
        public int DS { get; init; }
        public int AT { get; init; }
        public int DE { get; init; }
        public int HT { get; init; }
        public int CT { get; init; }
        public int EV { get; init; }
        public int MS { get; init; }
        public int WS { get; init; }
        public int AS { get; init; }
        public int AR { get; init; }
        public int Battle { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name)
                ? $"Monster {MonsterId}"
                : Name;
    }

    public sealed class MonsterSkillRecord
    {
        public required XElement Node { get; init; }
        public uint SkillIndex { get; init; }
        public uint MonsterId { get; init; }
        public int UseTerms { get; init; }
        public int SkillType { get; init; }
        public int CoolTime { get; init; }
        public int CastTime { get; init; }
        public int TargetCount { get; init; }
        public int TargetMinCount { get; init; }
        public int TargetMaxCount { get; init; }
        public int EffValMin { get; init; }
        public int EffValMax { get; init; }
        public int RangeIndex { get; init; }
        public int SequenceId { get; init; }
        public int AniDelay { get; init; }
        public int Velocity { get; init; }
        public int Accel { get; init; }
        public int EffectFactor1 { get; init; }
        public int EffectFactor2 { get; init; }
        public int EffectFactor3 { get; init; }
        public int EffectFactorValue1 { get; init; }
        public int EffectFactorValue2 { get; init; }
        public int EffectFactorValue3 { get; init; }
        public int TalkId { get; init; }
        public int ActiveType { get; init; }
        public int NoticeTime { get; init; }
        public string NoticeEffectName { get; init; } = string.Empty;
    }

    public sealed class MonsterSkillTermRecord
    {
        public required XElement Node { get; init; }
        public int Idx { get; init; }
        public int Direction { get; init; }
        public int Range { get; init; }
        public int TargetingType { get; init; }
        public int RefCode { get; init; }
    }

    public sealed class UseTermInfo
    {
        public int Value { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Animation { get; init; } = string.Empty;
        public bool Implemented { get; init; }
    }

    public static class MonsterUseTermCatalog
    {
        private static readonly Dictionary<int, UseTermInfo> _map = new()
        {
            [13] = new UseTermInfo { Value = 13, Name = "SUMMON_MONSTER", Description = "Spawns minions + debuffs", Animation = "MonsterSkillVisualPacket (1123) + LoadMobsPacket", Implemented = true },
            [14] = new UseTermInfo { Value = 14, Name = "GROWTH", Description = "Incremental stat stacking", Animation = "MonsterSkillVisualPacket (1123)", Implemented = true },
            [18] = new UseTermInfo { Value = 18, Name = "ATTACK_SEED", Description = "Ground DoT zones", Animation = "MonsterSkillAttachSeedPacket", Implemented = true },
            [19] = new UseTermInfo { Value = 19, Name = "BERSERK", Description = "Enrage stat boost", Animation = "MonsterSkillVisualPacket (1123)", Implemented = true },
            [15] = new UseTermInfo { Value = 15, Name = "CALL_UP", Description = "Calls surviving monsters on map", Animation = string.Empty, Implemented = false },
            [16] = new UseTermInfo { Value = 16, Name = "ASSEMBLE", Description = "Damage divided by target count", Animation = string.Empty, Implemented = false },
            [17] = new UseTermInfo { Value = 17, Name = "DISPERSE", Description = "Damage multiplied by target count", Animation = string.Empty, Implemented = false },
            [20] = new UseTermInfo { Value = 20, Name = "CONTINUE_WIDE_ATTACK", Description = "Repeated AoE damage", Animation = string.Empty, Implemented = false },
            [21] = new UseTermInfo { Value = 21, Name = "BUFF_OCCURE", Description = "Buff occurrence", Animation = string.Empty, Implemented = false },
            [22] = new UseTermInfo { Value = 22, Name = "Single_StackDeBuff_Attack", Description = "Stacking debuff per hit", Animation = string.Empty, Implemented = false },
            [23] = new UseTermInfo { Value = 23, Name = "Region_Buff_Nesting", Description = "Area buff accumulation", Animation = string.Empty, Implemented = false },
            [24] = new UseTermInfo { Value = 24, Name = "Range_Buff_Nesting", Description = "Range buff accumulation", Animation = string.Empty, Implemented = false },
            [25] = new UseTermInfo { Value = 25, Name = "GatheringExt", Description = "Extended gather (with projectile)", Animation = string.Empty, Implemented = false },
            [26] = new UseTermInfo { Value = 26, Name = "DisperseExt", Description = "Extended disperse (with projectile)", Animation = string.Empty, Implemented = false },
        };

        public static UseTermInfo Get(int value)
        {
            if (_map.TryGetValue(value, out UseTermInfo? info))
                return info;

            return new UseTermInfo
            {
                Value = value,
                Name = value == 0 ? "NONE" : $"UseTerms_{value}",
                Description = "No catalog information available yet.",
                Animation = string.Empty,
                Implemented = false
            };
        }

        public static IReadOnlyList<UseTermInfo> All =>
            _map.Values.OrderBy(x => x.Value).ToList();
    }

    public sealed class MonsterEditorService
    {
        private readonly XDocument _document;
        private readonly List<MonsterRecord> _records;
        private readonly Dictionary<uint, MonsterRecord> _byId;

        private MonsterEditorService(string filePath, XDocument document, List<MonsterRecord> records)
        {
            FilePath = filePath;
            _document = document;
            _records = records;
            _byId = records.GroupBy(x => x.MonsterId).ToDictionary(x => x.Key, x => x.First());
        }

        public string FilePath { get; }
        public XElement Root => _document.Root!;
        public IReadOnlyList<MonsterRecord> Records => _records;
        public int Count => _records.Count;

        public static MonsterEditorService Load(string filePath)
        {
            string full = Path.GetFullPath(filePath);
            XDocument doc = XDocument.Load(full, LoadOptions.PreserveWhitespace);
            XElement root = doc.Root ?? throw new InvalidDataException("Monster.xml has no root element.");

            List<MonsterRecord> records = root.Elements("Monster")
                .Select(x => new MonsterRecord
                {
                    Node = x,
                    MonsterId = U(x, "MonsterID"),
                    ModelDigimon = U(x, "ModelDigimon"),
                    Name = S(x, "Name"),
                    Comment = S(x, "Comment"),
                    Title = FirstNonEmpty(x, "Title"),
                    HP = L(x, "HP"),
                    DS = I(x, "DS"),
                    DE = I(x, "DE"),
                    EV = I(x, "EV"),
                    MS = I(x, "MS"),
                    WS = I(x, "WS"),
                    CT = I(x, "CT"),
                    AT = I(x, "AT"),
                    AS = I(x, "AS"),
                    AR = I(x, "AR"),
                    HT = I(x, "HT"),
                    Level = I(x, "Level"),
                    Battle = I(x, "Battle")
                })
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.MonsterId)
                .ToList();

            return new MonsterEditorService(full, doc, records);
        }

        public MonsterRecord? Find(uint id) => _byId.TryGetValue(id, out var v) ? v : null;

        public IReadOnlyList<MonsterRecord> Search(string? query)
        {
            string q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return _records;

            return _records.Where(x =>
                x.MonsterId.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.ModelDigimon.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                x.Comment.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                x.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        public XElement CreateNewMonster()
        {
            uint nextId = _records.Count == 0 ? 1u : _records.Max(x => x.MonsterId) + 1u;
            var node = new XElement("Monster",
                E("MonsterID", nextId),
                E("ModelDigimon", 0),
                E("Name", string.Empty),
                E("Comment", string.Empty),
                E("Title", string.Empty),
                E("HP", 0),
                E("DS", 0),
                E("DE", 0),
                E("EV", 0),
                E("MS", 0),
                E("WS", 0),
                E("CT", 0),
                E("AT", 0),
                E("AS", 0),
                E("AR", 0),
                E("HT", 0),
                E("Sight", 0),
                E("HuntRange", 0),
                E("Scale", 1),
                E("Unknown2", 0),
                E("Class", 0),
                E("Icon1", 0),
                E("Icon2", 0),
                E("Icon3", 0),
                E("Icon4", 0),
                E("Icon5", 0),
                E("Icon6", 0),
                E("ExpMin", 0),
                E("ExpMax", 0),
                E("Unknown3", 0),
                E("Title", string.Empty),
                E("Level", 1),
                E("EXP", 0),
                E("Battle", 0),
                E("Unknown", 0));

            Root.Add(node);
            return node;
        }

        public void Delete(XElement node)
        {
            node.Remove();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? AppContext.BaseDirectory);
            _document.Save(FilePath);
        }

        private static XElement E(string name, object? value) => new(name, value ?? string.Empty);
        private static uint U(XElement node, string name) => uint.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : 0;
        private static int I(XElement node, string name) => int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        private static long L(XElement node, string name) => long.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
        private static string S(XElement node, string name) => node.Elements(name).FirstOrDefault()?.Value ?? string.Empty;
        private static string FirstNonEmpty(XElement node, string name) => node.Elements(name).Select(x => x.Value ?? string.Empty).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    }

    public sealed class MonsterSkillEditorService
    {
        private readonly XDocument _document;
        private readonly List<MonsterSkillRecord> _records;
        private readonly Dictionary<uint, MonsterSkillRecord> _byId;

        private MonsterSkillEditorService(string filePath, XDocument document, List<MonsterSkillRecord> records)
        {
            FilePath = filePath;
            _document = document;
            _records = records;
            _byId = records.GroupBy(x => x.SkillIndex).ToDictionary(x => x.Key, x => x.First());
        }

        public string FilePath { get; }
        public XElement Root => _document.Root!;
        public IReadOnlyList<MonsterSkillRecord> Records => _records;
        public int Count => _records.Count;

        public static MonsterSkillEditorService Load(string filePath)
        {
            string full = Path.GetFullPath(filePath);
            XDocument doc = XDocument.Load(full, LoadOptions.PreserveWhitespace);
            XElement root = doc.Root ?? throw new InvalidDataException("MonstersSkill.xml has no root element.");

            List<MonsterSkillRecord> records = root.Elements("MonsterSkill")
                .Select(x => new MonsterSkillRecord
                {
                    Node = x,
                    SkillIndex = U(x, "Skill_IDX"),
                    MonsterId = U(x, "MonsterID"),
                    CoolTime = I(x, "CoolTime"),
                    CastTime = I(x, "CastTime"),
                    TargetCount = I(x, "Target_Cnt"),
                    TargetMinCount = I(x, "Target_MinCnt"),
                    TargetMaxCount = I(x, "Target_MaxCnt"),
                    UseTerms = I(x, "UseTerms"),
                    SkillType = I(x, "Skill_Type"),
                    EffValMin = I(x, "Eff_Val_Min"),
                    EffValMax = I(x, "Eff_Val_Max"),
                    RangeIndex = I(x, "RangeIDX"),
                    SequenceId = I(x, "SequenceID"),
                    AniDelay = I(x, "Ani_Delay"),
                    Velocity = I(x, "Valocity"),
                    Accel = I(x, "Accel"),
                    EffectFactor1 = I(x, "Eff_Factor"),
                    EffectFactor2 = I(x, "Eff_Factor2"),
                    EffectFactor3 = I(x, "Eff_Factor3"),
                    EffectFactorValue1 = I(x, "Eff_Fact_Val"),
                    EffectFactorValue2 = I(x, "Eff_Fact_Val2"),
                    EffectFactorValue3 = I(x, "Eff_Fact_Val3"),
                    TalkId = I(x, "TalkID"),
                    ActiveType = I(x, "Activetype"),
                    NoticeTime = I(x, "NoticeTime"),
                    NoticeEffectName = S(x, "NoticeEffname")
                })
                .OrderBy(x => x.MonsterId)
                .ThenBy(x => x.SkillIndex)
                .ToList();

            return new MonsterSkillEditorService(full, doc, records);
        }

        public IReadOnlyList<MonsterSkillRecord> Search(string? query, int? useTerms = null)
        {
            string q = (query ?? string.Empty).Trim();
            IEnumerable<MonsterSkillRecord> items = _records;

            if (useTerms.HasValue)
                items = items.Where(x => x.UseTerms == useTerms.Value);

            if (q.Length == 0)
                return items.ToList();

            return items.Where(x =>
                x.SkillIndex.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.MonsterId.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.SkillType.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                MonsterUseTermCatalog.Get(x.UseTerms).Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                MonsterUseTermCatalog.Get(x.UseTerms).Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public XElement CreateNewSkill()
        {
            uint nextId = _records.Count == 0 ? 1u : _records.Max(x => x.SkillIndex) + 1u;
            var node = new XElement("MonsterSkill",
                E("Skill_IDX", nextId), E("unk", 119), E("MonsterID", 0), E("CoolTime", 0), E("CastTime", 0), E("CastCheck", 0),
                E("Target_Cnt", 0), E("Target_MinCnt", 0), E("Target_MaxCnt", 0), E("UseTerms", 0), E("Skill_Type", 27045),
                E("Eff_Val_Min", 0), E("Eff_Val_Max", 0), E("unk2", 0), E("RangeIDX", 25), E("SequenceID", 0), E("Ani_Delay", 0),
                E("Valocity", 0), E("Accel", 0), E("Eff_Factor", 0), E("Eff_Factor2", 0), E("Eff_Factor3", 0),
                E("Eff_Fact_Val", 0), E("Eff_Fact_Val2", 0), E("Eff_Fact_Val3", 0), E("TalkID", 0), E("Activetype", 0),
                E("NoticeTime", 0), E("NoticeEffname", string.Empty));
            Root.Add(node);
            return node;
        }

        public void Delete(XElement node) => node.Remove();
        public void Save() => _document.Save(FilePath);

        private static XElement E(string name, object? value) => new(name, value ?? string.Empty);
        private static uint U(XElement node, string name) => uint.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value) ? value : 0;
        private static int I(XElement node, string name) => int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        private static string S(XElement node, string name) => node.Element(name)?.Value ?? string.Empty;
    }

    public sealed class MonsterSkillTermsEditorService
    {
        private readonly XDocument _document;
        private readonly List<MonsterSkillTermRecord> _records;

        private MonsterSkillTermsEditorService(string filePath, XDocument document, List<MonsterSkillTermRecord> records)
        {
            FilePath = filePath;
            _document = document;
            _records = records;
        }

        public string FilePath { get; }
        public XElement Root => _document.Root!;
        public IReadOnlyList<MonsterSkillTermRecord> Records => _records;

        public static MonsterSkillTermsEditorService Load(string filePath)
        {
            string full = Path.GetFullPath(filePath);
            XDocument doc = XDocument.Load(full, LoadOptions.PreserveWhitespace);
            XElement root = doc.Root ?? throw new InvalidDataException("MonstersSkillTerms.xml has no root element.");
            List<MonsterSkillTermRecord> records = root.Elements("MonsterSkillTerm")
                .Select(x => new MonsterSkillTermRecord
                {
                    Node = x,
                    Idx = I(x, "IDX"),
                    Direction = I(x, "Direction"),
                    Range = I(x, "Range"),
                    TargetingType = I(x, "TargetingType"),
                    RefCode = I(x, "RefCode")
                })
                .OrderBy(x => x.Idx)
                .ToList();
            return new MonsterSkillTermsEditorService(full, doc, records);
        }

        public IReadOnlyList<MonsterSkillTermRecord> Search(string? query)
        {
            string q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return _records;
            return _records.Where(x =>
                x.Idx.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Range.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.TargetingType.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.RefCode.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public void Save() => _document.Save(FilePath);
        private static int I(XElement node, string name) => int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    public sealed class MonsterReferenceCatalog
    {
        private readonly List<MonsterRecord> _records;
        private readonly Dictionary<uint, MonsterRecord> _byId;

        public MonsterReferenceCatalog(MonsterEditorService service)
        {
            _records = service.Records.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.MonsterId).ToList();
            _byId = _records.GroupBy(x => x.MonsterId).ToDictionary(x => x.Key, x => x.First());
        }

        public IReadOnlyList<MonsterRecord> Records => _records;
        public MonsterRecord? Find(uint id) => _byId.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<MonsterRecord> Search(string? query)
        {
            string q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return _records;
            return _records.Where(x =>
                x.MonsterId.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.ModelDigimon.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
    }


    public sealed class BuffMiniRecord
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Buff {Id}" : Name;
    }

    public sealed class BuffMiniCatalog
    {
        private readonly List<BuffMiniRecord> _records;
        private readonly Dictionary<uint, BuffMiniRecord> _byId;

        private BuffMiniCatalog(List<BuffMiniRecord> records)
        {
            _records = records;
            _byId = records.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First());
        }

        public IReadOnlyList<BuffMiniRecord> Records => _records;
        public BuffMiniRecord? Find(uint id) => _byId.TryGetValue(id, out var v) ? v : null;
        public IReadOnlyList<BuffMiniRecord> Search(string? query)
        {
            string q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return _records;
            return _records.Where(x =>
                x.Id.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.IconId.ToString(CultureInfo.InvariantCulture).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
                x.Comment.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }

        public static BuffMiniCatalog? TryLoadDefault()
        {
            // Reuse the Buff.xml catalog already prepared by LoadingForm when
            // possible. This avoids parsing 877 buffs again when the monster
            // skill editor is opened.
            BuffReferenceService? preloaded =
                EditorPreloadService.TryGetBuffReferences();

            if (preloaded != null)
            {
                List<BuffMiniRecord> fromMemory = preloaded.Records
                    .Select(x => new BuffMiniRecord
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Comment = x.Comment,
                        IconId = x.IconId
                    })
                    .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Id)
                    .ToList();

                return new BuffMiniCatalog(fromMemory);
            }

            string path = Path.Combine(AppPaths.Xml, "Buff", "Buff.xml");
            if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, "Buff.xml");
            if (!File.Exists(path))
                return null;

            XDocument doc = XDocument.Load(path, LoadOptions.None);
            XElement? root = doc.Root;
            if (root == null)
                return null;

            List<BuffMiniRecord> records = root.Elements("BuffData")
                .Select(x => new BuffMiniRecord
                {
                    Id = uint.TryParse(x.Element("s_dwID")?.Value, out uint id) ? id : 0,
                    Name = x.Element("s_szName")?.Value ?? string.Empty,
                    Comment = x.Element("s_szComment")?.Value ?? string.Empty,
                    IconId = uint.TryParse(x.Element("s_nBuffIcon")?.Value, out uint icon) ? icon : 0,
                })
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Id)
                .ToList();
            return new BuffMiniCatalog(records);
        }
    }
    public static class MonsterAssetResolver
    {
        private static readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<uint, Bitmap?> _digimonCache = new();
        private static readonly Dictionary<uint, Bitmap?> _buffCache = new();
        private static readonly object _pathIndexSync = new();
        private static readonly Dictionary<string, Dictionary<string, string>> _pathIndexes =
            new(StringComparer.OrdinalIgnoreCase);

        public static Bitmap? TryGetPreloadedMonsterDigimonIcon(
            uint digimonId)
        {
            if (digimonId == 0)
                return null;

            lock (_digimonCache)
            {
                if (_digimonCache.TryGetValue(
                        digimonId,
                        out Bitmap? cached))
                {
                    return cached;
                }
            }

            Bitmap? image =
                EditorPreloadService.TryGetDigimonIcon(
                    digimonId);

            lock (_digimonCache)
                _digimonCache[digimonId] = image;

            return image;
        }

        public static Bitmap? TryLoadMonsterDigimonIcon(uint digimonId)
        {
            if (digimonId == 0)
                return null;

            Bitmap? image =
                TryGetPreloadedMonsterDigimonIcon(
                    digimonId);

            if (image != null)
                return image;

            // Slow disk fallback is intentionally kept out of card-list
            // rendering. It is only used by detail/picker views.
            image = FindByNumericFile(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "ImgDatabase",
                    "Digimon"),
                digimonId);

            lock (_digimonCache)
                _digimonCache[digimonId] = image;

            return image;
        }

        public static Bitmap? TryLoadBuffIcon(uint iconId)
        {
            if (iconId == 0)
                return null;

            if (_buffCache.TryGetValue(iconId, out Bitmap? cached))
                return cached;

            // Buff.xml s_nBuffIcon uses the same Skill/sicon atlas mapping as
            // the Skill editor. ImageDatabasePreview.PreloadAllInterfaceIcons()
            // is executed by LoadingForm, so this is normally a pure RAM lookup.
            Bitmap? image = ImageDatabasePreview.TryLoadInterfaceIcon(iconId, "Skill");

            // Conservative fallback for loose exported image databases.
            image ??= FindByNumericFile(
                Path.Combine(AppContext.BaseDirectory, "ImgDatabase", "Skill"),
                iconId);

            _buffCache[iconId] = image;
            return image;
        }

        private static Bitmap? FindByNumericFile(
            string root,
            uint id)
        {
            if (!Directory.Exists(root))
                return null;

            Dictionary<string, string> index;

            lock (_pathIndexSync)
            {
                if (!_pathIndexes.TryGetValue(root, out index!))
                {
                    index = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                    foreach (string file in
                             Directory.EnumerateFiles(
                                 root,
                                 "*.*",
                                 SearchOption.AllDirectories))
                    {
                        string ext =
                            Path.GetExtension(file);

                        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) &&
                            !ext.Equals(".dds", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string key =
                            Path.GetFileNameWithoutExtension(file);

                        if (!index.ContainsKey(key))
                            index[key] = file;
                    }

                    _pathIndexes[root] = index;
                }
            }

            string numeric =
                id.ToString(
                    CultureInfo.InvariantCulture);

            return index.TryGetValue(
                    numeric,
                    out string? path)
                ? LoadBitmapSafe(path)
                : null;
        }

        private static Bitmap? LoadBitmapSafe(string path)
        {
            if (_cache.TryGetValue(path, out Bitmap? cached))
                return cached;

            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                Bitmap? image = ext switch
                {
                    ".tga" => TgaImageLoader.LoadBitmap(path),
                    ".dds" => DdsImageLoader.LoadBitmap(path),
                    _ => new Bitmap(Image.FromFile(path))
                };
                _cache[path] = image;
                return image;
            }
            catch
            {
                _cache[path] = null;
                return null;
            }
        }
    }
}
