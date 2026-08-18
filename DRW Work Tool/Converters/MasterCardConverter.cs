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
    public sealed class MasterCardConverter : IGameDataConverter
    {
        public string Name => "MasterCard";

        private const int MasterCardRecordSize = 268;
        private const int MasterCardNameChars = 64;
        private const int GradeCount = 6;

        private const int LeaderRecordSize = 44;
        private const int LeaderAbilityRecordSize = 40;

        private static readonly Encoding Cp949 = CreateCp949();

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MasterCard", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MasterCards", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\MasterCard.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            var sections = new List<string>();

            long start = ms.Position;
            XDocument cards = ReadMasterCards(br);
            sections.Add($"MasterCards={ms.Position - start:N0}");

            start = ms.Position;
            XDocument leaders = ReadLeaders(br);
            sections.Add($"Leaders={ms.Position - start:N0}");

            start = ms.Position;
            XDocument abilities = ReadLeaderAbilities(br);
            sections.Add($"LeaderAbilities={ms.Position - start:N0}");

            start = ms.Position;
            XDocument images = ReadDigimonImgPaths(br);
            sections.Add($"DigimonImgPaths={ms.Position - start:N0}");

            start = ms.Position;
            XDocument plates = ReadPlatePaths(br);
            sections.Add($"PlatePaths={ms.Position - start:N0}");

            start = ms.Position;
            XDocument elementals = ReadPathTable(
                br,
                "Elementals",
                "Elemental");
            sections.Add($"Elementals={ms.Position - start:N0}");

            start = ms.Position;
            XDocument attributes = ReadPathTable(
                br,
                "Attributes",
                "Attribute");
            sections.Add($"Attributes={ms.Position - start:N0}");

            start = ms.Position;
            XDocument unknown = ReadUnknownInformations(br);
            sections.Add($"UnknowInformations={ms.Position - start:N0}");

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"MasterCard.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"ficheiro possui {ms.Length:N0} bytes.");
            }

            SaveXml(cards, Path.Combine(folder, "MasterCards.xml"));
            SaveXml(leaders, Path.Combine(folder, "Leaders.xml"));
            SaveXml(abilities, Path.Combine(folder, "LeaderAbilities.xml"));
            SaveXml(images, Path.Combine(folder, "DigimonImgPaths.xml"));
            SaveXml(plates, Path.Combine(folder, "PlatePaths.xml"));
            SaveXml(elementals, Path.Combine(folder, "Elementals.xml"));
            SaveXml(attributes, Path.Combine(folder, "Attributes.xml"));
            SaveXml(unknown, Path.Combine(folder, "UnknowInformations.xml"));

            AppLogger.Log(
                "MasterCard: BIN -> XML concluído. 8 XMLs gerados.");

            AppLogger.Log(
                "MasterCard: secções em bytes -> " +
                string.Join(", ", sections) + ".");

            AppLogger.Log(
                $"MasterCard: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\MasterCard.");

            string cardsPath = Path.Combine(folder, "MasterCards.xml");
            string leadersPath = Path.Combine(folder, "Leaders.xml");
            string abilitiesPath = Path.Combine(folder, "LeaderAbilities.xml");
            string imagesPath = Path.Combine(folder, "DigimonImgPaths.xml");
            string platesPath = Path.Combine(folder, "PlatePaths.xml");
            string elementalsPath = Path.Combine(folder, "Elementals.xml");
            string attributesPath = Path.Combine(folder, "Attributes.xml");
            string unknownPath = Path.Combine(folder, "UnknowInformations.xml");

            string[] required =
            {
                cardsPath,
                leadersPath,
                abilitiesPath,
                imagesPath,
                platesPath,
                elementalsPath,
                attributesPath,
                unknownPath
            };

            foreach (string path in required)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"MasterCard: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument cards = LoadXml(cardsPath);
            XDocument leaders = LoadXml(leadersPath);
            XDocument abilities = LoadXml(abilitiesPath);
            XDocument images = LoadXml(imagesPath);
            XDocument plates = LoadXml(platesPath);
            XDocument elementals = LoadXml(elementalsPath);
            XDocument attributes = LoadXml(attributesPath);
            XDocument unknown = LoadXml(unknownPath);

            ValidateUnknownSealReferences(cards, unknown);

            long expectedSize;

            using (MemoryStream counter = new())
            using (BinaryWriter test =
                new(counter, Encoding.UTF8, leaveOpen: true))
            {
                WriteAll(
                    test,
                    cards,
                    leaders,
                    abilities,
                    images,
                    plates,
                    elementals,
                    attributes,
                    unknown);

                test.Flush();
                expectedSize = counter.Length;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para MasterCard."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteAll(
                bw,
                cards,
                leaders,
                abilities,
                images,
                plates,
                elementals,
                attributes,
                unknown);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"MasterCard.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "MasterCard: XML -> BIN concluído. 8 XMLs validados.");

            AppLogger.Log(
                $"MasterCard: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteAll(
            BinaryWriter bw,
            XDocument cards,
            XDocument leaders,
            XDocument abilities,
            XDocument images,
            XDocument plates,
            XDocument elementals,
            XDocument attributes,
            XDocument unknown)
        {
            WriteMasterCards(bw, cards);
            WriteLeaders(bw, leaders);
            WriteLeaderAbilities(bw, abilities);
            WriteDigimonImgPaths(bw, images);
            WritePlatePaths(bw, plates);
            WritePathTable(bw, elementals, "Elementals", "Elemental");
            WritePathTable(bw, attributes, "Attributes", "Attribute");
            WriteUnknownInformations(bw, unknown);
        }

        // ============================================================
        // MASTER CARDS
        // ============================================================

        private static XDocument ReadMasterCards(BinaryReader br)
        {
            int count = ReadCount(br, "MasterCards.Count", 1_000_000);
            XElement root = new("MasterCards");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();
                string name = ReadFixedUnicode(br, MasterCardNameChars);
                uint digimonId = br.ReadUInt32();
                uint icon = br.ReadUInt32();
                uint unknown = br.ReadUInt32();
                uint leader = br.ReadUInt32();
                uint scale = br.ReadUInt32();

                XElement grades = new("GradeInfo");

                for (int g = 0; g < GradeCount; g++)
                {
                    ushort gradeIcon = br.ReadUInt16();
                    ushort max = br.ReadUInt16();
                    ushort identi = br.ReadUInt16();
                    ushort eff1 = br.ReadUInt16();
                    ushort eff1val = br.ReadUInt16();
                    ushort eff2 = br.ReadUInt16();
                    ushort eff2val = br.ReadUInt16();
                    ushort unk = br.ReadUInt16();

                    // O 6.º GradeInfoItem NÃO possui ItemId físico no BIN.
                    uint itemId =
                        g < GradeCount - 1
                            ? br.ReadUInt32()
                            : 0u;

                    grades.Add(
                        new XElement(
                            "GradeInfoItem",
                            new XElement("s_nIcon", gradeIcon),
                            new XElement("s_nMax", max),
                            new XElement("s_nIdentiQuantity", identi),
                            new XElement("s_nEff1", eff1),
                            new XElement("s_nEff1val", eff1val),
                            new XElement("s_nEff2", eff2),
                            new XElement("s_nEff2val", eff2val),
                            new XElement("unk", unk),
                            new XElement("ItemId", itemId)));
                }

                long consumed = br.BaseStream.Position - start;

                if (consumed != MasterCardRecordSize)
                {
                    throw new InvalidDataException(
                        $"MasterCard ID={id}: record ocupa {consumed:N0} bytes; " +
                        $"esperado={MasterCardRecordSize:N0}.");
                }

                root.Add(
                    new XElement(
                        "Card",
                        new XElement("s_nID", id),
                        new XElement("s_szName", name),
                        new XElement("s_nDigimonID", digimonId),
                        new XElement("s_nIcon", icon),
                        new XElement("unknow", unknown),
                        new XElement("s_nLeader", leader),
                        new XElement("s_nScale", scale),
                        grades));
            }

            return Xml(root);
        }

        private static void WriteMasterCards(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "MasterCards", "MasterCards.xml");

            List<XElement> rows =
                root.Elements("Card").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(row, "s_nID", "MasterCards.xml");

                string context = $"MasterCard ID={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredText(row, "s_szName", context, true),
                    MasterCardNameChars,
                    $"{context} <s_szName>");

                bw.Write(RequiredUInt(row, "s_nDigimonID", context));
                bw.Write(RequiredUInt(row, "s_nIcon", context));
                bw.Write(RequiredUInt(row, "unknow", context));
                bw.Write(RequiredUInt(row, "s_nLeader", context));
                bw.Write(RequiredUInt(row, "s_nScale", context));

                XElement? gradeInfo = row.Element("GradeInfo");

                List<XElement> grades =
                    gradeInfo?
                        .Elements("GradeInfoItem")
                        .ToList()
                    ?? new List<XElement>();

                if (grades.Count != GradeCount)
                {
                    throw new InvalidDataException(
                        $"{context}: <GradeInfo> deve conter exatamente " +
                        $"{GradeCount} <GradeInfoItem>; encontrados {grades.Count}.");
                }

                for (int g = 0; g < GradeCount; g++)
                {
                    XElement grade = grades[g];
                    string gc = $"{context} GradeInfo[{g}]";

                    bw.Write(RequiredUInt16(grade, "s_nIcon", gc));
                    bw.Write(RequiredUInt16(grade, "s_nMax", gc));
                    bw.Write(RequiredUInt16(grade, "s_nIdentiQuantity", gc));
                    bw.Write(RequiredUInt16(grade, "s_nEff1", gc));
                    bw.Write(RequiredUInt16(grade, "s_nEff1val", gc));
                    bw.Write(RequiredUInt16(grade, "s_nEff2", gc));
                    bw.Write(RequiredUInt16(grade, "s_nEff2val", gc));
                    bw.Write(RequiredUInt16(grade, "unk", gc));

                    uint itemId =
                        RequiredUInt(grade, "ItemId", gc);

                    if (g < GradeCount - 1)
                    {
                        bw.Write(itemId);
                    }
                    else if (itemId != 0)
                    {
                        throw new InvalidDataException(
                            $"{gc}: o 6.º <GradeInfoItem> não possui bytes " +
                            $"para <ItemId> no MasterCard.bin. " +
                            $"Mantém <ItemId>0</ItemId>; atual={itemId}.");
                    }
                }

                long consumed = bw.BaseStream.Position - start;

                if (consumed != MasterCardRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={MasterCardRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // LEADERS
        // ============================================================

        private static readonly string[] LeaderFields =
        {
            "s_nID",
            "s_nDigimonID",
            "s_nPetID",
            "s_nAni1",
            "s_nAni2",
            "s_nSpecial1",
            "s_nSpecial2",
            "s_nAbil1",
            "s_nAbil2",
            "s_nAbil3",
            "s_nAbil4"
        };

        private static XDocument ReadLeaders(BinaryReader br)
        {
            int count = ReadCount(br, "Leaders.Count", 1_000_000);
            XElement root = new("Leaders");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;
                XElement leader = new("Leader");

                foreach (string field in LeaderFields)
                {
                    leader.Add(
                        new XElement(
                            field,
                            br.ReadUInt32()));
                }

                long consumed = br.BaseStream.Position - start;

                if (consumed != LeaderRecordSize)
                {
                    throw new InvalidDataException(
                        $"Leader #{i}: record ocupa {consumed} bytes; " +
                        $"esperado={LeaderRecordSize}.");
                }

                root.Add(leader);
            }

            return Xml(root);
        }

        private static void WriteLeaders(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "Leaders", "Leaders.xml");

            List<XElement> rows =
                root.Elements("Leader").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(row, "s_nID", "Leaders.xml");

                long start = bw.BaseStream.Position;

                foreach (string field in LeaderFields)
                {
                    bw.Write(
                        RequiredUInt(
                            row,
                            field,
                            $"Leader ID={id}"));
                }

                if (bw.BaseStream.Position - start != LeaderRecordSize)
                {
                    throw new InvalidDataException(
                        $"Leader ID={id}: record gerado não ocupa " +
                        $"{LeaderRecordSize} bytes.");
                }
            }
        }

        // ============================================================
        // LEADER ABILITIES
        //
        // Este BIN possui apenas dois records e apenas ID + primeiro
        // s_nterm são não-zero. Os restantes 36 bytes são zero.
        // Não inventamos semântica para bytes sem amostra não-zero.
        // ============================================================

        private static XDocument ReadLeaderAbilities(BinaryReader br)
        {
            int count =
                ReadCount(br, "LeaderAbilities.Count", 100_000);

            XElement root = new("LeaderAbilities");

            for (int i = 0; i < count; i++)
            {
                byte[] raw = ReadExact(
                    br,
                    LeaderAbilityRecordSize,
                    $"LeaderAbility[{i}]");

                ushort id =
                    BitConverter.ToUInt16(raw, 0);

                ushort firstTerm =
                    BitConverter.ToUInt16(raw, 2);

                for (int p = 4; p < raw.Length; p++)
                {
                    if (raw[p] != 0)
                    {
                        throw new InvalidDataException(
                            $"LeaderAbility ID={id}: existem bytes não-zero " +
                            $"a partir do offset interno {p}. " +
                            "A amostra de referência não permite mapear com " +
                            "segurança estes bytes para os STerms.");
                    }
                }

                root.Add(
                    new XElement(
                        "LeaderAbility",
                        new XElement("s_nID", id),
                        new XElement("unknow", 0),
                        new XElement(
                            "STerms",
                            new XElement(
                                "STerm",
                                new XElement("s_nterm", firstTerm),
                                new XElement("s_ntermval", 0),
                                new XElement("s_nEff", 0)),
                            new XElement(
                                "STerm",
                                new XElement("s_nterm", 0),
                                new XElement("s_ntermval", 0),
                                new XElement("s_nEff", 0)),
                            new XElement(
                                "STerm",
                                new XElement("s_nterm", 0),
                                new XElement("s_ntermval", 0),
                                new XElement("s_nEff", 0)))));
            }

            return Xml(root);
        }

        private static void WriteLeaderAbilities(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "LeaderAbilities",
                    "LeaderAbilities.xml");

            List<XElement> rows =
                root.Elements("LeaderAbility").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort id =
                    RequiredUInt16(
                        row,
                        "s_nID",
                        "LeaderAbilities.xml");

                uint unknown =
                    RequiredUInt(
                        row,
                        "unknow",
                        $"LeaderAbility ID={id}");

                if (unknown != 0)
                {
                    throw new InvalidDataException(
                        $"LeaderAbility ID={id}: <unknow> deve permanecer 0. " +
                        "Este campo não possui byte semântico confirmado nesta amostra.");
                }

                XElement? termsRoot = row.Element("STerms");

                List<XElement> terms =
                    termsRoot?
                        .Elements("STerm")
                        .ToList()
                    ?? new List<XElement>();

                if (terms.Count != 3)
                {
                    throw new InvalidDataException(
                        $"LeaderAbility ID={id}: são necessários exatamente 3 <STerm>.");
                }

                ushort firstTerm =
                    RequiredUInt16(
                        terms[0],
                        "s_nterm",
                        $"LeaderAbility ID={id}, STerm[0]");

                for (int i = 0; i < terms.Count; i++)
                {
                    XElement term = terms[i];

                    uint termValue =
                        RequiredUInt(
                            term,
                            "s_ntermval",
                            $"LeaderAbility ID={id}, STerm[{i}]");

                    uint effect =
                        RequiredUInt(
                            term,
                            "s_nEff",
                            $"LeaderAbility ID={id}, STerm[{i}]");

                    uint termId =
                        RequiredUInt(
                            term,
                            "s_nterm",
                            $"LeaderAbility ID={id}, STerm[{i}]");

                    if (termValue != 0 ||
                        effect != 0 ||
                        (i > 0 && termId != 0))
                    {
                        throw new InvalidDataException(
                            $"LeaderAbility ID={id}: esta amostra só confirma " +
                            "o primeiro <s_nterm>. Os restantes campos STerm devem " +
                            "permanecer 0 até termos um MasterCard.bin com valores " +
                            "não-zero para mapear esses offsets com segurança.");
                    }
                }

                byte[] raw =
                    new byte[LeaderAbilityRecordSize];

                Buffer.BlockCopy(
                    BitConverter.GetBytes(id),
                    0,
                    raw,
                    0,
                    2);

                Buffer.BlockCopy(
                    BitConverter.GetBytes(firstTerm),
                    0,
                    raw,
                    2,
                    2);

                bw.Write(raw);
            }
        }

        // ============================================================
        // DIGIMON IMAGE PATHS
        //
        // O Count físico é XMLCount + 1.
        // Existe um header/sentinel de 9 bytes:
        // int32 nullp, int32 unknow, byte unknow1.
        // Depois seguem XMLCount records dinâmicos:
        // uint32 ID, int32 PathByteLength, CP949 bytes.
        // ============================================================

        private static XDocument ReadDigimonImgPaths(BinaryReader br)
        {
            int physicalCount =
                ReadCount(
                    br,
                    "DigimonImgPaths.PhysicalCount",
                    1_000_000);

            if (physicalCount < 1)
            {
                throw new InvalidDataException(
                    "DigimonImgPaths: PhysicalCount deve ser pelo menos 1.");
            }

            int nullp = br.ReadInt32();
            int unknown = br.ReadInt32();
            byte unknown1 = br.ReadByte();

            int xmlCount = physicalCount - 1;

            XElement root = new("DigimonImgPaths");

            for (int i = 0; i < xmlCount; i++)
            {
                uint id = br.ReadUInt32();

                int byteLength =
                    ReadCount(
                        br,
                        $"DigimonImgPath ID={id}.PathLength",
                        10_000_000);

                string path =
                    Cp949.GetString(
                        ReadExact(
                            br,
                            byteLength,
                            $"DigimonImgPath ID={id}"));

                root.Add(
                    new XElement(
                        "DigimonImgPath",
                        new XElement("nullp", i == 0 ? nullp : 0),
                        new XElement("unknow", i == 0 ? unknown : 0),
                        new XElement("unknow1", i == 0 ? unknown1 : 0),
                        new XElement("ID", id),
                        new XElement("s_szDigimonSealImgPath", path)));
            }

            return Xml(root);
        }

        private static void WriteDigimonImgPaths(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "DigimonImgPaths",
                    "DigimonImgPaths.xml");

            List<XElement> rows =
                root.Elements("DigimonImgPath").ToList();

            if (rows.Count == 0)
            {
                throw new InvalidDataException(
                    "DigimonImgPaths.xml: é necessária pelo menos 1 entrada " +
                    "porque o header/sentinel está armazenado na primeira.");
            }

            bw.Write(checked(rows.Count + 1));

            XElement first = rows[0];

            bw.Write(
                RequiredInt(
                    first,
                    "nullp",
                    "DigimonImgPaths first row"));

            bw.Write(
                RequiredInt(
                    first,
                    "unknow",
                    "DigimonImgPaths first row"));

            bw.Write(
                RequiredByte(
                    first,
                    "unknow1",
                    "DigimonImgPaths first row"));

            for (int i = 0; i < rows.Count; i++)
            {
                XElement row = rows[i];

                if (i > 0)
                {
                    int nullp =
                        RequiredInt(row, "nullp", $"DigimonImgPath[{i}]");

                    int unknown =
                        RequiredInt(row, "unknow", $"DigimonImgPath[{i}]");

                    byte unknown1 =
                        RequiredByte(row, "unknow1", $"DigimonImgPath[{i}]");

                    if (nullp != 0 || unknown != 0 || unknown1 != 0)
                    {
                        throw new InvalidDataException(
                            $"DigimonImgPath[{i}]: nullp/unknow/unknow1 só têm " +
                            "bytes físicos no header associado à primeira entrada. " +
                            "Nas restantes entradas devem permanecer 0.");
                    }
                }

                uint id =
                    RequiredUInt(
                        row,
                        "ID",
                        $"DigimonImgPath[{i}]");

                string path =
                    RequiredText(
                        row,
                        "s_szDigimonSealImgPath",
                        $"DigimonImgPath ID={id}",
                        true);

                byte[] raw =
                    Cp949.GetBytes(path);

                bw.Write(id);
                bw.Write(raw.Length);
                bw.Write(raw);
            }
        }

        // ============================================================
        // PLATE PATHS
        // ============================================================

        private static XDocument ReadPlatePaths(BinaryReader br)
        {
            int count =
                ReadCount(br, "PlatePaths.Count", 100_000);

            XElement root = new("PlatePaths");

            for (int i = 0; i < count; i++)
            {
                uint id = br.ReadUInt32();

                string name =
                    ReadDynamicUnicode(
                        br,
                        $"PlatePath ID={id}.Name");

                string nif =
                    ReadDynamicCp949(
                        br,
                        $"PlatePath ID={id}.Nif");

                string background =
                    ReadDynamicCp949(
                        br,
                        $"PlatePath ID={id}.Background");

                root.Add(
                    new XElement(
                        "PlatePath",
                        new XElement("ID", id),
                        new XElement("s_szName", name),
                        new XElement("s_szNifFilePath", nif),
                        new XElement("s_szGradeBackImagePath", background)));
            }

            return Xml(root);
        }

        private static void WritePlatePaths(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "PlatePaths", "PlatePaths.xml");

            List<XElement> rows =
                root.Elements("PlatePath").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(row, "ID", "PlatePaths.xml");

                bw.Write(id);

                WriteDynamicUnicode(
                    bw,
                    RequiredText(
                        row,
                        "s_szName",
                        $"PlatePath ID={id}",
                        true));

                WriteDynamicCp949(
                    bw,
                    RequiredText(
                        row,
                        "s_szNifFilePath",
                        $"PlatePath ID={id}",
                        true));

                WriteDynamicCp949(
                    bw,
                    RequiredText(
                        row,
                        "s_szGradeBackImagePath",
                        $"PlatePath ID={id}",
                        true));
            }
        }

        // ============================================================
        // ELEMENTALS / ATTRIBUTES
        // ============================================================

        private static XDocument ReadPathTable(
            BinaryReader br,
            string rootName,
            string childName)
        {
            int count =
                ReadCount(
                    br,
                    $"{rootName}.Count",
                    100_000);

            XElement root = new(rootName);

            for (int i = 0; i < count; i++)
            {
                uint type = br.ReadUInt32();

                string path =
                    ReadDynamicCp949(
                        br,
                        $"{rootName}[{i}].Path");

                root.Add(
                    new XElement(
                        childName,
                        new XElement("s_nType", type),
                        new XElement("s_nFilePath", path)));
            }

            return Xml(root);
        }

        private static void WritePathTable(
            BinaryWriter bw,
            XDocument doc,
            string rootName,
            string childName)
        {
            XElement root =
                RequireRoot(doc, rootName, rootName + ".xml");

            List<XElement> rows =
                root.Elements(childName).ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint type =
                    RequiredUInt(
                        row,
                        "s_nType",
                        rootName + ".xml");

                bw.Write(type);

                WriteDynamicCp949(
                    bw,
                    RequiredText(
                        row,
                        "s_nFilePath",
                        $"{rootName} Type={type}",
                        true));
            }
        }

        // ============================================================
        // UNKNOW INFORMATIONS
        //
        // Semântica confirmada pelo utilizador:
        // unknow  = AreaMapID
        // unknow1 = MapID
        // unknow2 = Seal identifier; no XML fornecido corresponde
        //           a MasterCards/Card/s_nScale.
        // ============================================================

        private static XDocument ReadUnknownInformations(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "UnknowInformations.Count",
                    1_000_000);

            XElement root =
                new("UnknowInformations");

            for (int i = 0; i < count; i++)
            {
                root.Add(
                    new XElement(
                        "UnknowInformation",
                        new XElement("unknow", br.ReadUInt32()),
                        new XElement("unknow1", br.ReadUInt32()),
                        new XElement("unknow2", br.ReadUInt32())));
            }

            return Xml(root);
        }

        private static void WriteUnknownInformations(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "UnknowInformations",
                    "UnknowInformations.xml");

            List<XElement> rows =
                root.Elements("UnknowInformation").ToList();

            bw.Write(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                XElement row = rows[i];

                bw.Write(
                    RequiredUInt(
                        row,
                        "unknow",
                        $"UnknowInformation[{i}] AreaMapID"));

                bw.Write(
                    RequiredUInt(
                        row,
                        "unknow1",
                        $"UnknowInformation[{i}] MapID"));

                bw.Write(
                    RequiredUInt(
                        row,
                        "unknow2",
                        $"UnknowInformation[{i}] SealID/s_nScale"));
            }
        }

        private static void ValidateUnknownSealReferences(
            XDocument cards,
            XDocument unknown)
        {
            XElement cardRoot =
                RequireRoot(
                    cards,
                    "MasterCards",
                    "MasterCards.xml");

            var scales =
                new HashSet<uint>(
                    cardRoot
                        .Elements("Card")
                        .Select(
                            c => RequiredUInt(
                                c,
                                "s_nScale",
                                "MasterCards.xml")));

            XElement unknownRoot =
                RequireRoot(
                    unknown,
                    "UnknowInformations",
                    "UnknowInformations.xml");

            int index = 0;

            foreach (XElement row in
                unknownRoot.Elements("UnknowInformation"))
            {
                uint seal =
                    RequiredUInt(
                        row,
                        "unknow2",
                        $"UnknowInformation[{index}]");

                if (!scales.Contains(seal))
                {
                    uint area =
                        RequiredUInt(
                            row,
                            "unknow",
                            $"UnknowInformation[{index}]");

                    uint map =
                        RequiredUInt(
                            row,
                            "unknow1",
                            $"UnknowInformation[{index}]");

                    throw new InvalidDataException(
                        $"UnknowInformations.xml [{index}]: " +
                        $"AreaMapID={area}, MapID={map}, SealID={seal}. " +
                        $"O valor <unknow2>{seal}</unknow2> não existe em " +
                        $"MasterCards.xml como <s_nScale>. " +
                        "Confirma se a Seal foi removida/renumerada ou corrige " +
                        "a referência de obtenção.");
                }

                index++;
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            byte[] raw =
                ReadExact(
                    br,
                    checked(wcharCount * 2),
                    "Fixed UTF-16LE string");

            string value =
                Encoding.Unicode.GetString(raw);

            int zero = value.IndexOf('\0');

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
                Encoding.Unicode.GetBytes(
                    value ?? string.Empty);

            int maxBytes =
                checked(wcharCount * 2);

            if (raw.Length > maxBytes)
            {
                throw new InvalidDataException(
                    $"{field}: texto ocupa {raw.Length:N0} bytes UTF-16LE; " +
                    $"buffer disponível={maxBytes:N0} bytes " +
                    $"(wchar[{wcharCount}]). Reduz o texto.");
            }

            byte[] buffer =
                new byte[maxBytes];

            Buffer.BlockCopy(
                raw,
                0,
                buffer,
                0,
                raw.Length);

            bw.Write(buffer);
        }

        private static string ReadDynamicUnicode(
            BinaryReader br,
            string field)
        {
            int charCount =
                ReadCount(
                    br,
                    field + ".Length",
                    10_000_000);

            byte[] raw =
                ReadExact(
                    br,
                    checked(charCount * 2),
                    field);

            return Encoding.Unicode.GetString(raw);
        }

        private static void WriteDynamicUnicode(
            BinaryWriter bw,
            string value)
        {
            string text = value ?? string.Empty;

            bw.Write(text.Length);
            bw.Write(
                Encoding.Unicode.GetBytes(text));
        }

        private static string ReadDynamicCp949(
            BinaryReader br,
            string field)
        {
            int byteCount =
                ReadCount(
                    br,
                    field + ".Length",
                    10_000_000);

            return Cp949.GetString(
                ReadExact(
                    br,
                    byteCount,
                    field));
        }

        private static void WriteDynamicCp949(
            BinaryWriter bw,
            string value)
        {
            byte[] raw =
                Cp949.GetBytes(
                    value ?? string.Empty);

            bw.Write(raw.Length);
            bw.Write(raw);
        }

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] raw = br.ReadBytes(count);

            if (raw.Length != count)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {count:N0} bytes; " +
                    $"recebidos {raw.Length:N0}.");
            }

            return raw;
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
            string context)
        {
            string value =
                RequiredText(parent, name, context);

            if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é Int32 válido.");
            }

            return result;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(parent, name, context);

            if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é UInt32 válido.");
            }

            return result;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(parent, name, context);

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
            string value =
                RequiredText(parent, name, context);

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

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration(
                    "1.0",
                    "utf-8",
                    null),
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
