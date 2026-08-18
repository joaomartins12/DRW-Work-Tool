using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class ItemListConverter : IGameDataConverter
    {
        public string Name => "ItemList";

        private const int ItemRecordSize = 1596;
        private const int ItemNameChars = 64;
        private const int ItemCommentChars = 512;
        private const int ItemTypeCommentChars = 64;
        private const int FixedAnsi64 = 64;

        private const int ItemTapRecordSize = 66;
        private const int ItemTapNameChars = 32;

        private const int CoolTimeRecordSize = 16;
        private const int DisplayRecordSize = 8;
        private const int TypeNameRecordSize = 132;
        private const int TypeNameChars = 64;
        private const int RankRecordSize = 8;
        private const int ExchangeRecordSize = 44;
        private const int AccessoryRecordSize = 204;
        private const int AccessoryOptionCount = 16;
        private const int EnchantRecordSize = 12;
        private const int MakingGroupRecordSize = 20;
        private const int XaiRecordSize = 9;
        private const int LookHeaderSize = 16;

        private static readonly Encoding Cp949 = CreateCp949();

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("ItemList", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("ItemList", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\ItemList.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            var sectionInfo = new List<string>();

            long start = ms.Position;
            XDocument itemList = ReadItemList(br);
            sectionInfo.Add($"ItemList={ms.Position - start:N0}");

            start = ms.Position;
            XDocument itemTap = ReadItemTap(br);
            sectionInfo.Add($"ItemTap={ms.Position - start:N0}");

            start = ms.Position;
            XDocument coolTime = ReadCoolTime(br);
            sectionInfo.Add($"ItemCoolTime={ms.Position - start:N0}");

            start = ms.Position;
            XDocument display = ReadDisplay(br);
            sectionInfo.Add($"ItemDisplay={ms.Position - start:N0}");

            start = ms.Position;
            XDocument typeName = ReadTypeName(br);
            sectionInfo.Add($"ItemTypeName={ms.Position - start:N0}");

            start = ms.Position;
            XDocument rank = ReadRank(br);
            sectionInfo.Add($"ItemRank={ms.Position - start:N0}");

            start = ms.Position;
            XDocument element = ReadElement(br);
            sectionInfo.Add($"ElementItem={ms.Position - start:N0}");

            start = ms.Position;
            XDocument element1 = ReadElement(br);
            sectionInfo.Add($"ElementItem1={ms.Position - start:N0}");

            start = ms.Position;
            XDocument exchange = ReadExchange(br);
            sectionInfo.Add($"ItemExchange={ms.Position - start:N0}");

            start = ms.Position;
            XDocument accessory = ReadAccessory(br);
            sectionInfo.Add($"ItemAcessorys={ms.Position - start:N0}");

            start = ms.Position;
            XDocument enchant = ReadEnchant(br);
            sectionInfo.Add($"AcessorysEnchant={ms.Position - start:N0}");

            start = ms.Position;
            XDocument making = ReadMaking(br);
            sectionInfo.Add($"ItemMaking={ms.Position - start:N0}");

            start = ms.Position;
            XDocument makingGroup = ReadMakingGroup(br);
            sectionInfo.Add($"ItemMakingGroupList={ms.Position - start:N0}");

            start = ms.Position;
            XDocument xai = ReadXai(br);
            sectionInfo.Add($"ItemXai={ms.Position - start:N0}");

            start = ms.Position;
            XDocument rankEffect = ReadRankEffect(br);
            sectionInfo.Add($"ItemRankEffectList={ms.Position - start:N0}");

            start = ms.Position;
            XDocument look = ReadLook(br);
            sectionInfo.Add($"ItemLook={ms.Position - start:N0}");

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"ItemList.bin contém {extra:N0} bytes extra. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho do ficheiro={ms.Length:N0}.");
            }

            SaveXml(itemList, Path.Combine(folder, "ItemList.xml"));

            // ServerItemList não ocupa uma segunda tabela no BIN.
            // Geramos um mirror da tabela binária para conveniência.
            SaveXml(new XDocument(itemList), Path.Combine(folder, "ServerItemList.xml"));

            SaveXml(accessory, Path.Combine(folder, "ItemAcessorys.xml"));
            SaveXml(enchant, Path.Combine(folder, "AcessorysEnchant.xml"));
            SaveXml(coolTime, Path.Combine(folder, "ItemCoolTime.xml"));
            SaveXml(making, Path.Combine(folder, "ItemMaking.xml"));
            SaveXml(makingGroup, Path.Combine(folder, "ItemMakingGroupList.xml"));
            SaveXml(element, Path.Combine(folder, "ElementItem.xml"));
            SaveXml(exchange, Path.Combine(folder, "ItemExchange.xml"));
            SaveXml(element1, Path.Combine(folder, "ElementItem1.xml"));
            SaveXml(itemTap, Path.Combine(folder, "ItemTap.xml"));
            SaveXml(look, Path.Combine(folder, "ItemLook.xml"));
            SaveXml(display, Path.Combine(folder, "ItemDisplay.xml"));
            SaveXml(typeName, Path.Combine(folder, "ItemTypeName.xml"));
            SaveXml(xai, Path.Combine(folder, "ItemXai.xml"));
            SaveXml(rankEffect, Path.Combine(folder, "ItemRankEffectList.xml"));
            SaveXml(rank, Path.Combine(folder, "ItemRank.xml"));

            AppLogger.Log(
                "ItemList: BIN -> XML concluído. " +
                "17 XMLs gerados (16 blocos binários + ServerItemList mirror).");

            AppLogger.Warning(
                "ItemList: ServerItemList.xml é auxiliar e não possui um bloco " +
                "próprio dentro do ItemList.bin. O EXPORT gera-o como mirror do ItemList.xml.");

            AppLogger.Log(
                "ItemList: secções em bytes -> " +
                string.Join(", ", sectionInfo) + ".");

            AppLogger.Log(
                $"ItemList: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\ItemList.");

            string[] required =
            {
                "ItemList.xml",
                "ItemAcessorys.xml",
                "AcessorysEnchant.xml",
                "ItemCoolTime.xml",
                "ItemMaking.xml",
                "ItemMakingGroupList.xml",
                "ElementItem.xml",
                "ItemExchange.xml",
                "ElementItem1.xml",
                "ItemTap.xml",
                "ItemLook.xml",
                "ItemDisplay.xml",
                "ItemTypeName.xml",
                "ItemXai.xml",
                "ItemRankEffectList.xml",
                "ItemRank.xml"
            };

            foreach (string file in required)
            {
                string path = Path.Combine(folder, file);

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"ItemList: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument itemList = LoadXml(Path.Combine(folder, "ItemList.xml"));
            XDocument accessory = LoadXml(Path.Combine(folder, "ItemAcessorys.xml"));
            XDocument enchant = LoadXml(Path.Combine(folder, "AcessorysEnchant.xml"));
            XDocument coolTime = LoadXml(Path.Combine(folder, "ItemCoolTime.xml"));
            XDocument making = LoadXml(Path.Combine(folder, "ItemMaking.xml"));
            XDocument makingGroup = LoadXml(Path.Combine(folder, "ItemMakingGroupList.xml"));
            XDocument element = LoadXml(Path.Combine(folder, "ElementItem.xml"));
            XDocument exchange = LoadXml(Path.Combine(folder, "ItemExchange.xml"));
            XDocument element1 = LoadXml(Path.Combine(folder, "ElementItem1.xml"));
            XDocument itemTap = LoadXml(Path.Combine(folder, "ItemTap.xml"));
            XDocument look = LoadXml(Path.Combine(folder, "ItemLook.xml"));
            XDocument display = LoadXml(Path.Combine(folder, "ItemDisplay.xml"));
            XDocument typeName = LoadXml(Path.Combine(folder, "ItemTypeName.xml"));
            XDocument xai = LoadXml(Path.Combine(folder, "ItemXai.xml"));
            XDocument rankEffect = LoadXml(Path.Combine(folder, "ItemRankEffectList.xml"));
            XDocument rank = LoadXml(Path.Combine(folder, "ItemRank.xml"));

            // Faz uma validação integral antes de tocar no Output.
            long expectedSize;

            using (MemoryStream counter = new())
            using (BinaryWriter test = new(counter, Encoding.UTF8, leaveOpen: true))
            {
                WriteAll(
                    test,
                    itemList,
                    itemTap,
                    coolTime,
                    display,
                    typeName,
                    rank,
                    element,
                    element1,
                    exchange,
                    accessory,
                    enchant,
                    making,
                    makingGroup,
                    xai,
                    rankEffect,
                    look);

                test.Flush();
                expectedSize = counter.Length;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para ItemList."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs, Encoding.UTF8, leaveOpen: true);

            WriteAll(
                bw,
                itemList,
                itemTap,
                coolTime,
                display,
                typeName,
                rank,
                element,
                element1,
                exchange,
                accessory,
                enchant,
                making,
                makingGroup,
                xai,
                rankEffect,
                look);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"ItemList.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "ItemList: XML -> BIN concluído. " +
                "16 blocos binários serializados.");

            AppLogger.Log(
                $"ItemList: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteAll(
            BinaryWriter bw,
            XDocument itemList,
            XDocument itemTap,
            XDocument coolTime,
            XDocument display,
            XDocument typeName,
            XDocument rank,
            XDocument element,
            XDocument element1,
            XDocument exchange,
            XDocument accessory,
            XDocument enchant,
            XDocument making,
            XDocument makingGroup,
            XDocument xai,
            XDocument rankEffect,
            XDocument look)
        {
            WriteItemList(bw, itemList);
            WriteItemTap(bw, itemTap);
            WriteCoolTime(bw, coolTime);
            WriteDisplay(bw, display);
            WriteTypeName(bw, typeName);
            WriteRank(bw, rank);
            WriteElement(bw, element);
            WriteElement(bw, element1);
            WriteExchange(bw, exchange);
            WriteAccessory(bw, accessory);
            WriteEnchant(bw, enchant);
            WriteMaking(bw, making);
            WriteMakingGroup(bw, makingGroup);
            WriteXai(bw, xai);
            WriteRankEffect(bw, rankEffect);
            WriteLook(bw, look);
        }

        // ============================================================
        // ITEM LIST - 1596 bytes por sINFO
        // ============================================================

        private static XDocument ReadItemList(BinaryReader br)
        {
            int count = ReadCount(br, "ItemList.Count", 1_000_000);

            XElement root = new(
                "ITEM",
                new XElement("icount", count));

            XElement index = new("index");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();
                string name = ReadFixedUnicode(br, ItemNameChars);
                uint icon = br.ReadUInt32();
                string comment = ReadFixedUnicode(br, ItemCommentChars);
                string cNif = ReadFixedCp949(br, FixedAnsi64);
                ushort itemClass = br.ReadUInt16();
                string typeComment = ReadFixedUnicode(br, ItemTypeCommentChars);

                byte codeTag = br.ReadByte();
                byte unkt = br.ReadByte();
                ushort typeL = br.ReadUInt16();
                ushort typeS = br.ReadUInt16();
                uint typeValue = br.ReadUInt32();
                uint section = br.ReadUInt32();
                ushort sellType = br.ReadUInt16();
                byte useMode = br.ReadByte();
                byte unkr = br.ReadByte();
                ushort useTimeGroup = br.ReadUInt16();
                ushort overlap = br.ReadUInt16();
                ushort tamerMin = br.ReadUInt16();
                ushort tamerMax = br.ReadUInt16();
                ushort digiMin = br.ReadUInt16();
                ushort digiMax = br.ReadUInt16();
                ushort possess = br.ReadUInt16();
                ushort equipSeries = br.ReadUInt16();
                ushort useCharacter = br.ReadUInt16();
                byte dummy = br.ReadByte();
                byte uktest = br.ReadByte();
                ushort drop = br.ReadUInt16();
                ushort ukteste1 = br.ReadUInt16();
                uint eventItemType = br.ReadUInt32();
                ushort eventPrice = br.ReadUInt16();
                ushort digiCorePrice = br.ReadUInt16();
                uint scanPrice = br.ReadUInt32();
                uint sale = br.ReadUInt32();

                string modelNif = ReadFixedCp949(br, FixedAnsi64);
                string modelEffect = ReadFixedCp949(br, FixedAnsi64);

                byte modelLoop = br.ReadByte();
                byte modelShader = br.ReadByte();
                ushort skillCodeType = br.ReadUInt16();
                uint skill = br.ReadUInt32();
                byte applyRateMax = br.ReadByte();
                byte applyRateMin = br.ReadByte();
                byte applyElement = br.ReadByte();

                byte unknownAlias = br.ReadByte();

                ushort socketCount = br.ReadUInt16();
                ushort soundId = br.ReadUInt16();
                byte belonging = br.ReadByte();

                byte[] unk2 = ReadExact(br, 3, "ItemList.unk2");

                uint quest1 = br.ReadUInt32();
                uint quest2 = br.ReadUInt32();
                uint quest3 = br.ReadUInt32();

                byte digiviceSkill = br.ReadByte();
                byte digiviceChipset = br.ReadByte();

                byte[] unk3 = ReadExact(br, 2, "ItemList.unk3");

                uint questRequire = br.ReadUInt32();
                byte useTimeType = br.ReadByte();

                byte[] unk4 = ReadExact(br, 3, "ItemList.unk4");

                uint useTimeMin = br.ReadUInt32();
                byte useBattle = br.ReadByte();
                byte unks = br.ReadByte();
                ushort doNotUseType = br.ReadUInt16();
                byte bUseTimeType = br.ReadByte();

                byte[] unkss = ReadExact(br, 3, "ItemList.unkss");

                long consumed = br.BaseStream.Position - start;

                if (consumed != ItemRecordSize)
                {
                    throw new InvalidDataException(
                        $"ItemList ID={id}: record ocupa {consumed} bytes; " +
                        $"esperado={ItemRecordSize}.");
                }

                index.Add(
                    new XElement(
                        "sINFO",
                        new XElement("s_dwItemID", id),
                        new XElement("s_szName", name),
                        new XElement("s_nIcon", icon),
                        new XElement("s_szComment", comment),
                        new XElement("s_cNif", cNif),
                        new XElement("s_nClass", itemClass),
                        new XElement("s_szTypeComment", typeComment),
                        new XElement("s_btCodeTag", codeTag),
                        new XElement("unkt", unkt),
                        new XElement("s_nType_L", typeL),
                        new XElement("s_nType_S", typeS),
                        new XElement("s_nTypeValue", typeValue),
                        new XElement("s_nSection", section),
                        new XElement("s_nSellType", sellType),
                        new XElement("s_nUseMode", useMode),
                        new XElement("unkr", unkr),
                        new XElement("s_nUseTimeGroup", useTimeGroup),
                        new XElement("s_nOverlap", overlap),
                        new XElement("s_nTamerReqMinLevel", tamerMin),
                        new XElement("s_nTamerReqMaxLevel", tamerMax),
                        new XElement("s_nDigimonReqMinLevel", digiMin),
                        new XElement("s_nDigimonReqMaxLevel", digiMax),
                        new XElement("s_nPossess", possess),
                        new XElement("s_nEquipSeries", equipSeries),
                        new XElement("s_nUseCharacter", useCharacter),
                        new XElement("s_bDummy", dummy),
                        new XElement("ukteste1", ukteste1),

                        // O extractor antigo repetia este alias.
                        new XElement("unk", unknownAlias),

                        new XElement("s_nDrop", drop),
                        new XElement("uktest", uktest),
                        new XElement("s_nEventItemType", eventItemType),
                        new XElement("s_dwEventItemPrice", eventPrice),
                        new XElement("s_dwDigiCorePrice", digiCorePrice),
                        new XElement("s_dwScanPrice", scanPrice),
                        new XElement("s_dwSale", sale),
                        new XElement("s_cModel_Nif", modelNif),
                        new XElement("s_cModel_Effect", modelEffect),
                        new XElement("s_bModel_Loop", modelLoop),
                        new XElement("s_bModel_Shader", modelShader),
                        new XElement("s_nSkillCodeType", skillCodeType),
                        new XElement("s_dwSkill", skill),
                        new XElement("s_btApplyRateMax", applyRateMax),
                        new XElement("s_btApplyRateMin", applyRateMin),
                        new XElement("s_btApplyElement", applyElement),

                        new XElement("unk", unknownAlias),

                        new XElement("s_nSocketCount", socketCount),
                        new XElement("s_dwSoundID", soundId),
                        new XElement("s_nBelonging", belonging),
                        new XElement("unk2", Convert.ToBase64String(unk2)),
                        new XElement("s_nQuest1", quest1),
                        new XElement("s_nQuest2", quest2),
                        new XElement("s_nQuest3", quest3),
                        new XElement("s_nDigiviceSkillSlot", digiviceSkill),
                        new XElement("s_nDigiviceChipsetSlot", digiviceChipset),
                        new XElement("unk3", Convert.ToBase64String(unk3)),
                        new XElement("s_nQuestRequire", questRequire),
                        new XElement("s_btUseTimeType", useTimeType),
                        new XElement("unk4", Convert.ToBase64String(unk4)),
                        new XElement("s_nUseTime_Min", useTimeMin),
                        new XElement("s_nUseBattle", useBattle),
                        new XElement("unks", unks),
                        new XElement("s_nDoNotUseType", doNotUseType),
                        new XElement("s_bUseTimeType", bUseTimeType),
                        new XElement("unkss", Convert.ToBase64String(unkss))));
            }

            root.Add(index);
            return Xml(root);
        }

        private static void WriteItemList(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ITEM", "ItemList.xml");

            XElement? index = root.Element("index");

            if (index == null)
                throw new InvalidDataException("ItemList.xml: falta <index>.");

            List<XElement> rows = index.Elements("sINFO").ToList();

            XElement? countElement = root.Element("icount");

            if (countElement != null)
            {
                int declared = ParseInt(
                    countElement.Value,
                    "ItemList.xml <icount>");

                if (declared != rows.Count)
                {
                    throw new InvalidDataException(
                        $"ItemList.xml: <icount>={declared}, " +
                        $"mas existem {rows.Count} <sINFO>.");
                }
            }

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id = RequiredUInt(row, "s_dwItemID", "ItemList");

                long start = bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredText(row, "s_szName", $"Item {id}", true),
                    ItemNameChars,
                    $"Item {id} <s_szName>");

                bw.Write(RequiredUInt(row, "s_nIcon", $"Item {id}"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(row, "s_szComment", $"Item {id}", true),
                    ItemCommentChars,
                    $"Item {id} <s_szComment>");

                WriteFixedCp949(
                    bw,
                    RequiredText(row, "s_cNif", $"Item {id}", true),
                    FixedAnsi64,
                    $"Item {id} <s_cNif>");

                bw.Write(RequiredUInt16(row, "s_nClass", $"Item {id}"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(row, "s_szTypeComment", $"Item {id}", true),
                    ItemTypeCommentChars,
                    $"Item {id} <s_szTypeComment>");

                bw.Write(RequiredByte(row, "s_btCodeTag", $"Item {id}"));
                bw.Write(RequiredByte(row, "unkt", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nType_L", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nType_S", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_nTypeValue", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_nSection", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nSellType", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_nUseMode", $"Item {id}"));
                bw.Write(RequiredByte(row, "unkr", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nUseTimeGroup", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nOverlap", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nTamerReqMinLevel", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nTamerReqMaxLevel", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nDigimonReqMinLevel", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nDigimonReqMaxLevel", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nPossess", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nEquipSeries", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nUseCharacter", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_bDummy", $"Item {id}"));

                // Ordem binária real difere da ordem XML antiga.
                bw.Write(RequiredByte(row, "uktest", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nDrop", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "ukteste1", $"Item {id}"));

                bw.Write(RequiredUInt(row, "s_nEventItemType", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_dwEventItemPrice", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_dwDigiCorePrice", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_dwScanPrice", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_dwSale", $"Item {id}"));

                WriteFixedCp949(
                    bw,
                    RequiredText(row, "s_cModel_Nif", $"Item {id}", true),
                    FixedAnsi64,
                    $"Item {id} <s_cModel_Nif>");

                WriteFixedCp949(
                    bw,
                    RequiredText(row, "s_cModel_Effect", $"Item {id}", true),
                    FixedAnsi64,
                    $"Item {id} <s_cModel_Effect>");

                bw.Write(RequiredByte(row, "s_bModel_Loop", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_bModel_Shader", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nSkillCodeType", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_dwSkill", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_btApplyRateMax", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_btApplyRateMin", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_btApplyElement", $"Item {id}"));

                List<XElement> unknowns = row.Elements("unk").ToList();

                if (unknowns.Count != 2)
                {
                    throw new InvalidDataException(
                        $"Item {id}: são esperados exatamente 2 elementos <unk>; " +
                        $"encontrados {unknowns.Count}.");
                }

                byte unkA = ParseByte(unknowns[0].Value, $"Item {id} <unk>[0]");
                byte unkB = ParseByte(unknowns[1].Value, $"Item {id} <unk>[1]");

                if (unkA != unkB)
                {
                    throw new InvalidDataException(
                        $"Item {id}: os dois <unk> são aliases do mesmo byte binário, " +
                        $"mas têm valores diferentes ({unkA} e {unkB}). " +
                        "Mantém os dois com o mesmo valor.");
                }

                bw.Write(unkB);

                bw.Write(RequiredUInt16(row, "s_nSocketCount", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_dwSoundID", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_nBelonging", $"Item {id}"));

                bw.Write(ReadBase64Fixed(
                    RequiredText(row, "unk2", $"Item {id}", true),
                    3,
                    $"Item {id} <unk2>"));

                bw.Write(RequiredUInt(row, "s_nQuest1", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_nQuest2", $"Item {id}"));
                bw.Write(RequiredUInt(row, "s_nQuest3", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_nDigiviceSkillSlot", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_nDigiviceChipsetSlot", $"Item {id}"));

                bw.Write(ReadBase64Fixed(
                    RequiredText(row, "unk3", $"Item {id}", true),
                    2,
                    $"Item {id} <unk3>"));

                bw.Write(RequiredUInt(row, "s_nQuestRequire", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_btUseTimeType", $"Item {id}"));

                bw.Write(ReadBase64Fixed(
                    RequiredText(row, "unk4", $"Item {id}", true),
                    3,
                    $"Item {id} <unk4>"));

                bw.Write(RequiredUInt(row, "s_nUseTime_Min", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_nUseBattle", $"Item {id}"));
                bw.Write(RequiredByte(row, "unks", $"Item {id}"));
                bw.Write(RequiredUInt16(row, "s_nDoNotUseType", $"Item {id}"));
                bw.Write(RequiredByte(row, "s_bUseTimeType", $"Item {id}"));

                bw.Write(ReadBase64Fixed(
                    RequiredText(row, "unkss", $"Item {id}", true),
                    3,
                    $"Item {id} <unkss>"));

                long consumed = bw.BaseStream.Position - start;

                if (consumed != ItemRecordSize)
                {
                    throw new InvalidDataException(
                        $"Item {id}: record gerado ocupa {consumed} bytes; " +
                        $"esperado={ItemRecordSize}.");
                }
            }
        }

        // ============================================================
        // ITEM TAP
        // ============================================================

        private static XDocument ReadItemTap(BinaryReader br)
        {
            int count = ReadCount(br, "ItemTap.Count", 100_000);
            XElement root = new("ItemTap");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("s_nSellClass", br.ReadUInt16()),
                        new XElement("s_szName", ReadFixedUnicode(br, ItemTapNameChars))));
            }

            return Xml(root);
        }

        private static void WriteItemTap(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemTap", "ItemTap.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt16(row, "s_nSellClass", "ItemTap.xml"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(row, "s_szName", "ItemTap.xml", true),
                    ItemTapNameChars,
                    "ItemTap.xml <s_szName>");
            }
        }

        // ============================================================
        // ITEM COOL TIME
        // ============================================================

        private static XDocument ReadCoolTime(BinaryReader br)
        {
            int count = ReadCount(br, "ItemCoolTime.Count", 100_000);
            XElement root = new("ItemCoolTime");

            for (int i = 0; i < count; i++)
            {
                uint group = br.ReadUInt32();
                byte[] raw = ReadExact(br, 12, "ItemCoolTime.TimeGroup");

                root.Add(
                    new XElement(
                        "Item",
                        new XElement("s_nGroupID", group),
                        new XElement("TimeGroup", Convert.ToHexString(raw))));
            }

            return Xml(root);
        }

        private static void WriteCoolTime(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemCoolTime", "ItemCoolTime.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "s_nGroupID", "ItemCoolTime.xml"));

                string hex = RequiredText(row, "TimeGroup", "ItemCoolTime.xml");

                byte[] raw;

                try
                {
                    raw = Convert.FromHexString(hex.Trim());
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        $"ItemCoolTime.xml: <TimeGroup> não é HEX válido: '{hex}'.",
                        ex);
                }

                if (raw.Length != 12)
                {
                    throw new InvalidDataException(
                        $"ItemCoolTime.xml: <TimeGroup> deve conter exatamente " +
                        $"12 bytes (24 caracteres hex), mas contém {raw.Length} bytes.");
                }

                bw.Write(raw);
            }
        }

        // ============================================================
        // DISPLAY
        // ============================================================

        private static XDocument ReadDisplay(BinaryReader br)
        {
            int count = ReadCount(br, "ItemDisplay.Count", 1_000_000);
            XElement root = new("ItemDisplay");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("nItemS", br.ReadUInt32()),
                        new XElement("dwDispID", br.ReadUInt32())));
            }

            return Xml(root);
        }

        private static void WriteDisplay(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemDisplay", "ItemDisplay.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "nItemS", "ItemDisplay.xml"));
                bw.Write(RequiredUInt(row, "dwDispID", "ItemDisplay.xml"));
            }
        }

        // ============================================================
        // TYPE NAME
        // ============================================================

        private static XDocument ReadTypeName(BinaryReader br)
        {
            int count = ReadCount(br, "ItemTypeName.Count", 1_000_000);
            XElement root = new("ItemTypeName");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("s_szId", br.ReadUInt32()),
                        new XElement("s_szName", ReadFixedUnicode(br, TypeNameChars))));
            }

            return Xml(root);
        }

        private static void WriteTypeName(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemTypeName", "ItemTypeName.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "s_szId", "ItemTypeName.xml"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(row, "s_szName", "ItemTypeName.xml", true),
                    TypeNameChars,
                    "ItemTypeName.xml <s_szName>");
            }
        }

        // ============================================================
        // ITEM RANK
        // ============================================================

        private static XDocument ReadRank(BinaryReader br)
        {
            int count = ReadCount(br, "ItemRank.Count", 1_000_000);
            XElement root = new("ItemRank");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("ID", br.ReadUInt32()),
                        new XElement("Drop_Class", br.ReadUInt16()),
                        new XElement("Drop_Count", br.ReadUInt16())));
            }

            return Xml(root);
        }

        private static void WriteRank(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemRank", "ItemRank.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "ID", "ItemRank.xml"));
                bw.Write(RequiredUInt16(row, "Drop_Class", "ItemRank.xml"));
                bw.Write(RequiredUInt16(row, "Drop_Count", "ItemRank.xml"));
            }
        }

        // ============================================================
        // ELEMENT ITEM / ELEMENT ITEM 1
        // ============================================================

        private static XDocument ReadElement(BinaryReader br)
        {
            int count = ReadCount(br, "ElementItem.Count", 1_000_000);
            XElement root = new("ItemElement");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("s_dwItemID", br.ReadUInt32())));
            }

            return Xml(root);
        }

        private static void WriteElement(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemElement", "ElementItem.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
                bw.Write(RequiredUInt(row, "s_dwItemID", "ElementItem.xml"));
        }

        // ============================================================
        // ITEM EXCHANGE
        // ============================================================

        private static XDocument ReadExchange(BinaryReader br)
        {
            int count = ReadCount(br, "ItemExchange.Count", 1_000_000);

            int nullo = br.ReadInt32();

            XElement root =
                new(
                    "ItemExchange",
                    new XElement("nullo", nullo));

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                XElement row =
                    new(
                        "Item",
                        new XElement("s_dwNpcID", br.ReadUInt32()),
                        new XElement("s_dwItemIndex", br.ReadUInt16()),
                        new XElement("unk", br.ReadUInt16()),
                        new XElement("s_dwItemID", br.ReadUInt32()),
                        new XElement("s_dwExchange_Code_A", br.ReadUInt32()),
                        new XElement("s_dwExchange_Code_B", br.ReadUInt32()),
                        new XElement("s_dwExchange_Code_C", br.ReadUInt32()),
                        new XElement("s_dwExchange_Code_D", br.ReadUInt32()),
                        new XElement("s_dwPropertyA_Price", br.ReadUInt16()),
                        new XElement("s_dwPropertyB_Price", br.ReadUInt16()),
                        new XElement("s_dwPropertyC_Price", br.ReadUInt16()),
                        new XElement("s_dwPropertyD_Price", br.ReadUInt16()),
                        new XElement("s_dwCount", br.ReadUInt16()),
                        new XElement("unk1", br.ReadUInt16()),
                        new XElement("unk2", br.ReadUInt32()));

                if (br.BaseStream.Position - start != ExchangeRecordSize)
                {
                    throw new InvalidDataException(
                        $"ItemExchange record #{i}: tamanho diferente de {ExchangeRecordSize}.");
                }

                root.Add(row);
            }

            return Xml(root);
        }

        private static void WriteExchange(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemExchange", "ItemExchange.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);
            bw.Write(RequiredInt(root, "nullo", "ItemExchange.xml"));

            foreach (XElement row in rows)
            {
                uint npc = RequiredUInt(row, "s_dwNpcID", "ItemExchange.xml");
                string ctx = $"ItemExchange NpcID={npc}";

                bw.Write(npc);
                bw.Write(RequiredUInt16(row, "s_dwItemIndex", ctx));
                bw.Write(RequiredUInt16(row, "unk", ctx));
                bw.Write(RequiredUInt(row, "s_dwItemID", ctx));
                bw.Write(RequiredUInt(row, "s_dwExchange_Code_A", ctx));
                bw.Write(RequiredUInt(row, "s_dwExchange_Code_B", ctx));
                bw.Write(RequiredUInt(row, "s_dwExchange_Code_C", ctx));
                bw.Write(RequiredUInt(row, "s_dwExchange_Code_D", ctx));
                bw.Write(RequiredUInt16(row, "s_dwPropertyA_Price", ctx));
                bw.Write(RequiredUInt16(row, "s_dwPropertyB_Price", ctx));
                bw.Write(RequiredUInt16(row, "s_dwPropertyC_Price", ctx));
                bw.Write(RequiredUInt16(row, "s_dwPropertyD_Price", ctx));
                bw.Write(RequiredUInt16(row, "s_dwCount", ctx));
                bw.Write(RequiredUInt16(row, "unk1", ctx));
                bw.Write(RequiredUInt(row, "unk2", ctx));
            }
        }

        // ============================================================
        // ACCESSORY
        // Não existe count antes desta secção.
        // Cada record começa com dois IDs iguais.
        // ============================================================

        private static XDocument ReadAccessory(BinaryReader br)
        {
            XElement root = new("ItemAcessory");

            XElement? previous = null;
            int recordIndex = 0;

            while (true)
            {
                long pos = br.BaseStream.Position;

                if (br.BaseStream.Length - pos < 8)
                    throw new EndOfStreamException("ItemAcessorys: BIN truncado.");

                uint id1 = br.ReadUInt32();
                uint id2 = br.ReadUInt32();

                // O início de AcessorysEnchant quebra a igualdade.
                if (id1 != id2)
                {
                    br.BaseStream.Position = pos;
                    break;
                }

                ushort gainOption = br.ReadUInt16();
                ushort changeable = br.ReadUInt16();

                XElement option = new("Option");

                for (int i = 0; i < AccessoryOptionCount; i++)
                {
                    option.Add(new XElement("s_nOptIdx", br.ReadUInt16()));
                    option.Add(new XElement("unknow", br.ReadInt16()));
                    option.Add(new XElement("s_nMin", br.ReadInt32()));
                    option.Add(new XElement("s_nMax", br.ReadInt32()));
                }

                XElement item =
                    new(
                        "Item",
                        new XElement("index_Accessory1", id1),
                        new XElement("index_Accessory", id2),
                        new XElement("Gain_Option", gainOption),
                        new XElement("Changeable_Option_Number", changeable),
                        option);

                if (previous == null)
                    root.Add(item);
                else
                    previous.Add(item);

                previous = item;
                recordIndex++;
            }

            if (recordIndex == 0)
            {
                throw new InvalidDataException(
                    "ItemAcessorys: não foi encontrado nenhum record.");
            }

            return Xml(root);
        }

        private static void WriteAccessory(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemAcessory", "ItemAcessorys.xml");

            List<XElement> rows = FlattenNestedItems(root);

            if (rows.Count == 0)
                throw new InvalidDataException("ItemAcessorys.xml: sem <Item>.");

            foreach (XElement row in rows)
            {
                uint id1 = RequiredUInt(row, "index_Accessory1", "ItemAcessorys.xml");
                uint id2 = RequiredUInt(row, "index_Accessory", $"Accessory {id1}");

                if (id1 != id2)
                {
                    throw new InvalidDataException(
                        $"Accessory {id1}: index_Accessory1 e index_Accessory " +
                        $"têm de ser iguais ({id1} != {id2}).");
                }

                bw.Write(id1);
                bw.Write(id2);
                bw.Write(RequiredUInt16(row, "Gain_Option", $"Accessory {id1}"));
                bw.Write(RequiredUInt16(row, "Changeable_Option_Number", $"Accessory {id1}"));

                XElement? option = row.Element("Option");

                if (option == null)
                    throw new InvalidDataException($"Accessory {id1}: falta <Option>.");

                List<XElement> values = option.Elements().ToList();

                if (values.Count != AccessoryOptionCount * 4)
                {
                    throw new InvalidDataException(
                        $"Accessory {id1}: <Option> deve conter exatamente " +
                        $"{AccessoryOptionCount * 4} elementos; encontrados {values.Count}.");
                }

                for (int i = 0; i < AccessoryOptionCount; i++)
                {
                    int p = i * 4;

                    RequireTag(values[p], "s_nOptIdx", $"Accessory {id1}");
                    RequireTag(values[p + 1], "unknow", $"Accessory {id1}");
                    RequireTag(values[p + 2], "s_nMin", $"Accessory {id1}");
                    RequireTag(values[p + 3], "s_nMax", $"Accessory {id1}");

                    bw.Write(ParseUInt16(values[p].Value, $"Accessory {id1} s_nOptIdx"));
                    bw.Write(ParseInt16(values[p + 1].Value, $"Accessory {id1} unknow"));
                    bw.Write(ParseInt(values[p + 2].Value, $"Accessory {id1} s_nMin"));
                    bw.Write(ParseInt(values[p + 3].Value, $"Accessory {id1} s_nMax"));
                }
            }
        }

        // ============================================================
        // ACCESSORY ENCHANT
        // segundo Index_Enchant do XML é alias.
        // ============================================================

        private static XDocument ReadEnchant(BinaryReader br)
        {
            int count = ReadCount(br, "AcessorysEnchant.Count", 100_000);
            XElement root = new("ItemEnchant");

            for (int i = 0; i < count; i++)
            {
                uint id = br.ReadUInt32();
                uint index = br.ReadUInt32();
                uint option = br.ReadUInt32();

                root.Add(
                    new XElement(
                        "Item",
                        new XElement("ID", id),
                        new XElement("Index_Enchant", index),
                        new XElement("Enchant_Option", option),
                        new XElement("Index_Enchant", index)));
            }

            return Xml(root);
        }

        private static void WriteEnchant(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemEnchant", "AcessorysEnchant.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id = RequiredUInt(row, "ID", "AcessorysEnchant.xml");

                List<XElement> indexes = row.Elements("Index_Enchant").ToList();

                if (indexes.Count != 2)
                {
                    throw new InvalidDataException(
                        $"Enchant ID={id}: são esperados exatamente 2 <Index_Enchant>.");
                }

                uint a = ParseUInt(indexes[0].Value, $"Enchant {id} Index_Enchant[0]");
                uint b = ParseUInt(indexes[1].Value, $"Enchant {id} Index_Enchant[1]");

                if (a != b)
                {
                    throw new InvalidDataException(
                        $"Enchant ID={id}: os dois <Index_Enchant> são aliases " +
                        $"do mesmo DWORD e devem ser iguais ({a} != {b}).");
                }

                bw.Write(id);
                bw.Write(a);
                bw.Write(RequiredUInt(row, "Enchant_Option", $"Enchant {id}"));
            }
        }

        // ============================================================
        // ITEM MAKING
        // Strings dinâmicas guardam int32 charCount + UTF16 bytes.
        // XML antigo guarda CarteSize / SizeNameCate em BYTES.
        // ============================================================

        private static XDocument ReadMaking(BinaryReader br)
        {
            int npcCount = ReadCount(br, "ItemMaking.Count", 100_000);

            XElement root =
                new(
                    "ItemMaking",
                    new XElement("count", npcCount));

            XElement index = new("index");

            for (int n = 0; n < npcCount; n++)
            {
                uint npcId = br.ReadUInt32();
                int mainCount = ReadCount(br, $"ItemMaking NPC={npcId}.MainCount", 100_000);

                XElement npc =
                    new(
                        "NPC",
                        new XElement("m_dwNpcIdx", npcId),
                        new XElement("m_mapMainCategoty", mainCount));

                XElement mainIndex = new("index");

                for (int m = 0; m < mainCount; m++)
                {
                    int id = br.ReadInt32();
                    int id1 = br.ReadInt32();

                    (string mainName, int mainChars) =
                        ReadDynamicUnicodeKeepDeclared(br, $"ItemMaking NPC={npcId} Main={id}");

                    int subCount = ReadCount(
                        br,
                        $"ItemMaking NPC={npcId} Main={id}.SubCount",
                        100_000);

                    XElement abar =
                        new(
                            "Abar",
                            new XElement("ID", id),
                            new XElement("ID1", id1),
                            new XElement("CarteSize", mainChars * 2),
                            new XElement("Abaname", mainName),
                            new XElement("size_mapSubCategoty", subCount));

                    XElement subIndex = new("index");

                    for (int s = 0; s < subCount; s++)
                    {
                        int subId = br.ReadInt32();
                        int subId1 = br.ReadInt32();

                        (string subName, int subChars) =
                            ReadDynamicUnicodeKeepDeclared(
                                br,
                                $"ItemMaking NPC={npcId} Sub={subId}");

                        int makeCount = ReadCount(
                            br,
                            $"ItemMaking NPC={npcId} Sub={subId}.MakeCount",
                            1_000_000);

                        XElement sub =
                            new(
                                "SubCategoty",
                                new XElement("ID", subId),
                                new XElement("ID1", subId1),
                                new XElement("SizeNameCate", subChars * 2),
                                new XElement("Name", subName),
                                new XElement("fcount", makeCount));

                        XElement makeIndex = new("index");

                        for (int k = 0; k < makeCount; k++)
                        {
                            int unique = br.ReadInt32();
                            int itemId = br.ReadInt32();
                            int itemNum = br.ReadInt32();
                            int probability = br.ReadInt32();
                            int ink = br.ReadInt32();
                            int unk = br.ReadInt32();
                            int valor = br.ReadInt32();

                            int materialCount =
                                ReadCount(
                                    br,
                                    $"ItemMaking Unique={unique}.MaterialCount",
                                    100_000);

                            XElement materialIndex = new("index");

                            for (int q = 0; q < materialCount; q++)
                            {
                                materialIndex.Add(
                                    new XElement(
                                        "MaterialList",
                                        new XElement("m_dwItemIdx", br.ReadInt32()),
                                        new XElement("m_nItemNum", br.ReadInt32())));
                            }

                            makeIndex.Add(
                                new XElement(
                                    "itemMake",
                                    new XElement("m_nUniqueIdx", unique),
                                    new XElement("m_dwItemIdx", itemId),
                                    new XElement("m_nItemNum", itemNum),
                                    new XElement("m_nProbabilityofSuccess", probability),
                                    new XElement("ink", ink),
                                    new XElement("unk", unk),
                                    new XElement("Valor", valor),
                                    new XElement("m_dwItemCost", materialCount),
                                    materialIndex));
                        }

                        sub.Add(makeIndex);
                        subIndex.Add(sub);
                    }

                    abar.Add(subIndex);
                    mainIndex.Add(abar);
                }

                npc.Add(mainIndex);
                index.Add(npc);
            }

            root.Add(index);
            return Xml(root);
        }

        private static void WriteMaking(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemMaking", "ItemMaking.xml");

            XElement? index = root.Element("index");

            if (index == null)
                throw new InvalidDataException("ItemMaking.xml: falta <index>.");

            List<XElement> npcs = index.Elements("NPC").ToList();

            int declaredNpcCount = RequiredInt(root, "count", "ItemMaking.xml");

            if (declaredNpcCount != npcs.Count)
            {
                throw new InvalidDataException(
                    $"ItemMaking.xml: <count>={declaredNpcCount}, " +
                    $"mas existem {npcs.Count} <NPC>.");
            }

            bw.Write(npcs.Count);

            foreach (XElement npc in npcs)
            {
                uint npcId = RequiredUInt(npc, "m_dwNpcIdx", "ItemMaking.xml");

                XElement? mainIndex = npc.Element("index");
                List<XElement> mains =
                    mainIndex?.Elements("Abar").ToList()
                    ?? new List<XElement>();

                int declaredMain = RequiredInt(
                    npc,
                    "m_mapMainCategoty",
                    $"ItemMaking NPC={npcId}");

                if (declaredMain != mains.Count)
                {
                    throw new InvalidDataException(
                        $"ItemMaking NPC={npcId}: <m_mapMainCategoty>={declaredMain}, " +
                        $"mas existem {mains.Count} <Abar>.");
                }

                bw.Write(npcId);
                bw.Write(mains.Count);

                foreach (XElement abar in mains)
                {
                    int id = RequiredInt(abar, "ID", $"ItemMaking NPC={npcId}");
                    int id1 = RequiredInt(abar, "ID1", $"ItemMaking NPC={npcId} Abar={id}");

                    bw.Write(id);
                    bw.Write(id1);

                    int declaredBytes =
                        RequiredInt(
                            abar,
                            "CarteSize",
                            $"ItemMaking NPC={npcId} Abar={id}");

                    string name =
                        RequiredText(
                            abar,
                            "Abaname",
                            $"ItemMaking NPC={npcId} Abar={id}",
                            true);

                    WriteDeclaredUnicode(
                        bw,
                        name,
                        declaredBytes,
                        $"ItemMaking NPC={npcId} Abar={id} <Abaname>/<CarteSize>");

                    XElement? subIndex = abar.Element("index");
                    List<XElement> subs =
                        subIndex?.Elements("SubCategoty").ToList()
                        ?? new List<XElement>();

                    int declaredSubs =
                        RequiredInt(
                            abar,
                            "size_mapSubCategoty",
                            $"ItemMaking NPC={npcId} Abar={id}");

                    if (declaredSubs != subs.Count)
                    {
                        throw new InvalidDataException(
                            $"ItemMaking NPC={npcId} Abar={id}: " +
                            $"<size_mapSubCategoty>={declaredSubs}, " +
                            $"mas existem {subs.Count} <SubCategoty>.");
                    }

                    bw.Write(subs.Count);

                    foreach (XElement sub in subs)
                    {
                        int subId = RequiredInt(sub, "ID", $"ItemMaking NPC={npcId} Abar={id}");
                        int subId1 = RequiredInt(sub, "ID1", $"ItemMaking Sub={subId}");

                        bw.Write(subId);
                        bw.Write(subId1);

                        int declaredSubBytes =
                            RequiredInt(
                                sub,
                                "SizeNameCate",
                                $"ItemMaking Sub={subId}");

                        string subName =
                            RequiredText(
                                sub,
                                "Name",
                                $"ItemMaking Sub={subId}",
                                true);

                        WriteDeclaredUnicode(
                            bw,
                            subName,
                            declaredSubBytes,
                            $"ItemMaking Sub={subId} <Name>/<SizeNameCate>");

                        XElement? makeIndex = sub.Element("index");
                        List<XElement> makes =
                            makeIndex?.Elements("itemMake").ToList()
                            ?? new List<XElement>();

                        int declaredMake =
                            RequiredInt(
                                sub,
                                "fcount",
                                $"ItemMaking Sub={subId}");

                        if (declaredMake != makes.Count)
                        {
                            throw new InvalidDataException(
                                $"ItemMaking Sub={subId}: <fcount>={declaredMake}, " +
                                $"mas existem {makes.Count} <itemMake>.");
                        }

                        bw.Write(makes.Count);

                        foreach (XElement make in makes)
                        {
                            int unique = RequiredInt(
                                make,
                                "m_nUniqueIdx",
                                $"ItemMaking Sub={subId}");

                            bw.Write(unique);
                            bw.Write(RequiredInt(make, "m_dwItemIdx", $"ItemMaking Unique={unique}"));
                            bw.Write(RequiredInt(make, "m_nItemNum", $"ItemMaking Unique={unique}"));
                            bw.Write(RequiredInt(make, "m_nProbabilityofSuccess", $"ItemMaking Unique={unique}"));
                            bw.Write(RequiredInt(make, "ink", $"ItemMaking Unique={unique}"));
                            bw.Write(RequiredInt(make, "unk", $"ItemMaking Unique={unique}"));
                            bw.Write(RequiredInt(make, "Valor", $"ItemMaking Unique={unique}"));

                            XElement? materialIndex = make.Element("index");
                            List<XElement> materials =
                                materialIndex?.Elements("MaterialList").ToList()
                                ?? new List<XElement>();

                            int declaredMaterials =
                                RequiredInt(
                                    make,
                                    "m_dwItemCost",
                                    $"ItemMaking Unique={unique}");

                            if (declaredMaterials != materials.Count)
                            {
                                throw new InvalidDataException(
                                    $"ItemMaking Unique={unique}: <m_dwItemCost>={declaredMaterials}, " +
                                    $"mas existem {materials.Count} <MaterialList>.");
                            }

                            bw.Write(materials.Count);

                            foreach (XElement material in materials)
                            {
                                bw.Write(RequiredInt(
                                    material,
                                    "m_dwItemIdx",
                                    $"ItemMaking Unique={unique} Material"));

                                bw.Write(RequiredInt(
                                    material,
                                    "m_nItemNum",
                                    $"ItemMaking Unique={unique} Material"));
                            }
                        }
                    }
                }
            }
        }

        // ============================================================
        // ITEM MAKING GROUP
        // ============================================================

        private static XDocument ReadMakingGroup(BinaryReader br)
        {
            int count = ReadCount(br, "ItemMakingGroup.Count", 100_000);
            XElement root = new("ItemMakingGroup");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("Index", br.ReadInt32()),
                        new XElement("Type_No", br.ReadInt32()),
                        new XElement("Item_Num", br.ReadInt32()),
                        new XElement("Item_Code", br.ReadInt32()),
                        new XElement("Item_Num1", br.ReadInt32())));
            }

            return Xml(root);
        }

        private static void WriteMakingGroup(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "ItemMakingGroup",
                "ItemMakingGroupList.xml");

            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredInt(row, "Index", "ItemMakingGroupList.xml"));
                bw.Write(RequiredInt(row, "Type_No", "ItemMakingGroupList.xml"));
                bw.Write(RequiredInt(row, "Item_Num", "ItemMakingGroupList.xml"));
                bw.Write(RequiredInt(row, "Item_Code", "ItemMakingGroupList.xml"));
                bw.Write(RequiredInt(row, "Item_Num1", "ItemMakingGroupList.xml"));
            }
        }

        // ============================================================
        // ITEM XAI
        // ============================================================

        private static XDocument ReadXai(BinaryReader br)
        {
            int count = ReadCount(br, "ItemXai.Count", 100_000);
            XElement root = new("ItemXai");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "Item",
                        new XElement("ItemID", br.ReadUInt32()),
                        new XElement("XGauge", br.ReadUInt32()),
                        new XElement("MaxCrystal", br.ReadByte())));
            }

            return Xml(root);
        }

        private static void WriteXai(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemXai", "ItemXai.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "ItemID", "ItemXai.xml"));
                bw.Write(RequiredUInt(row, "XGauge", "ItemXai.xml"));
                bw.Write(RequiredByte(row, "MaxCrystal", "ItemXai.xml"));
            }
        }

        // ============================================================
        // ITEM RANK EFFECT
        // XML antigo usa cadeia recursiva de <Item>.
        // ============================================================

        private static XDocument ReadRankEffect(BinaryReader br)
        {
            int count = ReadCount(br, "ItemRankEffect.Count", 100_000);
            XElement root = new("ItemRankEffect");

            XElement? previous = null;

            for (int i = 0; i < count; i++)
            {
                uint itemCode = br.ReadUInt32();
                int intervalCount = ReadCount(
                    br,
                    $"ItemRankEffect Item={itemCode}.nInterval",
                    100_000);

                XElement intervals = new("nIntervals");

                for (int n = 0; n < intervalCount; n++)
                {
                    intervals.Add(new XElement("dwItemCode", br.ReadUInt32()));
                    intervals.Add(new XElement("IconNo", br.ReadInt32()));
                    intervals.Add(new XElement("Rank", br.ReadInt32()));
                }

                XElement item =
                    new(
                        "Item",
                        new XElement("nItemCode", itemCode),
                        new XElement("nInterval", intervalCount),
                        intervals);

                if (previous == null)
                    root.Add(item);
                else
                    previous.Add(item);

                previous = item;
            }

            return Xml(root);
        }

        private static void WriteRankEffect(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "ItemRankEffect",
                "ItemRankEffectList.xml");

            List<XElement> rows = FlattenNestedItems(root);

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint itemCode = RequiredUInt(
                    row,
                    "nItemCode",
                    "ItemRankEffectList.xml");

                int declared = RequiredInt(
                    row,
                    "nInterval",
                    $"ItemRankEffect Item={itemCode}");

                XElement? container = row.Element("nIntervals");

                if (container == null)
                    throw new InvalidDataException(
                        $"ItemRankEffect Item={itemCode}: falta <nIntervals>.");

                List<XElement> values = container.Elements().ToList();

                if (values.Count != declared * 3)
                {
                    throw new InvalidDataException(
                        $"ItemRankEffect Item={itemCode}: <nInterval>={declared}, " +
                        $"mas <nIntervals> contém {values.Count} valores; " +
                        $"esperado={declared * 3}.");
                }

                bw.Write(itemCode);
                bw.Write(declared);

                for (int i = 0; i < declared; i++)
                {
                    int p = i * 3;

                    RequireTag(values[p], "dwItemCode", $"ItemRankEffect {itemCode}");
                    RequireTag(values[p + 1], "IconNo", $"ItemRankEffect {itemCode}");
                    RequireTag(values[p + 2], "Rank", $"ItemRankEffect {itemCode}");

                    bw.Write(ParseUInt(values[p].Value, $"ItemRankEffect {itemCode} dwItemCode"));
                    bw.Write(ParseInt(values[p + 1].Value, $"ItemRankEffect {itemCode} IconNo"));
                    bw.Write(ParseInt(values[p + 2].Value, $"ItemRankEffect {itemCode} Rank"));
                }
            }
        }

        // ============================================================
        // ITEM LOOK
        // ============================================================

        private static XDocument ReadLook(BinaryReader br)
        {
            int count = ReadCount(br, "ItemLook.Count", 100_000);
            XElement root = new("ItemLook");

            for (int i = 0; i < count; i++)
            {
                uint itemCode = br.ReadUInt32();
                uint diNo = br.ReadUInt32();
                uint changeType = br.ReadUInt32();

                int nameSize = ReadCount(
                    br,
                    $"ItemLook Item={itemCode}.NameSize",
                    1_000_000);

                byte[] raw = ReadExact(
                    br,
                    nameSize,
                    $"ItemLook Item={itemCode}.File_Name");

                string file = Cp949.GetString(raw);

                root.Add(
                    new XElement(
                        "Item",
                        new XElement("Item_Code", itemCode),
                        new XElement("Di_No", diNo),
                        new XElement("Change_Type", changeType),
                        new XElement("NameSize", nameSize),
                        new XElement("File_Name", file)));
            }

            return Xml(root);
        }

        private static void WriteLook(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "ItemLook", "ItemLook.xml");
            List<XElement> rows = root.Elements("Item").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint itemCode = RequiredUInt(row, "Item_Code", "ItemLook.xml");

                string file = RequiredText(
                    row,
                    "File_Name",
                    $"ItemLook Item={itemCode}",
                    true);

                byte[] raw = Cp949.GetBytes(file);

                int declared = RequiredInt(
                    row,
                    "NameSize",
                    $"ItemLook Item={itemCode}");

                if (declared != raw.Length)
                {
                    throw new InvalidDataException(
                        $"ItemLook Item={itemCode}: <NameSize>={declared}, " +
                        $"mas <File_Name> ocupa {raw.Length} bytes CP949. " +
                        $"Atualiza <NameSize> para {raw.Length}.");
                }

                bw.Write(itemCode);
                bw.Write(RequiredUInt(row, "Di_No", $"ItemLook Item={itemCode}"));
                bw.Write(RequiredUInt(row, "Change_Type", $"ItemLook Item={itemCode}"));
                bw.Write(raw.Length);
                bw.Write(raw);
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static List<XElement> FlattenNestedItems(XElement root)
        {
            var result = new List<XElement>();

            XElement? current = root.Element("Item");

            while (current != null)
            {
                result.Add(current);
                current = current.Element("Item");
            }

            return result;
        }

        private static void RequireTag(
            XElement element,
            string expected,
            string context)
        {
            if (element.Name.LocalName != expected)
            {
                throw new InvalidDataException(
                    $"{context}: esperado <{expected}>, encontrado <{element.Name.LocalName}>.");
            }
        }

        private static (string Text, int DeclaredChars) ReadDynamicUnicodeKeepDeclared(
            BinaryReader br,
            string field)
        {
            int chars = ReadCount(br, field + ".CharCount", 10_000_000);

            byte[] raw = ReadExact(br, checked(chars * 2), field);

            string text = Encoding.Unicode.GetString(raw);

            int zero = text.IndexOf('\0');

            if (zero >= 0)
                text = text[..zero];

            return (text, chars);
        }

        private static void WriteDeclaredUnicode(
            BinaryWriter bw,
            string text,
            int declaredBytes,
            string field)
        {
            if (declaredBytes < 0 || (declaredBytes & 1) != 0)
            {
                throw new InvalidDataException(
                    $"{field}: tamanho declarado {declaredBytes} não é um " +
                    "número par de bytes UTF-16LE.");
            }

            byte[] raw = Encoding.Unicode.GetBytes(text ?? string.Empty);

            if (raw.Length > declaredBytes)
            {
                throw new InvalidDataException(
                    $"{field}: o texto ocupa {raw.Length} bytes UTF-16LE, " +
                    $"mas o XML declara apenas {declaredBytes} bytes. " +
                    $"Aumenta o campo de tamanho para pelo menos {raw.Length} " +
                    $"ou reduz o texto.");
            }

            bw.Write(declaredBytes / 2);
            bw.Write(raw);

            if (raw.Length < declaredBytes)
                bw.Write(new byte[declaredBytes - raw.Length]);
        }

        private static byte[] ReadBase64Fixed(
            string text,
            int expectedBytes,
            string field)
        {
            byte[] raw;

            try
            {
                raw = Convert.FromBase64String(text.Trim());
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"{field}: conteúdo Base64 inválido.",
                    ex);
            }

            if (raw.Length != expectedBytes)
            {
                throw new InvalidDataException(
                    $"{field}: Base64 representa {raw.Length} bytes; " +
                    $"esperado={expectedBytes}.");
            }

            return raw;
        }

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] bytes = br.ReadBytes(count);

            if (bytes.Length != count)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {count} bytes, recebidos {bytes.Length}.");
            }

            return bytes;
        }

        private static string ReadFixedUnicode(BinaryReader br, int chars)
        {
            byte[] raw = ReadExact(br, chars * 2, "UTF16 fixed string");

            string value = Encoding.Unicode.GetString(raw);

            int zero = value.IndexOf('\0');

            return zero >= 0 ? value[..zero] : value;
        }

        private static void WriteFixedUnicode(
            BinaryWriter bw,
            string text,
            int chars,
            string field)
        {
            byte[] raw = Encoding.Unicode.GetBytes(text ?? string.Empty);

            int max = (chars - 1) * 2;

            if (raw.Length > max)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes UTF-16LE; " +
                    $"limite útil={max} bytes ({chars - 1} caracteres + NUL).");
            }

            byte[] buffer = new byte[chars * 2];

            Buffer.BlockCopy(raw, 0, buffer, 0, raw.Length);

            bw.Write(buffer);
        }

        private static string ReadFixedCp949(BinaryReader br, int bytes)
        {
            byte[] raw = ReadExact(br, bytes, "CP949 fixed string");

            int zero = Array.IndexOf(raw, (byte)0);

            if (zero < 0)
                zero = raw.Length;

            return Cp949.GetString(raw, 0, zero);
        }

        private static void WriteFixedCp949(
            BinaryWriter bw,
            string text,
            int bytes,
            string field)
        {
            byte[] raw = Cp949.GetBytes(text ?? string.Empty);

            if (raw.Length >= bytes)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes CP949; " +
                    $"limite útil={bytes - 1} bytes + terminador.");
            }

            byte[] buffer = new byte[bytes];

            Buffer.BlockCopy(raw, 0, buffer, 0, raw.Length);

            bw.Write(buffer);
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value = br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max}.");
            }

            return value;
        }

        private static XDocument LoadXml(string path)
        {
            try
            {
                return XDocument.Load(path, LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                throw;
            }
        }

        private static XElement RequireRoot(
            XDocument doc,
            string expected,
            string context)
        {
            XElement? root = doc.Root;

            if (root == null)
                throw new InvalidDataException($"{context}: XML sem root.");

            if (root.Name.LocalName != expected)
            {
                throw new InvalidDataException(
                    $"{context}: root <{root.Name.LocalName}> inválido. " +
                    $"Esperado <{expected}>.");
            }

            return root;
        }

        private static string RequiredText(
            XElement parent,
            string name,
            string context,
            bool allowEmpty = false)
        {
            XElement? e = parent.Element(name);

            if (e == null)
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");

            string value = e.Value;

            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> está vazio.");
            }

            return value;
        }

        private static int RequiredInt(
            XElement parent,
            string name,
            string context) =>
            ParseInt(
                RequiredText(parent, name, context),
                $"{context} <{name}>");

        private static int ParseInt(string value, string context)
        {
            if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não é Int32 válido.");
            }

            return result;
        }

        private static short ParseInt16(string value, string context)
        {
            if (!short.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out short result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não cabe em Int16 (-32768..32767).");
            }

            return result;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context) =>
            ParseUInt(
                RequiredText(parent, name, context),
                $"{context} <{name}>");

        private static uint ParseUInt(string value, string context)
        {
            if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não é UInt32 válido.");
            }

            return result;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context) =>
            ParseUInt16(
                RequiredText(parent, name, context),
                $"{context} <{name}>");

        private static ushort ParseUInt16(string value, string context)
        {
            if (!ushort.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não cabe em UInt16 (0..65535).");
            }

            return result;
        }

        private static byte RequiredByte(
            XElement parent,
            string name,
            string context) =>
            ParseByte(
                RequiredText(parent, name, context),
                $"{context} <{name}>");

        private static byte ParseByte(string value, string context)
        {
            if (!byte.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out byte result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não cabe em byte (0..255).");
            }

            return result;
        }

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration("1.0", "utf-8", null),
                root);

        private static void SaveXml(XDocument doc, string path)
        {
            using XmlWriter writer =
                XmlWriter.Create(
                    path,
                    new XmlWriterSettings
                    {
                        Indent = true,
                        Encoding = new UTF8Encoding(false),
                        OmitXmlDeclaration = false
                    });

            doc.Save(writer);
        }

        private static Encoding CreateCp949()
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(
                949,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
        }
    }
}
