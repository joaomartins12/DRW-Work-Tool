using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class AccessoryStatDefinition
    {
        public int Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public bool IsPercent { get; init; }
        public bool UsesHundredScale { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Code)
                ? $"{Id} — {Name}"
                : $"{Id} — {Code} — {Name}";
    }

    public static class AccessoryStatCatalog
    {
        private static readonly AccessoryStatDefinition[] Definitions =
        {
            new() { Id = 0, Code = "—", Name = "Empty / No option" },

            new() { Id = 1, Code = "AT", Name = "Attack" },
            new() { Id = 2, Code = "DE", Name = "Defense" },
            new() { Id = 3, Code = "HP", Name = "Health Points" },
            new() { Id = 4, Code = "DS", Name = "Digi-Soul" },
            new() { Id = 5, Code = "SCD", Name = "Skill Cooldown" },

            new()
            {
                Id = 6,
                Code = "ATT",
                Name = "Attribute Damage",
                IsPercent = true
            },

            new()
            {
                Id = 7,
                Code = "CT",
                Name = "Critical Rate",
                IsPercent = true
            },

            new()
            {
                Id = 8,
                Code = "CD",
                Name = "Critical Damage",
                IsPercent = true,
                UsesHundredScale = true
            },

            new()
            {
                Id = 9,
                Code = "AS",
                Name = "Attack Speed",
                IsPercent = true,
                UsesHundredScale = true
            },

            new()
            {
                Id = 10,
                Code = "EV",
                Name = "Evade",
                IsPercent = true
            },

            new()
            {
                Id = 11,
                Code = "BL",
                Name = "Block",
                IsPercent = true
            },

            new() { Id = 12, Code = "HT", Name = "Hit Rate" },

            new()
            {
                Id = 13,
                Code = "MS",
                Name = "Movement Speed",
                IsPercent = true
            },

            new() { Id = 101, Name = "Data Attribute" },
            new() { Id = 102, Name = "Vaccine Attribute" },
            new() { Id = 103, Name = "Virus Attribute" },
            new() { Id = 104, Name = "Unknown Attribute" },
            new() { Id = 105, Name = "Ice Element" },
            new() { Id = 106, Name = "Water Element" },
            new() { Id = 107, Name = "Fire Element" },
            new() { Id = 108, Name = "Earth / Land Element" },
            new() { Id = 109, Name = "Wind Element" },
            new() { Id = 110, Name = "Wood Element" },
            new() { Id = 111, Name = "Light Element" },
            new() { Id = 112, Name = "Dark Element" },
            new() { Id = 113, Name = "Thunder Element" },
            new() { Id = 114, Name = "Steel Element" }
        };

        public static IReadOnlyList<AccessoryStatDefinition> All =>
            Definitions;

        public static AccessoryStatDefinition Get(int id) =>
            Definitions.FirstOrDefault(x => x.Id == id)
            ?? new AccessoryStatDefinition
            {
                Id = id,
                Name = $"Unknown Stat {id}"
            };

        public static bool UsesHundredScale(int id) =>
            Get(id).UsesHundredScale;

        public static string FormatUiValue(
            int statId,
            int rawValue)
        {
            if (!UsesHundredScale(statId))
                return rawValue.ToString(CultureInfo.InvariantCulture);

            decimal value =
                rawValue / 100m;

            return value.ToString(
                "0.##",
                CultureInfo.InvariantCulture);
        }

        public static int ParseUiValue(
            int statId,
            string text,
            string fieldName)
        {
            string normalized =
                (text ?? string.Empty)
                    .Trim()
                    .Replace(',', '.');

            if (UsesHundredScale(statId))
            {
                if (!decimal.TryParse(
                    normalized,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal percent))
                {
                    throw new InvalidDataException(
                        $"{fieldName}: '{text}' não é uma percentagem válida.");
                }

                decimal scaled =
                    decimal.Round(
                        percent * 100m,
                        0,
                        MidpointRounding.AwayFromZero);

                if (scaled < int.MinValue ||
                    scaled > int.MaxValue)
                {
                    throw new OverflowException(
                        $"{fieldName}: valor fora de Int32.");
                }

                return (int)scaled;
            }

            if (!int.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
            {
                throw new InvalidDataException(
                    $"{fieldName}: '{text}' não é um Int32 válido.");
            }

            return value;
        }
    }

    public sealed class ItemAccessoryStatSlot
    {
        public int StatId { get; set; }
        public short Unknown { get; set; }
        public int MinRaw { get; set; }
        public int MaxRaw { get; set; }

        public ItemAccessoryStatSlot Clone() =>
            new()
            {
                StatId = StatId,
                Unknown = Unknown,
                MinRaw = MinRaw,
                MaxRaw = MaxRaw
            };
    }

    public sealed class ItemAccessoryLinkedItem
    {
        public uint ItemId { get; init; }
        public string ItemName { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public int SkillCodeType { get; init; }
    }

    public sealed class ItemAccessoryRecord
    {
        public required XElement Element { get; init; }
        public int RecordIndex { get; init; }

        public uint AccessoryId { get; set; }
        public int GainOption { get; set; }
        public int RenewalChanges { get; set; }

        public List<ItemAccessoryStatSlot> Slots { get; init; } = new();

        public List<ItemAccessoryLinkedItem> LinkedItems { get; init; } = new();

        public ItemAccessoryLinkedItem? PrimaryLinkedItem =>
            LinkedItems
                .OrderByDescending(x => x.SkillCodeType == 2)
                .ThenBy(x => x.ItemId)
                .FirstOrDefault();

        public string SearchText { get; set; } = string.Empty;

        public ItemAccessoryRecord CloneWorking()
        {
            var clone =
                new ItemAccessoryRecord
                {
                    Element = Element,
                    RecordIndex = RecordIndex,
                    AccessoryId = AccessoryId,
                    GainOption = GainOption,
                    RenewalChanges = RenewalChanges
                };

            clone.Slots.AddRange(
                Slots.Select(x => x.Clone()));

            clone.LinkedItems.AddRange(
                LinkedItems);

            clone.SearchText = SearchText;

            return clone;
        }
    }

    public sealed class ItemAccessoryEditorService
    {
        public const int SlotCount = 16;

        private XDocument _document = null!;
        private XElement _root = null!;

        private readonly List<ItemAccessoryRecord> _records = new();
        private readonly Dictionary<uint, List<ItemAccessoryRecord>> _byId = new();

        public string FilePath { get; private set; } = string.Empty;
        public string ItemListPath { get; private set; } = string.Empty;

        public int TotalRecords => _records.Count;
        public int DistinctAccessoryIds => _byId.Count;

        public IReadOnlyList<ItemAccessoryRecord> Records =>
            _records;

        public void Load(
            string accessoryXmlPath,
            string? itemListPath = null)
        {
            if (!File.Exists(accessoryXmlPath))
            {
                throw new FileNotFoundException(
                    "ItemAcessorys.xml não encontrado.",
                    accessoryXmlPath);
            }

            FilePath =
                Path.GetFullPath(accessoryXmlPath);

            ItemListPath =
                string.IsNullOrWhiteSpace(itemListPath)
                    ? Path.Combine(
                        Path.GetDirectoryName(FilePath)
                        ?? string.Empty,
                        "ItemList.xml")
                    : Path.GetFullPath(itemListPath);

            _document =
                XDocument.Load(
                    FilePath,
                    LoadOptions.PreserveWhitespace);

            _root =
                _document.Root
                ?? throw new InvalidDataException(
                    "ItemAcessorys.xml não possui root.");

            if (!_root.Name.LocalName.Equals(
                "ItemAcessory",
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Root inválido: <{_root.Name.LocalName}>. Esperado <ItemAcessory>.");
            }

            _records.Clear();
            _byId.Clear();

            List<XElement> rows =
                _root
                    .Descendants("Item")
                    .Where(
                        x =>
                            x.Element("index_Accessory1") != null &&
                            x.Element("Option") != null)
                    .ToList();

            int index = 0;

            foreach (XElement row in rows)
            {
                uint id1 =
                    ReadUInt(
                        row,
                        "index_Accessory1");

                uint id2 =
                    ReadUInt(
                        row,
                        "index_Accessory");

                if (id1 != id2)
                {
                    throw new InvalidDataException(
                        $"Accessory {id1}: index_Accessory1 ({id1}) != index_Accessory ({id2}).");
                }

                var record =
                    new ItemAccessoryRecord
                    {
                        Element = row,
                        RecordIndex = index++,
                        AccessoryId = id1,
                        GainOption =
                            ReadInt(
                                row,
                                "Gain_Option"),
                        RenewalChanges =
                            ReadInt(
                                row,
                                "Changeable_Option_Number")
                    };

                XElement option =
                    row.Element("Option")
                    ?? throw new InvalidDataException(
                        $"Accessory {id1}: falta <Option>.");

                List<XElement> values =
                    option.Elements().ToList();

                if (values.Count != SlotCount * 4)
                {
                    throw new InvalidDataException(
                        $"Accessory {id1}: <Option> deve conter exatamente {SlotCount * 4} elementos; " +
                        $"encontrados {values.Count}.");
                }

                for (int slot = 0; slot < SlotCount; slot++)
                {
                    int p = slot * 4;

                    if (values[p + 0].Name.LocalName != "s_nOptIdx" ||
                        values[p + 1].Name.LocalName != "unknow" ||
                        values[p + 2].Name.LocalName != "s_nMin" ||
                        values[p + 3].Name.LocalName != "s_nMax")
                    {
                        throw new InvalidDataException(
                            $"Accessory {id1}, Slot {slot + 1}: ordem esperada " +
                            "s_nOptIdx, unknow, s_nMin, s_nMax.");
                    }

                    record.Slots.Add(
                        new ItemAccessoryStatSlot
                        {
                            StatId =
                                ParseInt(
                                    values[p + 0].Value,
                                    $"Accessory {id1} Slot {slot + 1} s_nOptIdx"),
                            Unknown =
                                ParseInt16(
                                    values[p + 1].Value,
                                    $"Accessory {id1} Slot {slot + 1} unknow"),
                            MinRaw =
                                ParseInt(
                                    values[p + 2].Value,
                                    $"Accessory {id1} Slot {slot + 1} s_nMin"),
                            MaxRaw =
                                ParseInt(
                                    values[p + 3].Value,
                                    $"Accessory {id1} Slot {slot + 1} s_nMax")
                        });
                }

                _records.Add(record);

                if (!_byId.TryGetValue(
                    id1,
                    out List<ItemAccessoryRecord>? list))
                {
                    list = new List<ItemAccessoryRecord>();
                    _byId[id1] = list;
                }

                list.Add(record);
            }

            LoadLinkedItems();
            RebuildSearchText();
        }

        public int CountById(uint accessoryId) =>
            _byId.TryGetValue(
                accessoryId,
                out List<ItemAccessoryRecord>? rows)
                    ? rows.Count
                    : 0;

        public bool Exists(uint accessoryId) =>
            CountById(accessoryId) > 0;

        public IReadOnlyList<ItemAccessoryRecord> Search(
            string? query,
            int maxResults = 100)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            IEnumerable<ItemAccessoryRecord> source =
                _records;

            if (q.Length > 0)
            {
                string upper =
                    q.ToUpperInvariant();

                source =
                    source.Where(
                        row =>
                            row.AccessoryId.ToString(
                                CultureInfo.InvariantCulture)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase) ||
                            row.SearchText.Contains(
                                upper,
                                StringComparison.Ordinal));
            }

            return source
                .Take(
                    Math.Max(
                        1,
                        maxResults))
                .ToArray();
        }

        public int CountSearch(
            string? query)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            if (q.Length == 0)
                return _records.Count;

            string upper =
                q.ToUpperInvariant();

            return _records.Count(
                row =>
                    row.AccessoryId.ToString(
                        CultureInfo.InvariantCulture)
                        .Contains(
                            q,
                            StringComparison.OrdinalIgnoreCase) ||
                    row.SearchText.Contains(
                        upper,
                        StringComparison.Ordinal));
        }

        public ItemAccessoryRecord CreateNewWorking()
        {
            var placeholder =
                new XElement("Item");

            var record =
                new ItemAccessoryRecord
                {
                    Element = placeholder,
                    RecordIndex = -1,
                    AccessoryId = 0,
                    GainOption = 1,
                    RenewalChanges = 0
                };

            for (int i = 0; i < SlotCount; i++)
            {
                record.Slots.Add(
                    new ItemAccessoryStatSlot
                    {
                        StatId = 0,
                        Unknown = 0,
                        MinRaw = 0,
                        MaxRaw = 0
                    });
            }

            return record;
        }

        public void Save(
            ItemAccessoryRecord working,
            bool isNew)
        {
            if (working.AccessoryId == 0)
            {
                throw new InvalidDataException(
                    "Accessory ID tem de ser maior que 0.");
            }

            if (working.GainOption < 0 ||
                working.GainOption > SlotCount)
            {
                throw new InvalidDataException(
                    $"Gain Option deve ficar entre 0 e {SlotCount}. " +
                    "Este valor indica quantos stats o equipamento pode ganhar, " +
                    "não quantos dos 16 Option slots podem ser configurados.");
            }

            if (working.RenewalChanges < 0 ||
                working.RenewalChanges > ushort.MaxValue)
            {
                throw new InvalidDataException(
                    $"Renewal Changes deve ficar entre 0 e {ushort.MaxValue}.");
            }

            if (working.Slots.Count != SlotCount)
            {
                throw new InvalidDataException(
                    $"Accessory deve possuir exatamente {SlotCount} stat slots.");
            }

            XElement target;

            if (isNew)
            {
                target =
                    BuildElement(
                        working,
                        unknownZeroForAllSlots: true);

                AppendNestedAtEnd(target);
            }
            else
            {
                target =
                    working.Element;

                ReplaceRecordContents(
                    target,
                    working);
            }

            SaveAtomic();

            // Reload to refresh references, record indexes and search index.
            Load(
                FilePath,
                ItemListPath);
        }

        public string BuildPreviewXml(
            ItemAccessoryRecord working,
            bool isNew)
        {
            XElement element =
                BuildElement(
                    working,
                    unknownZeroForAllSlots: isNew);

            return element.ToString();
        }

        private void LoadLinkedItems()
        {
            if (!File.Exists(ItemListPath))
                return;

            XDocument itemList =
                XDocument.Load(
                    ItemListPath,
                    LoadOptions.None);

            var bySkill =
                new Dictionary<uint, List<ItemAccessoryLinkedItem>>();

            // Real ItemList.xml structure:
            //
            // <ITEM>
            //   <icount>...</icount>
            //   <index>
            //     <sINFO>...</sINFO>
            //   </index>
            // </ITEM>
            //
            // The previous implementation incorrectly used:
            //     itemList.Root.Elements("sINFO")
            // which always returned zero rows.
            //
            // Descendants("sINFO") is deliberate here: it supports the
            // real exported structure and remains safe if an intermediate
            // container is added later.
            IEnumerable<XElement> itemRows =
                itemList.Root?.Descendants("sINFO")
                ?? Enumerable.Empty<XElement>();

            foreach (XElement item in itemRows)
            {
                if (!uint.TryParse(
                    item.Element("s_dwSkill")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint accessoryId) ||
                    accessoryId == 0)
                {
                    continue;
                }

                if (!uint.TryParse(
                    item.Element("s_dwItemID")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint itemId))
                {
                    continue;
                }

                uint iconId = 0;

                uint.TryParse(
                    item.Element("s_nIcon")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out iconId);

                int skillCodeType = 0;

                int.TryParse(
                    item.Element("s_nSkillCodeType")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out skillCodeType);

                var linked =
                    new ItemAccessoryLinkedItem
                    {
                        ItemId = itemId,
                        ItemName =
                            item.Element("s_szName")?.Value?.Trim()
                            ?? $"Item {itemId}",
                        IconId = iconId,
                        SkillCodeType = skillCodeType
                    };

                if (!bySkill.TryGetValue(
                    accessoryId,
                    out List<ItemAccessoryLinkedItem>? list))
                {
                    list =
                        new List<ItemAccessoryLinkedItem>();

                    bySkill[accessoryId] =
                        list;
                }

                list.Add(linked);
            }

            foreach (ItemAccessoryRecord record in _records)
            {
                if (!bySkill.TryGetValue(
                    record.AccessoryId,
                    out List<ItemAccessoryLinkedItem>? linked))
                {
                    continue;
                }

                // Type 2 is the ItemAccessory-oriented reference mode and
                // is therefore preferred for the card's primary item.
                //
                // We still keep type 0/1 rows when the exact same numeric
                // reference exists there because the supplied ItemList
                // contains overlapping IDs and the editor should show the
                // complete relationship instead of hiding data.
                record.LinkedItems.AddRange(
                    linked
                        .OrderByDescending(
                            x =>
                                x.SkillCodeType == 2)
                        .ThenBy(
                            x =>
                                x.ItemId));
            }
        }

        private void RebuildSearchText()
        {
            foreach (ItemAccessoryRecord record in _records)
            {
                var sb =
                    new StringBuilder();

                foreach (ItemAccessoryLinkedItem item in record.LinkedItems)
                {
                    sb.AppendLine(
                        item.ItemId.ToString(
                            CultureInfo.InvariantCulture));

                    sb.AppendLine(
                        item.ItemName);
                }

                foreach (ItemAccessoryStatSlot slot in record.Slots)
                {
                    if (slot.StatId == 0)
                        continue;

                    AccessoryStatDefinition definition =
                        AccessoryStatCatalog.Get(
                            slot.StatId);

                    sb.AppendLine(definition.Code);
                    sb.AppendLine(definition.Name);
                }

                record.SearchText =
                    sb.ToString()
                        .ToUpperInvariant();
            }
        }

        private void AppendNestedAtEnd(
            XElement item)
        {
            XElement? first =
                _root.Element("Item");

            if (first == null)
            {
                _root.Add(item);
                return;
            }

            XElement current =
                first;

            while (true)
            {
                XElement? next =
                    current.Elements("Item")
                        .FirstOrDefault();

                if (next == null)
                    break;

                current = next;
            }

            current.Add(item);
        }

        private static XElement BuildElement(
            ItemAccessoryRecord working,
            bool unknownZeroForAllSlots)
        {
            var option =
                new XElement("Option");

            for (int i = 0; i < SlotCount; i++)
            {
                ItemAccessoryStatSlot slot =
                    working.Slots[i];

                option.Add(
                    new XElement(
                        "s_nOptIdx",
                        slot.StatId));

                option.Add(
                    new XElement(
                        "unknow",
                        unknownZeroForAllSlots
                            ? 0
                            : slot.Unknown));

                option.Add(
                    new XElement(
                        "s_nMin",
                        slot.MinRaw));

                option.Add(
                    new XElement(
                        "s_nMax",
                        slot.MaxRaw));
            }

            return new XElement(
                "Item",
                new XElement(
                    "index_Accessory1",
                    working.AccessoryId),
                new XElement(
                    "index_Accessory",
                    working.AccessoryId),
                new XElement(
                    "Gain_Option",
                    working.GainOption),
                new XElement(
                    "Changeable_Option_Number",
                    working.RenewalChanges),
                option);
        }

        private static void ReplaceRecordContents(
            XElement target,
            ItemAccessoryRecord working)
        {
            // Preserve the nested child <Item> chain after this record.
            List<XElement> nestedChildren =
                target.Elements("Item")
                    .ToList();

            foreach (XElement child in nestedChildren)
                child.Remove();

            target.RemoveNodes();

            XElement replacement =
                BuildElement(
                    working,
                    unknownZeroForAllSlots: false);

            foreach (XElement node in replacement.Elements())
            {
                target.Add(
                    new XElement(node));
            }

            foreach (XElement child in nestedChildren)
                target.Add(child);
        }

        private void SaveAtomic()
        {
            string backup =
                FilePath + ".editor.bak";

            string temp =
                FilePath + ".editor.tmp";

            File.Copy(
                FilePath,
                backup,
                overwrite: true);

            using (XmlWriter writer =
                   XmlWriter.Create(
                       temp,
                       new XmlWriterSettings
                       {
                           Indent = true,
                           Encoding =
                               new UTF8Encoding(false),
                           OmitXmlDeclaration = false,
                           NewLineHandling =
                               NewLineHandling.None
                       }))
            {
                _document.Save(writer);
            }

            File.Copy(
                temp,
                FilePath,
                overwrite: true);

            File.Delete(temp);
        }

        private static uint ReadUInt(
            XElement row,
            string tag)
        {
            if (!uint.TryParse(
                row.Element(tag)?.Value?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"<{tag}> inválido.");
            }

            return value;
        }

        private static int ReadInt(
            XElement row,
            string tag)
        {
            return ParseInt(
                row.Element(tag)?.Value,
                tag);
        }

        private static int ParseInt(
            string? raw,
            string field)
        {
            if (!int.TryParse(
                raw?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
            {
                throw new InvalidDataException(
                    $"{field}: '{raw}' não é Int32 válido.");
            }

            return value;
        }

        private static short ParseInt16(
            string? raw,
            string field)
        {
            if (!short.TryParse(
                raw?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out short value))
            {
                throw new InvalidDataException(
                    $"{field}: '{raw}' não é Int16 válido.");
            }

            return value;
        }
    }
}
