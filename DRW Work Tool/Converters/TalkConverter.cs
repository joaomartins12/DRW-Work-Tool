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
    public sealed class TalkConverter : IGameDataConverter
    {
        public string Name => "Talk";

        private const int TalkDigimonRecordSize = 412;
        private const int TalkEventRecordSize = 408;
        private const int TalkMessageRecordSize = 560;
        private const int TalkTipRecordSize = 404;
        private const int TalkLoadingTipRecordSize = 408;

        private const int TalkDigimonTextChars = 100;
        private const int TalkDigimonListChars = 100;
        private const int TalkEventTextChars = 200;
        private const int TalkMessageTitleChars = 16;
        private const int TalkMessageBodyChars = 256;
        private const int TalkTipTextChars = 200;
        private const int TalkLoadingTipTextChars = 200;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Talk", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath)
        {
            string stem = Path.GetFileNameWithoutExtension(filePath);

            return stem.Equals("TalkDigimon", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("TalkEvent", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("TalkMessage", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("TalkTip", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("TalkLoadingTip", StringComparison.OrdinalIgnoreCase);
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Talk: não foi possível determinar XML\\Talk.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            long s0 = ms.Position;
            XDocument digimon = ReadTalkDigimon(br);
            long e0 = ms.Position;

            long s1 = ms.Position;
            XDocument events = ReadTalkEvent(br);
            long e1 = ms.Position;

            long s2 = ms.Position;
            XDocument messages = ReadTalkMessage(br);
            long e2 = ms.Position;

            long s3 = ms.Position;
            XDocument tips = ReadTalkTip(br);
            long e3 = ms.Position;

            long s4 = ms.Position;
            XDocument loadingTips = ReadTalkLoadingTip(br);
            long e4 = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Talk.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou em {ms.Position:N0}; tamanho total={ms.Length:N0}.");
            }

            SaveXml(digimon, Path.Combine(folder, "TalkDigimon.xml"));
            SaveXml(events, Path.Combine(folder, "TalkEvent.xml"));
            SaveXml(messages, Path.Combine(folder, "TalkMessage.xml"));
            SaveXml(tips, Path.Combine(folder, "TalkTip.xml"));
            SaveXml(loadingTips, Path.Combine(folder, "TalkLoadingTip.xml"));

            AppLogger.Log(
                "Talk: BIN -> XML concluído. 5 XMLs gerados.");

            AppLogger.Log(
                $"Talk: secções em bytes -> " +
                $"TalkDigimon={e0 - s0:N0}, " +
                $"TalkEvent={e1 - s1:N0}, " +
                $"TalkMessage={e2 - s2:N0}, " +
                $"TalkTip={e3 - s3:N0}, " +
                $"TalkLoadingTip={e4 - s4:N0}.");

            AppLogger.Log(
                $"Talk: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Directory.Exists(inputXml)
                    ? inputXml
                    : Path.GetDirectoryName(inputXml)
                        ?? throw new InvalidDataException(
                            "Talk: não foi possível determinar XML\\Talk.");

            string digimonPath = Path.Combine(folder, "TalkDigimon.xml");
            string eventPath = Path.Combine(folder, "TalkEvent.xml");
            string messagePath = Path.Combine(folder, "TalkMessage.xml");
            string tipPath = Path.Combine(folder, "TalkTip.xml");
            string loadingTipPath = Path.Combine(folder, "TalkLoadingTip.xml");

            string[] required =
            {
                digimonPath,
                eventPath,
                messagePath,
                tipPath,
                loadingTipPath
            };

            List<string> missing =
                required
                    .Where(path => !File.Exists(path))
                    .ToList();

            if (missing.Count > 0)
            {
                throw new FileNotFoundException(
                    "Talk: faltam XMLs obrigatórios em XML\\Talk:\n" +
                    string.Join(
                        "\n",
                        missing.Select(x => "- " + Path.GetFileName(x))) +
                    "\nSão necessários exatamente: TalkDigimon.xml, TalkEvent.xml, " +
                    "TalkMessage.xml, TalkTip.xml e TalkLoadingTip.xml.");
            }

            XDocument digimon = LoadXml(digimonPath);
            XDocument events = LoadXml(eventPath);
            XDocument messages = LoadXml(messagePath);
            XDocument tips = LoadXml(tipPath);
            XDocument loadingTips = LoadXml(loadingTipPath);

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteAll(
                    test,
                    digimon,
                    events,
                    messages,
                    tips,
                    loadingTips);

                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Talk: pasta Output inválida.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteAll(
                bw,
                digimon,
                events,
                messages,
                tips,
                loadingTips);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Talk.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "Talk: XML -> BIN concluído. 5 tabelas serializadas.");

            AppLogger.Log(
                $"Talk: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteAll(
            BinaryWriter bw,
            XDocument digimon,
            XDocument events,
            XDocument messages,
            XDocument tips,
            XDocument loadingTips)
        {
            WriteTalkDigimon(bw, digimon);
            WriteTalkEvent(bw, events);
            WriteTalkMessage(bw, messages);
            WriteTalkTip(bw, tips);
            WriteTalkLoadingTip(bw, loadingTips);
        }

        // ============================================================
        // TALK DIGIMON
        //
        // int32 Count
        //
        // Record 412 bytes:
        // uint32 Id
        // uint32 s_dwParam
        // uint16 s_nType
        // wchar_t s_szText[100]
        // wchar_t s_szList[100]
        // uint16 unknow
        // ============================================================

        private static XDocument ReadTalkDigimon(BinaryReader br)
        {
            int count =
                ReadCount(br, "TalkDigimon.Count", 1_000_000);

            XElement root = new("TalkDigimon");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();
                uint param = br.ReadUInt32();
                ushort type = br.ReadUInt16();

                string text =
                    ReadFixedUnicode(
                        br,
                        TalkDigimonTextChars,
                        $"TalkDigimon Id={id}.s_szText");

                string list =
                    ReadFixedUnicode(
                        br,
                        TalkDigimonListChars,
                        $"TalkDigimon Id={id}.s_szList");

                ushort unknown = br.ReadUInt16();

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    TalkDigimonRecordSize,
                    $"TalkDigimon Id={id}");

                root.Add(
                    new XElement(
                        "TalkDigimon",
                        new XElement("Id", id),
                        new XElement("s_dwParam", param),
                        new XElement("s_nType", type),
                        new XElement("s_szText", text),
                        new XElement("s_szList", list),
                        new XElement("unknow", unknown)));
            }

            return Xml(root);
        }

        private static void WriteTalkDigimon(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "TalkDigimon", "TalkDigimon.xml");

            List<XElement> rows =
                root.Elements("TalkDigimon").ToList();

            ValidateUniqueIds(rows, "Id", "TalkDigimon.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id = RequiredUInt(row, "Id", "TalkDigimon.xml");
                string context = $"TalkDigimon Id={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);
                bw.Write(RequiredUInt(row, "s_dwParam", context));
                bw.Write(RequiredUInt16(row, "s_nType", context));

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "s_szText", context),
                    TalkDigimonTextChars,
                    $"{context} <s_szText>");

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "s_szList", context),
                    TalkDigimonListChars,
                    $"{context} <s_szList>");

                bw.Write(RequiredUInt16(row, "unknow", context));

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    TalkDigimonRecordSize,
                    context);
            }
        }

        // ============================================================
        // TALK EVENT
        //
        // int32 Count
        //
        // Record 408 bytes:
        // uint32 Id
        // uint32 s_dwTalkNum
        // wchar_t s_szText[200]
        //
        // <unknow> existe no XML, mas NÃO possui bytes físicos.
        // ============================================================

        private static XDocument ReadTalkEvent(BinaryReader br)
        {
            int count =
                ReadCount(br, "TalkEvent.Count", 1_000_000);

            XElement root = new("TalkEvent");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();
                uint talkNum = br.ReadUInt32();

                string text =
                    ReadFixedUnicode(
                        br,
                        TalkEventTextChars,
                        $"TalkEvent Id={id}.s_szText");

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    TalkEventRecordSize,
                    $"TalkEvent Id={id}");

                root.Add(
                    new XElement(
                        "TalkEvent",
                        new XElement("Id", id),
                        new XElement("s_dwTalkNum", talkNum),
                        new XElement("s_szText", text),
                        new XElement("unknow", 0)));
            }

            return Xml(root);
        }

        private static void WriteTalkEvent(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "TalkEvent", "TalkEvent.xml");

            List<XElement> rows =
                root.Elements("TalkEvent").ToList();

            ValidateUniqueIds(rows, "Id", "TalkEvent.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id = RequiredUInt(row, "Id", "TalkEvent.xml");
                string context = $"TalkEvent Id={id}";

                uint unknown =
                    RequiredUInt(row, "unknow", context);

                if (unknown != 0)
                {
                    throw new InvalidDataException(
                        $"{context}: <unknow>={unknown}, mas este campo não possui " +
                        "armazenamento físico no Talk.bin analisado. " +
                        "Mantém <unknow>0</unknow>.");
                }

                long start = bw.BaseStream.Position;

                bw.Write(id);
                bw.Write(RequiredUInt(row, "s_dwTalkNum", context));

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "s_szText", context),
                    TalkEventTextChars,
                    $"{context} <s_szText>");

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    TalkEventRecordSize,
                    context);
            }
        }

        // ============================================================
        // TALK MESSAGE
        //
        // int32 Count
        //
        // Record 560 bytes:
        // uint32 s_dwID
        // uint32 s_MsgType
        // uint32 s_Type
        // wchar_t s_TitleName[16]
        // wchar_t s_Message[256]
        // uint32 s_dwLinkID
        // ============================================================

        private static XDocument ReadTalkMessage(BinaryReader br)
        {
            int count =
                ReadCount(br, "TalkMessage.Count", 10_000_000);

            XElement root = new("TalkMessage");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();
                uint msgType = br.ReadUInt32();
                uint type = br.ReadUInt32();

                string title =
                    ReadFixedUnicode(
                        br,
                        TalkMessageTitleChars,
                        $"TalkMessage ID={id}.s_TitleName");

                string message =
                    ReadFixedUnicode(
                        br,
                        TalkMessageBodyChars,
                        $"TalkMessage ID={id}.s_Message");

                uint linkId = br.ReadUInt32();

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    TalkMessageRecordSize,
                    $"TalkMessage ID={id}");

                root.Add(
                    new XElement(
                        "TalkMessage",
                        new XElement("s_dwID", id),
                        new XElement("s_MsgType", msgType),
                        new XElement("s_Type", type),
                        new XElement("s_TitleName", title),
                        new XElement("s_Message", message),
                        new XElement("s_dwLinkID", linkId)));
            }

            return Xml(root);
        }

        private static void WriteTalkMessage(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "TalkMessage", "TalkMessage.xml");

            List<XElement> rows =
                root.Elements("TalkMessage").ToList();

            ValidateUniqueIds(rows, "s_dwID", "TalkMessage.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(row, "s_dwID", "TalkMessage.xml");

                string context = $"TalkMessage ID={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);
                bw.Write(RequiredUInt(row, "s_MsgType", context));
                bw.Write(RequiredUInt(row, "s_Type", context));

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "s_TitleName", context),
                    TalkMessageTitleChars,
                    $"{context} <s_TitleName>");

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "s_Message", context),
                    TalkMessageBodyChars,
                    $"{context} <s_Message>");

                bw.Write(RequiredUInt(row, "s_dwLinkID", context));

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    TalkMessageRecordSize,
                    context);
            }
        }

        // ============================================================
        // TALK TIP
        //
        // int32 Count
        //
        // Record 404 bytes:
        // uint32 Id
        // wchar_t s_szTip[200]
        // ============================================================

        private static XDocument ReadTalkTip(BinaryReader br)
        {
            int count =
                ReadCount(br, "TalkTip.Count", 1_000_000);

            XElement root = new("TalkTip");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();

                string text =
                    ReadFixedUnicode(
                        br,
                        TalkTipTextChars,
                        $"TalkTip Id={id}.s_szTip");

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    TalkTipRecordSize,
                    $"TalkTip Id={id}");

                root.Add(
                    new XElement(
                        "TalkTip",
                        new XElement("Id", id),
                        new XElement("s_szTip", text)));
            }

            return Xml(root);
        }

        private static void WriteTalkTip(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(doc, "TalkTip", "TalkTip.xml");

            List<XElement> rows =
                root.Elements("TalkTip").ToList();

            ValidateUniqueIds(rows, "Id", "TalkTip.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id = RequiredUInt(row, "Id", "TalkTip.xml");
                string context = $"TalkTip Id={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(row, "s_szTip", context),
                    TalkTipTextChars,
                    $"{context} <s_szTip>");

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    TalkTipRecordSize,
                    context);
            }
        }

        // ============================================================
        // TALK LOADING TIP
        //
        // int32 Count
        //
        // Record 408 bytes:
        // uint32 Id
        // wchar_t s_szLoadingTip[200]
        // uint32 s_nLevel
        // ============================================================

        private static XDocument ReadTalkLoadingTip(BinaryReader br)
        {
            int count =
                ReadCount(br, "TalkLoadingTip.Count", 1_000_000);

            XElement root = new("TalkLoadingTip");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                uint id = br.ReadUInt32();

                string text =
                    ReadFixedUnicode(
                        br,
                        TalkLoadingTipTextChars,
                        $"TalkLoadingTip Id={id}.s_szLoadingTip");

                uint level = br.ReadUInt32();

                ValidateRecordSize(
                    br.BaseStream.Position - start,
                    TalkLoadingTipRecordSize,
                    $"TalkLoadingTip Id={id}");

                root.Add(
                    new XElement(
                        "TalkLoadingTip",
                        new XElement("Id", id),
                        new XElement("s_szLoadingTip", text),
                        new XElement("s_nLevel", level)));
            }

            return Xml(root);
        }

        private static void WriteTalkLoadingTip(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "TalkLoadingTip",
                    "TalkLoadingTip.xml");

            List<XElement> rows =
                root.Elements("TalkLoadingTip").ToList();

            ValidateUniqueIds(rows, "Id", "TalkLoadingTip.xml");

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint id =
                    RequiredUInt(
                        row,
                        "Id",
                        "TalkLoadingTip.xml");

                string context =
                    $"TalkLoadingTip Id={id}";

                long start = bw.BaseStream.Position;

                bw.Write(id);

                WriteFixedUnicode(
                    bw,
                    RequiredTextAllowEmpty(
                        row,
                        "s_szLoadingTip",
                        context),
                    TalkLoadingTipTextChars,
                    $"{context} <s_szLoadingTip>");

                bw.Write(
                    RequiredUInt(
                        row,
                        "s_nLevel",
                        context));

                ValidateRecordSize(
                    bw.BaseStream.Position - start,
                    TalkLoadingTipRecordSize,
                    context);
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static void ValidateUniqueIds(
            IReadOnlyList<XElement> rows,
            string idField,
            string fileName)
        {
            Dictionary<uint, int> seen = new();

            for (int i = 0; i < rows.Count; i++)
            {
                uint id =
                    RequiredUInt(
                        rows[i],
                        idField,
                        $"{fileName} row #{i + 1}");

                if (seen.TryGetValue(id, out int previous))
                {
                    throw new InvalidDataException(
                        $"{fileName}: ID duplicado {id}. " +
                        $"Aparece nas entradas #{previous + 1} e #{i + 1}. " +
                        "Cada ID deve ser único dentro da tabela.");
                }

                seen[id] = i;
            }
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount,
            string field)
        {
            int byteCount = checked(wcharCount * 2);

            byte[] raw = br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: texto UTF-16LE truncado. " +
                    $"Esperados={byteCount:N0} bytes; recebidos={raw.Length:N0}. " +
                    $"Offset atual={br.BaseStream.Position:N0}.");
            }

            string text =
                Encoding.Unicode.GetString(raw);

            int zero = text.IndexOf('\0');

            return zero >= 0
                ? text[..zero]
                : text;
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

            int maxBytes = checked(wcharCount * 2);

            if (raw.Length > maxBytes)
            {
                int overChars =
                    Math.Max(1, text.Length - wcharCount);

                throw new InvalidDataException(
                    $"{field}: texto excede o buffer físico do client. " +
                    $"Atual={text.Length:N0} caracteres / {raw.Length:N0} bytes UTF-16LE. " +
                    $"Máximo={wcharCount:N0} caracteres / {maxBytes:N0} bytes. " +
                    $"Reduz pelo menos {overChars:N0} caractere(s).");
            }

            bw.Write(raw);

            int padding = maxBytes - raw.Length;

            if (padding > 0)
                bw.Write(new byte[padding]);
        }

        private static void ValidateRecordSize(
            long actual,
            int expected,
            string context)
        {
            if (actual != expected)
            {
                throw new InvalidDataException(
                    $"{context}: record ocupa {actual:N0} bytes; " +
                    $"esperado={expected:N0}. " +
                    $"Diferença={(actual - expected):+#;-#;0} bytes.");
            }
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
                    LoadOptions.SetLineInfo |
                    LoadOptions.PreserveWhitespace);
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
                    "O texto pode estar vazio, mas a tag tem de existir.");
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
