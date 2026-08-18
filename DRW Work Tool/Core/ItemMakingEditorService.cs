using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class ItemMakingValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    public sealed class ItemMakingEditorService
    {
        public const int UiTextCharacterLimit = 50;

        public string FilePath { get; }
        public XDocument OriginalDocument { get; }

        public ItemMakingEditorService(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);

            if (!File.Exists(FilePath))
                throw new FileNotFoundException("ItemMaking.xml não encontrado.", FilePath);

            OriginalDocument = XDocument.Load(FilePath, LoadOptions.PreserveWhitespace);

            if (OriginalDocument.Root?.Name.LocalName != "ItemMaking")
                throw new InvalidDataException("ItemMaking.xml inválido: root <ItemMaking> esperada.");

            if (OriginalDocument.Root.Element("index") == null)
                throw new InvalidDataException("ItemMaking.xml inválido: <index> principal ausente.");
        }

        public XDocument CreateWorkingCopy() => new XDocument(OriginalDocument);

        public static IEnumerable<XElement> GetNpcBlocks(XDocument doc) =>
            doc.Root?.Element("index")?.Elements("NPC")
            ?? Enumerable.Empty<XElement>();

        public static IEnumerable<XElement> GetAbars(XElement npc) =>
            npc.Element("index")?.Elements("Abar")
            ?? Enumerable.Empty<XElement>();

        public static IEnumerable<XElement> GetSubCategories(XElement abar) =>
            abar.Element("index")?.Elements("SubCategoty")
            ?? Enumerable.Empty<XElement>();

        public static IEnumerable<XElement> GetCrafts(XElement sub) =>
            sub.Element("index")?.Elements("itemMake")
            ?? Enumerable.Empty<XElement>();

        public static IEnumerable<XElement> GetMaterials(XElement craft) =>
            craft.Element("index")?.Elements("MaterialList")
            ?? Enumerable.Empty<XElement>();

        public static XElement CreateNpcBlock(uint npcId) =>
            new XElement(
                "NPC",
                new XElement("m_dwNpcIdx", npcId),
                new XElement("m_mapMainCategoty", 0),
                new XElement("index"));

        public static XElement CreateAbar(int id, string name = "New Tab") =>
            new XElement(
                "Abar",
                new XElement("ID", id),
                new XElement("ID1", id),
                new XElement("CarteSize", Math.Max(2, Encoding.Unicode.GetByteCount(name))),
                new XElement("Abaname", name),
                new XElement("size_mapSubCategoty", 0),
                new XElement("index"));

        public static XElement CreateSubCategory(int id, string name = "New Category") =>
            new XElement(
                "SubCategoty",
                new XElement("ID", id),
                new XElement("ID1", id),
                new XElement("SizeNameCate", Math.Max(2, Encoding.Unicode.GetByteCount(name))),
                new XElement("Name", name),
                new XElement("fcount", 0),
                new XElement("index"));

        public static XElement CreateCraft(XDocument doc)
        {
            int nextUnique =
                GetNpcBlocks(doc)
                    .SelectMany(GetAbars)
                    .SelectMany(GetSubCategories)
                    .SelectMany(GetCrafts)
                    .Select(x => int.TryParse(x.Element("m_nUniqueIdx")?.Value, out int id) ? id : 0)
                    .DefaultIfEmpty(0)
                    .Max() + 1;

            return new XElement(
                "itemMake",
                new XElement("m_nUniqueIdx", nextUnique),
                new XElement("m_dwItemIdx", 0),
                new XElement("m_nItemNum", 1),
                new XElement("m_nProbabilityofSuccess", 10000),
                new XElement("ink", 0),
                new XElement("unk", 0),
                new XElement("Valor", 0),
                new XElement("m_dwItemCost", 0),
                new XElement("index"));
        }

        public static XElement CreateMaterial() =>
            new XElement(
                "MaterialList",
                new XElement("m_dwItemIdx", 0),
                new XElement("m_nItemNum", 1));

        public static void NormalizeCountsAndHiddenSizes(XDocument doc)
        {
            XElement root = doc.Root ?? throw new InvalidDataException("ItemMaking root ausente.");
            XElement index = root.Element("index") ?? throw new InvalidDataException("ItemMaking/index ausente.");

            List<XElement> npcs = index.Elements("NPC").ToList();
            SetElementValue(root, "count", npcs.Count);

            foreach (XElement npc in npcs)
            {
                List<XElement> abars = GetAbars(npc).ToList();
                SetElementValue(npc, "m_mapMainCategoty", abars.Count);

                for (int ai = 0; ai < abars.Count; ai++)
                {
                    XElement abar = abars[ai];
                    SetElementValue(abar, "ID", ai + 1);
                    SetElementValue(abar, "ID1", ai + 1);

                    string abarName = abar.Element("Abaname")?.Value ?? string.Empty;
                    ValidateUiName("Abaname", abarName);
                    GrowHiddenTextSize(abar, "CarteSize", abarName);

                    List<XElement> subs = GetSubCategories(abar).ToList();
                    SetElementValue(abar, "size_mapSubCategoty", subs.Count);

                    for (int si = 0; si < subs.Count; si++)
                    {
                        XElement sub = subs[si];
                        SetElementValue(sub, "ID", si + 1);
                        SetElementValue(sub, "ID1", si + 1);

                        string subName = sub.Element("Name")?.Value ?? string.Empty;
                        ValidateUiName("SubCategory Name", subName);
                        GrowHiddenTextSize(sub, "SizeNameCate", subName);

                        List<XElement> crafts = GetCrafts(sub).ToList();
                        SetElementValue(sub, "fcount", crafts.Count);

                        foreach (XElement craft in crafts)
                        {
                            List<XElement> materials = GetMaterials(craft).ToList();
                            SetElementValue(craft, "m_dwItemCost", materials.Count);
                        }
                    }
                }
            }
        }

        public ItemMakingValidationResult Validate(
            XDocument doc,
            EditorReferenceCatalogService references)
        {
            var result = new ItemMakingValidationResult();

            NormalizeCountsAndHiddenSizes(doc);

            var npcIds = new HashSet<uint>();
            var craftIds = new HashSet<int>();

            foreach (XElement npc in GetNpcBlocks(doc))
            {
                if (!uint.TryParse(npc.Element("m_dwNpcIdx")?.Value, out uint npcId) || npcId == 0)
                {
                    result.Errors.Add("Existe um bloco NPC com m_dwNpcIdx inválido/0.");
                    continue;
                }

                if (!npcIds.Add(npcId))
                    result.Errors.Add($"NPC {npcId}: existe mais de um bloco ItemMaking para o mesmo NPC.");

                if (!references.TryGetNpc(npcId, out EditorNpcReference? npcReference))
                {
                    result.Warnings.Add($"NPC {npcId}: não existe em Npc.xml.");
                }
                else if (npcReference.Type != 20)
                {
                    result.Warnings.Add(
                        $"NPC {npcId} ({npcReference.Name}): NPCType={npcReference.Type}, não 20. " +
                        "Entrada legada preservada; novos Item Creators devem usar Type 20.");
                }

                foreach (XElement craft in GetAbars(npc).SelectMany(GetSubCategories).SelectMany(GetCrafts))
                {
                    if (!int.TryParse(craft.Element("m_nUniqueIdx")?.Value, out int unique) || unique <= 0)
                    {
                        result.Errors.Add($"NPC {npcId}: craft com m_nUniqueIdx inválido.");
                    }
                    else if (!craftIds.Add(unique))
                    {
                        result.Errors.Add($"m_nUniqueIdx {unique} está duplicado.");
                    }

                    ValidateItemReference(result, references, craft, "m_dwItemIdx", $"Craft {unique} output");

                    if (!int.TryParse(craft.Element("m_nProbabilityofSuccess")?.Value, out int probability) ||
                        probability < 0 ||
                        probability > 10000)
                    {
                        result.Errors.Add($"Craft {unique}: m_nProbabilityofSuccess deve ficar entre 0 e 10000.");
                    }

                    foreach (XElement material in GetMaterials(craft))
                        ValidateItemReference(result, references, material, "m_dwItemIdx", $"Craft {unique} material");
                }
            }

            return result;
        }

        public void Save(XDocument working, EditorReferenceCatalogService references)
        {
            ItemMakingValidationResult validation = Validate(working, references);

            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    "ItemMaking.xml falhou validação:\r\n- " +
                    string.Join("\r\n- ", validation.Errors));
            }

            File.Copy(FilePath, FilePath + ".editor.bak", overwrite: true);
            working.Save(FilePath, SaveOptions.None);
        }

        private static void ValidateItemReference(
            ItemMakingValidationResult result,
            EditorReferenceCatalogService references,
            XElement owner,
            string tag,
            string context)
        {
            if (!uint.TryParse(owner.Element(tag)?.Value, out uint itemId) || itemId == 0)
            {
                result.Errors.Add($"{context}: ItemID inválido/0.");
                return;
            }

            if (!references.TryGetItem(itemId, out _))
                result.Errors.Add($"{context}: ItemID {itemId} não existe em ItemList.xml.");
        }

        private static void GrowHiddenTextSize(XElement owner, string sizeTag, string text)
        {
            int required = Math.Max(2, Encoding.Unicode.GetByteCount(text));
            int current = int.TryParse(owner.Element(sizeTag)?.Value, out int parsed) ? parsed : 0;
            SetElementValue(owner, sizeTag, Math.Max(current, required));
        }

        private static void ValidateUiName(string field, string value)
        {
            if (value.Length > UiTextCharacterLimit)
            {
                throw new InvalidDataException(
                    $"{field} ultrapassa {UiTextCharacterLimit} caracteres. Atual={value.Length}.");
            }
        }

        private static void SetElementValue(XElement owner, string tag, object value)
        {
            XElement? element = owner.Element(tag);

            if (element == null)
            {
                owner.AddFirst(new XElement(tag, value));
                return;
            }

            element.Value =
                Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty;
        }
    }
}
