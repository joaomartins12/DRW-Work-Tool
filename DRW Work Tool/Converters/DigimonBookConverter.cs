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
    public sealed class DigimonBookConverter : IGameDataConverter
    {
        public string Name => "Digimon_Book";

        private const int BookInfoRecordSize = 1160;
        private const int ExceptionRecordSize = 132;
        private const int DeckOptionRecordSize = 1204;
        private const int DeckDigimonRecordSize = 268;

        private const int NameChars = 64;
        private const int ExplainChars = 512;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Digimon_Book", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath)
        {
            string stem = Path.GetFileNameWithoutExtension(filePath);

            return stem.Equals("BookInfo", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("EncyclopediaException", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("DeckOption", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("DeckComposition", StringComparison.OrdinalIgnoreCase);
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\Digimon_Book.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            long start = ms.Position;
            XDocument bookInfo = ReadBookInfo(br);
            long bookEnd = ms.Position;

            start = ms.Position;
            XDocument exceptions = ReadExceptions(br);
            long exceptionEnd = ms.Position;

            start = ms.Position;
            XDocument deckOptions = ReadDeckOptions(br);
            long optionEnd = ms.Position;

            start = ms.Position;
            XDocument deckCompositions = ReadDeckCompositions(br);
            long compositionEnd = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Digimon_Book.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou em {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            ValidateCrossReferences(deckOptions, deckCompositions);

            SaveXml(
                bookInfo,
                Path.Combine(folder, "BookInfo.xml"));

            SaveXml(
                exceptions,
                Path.Combine(folder, "EncyclopediaException.xml"));

            SaveXml(
                deckOptions,
                Path.Combine(folder, "DeckOption.xml"));

            SaveXml(
                deckCompositions,
                Path.Combine(folder, "DeckComposition.xml"));

            AppLogger.Log(
                "Digimon_Book: BIN -> XML concluído. 4 XMLs gerados.");

            AppLogger.Log(
                $"Digimon_Book: secções em bytes -> " +
                $"BookInfo={bookEnd:N0}, " +
                $"EncyclopediaException={exceptionEnd - bookEnd:N0}, " +
                $"DeckOption={optionEnd - exceptionEnd:N0}, " +
                $"DeckComposition={compositionEnd - optionEnd:N0}.");

            AppLogger.Log(
                $"Digimon_Book: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\Digimon_Book.");

            string bookInfoPath =
                Path.Combine(folder, "BookInfo.xml");

            string exceptionPath =
                Path.Combine(folder, "EncyclopediaException.xml");

            string deckOptionPath =
                Path.Combine(folder, "DeckOption.xml");

            string deckCompositionPath =
                Path.Combine(folder, "DeckComposition.xml");

            string[] required =
            {
                bookInfoPath,
                exceptionPath,
                deckOptionPath,
                deckCompositionPath
            };

            foreach (string path in required)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Digimon_Book: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument bookInfo = LoadXml(bookInfoPath);
            XDocument exceptions = LoadXml(exceptionPath);
            XDocument deckOptions = LoadXml(deckOptionPath);
            XDocument deckCompositions = LoadXml(deckCompositionPath);

            ValidateCrossReferences(deckOptions, deckCompositions);

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteAll(
                    test,
                    bookInfo,
                    exceptions,
                    deckOptions,
                    deckCompositions);

                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Digimon_Book.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteAll(
                bw,
                bookInfo,
                exceptions,
                deckOptions,
                deckCompositions);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Digimon_Book.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            int bookCount =
                RequireRoot(bookInfo, "BookInfos", "BookInfo.xml")
                    .Elements("BookInfo")
                    .Count();

            int exceptionCount =
                RequireRoot(exceptions, "EncyclopediaExceptions", "EncyclopediaException.xml")
                    .Elements("EncyclopediaException")
                    .Count();

            int optionCount =
                RequireRoot(deckOptions, "DeckOptions", "DeckOption.xml")
                    .Elements("DeckOption")
                    .Count();

            int compositionCount =
                RequireRoot(deckCompositions, "DeckCompositions", "DeckComposition.xml")
                    .Elements("DeckComposition")
                    .Count();

            AppLogger.Log(
                $"Digimon_Book: XML -> BIN concluído. " +
                $"BookInfo={bookCount}, Exceptions={exceptionCount}, " +
                $"DeckOptions={optionCount}, DeckCompositions={compositionCount}.");

            AppLogger.Log(
                $"Digimon_Book: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteAll(
            BinaryWriter bw,
            XDocument bookInfo,
            XDocument exceptions,
            XDocument deckOptions,
            XDocument deckCompositions)
        {
            WriteBookInfo(bw, bookInfo);
            WriteExceptions(bw, exceptions);
            WriteDeckOptions(bw, deckOptions);
            WriteDeckCompositions(bw, deckCompositions);
        }

        // ============================================================
        // BOOK INFO
        // ============================================================

        private static XDocument ReadBookInfo(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "BookInfo.Count",
                    100_000);

            XElement root = new("BookInfos");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint optId = br.ReadUInt32();

                string name =
                    ReadFixedUnicode(
                        br,
                        NameChars,
                        $"BookInfo OptID={optId}.s_szOptName");

                ushort icon = br.ReadUInt16();

                string explain =
                    ReadFixedUnicode(
                        br,
                        ExplainChars,
                        $"BookInfo OptID={optId}.s_szOptExplain");

                ushort padding = br.ReadUInt16();

                if (padding != 0)
                {
                    throw new InvalidDataException(
                        $"BookInfo OptID={optId}: padding final={padding}; esperado=0.");
                }

                long consumed = br.BaseStream.Position - start;

                if (consumed != BookInfoRecordSize)
                {
                    throw new InvalidDataException(
                        $"BookInfo OptID={optId}: record ocupa {consumed:N0} bytes; " +
                        $"esperado={BookInfoRecordSize:N0}.");
                }

                root.Add(
                    new XElement(
                        "BookInfo",
                        new XElement("s_dwOptID", optId),
                        new XElement("s_szOptName", name),
                        new XElement("s_nIcon", icon),
                        new XElement("s_szOptExplain", explain)));
            }

            return Xml(root);
        }

        private static void WriteBookInfo(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "BookInfos",
                    "BookInfo.xml");

            List<XElement> rows =
                root.Elements("BookInfo").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(
                        row,
                        "s_dwOptID",
                        "BookInfo.xml");

                string context =
                    $"BookInfo OptID={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        row,
                        "s_szOptName",
                        context),
                    NameChars,
                    $"{context} <s_szOptName>");

                bw.Write(
                    RequiredUInt16(
                        row,
                        "s_nIcon",
                        context));

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        row,
                        "s_szOptExplain",
                        context),
                    ExplainChars,
                    $"{context} <s_szOptExplain>");

                bw.Write((ushort)0);

                long consumed = bw.BaseStream.Position - start;

                if (consumed != BookInfoRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={BookInfoRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // ENCYCLOPEDIA EXCEPTIONS
        // ============================================================

        private static XDocument ReadExceptions(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "EncyclopediaException.Count",
                    1_000_000);

            XElement root =
                new("EncyclopediaExceptions");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint digimonId = br.ReadUInt32();

                string name =
                    ReadFixedUnicode(
                        br,
                        NameChars,
                        $"EncyclopediaException DigimonID={digimonId}.s_szName");

                long consumed = br.BaseStream.Position - start;

                if (consumed != ExceptionRecordSize)
                {
                    throw new InvalidDataException(
                        $"EncyclopediaException DigimonID={digimonId}: " +
                        $"record ocupa {consumed:N0} bytes; " +
                        $"esperado={ExceptionRecordSize:N0}.");
                }

                root.Add(
                    new XElement(
                        "EncyclopediaException",
                        new XElement("s_dwDigimonID", digimonId),
                        new XElement("s_szName", name)));
            }

            return Xml(root);
        }

        private static void WriteExceptions(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "EncyclopediaExceptions",
                    "EncyclopediaException.xml");

            List<XElement> rows =
                root.Elements("EncyclopediaException").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(
                        row,
                        "s_dwDigimonID",
                        "EncyclopediaException.xml");

                string context =
                    $"EncyclopediaException DigimonID={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        row,
                        "s_szName",
                        context),
                    NameChars,
                    $"{context} <s_szName>");

                long consumed = bw.BaseStream.Position - start;

                if (consumed != ExceptionRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={ExceptionRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // DECK OPTIONS
        //
        // 1204 bytes fixos:
        // ushort GroupIdx
        // wchar[64] GroupName
        // wchar[512] Explain
        // ushort Reserved0
        // ushort Condition[3]
        // ushort AT_Type[3]
        // ushort Option[3]
        // ushort Val[3]
        // uint Prob[3]
        // uint Time[3]
        // ============================================================

        private static XDocument ReadDeckOptions(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "DeckOption.Count",
                    100_000);

            XElement root = new("DeckOptions");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                ushort groupIdx = br.ReadUInt16();

                string groupName =
                    ReadFixedUnicode(
                        br,
                        NameChars,
                        $"DeckOption Group={groupIdx}.Name");

                string explain =
                    ReadFixedUnicode(
                        br,
                        ExplainChars,
                        $"DeckOption Group={groupIdx}.Explain");

                ushort reserved = br.ReadUInt16();

                if (reserved != 0)
                {
                    throw new InvalidDataException(
                        $"DeckOption Group={groupIdx}: campo reservado={reserved}; esperado=0.");
                }

                ushort[] conditions = ReadUInt16Array(br, 3);
                ushort[] atTypes = ReadUInt16Array(br, 3);
                ushort[] options = ReadUInt16Array(br, 3);
                ushort[] values = ReadUInt16Array(br, 3);
                uint[] probabilities = ReadUInt32Array(br, 3);
                uint[] times = ReadUInt32Array(br, 3);

                long consumed = br.BaseStream.Position - start;

                if (consumed != DeckOptionRecordSize)
                {
                    throw new InvalidDataException(
                        $"DeckOption Group={groupIdx}: record ocupa {consumed:N0} bytes; " +
                        $"esperado={DeckOptionRecordSize:N0}.");
                }

                root.Add(
                    new XElement(
                        "DeckOption",
                        new XElement("s_nGroupIdx", groupIdx),
                        new XElement("s_szGroupName", groupName),
                        new XElement("s_szExplain", explain),
                        ArrayElement("s_nCondition", "condition", conditions),
                        ArrayElement("s_nAT_Type", "atType", atTypes),
                        ArrayElement("s_nOption", "option", options),
                        ArrayElement("s_nVal", "value", values),
                        ArrayElement("s_nProb", "prob", probabilities),
                        ArrayElement("s_nTime", "time", times)));
            }

            return Xml(root);
        }

        private static void WriteDeckOptions(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "DeckOptions",
                    "DeckOption.xml");

            List<XElement> rows =
                root.Elements("DeckOption").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort groupIdx =
                    RequiredUInt16(
                        row,
                        "s_nGroupIdx",
                        "DeckOption.xml");

                string context =
                    $"DeckOption Group={groupIdx}";

                long start = bw.BaseStream.Position;

                bw.Write(groupIdx);

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        row,
                        "s_szGroupName",
                        context),
                    NameChars,
                    $"{context} <s_szGroupName>");

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        row,
                        "s_szExplain",
                        context),
                    ExplainChars,
                    $"{context} <s_szExplain>");

                bw.Write((ushort)0);

                WriteUInt16Array(
                    bw,
                    RequireArray(
                        row,
                        "s_nCondition",
                        "condition",
                        context),
                    context + " s_nCondition");

                WriteUInt16Array(
                    bw,
                    RequireArray(
                        row,
                        "s_nAT_Type",
                        "atType",
                        context),
                    context + " s_nAT_Type");

                WriteUInt16Array(
                    bw,
                    RequireArray(
                        row,
                        "s_nOption",
                        "option",
                        context),
                    context + " s_nOption");

                WriteUInt16Array(
                    bw,
                    RequireArray(
                        row,
                        "s_nVal",
                        "value",
                        context),
                    context + " s_nVal");

                WriteUInt32Array(
                    bw,
                    RequireArray(
                        row,
                        "s_nProb",
                        "prob",
                        context),
                    context + " s_nProb");

                WriteUInt32Array(
                    bw,
                    RequireArray(
                        row,
                        "s_nTime",
                        "time",
                        context),
                    context + " s_nTime");

                long consumed = bw.BaseStream.Position - start;

                if (consumed != DeckOptionRecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={DeckOptionRecordSize:N0}.");
                }
            }
        }

        // ============================================================
        // DECK COMPOSITIONS
        //
        // Composition:
        // ushort GroupIdx
        // ushort DigimonCount (= s_nVal)
        //
        // DeckDigimon = 268 bytes:
        // uint BaseDigimonID
        // wchar[64] BaseName
        // ushort Reserved0
        // ushort Evolslot
        // uint DestDigimonID
        // wchar[64] DestName
        //
        // s_szGroupName do XML NÃO possui bytes físicos.
        // ============================================================

        private static XDocument ReadDeckCompositions(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "DeckComposition.Count",
                    100_000);

            XElement root =
                new("DeckCompositions");

            for (int i = 0; i < count; i++)
            {
                ushort groupIdx = br.ReadUInt16();
                ushort digimonCount = br.ReadUInt16();

                XElement composition =
                    new(
                        "DeckComposition",
                        new XElement("s_nGroupIdx", groupIdx),

                        // Não existe fisicamente no BIN.
                        new XElement("s_szGroupName", string.Empty),

                        new XElement("s_nVal", digimonCount));

                for (int d = 0; d < digimonCount; d++)
                {
                    long start = br.BaseStream.Position;

                    uint baseId = br.ReadUInt32();

                    string baseName =
                        ReadFixedUnicode(
                            br,
                            NameChars,
                            $"DeckComposition Group={groupIdx}, Digimon[{d}].BaseName");

                    ushort reserved = br.ReadUInt16();

                    if (reserved != 0)
                    {
                        throw new InvalidDataException(
                            $"DeckComposition Group={groupIdx}, Digimon[{d}]: " +
                            $"campo reservado={reserved}; esperado=0.");
                    }

                    ushort evolSlot = br.ReadUInt16();
                    uint destId = br.ReadUInt32();

                    string destName =
                        ReadFixedUnicode(
                            br,
                            NameChars,
                            $"DeckComposition Group={groupIdx}, Digimon[{d}].DestName");

                    long consumed = br.BaseStream.Position - start;

                    if (consumed != DeckDigimonRecordSize)
                    {
                        throw new InvalidDataException(
                            $"DeckComposition Group={groupIdx}, Digimon[{d}]: " +
                            $"record ocupa {consumed:N0} bytes; " +
                            $"esperado={DeckDigimonRecordSize:N0}.");
                    }

                    composition.Add(
                        new XElement(
                            "DeckDigimon",
                            new XElement("s_dwBaseDigimonID", baseId),
                            new XElement("s_szBaseDigimonName", baseName),
                            new XElement("s_nEvolslot", evolSlot),
                            new XElement("s_dwDestDigimonID", destId),
                            new XElement("s_szDestDigimonName", destName)));
                }

                root.Add(composition);
            }

            return Xml(root);
        }

        private static void WriteDeckCompositions(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "DeckCompositions",
                    "DeckComposition.xml");

            List<XElement> rows =
                root.Elements("DeckComposition").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort groupIdx =
                    RequiredUInt16(
                        row,
                        "s_nGroupIdx",
                        "DeckComposition.xml");

                string context =
                    $"DeckComposition Group={groupIdx}";

                string groupName =
                    RequiredTextAllowEmpty(
                        row,
                        "s_szGroupName",
                        context);

                if (!string.IsNullOrEmpty(groupName))
                {
                    throw new InvalidDataException(
                        $"{context}: <s_szGroupName>=\"{groupName}\" não pode ser " +
                        "gravado. Este campo existe no XML, mas NÃO possui bytes físicos " +
                        "no Digimon_Book.bin. Mantém <s_szGroupName /> vazio.");
                }

                List<XElement> digimons =
                    row.Elements("DeckDigimon").ToList();

                ushort declared =
                    RequiredUInt16(
                        row,
                        "s_nVal",
                        context);

                if (declared != digimons.Count)
                {
                    throw new InvalidDataException(
                        $"{context}: <s_nVal>={declared}, mas existem " +
                        $"{digimons.Count} <DeckDigimon>. " +
                        $"Corrige s_nVal para {digimons.Count} ou ajusta a composição.");
                }

                if (digimons.Count > ushort.MaxValue)
                {
                    throw new InvalidDataException(
                        $"{context}: {digimons.Count:N0} Digimon excedem o limite UInt16.");
                }

                bw.Write(groupIdx);
                bw.Write((ushort)digimons.Count);

                for (int d = 0; d < digimons.Count; d++)
                {
                    XElement digimon = digimons[d];

                    uint baseId =
                        RequiredUInt(
                            digimon,
                            "s_dwBaseDigimonID",
                            $"{context}, DeckDigimon[{d}]");

                    string dc =
                        $"{context}, BaseID={baseId}, DeckDigimon[{d}]";

                    long start = bw.BaseStream.Position;

                    bw.Write(baseId);

                    WriteFixedUnicode(
                        bw,
                        RequiredTextAllowEmpty(
                            digimon,
                            "s_szBaseDigimonName",
                            dc),
                        NameChars,
                        $"{dc} <s_szBaseDigimonName>");

                    bw.Write((ushort)0);

                    bw.Write(
                        RequiredUInt16(
                            digimon,
                            "s_nEvolslot",
                            dc));

                    bw.Write(
                        RequiredUInt(
                            digimon,
                            "s_dwDestDigimonID",
                            dc));

                    WriteFixedUnicode(
                        bw,
                        RequiredTextAllowEmpty(
                            digimon,
                            "s_szDestDigimonName",
                            dc),
                        NameChars,
                        $"{dc} <s_szDestDigimonName>");

                    long consumed = bw.BaseStream.Position - start;

                    if (consumed != DeckDigimonRecordSize)
                    {
                        throw new InvalidDataException(
                            $"{dc}: record gerado ocupa {consumed:N0} bytes; " +
                            $"esperado={DeckDigimonRecordSize:N0}.");
                    }
                }
            }
        }

        // ============================================================
        // CROSS-VALIDATION
        // ============================================================

        private static void ValidateCrossReferences(
            XDocument deckOptions,
            XDocument deckCompositions)
        {
            XElement optionRoot =
                RequireRoot(
                    deckOptions,
                    "DeckOptions",
                    "DeckOption.xml");

            XElement compositionRoot =
                RequireRoot(
                    deckCompositions,
                    "DeckCompositions",
                    "DeckComposition.xml");

            List<ushort> optionIds =
                optionRoot
                    .Elements("DeckOption")
                    .Select(
                        x => RequiredUInt16(
                            x,
                            "s_nGroupIdx",
                            "DeckOption.xml"))
                    .ToList();

            List<ushort> compositionIds =
                compositionRoot
                    .Elements("DeckComposition")
                    .Select(
                        x => RequiredUInt16(
                            x,
                            "s_nGroupIdx",
                            "DeckComposition.xml"))
                    .ToList();

            List<ushort> duplicateOptions =
                optionIds
                    .GroupBy(x => x)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

            if (duplicateOptions.Count > 0)
            {
                throw new InvalidDataException(
                    "DeckOption.xml contém s_nGroupIdx duplicados: " +
                    string.Join(", ", duplicateOptions) + ".");
            }

            List<ushort> duplicateCompositions =
                compositionIds
                    .GroupBy(x => x)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

            if (duplicateCompositions.Count > 0)
            {
                throw new InvalidDataException(
                    "DeckComposition.xml contém s_nGroupIdx duplicados: " +
                    string.Join(", ", duplicateCompositions) + ".");
            }

            List<ushort> missingOptions =
                compositionIds
                    .Except(optionIds)
                    .OrderBy(x => x)
                    .ToList();

            List<ushort> missingCompositions =
                optionIds
                    .Except(compositionIds)
                    .OrderBy(x => x)
                    .ToList();

            if (missingOptions.Count > 0 ||
                missingCompositions.Count > 0)
            {
                string a =
                    missingOptions.Count == 0
                        ? "nenhum"
                        : string.Join(", ", missingOptions);

                string b =
                    missingCompositions.Count == 0
                        ? "nenhum"
                        : string.Join(", ", missingCompositions);

                throw new InvalidDataException(
                    "Digimon_Book: DeckOption e DeckComposition não têm os mesmos grupos. " +
                    $"Composition sem Option=[{a}]. " +
                    $"Option sem Composition=[{b}]. " +
                    "Cada deck deve possuir configuração e composição com o mesmo s_nGroupIdx.");
            }
        }

        // ============================================================
        // ARRAYS
        // ============================================================

        private static ushort[] ReadUInt16Array(
            BinaryReader br,
            int count)
        {
            ushort[] values = new ushort[count];

            for (int i = 0; i < count; i++)
                values[i] = br.ReadUInt16();

            return values;
        }

        private static uint[] ReadUInt32Array(
            BinaryReader br,
            int count)
        {
            uint[] values = new uint[count];

            for (int i = 0; i < count; i++)
                values[i] = br.ReadUInt32();

            return values;
        }

        private static XElement ArrayElement<T>(
            string rootName,
            string childName,
            IEnumerable<T> values)
        {
            XElement root = new(rootName);

            foreach (T value in values)
                root.Add(new XElement(childName, value));

            return root;
        }

        private static List<XElement> RequireArray(
            XElement parent,
            string rootName,
            string childName,
            string context)
        {
            XElement? arrayRoot =
                parent.Element(rootName);

            if (arrayRoot == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta <{rootName}>.");
            }

            List<XElement> values =
                arrayRoot.Elements(childName).ToList();

            if (values.Count != 3)
            {
                throw new InvalidDataException(
                    $"{context}: <{rootName}> deve conter exatamente " +
                    $"3 <{childName}>; encontrados {values.Count}.");
            }

            return values;
        }

        private static void WriteUInt16Array(
            BinaryWriter bw,
            IReadOnlyList<XElement> values,
            string context)
        {
            for (int i = 0; i < values.Count; i++)
            {
                string raw = values[i].Value;

                if (!ushort.TryParse(
                    raw.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort value))
                {
                    throw new InvalidDataException(
                        $"{context}[{i}]='{raw}' não cabe em UInt16 (0..65535).");
                }

                bw.Write(value);
            }
        }

        private static void WriteUInt32Array(
            BinaryWriter bw,
            IReadOnlyList<XElement> values,
            string context)
        {
            for (int i = 0; i < values.Count; i++)
            {
                string raw = values[i].Value;

                if (!uint.TryParse(
                    raw.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint value))
                {
                    throw new InvalidDataException(
                        $"{context}[{i}]='{raw}' não é UInt32 válido.");
                }

                bw.Write(value);
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount,
            string field)
        {
            int byteCount =
                checked(wcharCount * 2);

            byte[] raw =
                br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: string UTF-16LE truncada. " +
                    $"Esperados={byteCount:N0} bytes, recebidos={raw.Length:N0}.");
            }

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
            string text = value ?? string.Empty;

            byte[] raw =
                Encoding.Unicode.GetBytes(text);

            int maxBytes =
                checked(wcharCount * 2);

            if (raw.Length > maxBytes)
            {
                throw new InvalidDataException(
                    $"{field}: texto demasiado longo. " +
                    $"Atual={text.Length:N0} caracteres/{raw.Length:N0} bytes UTF-16LE. " +
                    $"Máximo físico={wcharCount:N0} caracteres/{maxBytes:N0} bytes.");
            }

            byte[] buffer = new byte[maxBytes];

            Buffer.BlockCopy(
                raw,
                0,
                buffer,
                0,
                raw.Length);

            bw.Write(buffer);
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            if (br.BaseStream.Position + 4 > br.BaseStream.Length)
            {
                throw new EndOfStreamException(
                    $"{field}: faltam 4 bytes para ler o Count.");
            }

            int value = br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: Count inválido ({value}). " +
                    $"Esperado entre 0 e {max:N0}.");
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

            if (!root.Name.LocalName.Equals(
                expected,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{context}: root <{root.Name.LocalName}> inválido. " +
                    $"Esperado <{expected}>.");
            }

            return root;
        }

        private static string RequiredTextAllowEmpty(
            XElement parent,
            string name,
            string context)
        {
            XElement? element = parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>. " +
                    "O valor pode estar vazio, mas a tag tem de existir.");
            }

            return element.Value;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredTextAllowEmpty(
                    parent,
                    name,
                    context);

            if (!uint.TryParse(
                raw.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não é UInt32 válido.");
            }

            return value;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredTextAllowEmpty(
                    parent,
                    name,
                    context);

            if (!ushort.TryParse(
                raw.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não cabe em UInt16 (0..65535).");
            }

            return value;
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
                        OmitXmlDeclaration = false,
                        NewLineHandling = NewLineHandling.None
                    });

            document.Save(writer);
        }
    }
}
