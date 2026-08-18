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
    public sealed class DigimonEvoDigimonRef
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public uint ModelId { get; init; }
        public int EvolutionType { get; init; }
        public int BaseLevel { get; init; }
    }

    public sealed class DigimonEvoItemRef
    {
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public int Section { get; init; }
    }

    public sealed class DigimonEvoQuestRef
    {
        public int Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public int Level { get; init; }
    }

    public sealed class DigimonEvoChainInfo
    {
        public required XElement Element { get; init; }
        public uint RootId { get; init; }
        public int BattleType { get; init; }
        public int Count { get; init; }
        public bool IsTrueStarter { get; init; }
        public IReadOnlyList<uint> EvolutionIds { get; init; } = Array.Empty<uint>();
    }

    /// <summary>
    /// Parsed/cached DigimonEvo.xml plus the four reference catalogs that the
    /// visual editor needs.  Heavy XML parsing is done once and can be cached
    /// by EditorPreloadService during the startup loading screen.
    /// </summary>
    public sealed class DigimonEvoEditorService
    {
        private readonly Dictionary<uint, DigimonEvoDigimonRef> _digimons = new();
        private readonly Dictionary<uint, DigimonEvoItemRef> _itemsById = new();
        private readonly Dictionary<int, DigimonEvoItemRef> _openItemsBySection = new();
        private readonly Dictionary<int, DigimonEvoQuestRef> _quests = new();

        public string FilePath { get; private set; } = string.Empty;
        public XDocument Document { get; private set; } = new();

        public IReadOnlyDictionary<uint, DigimonEvoDigimonRef> Digimons => _digimons;
        public IReadOnlyDictionary<uint, DigimonEvoItemRef> ItemsById => _itemsById;
        public IReadOnlyDictionary<int, DigimonEvoItemRef> OpenItemsBySection => _openItemsBySection;
        public IReadOnlyDictionary<int, DigimonEvoQuestRef> Quests => _quests;

        public int TotalTrees =>
            Document.Root?.Elements("Digimon").Count() ?? 0;

        public int TrueStarterTrees =>
            GetChains(startersOnly: true).Count;

        public static DigimonEvoEditorService Load(
            string evoPath,
            string digimonListPath,
            string itemListPath,
            string itemDisplayPath,
            string questPath)
        {
            if (!File.Exists(evoPath))
                throw new FileNotFoundException("DigimonEvo.xml does not exist.", evoPath);

            var service = new DigimonEvoEditorService
            {
                FilePath = Path.GetFullPath(evoPath),
                Document = XDocument.Load(
                    evoPath,
                    LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo)
            };

            XElement root =
                service.Document.Root
                ?? throw new InvalidDataException("DigimonEvo.xml has no root.");

            if (!root.Name.LocalName.Equals("DigimonList", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Invalid DigimonEvo root <{root.Name.LocalName}>. Expected <DigimonList>.");

            service.LoadDigimonRefs(digimonListPath);
            service.LoadItemRefs(itemListPath, itemDisplayPath);
            service.LoadQuestRefs(questPath);
            service.ValidateCounts();

            return service;
        }

        public IReadOnlyList<DigimonEvoChainInfo> GetChains(bool startersOnly)
        {
            var list = new List<DigimonEvoChainInfo>();

            foreach (XElement d in Document.Root!.Elements("Digimon"))
            {
                uint rootId = U(d.Element("digiId")?.Value);
                List<XElement> evolutions = d.Elements("Evolution").ToList();
                XElement? first = evolutions.FirstOrDefault();

                bool starter =
                    first != null &&
                    U(first.Element("digiId")?.Value) == rootId &&
                    I(first.Element("Level")?.Value) == 1;

                if (startersOnly && !starter)
                    continue;

                list.Add(
                    new DigimonEvoChainInfo
                    {
                        Element = d,
                        RootId = rootId,
                        BattleType = I(d.Element("BattleType")?.Value),
                        Count = evolutions.Count,
                        IsTrueStarter = starter,
                        EvolutionIds =
                            evolutions
                                .Select(x => U(x.Element("digiId")?.Value))
                                .ToArray()
                    });
            }

            return list;
        }

        public XElement GetChain(uint rootId)
        {
            XElement? found =
                Document.Root!
                    .Elements("Digimon")
                    .FirstOrDefault(
                        x => U(x.Element("digiId")?.Value) == rootId);

            return found
                ?? throw new KeyNotFoundException($"Digimon evolution tree {rootId} was not found.");
        }

        public DigimonEvoDigimonRef ResolveDigimon(uint id)
        {
            if (_digimons.TryGetValue(id, out DigimonEvoDigimonRef? item))
                return item;

            return new DigimonEvoDigimonRef
            {
                Id = id,
                Name = id == 0 ? "Empty" : $"Unknown Digimon {id}",
                ModelId = id
            };
        }

        public DigimonEvoItemRef? ResolveOpenItem(int section) =>
            _openItemsBySection.TryGetValue(section, out DigimonEvoItemRef? item)
                ? item
                : null;

        public DigimonEvoItemRef? ResolveItem(uint itemId) =>
            _itemsById.TryGetValue(itemId, out DigimonEvoItemRef? item)
                ? item
                : null;

        public DigimonEvoQuestRef? ResolveQuest(int questId) =>
            _quests.TryGetValue(questId, out DigimonEvoQuestRef? q)
                ? q
                : null;

        public bool IsJogressEvolution(
            uint digimonId,
            XElement? evolution = null)
        {
            int evolutionType =
                ResolveDigimon(digimonId).EvolutionType;

            bool hasJogressRequirement =
                evolution != null &&
                (
                    I(evolution.Element("m_nJoGressesNum")?.Value) > 0 ||
                    Enumerable.Range(1, 4)
                        .Any(
                            index =>
                                U(
                                    evolution.Element(
                                        $"m_nJoGress_Tacticses{index}")?.Value) != 0)
                );

            // EvolutionType 8 is Jogress in Digimon_List.
            // EvolutionType 16 is Jogress X and uses the same partner fields.
            return evolutionType == 8 ||
                   evolutionType == 16 ||
                   hasJogressRequirement;
        }

        public IReadOnlyList<DigimonEvoDigimonRef> SearchJogressPartners(
            string? query,
            int max = 120)
        {
            string q =
                (query ?? string.Empty).Trim();

            var alreadyUsedAsJogressRequirement =
                Document
                    .Descendants("Evolution")
                    .SelectMany(
                        evolution =>
                            Enumerable.Range(1, 4)
                                .Select(
                                    index =>
                                        U(
                                            evolution.Element(
                                                $"m_nJoGress_Tacticses{index}")?.Value)))
                    .Where(id => id != 0)
                    .ToHashSet();

            IEnumerable<DigimonEvoDigimonRef> source =
                _digimons.Values
                    .Where(
                        digimon =>
                            digimon.EvolutionType == 3 ||
                            alreadyUsedAsJogressRequirement.Contains(digimon.Id))
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Id);

            if (q.Length != 0)
            {
                source =
                    source.Where(
                        x =>
                            x.Id.ToString(CultureInfo.InvariantCulture)
                                .Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return source
                .Take(Math.Max(1, max))
                .ToList();
        }

        public IReadOnlyList<DigimonEvoDigimonRef> SearchDigimons(
            string? query,
            int max = 120)
        {
            string q = (query ?? string.Empty).Trim();

            IEnumerable<DigimonEvoDigimonRef> source =
                _digimons.Values.OrderBy(x => x.Id);

            if (q.Length != 0)
            {
                source =
                    source.Where(
                        x =>
                            x.Id.ToString(CultureInfo.InvariantCulture)
                                .Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(Math.Max(1, max)).ToList();
        }

        public IReadOnlyList<DigimonEvoQuestRef> SearchQuests(
            string? query,
            int max = 100)
        {
            string q = (query ?? string.Empty).Trim();

            IEnumerable<DigimonEvoQuestRef> source =
                _quests.Values.OrderBy(x => x.Id);

            if (q.Length != 0)
            {
                source =
                    source.Where(
                        x =>
                            x.Id.ToString(CultureInfo.InvariantCulture)
                                .Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(Math.Max(1, max)).ToList();
        }

        public IReadOnlyList<DigimonEvoItemRef> SearchOpenItems(
            string? query,
            int max = 100)
        {
            string q = (query ?? string.Empty).Trim();

            IEnumerable<DigimonEvoItemRef> source =
                _openItemsBySection.Values
                    .GroupBy(x => x.Section)
                    .Select(g => g.First())
                    .OrderBy(x => x.Section);

            if (q.Length != 0)
            {
                source =
                    source.Where(
                        x =>
                            x.Section.ToString(CultureInfo.InvariantCulture)
                                .Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.ItemId.ToString(CultureInfo.InvariantCulture)
                                .Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(Math.Max(1, max)).ToList();
        }

        public IReadOnlyList<DigimonEvoItemRef> SearchItems(
            string? query,
            int max = 100)
        {
            string q = (query ?? string.Empty).Trim();

            IEnumerable<DigimonEvoItemRef> source =
                _itemsById.Values.OrderBy(x => x.ItemId);

            if (q.Length != 0)
            {
                source =
                    source.Where(
                        x =>
                            x.ItemId.ToString(CultureInfo.InvariantCulture)
                                .Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            x.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(Math.Max(1, max)).ToList();
        }

        public XElement CreateChain(uint rootId)
        {
            if (_digimons.ContainsKey(rootId) == false)
                throw new InvalidOperationException(
                    $"Digimon ID {rootId} does not exist in Digimon_List.xml.");

            if (Document.Root!.Elements("Digimon")
                    .Any(x => U(x.Element("digiId")?.Value) == rootId))
            {
                throw new InvalidOperationException(
                    $"DigimonEvo already contains a tree with root {rootId}.");
            }

            XElement source =
                Document.Root.Elements("Digimon").FirstOrDefault()
                ?? throw new InvalidDataException(
                    "DigimonEvo.xml has no Digimon template to clone.");

            XElement created = new XElement(source);

            Set(created, "digiId", rootId);
            Set(created, "BattleType", 2);
            Set(created, "CountEvo", 1);

            List<XElement> existing = created.Elements("Evolution").ToList();
            XElement evo =
                existing.FirstOrDefault() != null
                    ? new XElement(existing[0])
                    : throw new InvalidDataException("Template contains no Evolution block.");

            foreach (XElement x in existing)
                x.Remove();

            ResetEvolutionForNewDigimon(evo, rootId, rootId, level: 1);
            created.Add(evo);
            Document.Root.Add(created);

            return created;
        }

        public XElement AddEvolution(
            uint rootId,
            uint parentId,
            uint targetId,
            int slot)
        {
            if (!_digimons.ContainsKey(targetId))
                throw new InvalidOperationException(
                    $"Digimon ID {targetId} does not exist in Digimon_List.xml.");

            XElement chain = GetChain(rootId);
            List<XElement> evolutions = chain.Elements("Evolution").ToList();

            if (evolutions.Any(x => U(x.Element("digiId")?.Value) == targetId))
                throw new InvalidOperationException(
                    $"{targetId} is already part of this evolution tree.");

            XElement parent =
                evolutions.FirstOrDefault(
                    x => U(x.Element("digiId")?.Value) == parentId)
                ?? throw new InvalidOperationException("Selected parent evolution was not found.");

            XElement template =
                evolutions.LastOrDefault()
                ?? throw new InvalidDataException("The tree has no evolution template.");

            XElement created = new XElement(template);

            int nextLevel =
                Math.Max(
                    1,
                    evolutions.Select(x => I(x.Element("Level")?.Value)).DefaultIfEmpty(0).Max() + 1);

            ResetEvolutionForNewDigimon(created, targetId, rootId, nextLevel);

            if (_digimons.TryGetValue(targetId, out DigimonEvoDigimonRef? dref))
                Set(created, "m_nOpenLevel", Math.Max(1, dref.BaseLevel));

            AddOrReplaceParentLink(parent, targetId, slot);

            chain.Add(created);
            Set(chain, "CountEvo", evolutions.Count + 1);

            return created;
        }

        public void RemoveEvolution(uint rootId, uint evolutionId)
        {
            XElement chain = GetChain(rootId);
            List<XElement> evolutions = chain.Elements("Evolution").ToList();

            XElement target =
                evolutions.FirstOrDefault(
                    x => U(x.Element("digiId")?.Value) == evolutionId)
                ?? throw new KeyNotFoundException("Evolution was not found.");

            if (ReferenceEquals(target, evolutions.FirstOrDefault()))
                throw new InvalidOperationException(
                    "The first/root evolution cannot be removed here. Remove the complete tree from the browser.");

            foreach (XElement evo in evolutions)
            {
                foreach (XElement link in evo.Elements("EvolutionType"))
                {
                    if (U(link.Element("dwDigimonID")?.Value) == evolutionId)
                    {
                        Set(link, "nSlot", 0);
                        Set(link, "dwDigimonID", 0);
                    }
                }
            }

            target.Remove();
            Set(chain, "CountEvo", chain.Elements("Evolution").Count());
        }

        public void RemoveChain(uint rootId)
        {
            XElement chain = GetChain(rootId);
            chain.Remove();
        }

        public void Save()
        {
            ValidateCounts();

            var settings =
                new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = Environment.NewLine,
                    NewLineHandling = NewLineHandling.Replace,
                    Encoding = new UTF8Encoding(false)
                };

            using XmlWriter writer =
                XmlWriter.Create(FilePath, settings);

            Document.Save(writer);
        }

        public void ReplaceDocument(XDocument document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            ValidateCounts();
        }

        private void LoadDigimonRefs(string path)
        {
            if (!File.Exists(path))
                return;

            XDocument doc = XDocument.Load(path);

            foreach (XElement d in doc.Root?.Elements("Digimon") ?? Enumerable.Empty<XElement>())
            {
                uint id = U(d.Attribute("ID")?.Value);
                if (id == 0)
                    continue;

                _digimons[id] =
                    new DigimonEvoDigimonRef
                    {
                        Id = id,
                        Name = d.Attribute("Name")?.Value ?? $"Digimon {id}",
                        ModelId = U(d.Element("ModelID")?.Value),
                        EvolutionType = I(d.Element("EvolutionType")?.Value),
                        BaseLevel = I(d.Element("BaseLevel")?.Value)
                    };
            }
        }

        private void LoadItemRefs(string itemListPath, string itemDisplayPath)
        {
            if (!File.Exists(itemListPath))
                return;

            XDocument items = XDocument.Load(itemListPath);

            foreach (XElement s in items.Descendants("sINFO"))
            {
                uint id = U(s.Element("s_dwItemID")?.Value);
                if (id == 0)
                    continue;

                _itemsById[id] =
                    new DigimonEvoItemRef
                    {
                        ItemId = id,
                        Name = s.Element("s_szName")?.Value ?? $"Item {id}",
                        IconId = U(s.Element("s_nIcon")?.Value),
                        Section = I(s.Element("s_nSection")?.Value)
                    };
            }

            if (!File.Exists(itemDisplayPath))
                return;

            XDocument display = XDocument.Load(itemDisplayPath);

            foreach (XElement item in display.Root?.Elements("Item") ?? Enumerable.Empty<XElement>())
            {
                int section = I(item.Element("nItemS")?.Value);
                uint displayId = U(item.Element("dwDispID")?.Value);

                if (section == 0 || displayId == 0)
                    continue;

                if (_itemsById.TryGetValue(displayId, out DigimonEvoItemRef? resolved))
                {
                    _openItemsBySection[section] =
                        new DigimonEvoItemRef
                        {
                            ItemId = resolved.ItemId,
                            Name = resolved.Name,
                            IconId = resolved.IconId,
                            Section = section
                        };
                }
                else
                {
                    _openItemsBySection[section] =
                        new DigimonEvoItemRef
                        {
                            ItemId = displayId,
                            Name = $"Unknown Item {displayId}",
                            IconId = 0,
                            Section = section
                        };
                }
            }
        }

        private void LoadQuestRefs(string path)
        {
            if (!File.Exists(path))
                return;

            XDocument doc = XDocument.Load(path);

            foreach (XElement q in doc.Root?.Elements("QuestInfo") ?? Enumerable.Empty<XElement>())
            {
                int id = I(q.Element("UniqID")?.Value);
                if (id == 0)
                    continue;

                _quests[id] =
                    new DigimonEvoQuestRef
                    {
                        Id = id,
                        Title =
                            q.Element("TitleText")?.Value?.Trim()
                            ?? q.Element("TitleTab")?.Value?.Trim()
                            ?? $"Quest {id}",
                        Level = I(q.Element("Level")?.Value)
                    };
            }
        }

        private void ValidateCounts()
        {
            foreach (XElement chain in Document.Root?.Elements("Digimon") ?? Enumerable.Empty<XElement>())
            {
                int actual = chain.Elements("Evolution").Count();
                int declared = I(chain.Element("CountEvo")?.Value);

                if (declared != actual)
                    Set(chain, "CountEvo", actual);
            }
        }

        private static void ResetEvolutionForNewDigimon(
            XElement evo,
            uint digimonId,
            uint rootId,
            int level)
        {
            Set(evo, "digiId", digimonId);
            Set(evo, "Level", level);
            Set(evo, "nType", 0);
            Set(evo, "uShort1", 0);

            List<XElement> links = evo.Elements("EvolutionType").ToList();
            while (links.Count < 9)
            {
                XElement l =
                    new XElement(
                        "EvolutionType",
                        new XElement("nSlot", 0),
                        new XElement("dwDigimonID", 0));

                XElement? before = evo.Element("m_IconPos");
                if (before != null)
                    before.AddBeforeSelf(l);
                else
                    evo.Add(l);

                links.Add(l);
            }

            foreach (XElement link in links)
            {
                Set(link, "nSlot", 0);
                Set(link, "dwDigimonID", 0);
            }

            XElement back = links.Last();
            Set(back, "nSlot", 65537);
            Set(back, "dwDigimonID", rootId);

            Set(evo, "m_nEnableSlot", 1);
            Set(evo, "m_nOpenQualification", 0);
            Set(evo, "m_nOpenLevel", 1);
            Set(evo, "m_nOpenQuest", 0);
            Set(evo, "m_nOpenItemTypeS", 0);
            Set(evo, "m_nOpenItemNum", 0);
            Set(evo, "m_nUseItem", 0);
            Set(evo, "m_nUseItemNum", 0);
            Set(evo, "m_nIntimacy", 0);
            Set(evo, "m_nOpenCrest", 0);
            Set(evo, "m_EvoCard1", 0);
            Set(evo, "m_EvoCard2", 0);
            Set(evo, "m_EvoCard3", 0);
            Set(evo, "m_nEvoDigimental", 0);
            Set(evo, "m_nEvoTamerDS", 0);
            Set(evo, "m_nEvolutionTree", 0);
            Set(evo, "m_nJoGressQuestCheck", 0);
            Set(evo, "m_nChipsetType", 0);
            Set(evo, "m_nChipsetTypeC", 0);
            Set(evo, "m_nChipsetNum", 0);
            Set(evo, "m_nChipsetTypeP", 0);
            Set(evo, "m_nJoGressesNum", 0);
            Set(evo, "unknow1", 0);
            Set(evo, "m_nJoGress_Tacticses1", 0);
            Set(evo, "m_nJoGress_Tacticses2", 0);
            Set(evo, "m_nJoGress_Tacticses3", 0);
            Set(evo, "m_nJoGress_Tacticses4", 0);
        }

        private static void AddOrReplaceParentLink(
            XElement parent,
            uint targetId,
            int preferredSlot)
        {
            List<XElement> links = parent.Elements("EvolutionType").ToList();

            XElement? chosen =
                links.FirstOrDefault(
                    x =>
                        I(x.Element("nSlot")?.Value) == preferredSlot &&
                        U(x.Element("dwDigimonID")?.Value) == 0);

            chosen ??=
                links.FirstOrDefault(
                    x =>
                        I(x.Element("nSlot")?.Value) == 0 &&
                        U(x.Element("dwDigimonID")?.Value) == 0);

            if (chosen == null)
                throw new InvalidOperationException(
                    "The parent evolution has no free EvolutionType slot.");

            Set(chosen, "nSlot", Math.Clamp(preferredSlot, 1, 9));
            Set(chosen, "dwDigimonID", targetId);
        }

        public static int I(string? value)
        {
            if (int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int n))
            {
                return n;
            }

            return 0;
        }

        public static uint U(string? value)
        {
            if (uint.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint n))
            {
                return n;
            }

            return 0;
        }

        public static void Set(XElement parent, string name, object value)
        {
            XElement? element = parent.Element(name);

            if (element == null)
            {
                element = new XElement(name);
                parent.Add(element);
            }

            element.Value =
                Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture)
                ?? string.Empty;
        }
    }
}
