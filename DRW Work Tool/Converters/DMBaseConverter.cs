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
    public sealed class DMBaseConverter : IGameDataConverter
    {
        public string Name => "DMBase";

        private const int BaseRecordSize = 40;
        private const int StoreFileNameChars = 64;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("DMBase", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath)
        {
            if (Directory.Exists(filePath))
                return Path.GetFileName(filePath)
                    .Equals("DMBase", StringComparison.OrdinalIgnoreCase);

            return Path.GetFileNameWithoutExtension(filePath)
                .Equals("DMBase", StringComparison.OrdinalIgnoreCase);
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do DMBase.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            XDocument tamer = ReadBaseTable(
                br,
                "CharacterList",
                "DMBaseInfo",
                "TamerBase");

            SaveXml(tamer, Path.Combine(folder, "TamerBase.xml"));
            SaveXml(new XDocument(tamer), Path.Combine(folder, "TamerBaseInfo.xml"));

            XDocument digimon = ReadBaseTable(
                br,
                "DigimonList",
                "DigiBaseInfo",
                "DigimonBase");

            SaveXml(digimon, Path.Combine(folder, "DigimonBase.xml"));
            SaveXml(new XDocument(digimon), Path.Combine(folder, "DigimonBaseInfo.xml"));

            XDocument maps = ReadMapInfo(br);
            SaveXml(maps, Path.Combine(folder, "CsBaseMapInfo.xml"));

            XDocument jump = ReadJumpBooster(br);
            SaveXml(jump, Path.Combine(folder, "JumpBooster.xml"));

            XDocument party = ReadParty(br);
            SaveXml(party, Path.Combine(folder, "Party.xml"));

            XDocument guild = ReadGuild(br);
            SaveXml(guild, Path.Combine(folder, "Guild.xml"));

            XDocument limit = ReadLimit(br);
            SaveXml(limit, Path.Combine(folder, "Limit.xml"));

            XDocument store = ReadStore(br);
            SaveXml(store, Path.Combine(folder, "Store.xml"));

            XDocument penalty = ReadPenalty(br);
            SaveXml(penalty, Path.Combine(folder, "PaneltyInfo.xml"));

            XDocument evoApply = ReadEvolutionBaseApply(br);
            SaveXml(evoApply, Path.Combine(folder, "EvolutionBaseApply.xml"));

            XDocument maxSkill = ReadDigimonEvoMaxSkill(br);
            SaveXml(maxSkill, Path.Combine(folder, "DigimonEvoMaxSkill.xml"));
            SaveXml(new XDocument(maxSkill), Path.Combine(folder, "DigimonEvoMaxSkillLevel.xml"));

            XDocument expansion = ReadExpansion(br);
            SaveXml(expansion, Path.Combine(folder, "ExpansionCondition.xml"));
            SaveXml(new XDocument(expansion), Path.Combine(folder, "ExpansionData.xml"));

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"DMBase.bin contém {extra:N0} bytes extra. " +
                    $"Leitura terminou no offset {ms.Position:N0}, " +
                    $"ficheiro possui {ms.Length:N0} bytes.");
            }

            AppLogger.Log(
                "DMBase: BIN -> XML concluído. 16 XMLs gerados.");

            AppLogger.Log(
                $"DMBase: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder = ResolveFolder(inputXml);

            // Valida a presença dos 16 XMLs porque o extractor original
            // produzia todos estes ficheiros.
            string[] required =
            {
                "CsBaseMapInfo.xml",
                "DigimonBase.xml",
                "DigimonBaseInfo.xml",
                "DigimonEvoMaxSkill.xml",
                "DigimonEvoMaxSkillLevel.xml",
                "EvolutionBaseApply.xml",
                "ExpansionCondition.xml",
                "ExpansionData.xml",
                "Guild.xml",
                "JumpBooster.xml",
                "Limit.xml",
                "PaneltyInfo.xml",
                "Party.xml",
                "Store.xml",
                "TamerBase.xml",
                "TamerBaseInfo.xml"
            };

            foreach (string name in required)
            {
                string path = Path.Combine(folder, name);

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"DMBase: XML obrigatório em falta: {path}",
                        path);
                }
            }

            // Valida os pares que são aliases do mesmo conteúdo.
            ValidateAliasPair(
                Path.Combine(folder, "TamerBase.xml"),
                Path.Combine(folder, "TamerBaseInfo.xml"),
                "TamerBase");

            ValidateAliasPair(
                Path.Combine(folder, "DigimonBase.xml"),
                Path.Combine(folder, "DigimonBaseInfo.xml"),
                "DigimonBase");

            ValidateAliasPair(
                Path.Combine(folder, "DigimonEvoMaxSkill.xml"),
                Path.Combine(folder, "DigimonEvoMaxSkillLevel.xml"),
                "DigimonEvoMaxSkill");

            ValidateAliasPair(
                Path.Combine(folder, "ExpansionCondition.xml"),
                Path.Combine(folder, "ExpansionData.xml"),
                "ExpansionCondition");

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para DMBase."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs, Encoding.UTF8, leaveOpen: true);

            WriteBaseTable(
                bw,
                LoadXml(Path.Combine(folder, "TamerBase.xml")),
                "CharacterList",
                "DMBaseInfo",
                "TamerBase");

            WriteBaseTable(
                bw,
                LoadXml(Path.Combine(folder, "DigimonBase.xml")),
                "DigimonList",
                "DigiBaseInfo",
                "DigimonBase");

            WriteMapInfo(
                bw,
                LoadXml(Path.Combine(folder, "CsBaseMapInfo.xml")));

            WriteJumpBooster(
                bw,
                LoadXml(Path.Combine(folder, "JumpBooster.xml")));

            WriteParty(
                bw,
                LoadXml(Path.Combine(folder, "Party.xml")));

            WriteGuild(
                bw,
                LoadXml(Path.Combine(folder, "Guild.xml")));

            WriteLimit(
                bw,
                LoadXml(Path.Combine(folder, "Limit.xml")));

            WriteStore(
                bw,
                LoadXml(Path.Combine(folder, "Store.xml")));

            WritePenalty(
                bw,
                LoadXml(Path.Combine(folder, "PaneltyInfo.xml")));

            WriteEvolutionBaseApply(
                bw,
                LoadXml(Path.Combine(folder, "EvolutionBaseApply.xml")));

            WriteDigimonEvoMaxSkill(
                bw,
                LoadXml(Path.Combine(folder, "DigimonEvoMaxSkill.xml")));

            WriteExpansion(
                bw,
                LoadXml(Path.Combine(folder, "ExpansionCondition.xml")));

            bw.Flush();

            long actualSize = fs.Length;

            AppLogger.Log(
                "DMBase: XML -> BIN concluído. 16 XMLs validados.");

            AppLogger.Log(
                $"DMBase: tamanho BIN gerado: {actualSize:N0} bytes (OK).");
        }

        // ============================================================
        // BASE TABLES
        // ============================================================

        private static XDocument ReadBaseTable(
            BinaryReader br,
            string rootName,
            string rowName,
            string label)
        {
            int count = ReadCount(br, $"{label}.Count", 100_000);

            XElement root = new(rootName);

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();
                ushort level = br.ReadUInt16();
                ushort unknown1 = br.ReadUInt16();
                ulong exp = br.ReadUInt64();
                uint hp = br.ReadUInt32();
                uint ds = br.ReadUInt32();
                ushort ms = br.ReadUInt16();
                ushort de = br.ReadUInt16();
                ushort ev = br.ReadUInt16();
                ushort ct = br.ReadUInt16();
                uint at = br.ReadUInt32();
                ushort ht = br.ReadUInt16();
                ushort unknown2 = br.ReadUInt16();

                if (br.BaseStream.Position - start != BaseRecordSize)
                {
                    throw new InvalidDataException(
                        $"{label}: record #{i} não ocupa {BaseRecordSize} bytes.");
                }

                root.Add(
                    new XElement(
                        rowName,
                        new XElement("Id", id),
                        new XElement("Level", level),
                        new XElement("Unknow1", unknown1),
                        new XElement("Exp", exp),
                        new XElement("Hp", hp),
                        new XElement("Ds", ds),
                        new XElement("Ms", ms),
                        new XElement("De", de),
                        new XElement("Ev", ev),
                        new XElement("Ct", ct),
                        new XElement("At", at),
                        new XElement("Ht", ht),
                        new XElement("Unknow2", unknown2)));
            }

            return Xml(root);
        }

        private static void WriteBaseTable(
            BinaryWriter bw,
            XDocument doc,
            string rootName,
            string rowName,
            string label)
        {
            XElement root = RequireRoot(doc, rootName, label);
            List<XElement> rows = root.Elements(rowName).ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "Id", label));
                bw.Write(RequiredUInt16(row, "Level", label));
                bw.Write(RequiredUInt16(row, "Unknow1", label));
                bw.Write(RequiredUInt64(row, "Exp", label));
                bw.Write(RequiredUInt(row, "Hp", label));
                bw.Write(RequiredUInt(row, "Ds", label));
                bw.Write(RequiredUInt16(row, "Ms", label));
                bw.Write(RequiredUInt16(row, "De", label));
                bw.Write(RequiredUInt16(row, "Ev", label));
                bw.Write(RequiredUInt16(row, "Ct", label));
                bw.Write(RequiredUInt(row, "At", label));
                bw.Write(RequiredUInt16(row, "Ht", label));
                bw.Write(RequiredUInt16(row, "Unknow2", label));
            }
        }

        // ============================================================
        // MAP INFO
        // ============================================================

        private static XDocument ReadMapInfo(BinaryReader br)
        {
            int count = ReadCount(br, "CsBaseMapInfo.Count", 100_000);

            XElement root = new("CsBaseMapInfoList");

            for (int i = 0; i < count; i++)
            {
                uint mapId = br.ReadUInt32();
                uint shoutSec = br.ReadUInt32();
                byte enableMacro = br.ReadByte();

                // Padding byte confirmado zero.
                byte padding = br.ReadByte();

                if (padding != 0)
                {
                    throw new InvalidDataException(
                        $"CsBaseMapInfo[{i}]: padding inesperado={padding}.");
                }

                ushort unk = br.ReadUInt16();

                root.Add(
                    new XElement(
                        "CsBaseMapInfo",
                        new XElement("s_nMapID", mapId),
                        new XElement("s_nShoutSec", shoutSec),
                        new XElement("s_bEnableCheckMacro", enableMacro),
                        new XElement("unk", unk)));
            }

            return Xml(root);
        }

        private static void WriteMapInfo(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "CsBaseMapInfoList",
                "CsBaseMapInfo.xml");

            List<XElement> rows =
                root.Elements("CsBaseMapInfo").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredUInt(row, "s_nMapID", "CsBaseMapInfo"));
                bw.Write(RequiredUInt(row, "s_nShoutSec", "CsBaseMapInfo"));
                bw.Write(RequiredByte(row, "s_bEnableCheckMacro", "CsBaseMapInfo"));
                bw.Write((byte)0);
                bw.Write(RequiredUInt16(row, "unk", "CsBaseMapInfo"));
            }
        }

        // ============================================================
        // JUMP BOOSTER
        // ============================================================

        private static XDocument ReadJumpBooster(BinaryReader br)
        {
            int count = ReadCount(br, "JumpBooster.Count", 100_000);

            XElement root = new("JumpboosterList");

            for (int i = 0; i < count; i++)
            {
                uint itemId = br.ReadUInt32();
                int mapCount = ReadCount(
                    br,
                    $"JumpBooster[{i}].mapcount",
                    100_000);

                XElement maps = new("dwMapIDs");

                for (int m = 0; m < mapCount; m++)
                {
                    maps.Add(
                        new XElement(
                            "dwMapID",
                            br.ReadUInt32()));
                }

                root.Add(
                    new XElement(
                        "Jumpbooster",
                        new XElement("dwItemID", itemId),
                        new XElement("mapcount", mapCount),
                        maps));
            }

            return Xml(root);
        }

        private static void WriteJumpBooster(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "JumpboosterList",
                "JumpBooster.xml");

            List<XElement> rows =
                root.Elements("Jumpbooster").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint itemId =
                    RequiredUInt(
                        row,
                        "dwItemID",
                        "JumpBooster");

                XElement? mapContainer = row.Element("dwMapIDs");

                List<XElement> maps =
                    mapContainer?
                        .Elements("dwMapID")
                        .ToList()
                    ?? new List<XElement>();

                int declared =
                    RequiredInt(
                        row,
                        "mapcount",
                        $"JumpBooster ItemID={itemId}");

                if (declared != maps.Count)
                {
                    throw new InvalidDataException(
                        $"JumpBooster ItemID={itemId}: " +
                        $"<mapcount>={declared}, mas existem {maps.Count} <dwMapID>.");
                }

                bw.Write(itemId);
                bw.Write(maps.Count);

                foreach (XElement map in maps)
                {
                    bw.Write(ParseUInt(
                        map.Value,
                        $"JumpBooster ItemID={itemId} <dwMapID>"));
                }
            }
        }

        // ============================================================
        // PARTY
        // ============================================================

        private static XDocument ReadParty(BinaryReader br)
        {
            float dist = br.ReadSingle();

            return Xml(
                new XElement(
                    "Parties",
                    new XElement(
                        "Party",
                        new XElement(
                            "distc",
                            FormatFloat(dist)))));
        }

        private static void WriteParty(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "Parties", "Party.xml");
            XElement party =
                root.Element("Party")
                ?? throw new InvalidDataException(
                    "Party.xml: falta <Party>.");

            bw.Write(
                RequiredFloat(
                    party,
                    "distc",
                    "Party.xml"));
        }

        // ============================================================
        // GUILD
        // ============================================================

        private static readonly string[] GuildFields =
        {
            "s_nLevel",
            "s_nFame",
            "s_nItemNo1",
            "s_nItemCount1",
            "s_nItemNo2",
            "s_nItemCount2",
            "s_nMasterLevel",
            "s_nNeedPerson",
            "s_nMaxGuildPerson",
            "s_nIncMember",
            "s_nMaxGuild2Master"
        };

        private static XDocument ReadGuild(BinaryReader br)
        {
            int count = ReadCount(br, "Guild.Count", 10_000);

            XElement root = new("GuildData");

            for (int i = 0; i < count; i++)
            {
                XElement guild = new("Guild");

                foreach (string field in GuildFields)
                {
                    guild.Add(
                        new XElement(
                            field,
                            br.ReadInt32()));
                }

                root.Add(guild);
            }

            return Xml(root);
        }

        private static void WriteGuild(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "GuildData",
                "Guild.xml");

            List<XElement> rows = root.Elements("Guild").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                foreach (string field in GuildFields)
                {
                    bw.Write(
                        RequiredInt(
                            row,
                            field,
                            "Guild.xml"));
                }
            }
        }

        // ============================================================
        // LIMIT
        // ============================================================

        private static XDocument ReadLimit(BinaryReader br)
        {
            XElement limit =
                new(
                    "Limit",
                    new XElement("s_nMaxTacticsHouse", br.ReadUInt16()),
                    new XElement("s_nMaxWareHouse", br.ReadUInt16()),
                    new XElement("s_nUnionStore", br.ReadUInt16()),
                    new XElement("s_nMaxShareStash", br.ReadUInt16()),
                    new XElement("s_nConsume_XG", br.ReadUInt32()),
                    new XElement("s_nCharge_XG", br.ReadUInt32()));

            return Xml(new XElement("Limits", limit));
        }

        private static void WriteLimit(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "Limits", "Limit.xml");
            XElement row =
                root.Element("Limit")
                ?? throw new InvalidDataException(
                    "Limit.xml: falta <Limit>.");

            bw.Write(RequiredUInt16(row, "s_nMaxTacticsHouse", "Limit.xml"));
            bw.Write(RequiredUInt16(row, "s_nMaxWareHouse", "Limit.xml"));
            bw.Write(RequiredUInt16(row, "s_nUnionStore", "Limit.xml"));
            bw.Write(RequiredUInt16(row, "s_nMaxShareStash", "Limit.xml"));
            bw.Write(RequiredUInt(row, "s_nConsume_XG", "Limit.xml"));
            bw.Write(RequiredUInt(row, "s_nCharge_XG", "Limit.xml"));
        }

        // ============================================================
        // STORE
        // ============================================================

        private static XDocument ReadStore(BinaryReader br)
        {
            float person = br.ReadSingle();
            float employment = br.ReadSingle();
            float dist = br.ReadSingle();

            int count = ReadCount(br, "Store.ItemCount", 100_000);

            XElement items = new("StoreItems");

            for (int i = 0; i < count; i++)
            {
                uint itemId = br.ReadUInt32();
                uint digimonId = br.ReadUInt32();
                float scale = br.ReadSingle();
                uint slots = br.ReadUInt32();
                string fileName =
                    ReadFixedUnicode(
                        br,
                        StoreFileNameChars);

                items.Add(
                    new XElement(
                        "StoreItem",
                        new XElement("s_nItemID", itemId),
                        new XElement("s_nDigimonID", digimonId),
                        new XElement("s_fScale", FormatFloatComma(scale)),
                        new XElement("s_nSlotCount", slots),
                        new XElement("s_szFileName", fileName)));
            }

            return Xml(
                new XElement(
                    "Stores",
                    new XElement(
                        "Store",
                        new XElement("s_fPerson_Charge", FormatFloat(person)),
                        new XElement("s_fEmployment_Charge", FormatFloat(employment)),
                        new XElement("s_fStoreDist", FormatFloat(dist)),
                        items)));
        }

        private static void WriteStore(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(doc, "Stores", "Store.xml");
            XElement store =
                root.Element("Store")
                ?? throw new InvalidDataException(
                    "Store.xml: falta <Store>.");

            bw.Write(RequiredFloat(store, "s_fPerson_Charge", "Store.xml"));
            bw.Write(RequiredFloat(store, "s_fEmployment_Charge", "Store.xml"));
            bw.Write(RequiredFloat(store, "s_fStoreDist", "Store.xml"));

            XElement? container = store.Element("StoreItems");

            List<XElement> items =
                container?
                    .Elements("StoreItem")
                    .ToList()
                ?? new List<XElement>();

            bw.Write(items.Count);

            foreach (XElement item in items)
            {
                uint itemId = RequiredUInt(
                    item,
                    "s_nItemID",
                    "Store.xml");

                bw.Write(itemId);
                bw.Write(RequiredUInt(item, "s_nDigimonID", $"Store ItemID={itemId}"));
                bw.Write(RequiredFloat(item, "s_fScale", $"Store ItemID={itemId}"));
                bw.Write(RequiredUInt(item, "s_nSlotCount", $"Store ItemID={itemId}"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        item,
                        "s_szFileName",
                        $"Store ItemID={itemId}",
                        allowEmpty: true),
                    StoreFileNameChars,
                    $"Store ItemID={itemId} <s_szFileName>");
            }
        }

        // ============================================================
        // PENALTY
        // ============================================================

        private static XDocument ReadPenalty(BinaryReader br)
        {
            int count = ReadCount(br, "PaneltyInfo.Count", 1000);

            XElement root = new("PaneltyInfos");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "PaneltyInfo",
                        new XElement("s_nPaneltyLevel", br.ReadInt32()),
                        new XElement("s_nExp", br.ReadInt32()),
                        new XElement("s_nDrop", br.ReadInt32())));
            }

            return Xml(root);
        }

        private static void WritePenalty(BinaryWriter bw, XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "PaneltyInfos",
                "PaneltyInfo.xml");

            List<XElement> rows =
                root.Elements("PaneltyInfo").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(RequiredInt(row, "s_nPaneltyLevel", "PaneltyInfo.xml"));
                bw.Write(RequiredInt(row, "s_nExp", "PaneltyInfo.xml"));
                bw.Write(RequiredInt(row, "s_nDrop", "PaneltyInfo.xml"));
            }
        }

        // ============================================================
        // EVOLUTION BASE APPLY
        // ============================================================

        private static XDocument ReadEvolutionBaseApply(BinaryReader br)
        {
            int count = ReadCount(
                br,
                "EvolutionBaseApply.Count",
                1000);

            XElement root = new("EvolutionBaseApplies");

            for (int i = 0; i < count; i++)
            {
                int type = br.ReadInt32();
                int nameSize = ReadCount(
                    br,
                    $"EvolutionBaseApply[{i}].NameSize",
                    10_000);

                byte[] nameBytes = br.ReadBytes(nameSize * 2);

                if (nameBytes.Length != nameSize * 2)
                {
                    throw new EndOfStreamException(
                        "EvolutionBaseApply: string truncada.");
                }

                string name =
                    Encoding.Unicode.GetString(nameBytes);

                int value = br.ReadInt32();

                root.Add(
                    new XElement(
                        "EvolutionBaseApply",
                        new XElement("EvolutionType", type),
                        new XElement("EvolutionName", name),
                        new XElement("EvolutionApplyValue", value),
                        new XElement("NameSize", nameSize)));
            }

            return Xml(root);
        }

        private static void WriteEvolutionBaseApply(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "EvolutionBaseApplies",
                "EvolutionBaseApply.xml");

            List<XElement> rows =
                root.Elements("EvolutionBaseApply").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                string name = RequiredText(
                    row,
                    "EvolutionName",
                    "EvolutionBaseApply.xml",
                    allowEmpty: true);

                int declared =
                    RequiredInt(
                        row,
                        "NameSize",
                        "EvolutionBaseApply.xml");

                if (declared != name.Length)
                {
                    throw new InvalidDataException(
                        $"EvolutionBaseApply: <NameSize>={declared}, " +
                        $"mas EvolutionName tem {name.Length} caracteres.");
                }

                bw.Write(RequiredInt(row, "EvolutionType", "EvolutionBaseApply.xml"));
                bw.Write(name.Length);
                bw.Write(Encoding.Unicode.GetBytes(name));
                bw.Write(RequiredInt(row, "EvolutionApplyValue", "EvolutionBaseApply.xml"));
            }
        }

        // ============================================================
        // DIGIMON EVO MAX SKILL
        // ============================================================

        private static XDocument ReadDigimonEvoMaxSkill(BinaryReader br)
        {
            int count = ReadCount(
                br,
                "DigimonEvoMaxSkill.Count",
                1000);

            XElement root = new("DigimonEvoMaxSkillLevels");

            for (int i = 0; i < count; i++)
            {
                int evoType = br.ReadInt32();
                int startLv = br.ReadInt32();
                int subCount = ReadCount(
                    br,
                    $"DigimonEvoMaxSkill[{i}].nSubCount",
                    10_000);

                XElement values = new("s_SkillMaxLvs");

                for (int s = 0; s < subCount; s++)
                {
                    values.Add(
                        new XElement(
                            "SkillMaxLv",
                            br.ReadInt32()));
                }

                root.Add(
                    new XElement(
                        "DigimonEvoMaxSkillLevel",
                        new XElement("nEvoType", evoType),
                        new XElement("s_SkillExpStartLv", startLv),
                        new XElement("nSubCount", subCount),
                        values));
            }

            return Xml(root);
        }

        private static void WriteDigimonEvoMaxSkill(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "DigimonEvoMaxSkillLevels",
                "DigimonEvoMaxSkill.xml");

            List<XElement> rows =
                root.Elements("DigimonEvoMaxSkillLevel").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int evoType =
                    RequiredInt(
                        row,
                        "nEvoType",
                        "DigimonEvoMaxSkill.xml");

                XElement? container =
                    row.Element("s_SkillMaxLvs");

                List<XElement> values =
                    container?
                        .Elements("SkillMaxLv")
                        .ToList()
                    ?? new List<XElement>();

                int declared =
                    RequiredInt(
                        row,
                        "nSubCount",
                        $"DigimonEvoMaxSkill nEvoType={evoType}");

                if (declared != values.Count)
                {
                    throw new InvalidDataException(
                        $"DigimonEvoMaxSkill nEvoType={evoType}: " +
                        $"<nSubCount>={declared}, mas existem " +
                        $"{values.Count} <SkillMaxLv>.");
                }

                bw.Write(evoType);
                bw.Write(RequiredInt(
                    row,
                    "s_SkillExpStartLv",
                    $"DigimonEvoMaxSkill nEvoType={evoType}"));

                bw.Write(values.Count);

                foreach (XElement value in values)
                {
                    bw.Write(ParseInt(
                        value.Value,
                        $"DigimonEvoMaxSkill nEvoType={evoType} SkillMaxLv"));
                }
            }
        }

        // ============================================================
        // EXPANSION
        // ============================================================

        private static XDocument ReadExpansion(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "Expansion.Count",
                    100_000);

            XElement root = new("ExpansionConditions");

            for (int i = 0; i < count; i++)
            {
                int subtype = br.ReadInt32();
                int rank = br.ReadInt32();
                int typeCount = ReadCount(
                    br,
                    $"Expansion[{i}].TypeCount",
                    10_000);

                XElement types =
                    new("nEvoType");

                for (int t = 0; t < typeCount; t++)
                {
                    types.Add(
                        new XElement(
                            "Type",
                            br.ReadInt32()));
                }

                root.Add(
                    new XElement(
                        "ExpansionCondition",
                        new XElement("nOpenItemSubType", subtype),
                        new XElement("nExpansionRank", rank),
                        types));
            }

            return Xml(root);
        }

        private static void WriteExpansion(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root = RequireRoot(
                doc,
                "ExpansionConditions",
                "ExpansionCondition.xml");

            List<XElement> rows =
                root.Elements("ExpansionCondition").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int subtype =
                    RequiredInt(
                        row,
                        "nOpenItemSubType",
                        "ExpansionCondition.xml");

                XElement? container =
                    row.Element("nEvoType");

                List<XElement> types =
                    container?
                        .Elements("Type")
                        .ToList()
                    ?? new List<XElement>();

                bw.Write(subtype);
                bw.Write(RequiredInt(
                    row,
                    "nExpansionRank",
                    $"Expansion subtype={subtype}"));

                bw.Write(types.Count);

                foreach (XElement type in types)
                {
                    bw.Write(ParseInt(
                        type.Value,
                        $"Expansion subtype={subtype} <Type>"));
                }
            }
        }

        // ============================================================
        // XML / VALIDATION HELPERS
        // ============================================================

        private static string ResolveFolder(string inputXml)
        {
            if (Directory.Exists(inputXml))
                return inputXml;

            string? folder = Path.GetDirectoryName(inputXml);

            if (folder == null)
            {
                throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do DMBase.");
            }

            return folder;
        }

        private static void ValidateAliasPair(
            string canonical,
            string alias,
            string label)
        {
            XDocument a = LoadXml(canonical);
            XDocument b = LoadXml(alias);

            string normalizedA =
                a.ToString(SaveOptions.DisableFormatting);

            string normalizedB =
                b.ToString(SaveOptions.DisableFormatting);

            if (!string.Equals(
                normalizedA,
                normalizedB,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"DMBase: '{Path.GetFileName(canonical)}' e " +
                    $"'{Path.GetFileName(alias)}' deveriam conter os mesmos dados " +
                    $"({label}), mas existem diferenças entre eles.");
            }
        }

        private static XDocument LoadXml(string path)
        {
            try
            {
                return XDocument.Load(
                    path,
                    LoadOptions.SetLineInfo);
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
            {
                throw new InvalidDataException(
                    $"{context}: XML sem root.");
            }

            if (root.Name.LocalName != expected)
            {
                throw new InvalidDataException(
                    $"{context}: root <{root.Name.LocalName}> inválido. " +
                    $"Esperado <{expected}>.");
            }

            return root;
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

        private static string RequiredText(
            XElement parent,
            string name,
            string context,
            bool allowEmpty = false)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            string value = element.Value;

            if (!allowEmpty &&
                string.IsNullOrWhiteSpace(value))
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

        private static int ParseInt(
            string value,
            string context)
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

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context) =>
            ParseUInt(
                RequiredText(parent, name, context),
                $"{context} <{name}>");

        private static uint ParseUInt(
            string value,
            string context)
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

        private static ulong RequiredUInt64(
            XElement parent,
            string name,
            string context)
        {
            string value = RequiredText(parent, name, context);

            if (!ulong.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ulong result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é UInt64 válido.");
            }

            return result;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string value = RequiredText(parent, name, context);

            if (!ushort.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em UInt16 (0..65535).");
            }

            return result;
        }

        private static byte RequiredByte(
            XElement parent,
            string name,
            string context)
        {
            string value = RequiredText(parent, name, context);

            if (!byte.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out byte result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em byte (0..255).");
            }

            return result;
        }

        private static float RequiredFloat(
            XElement parent,
            string name,
            string context)
        {
            string value = RequiredText(parent, name, context);

            value = value.Replace(',', '.');

            if (!float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é float válido.");
            }

            return result;
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            int bytes = wcharCount * 2;
            byte[] raw = br.ReadBytes(bytes);

            if (raw.Length != bytes)
            {
                throw new EndOfStreamException(
                    $"Esperados {bytes} bytes UTF-16LE.");
            }

            string value =
                Encoding.Unicode.GetString(raw);

            int zero =
                value.IndexOf('\0');

            return zero >= 0
                ? value[..zero]
                : value;
        }

        private static void WriteFixedUnicode(
            BinaryWriter bw,
            string value,
            int wcharCount,
            string field)
        {
            byte[] raw =
                Encoding.Unicode.GetBytes(value ?? "");

            int max =
                (wcharCount - 1) * 2;

            if (raw.Length > max)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes UTF-16LE; " +
                    $"limite útil={max} bytes.");
            }

            byte[] buffer =
                new byte[wcharCount * 2];

            Buffer.BlockCopy(
                raw,
                0,
                buffer,
                0,
                raw.Length);

            bw.Write(buffer);
        }

        private static string FormatFloat(float value)
        {
            if (value == MathF.Truncate(value))
            {
                return value.ToString(
                    "0",
                    CultureInfo.InvariantCulture);
            }

            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }

        private static string FormatFloatComma(float value)
        {
            string valueText = FormatFloat(value);

            return valueText.Replace('.', ',');
        }

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration("1.0", "utf-8", null),
                root);

        private static void SaveXml(
            XDocument document,
            string path)
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

            document.Save(writer);
        }
    }
}
