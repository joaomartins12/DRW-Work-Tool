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
    public sealed class GotchaConverter : IGameDataConverter
    {
        public string Name => "Gotcha";

        private const int GotchaRecordSize = 36;
        private const int GotchaItemRecordSize = 64;
        private const int GotchaItemSlots = 10;

        private const int RareNameChars = 64;
        private const int RareRecordSize = 144;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Gotcha", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Gotcha", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Gotcha.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            long gotchaStart = ms.Position;
            XDocument gotchas = ReadGotchas(br);
            long gotchaEnd = ms.Position;

            long itemsStart = ms.Position;
            XDocument items = ReadGotchaItems(br);
            long itemsEnd = ms.Position;

            long rareStart = ms.Position;
            XDocument rare = ReadRareItems(br);
            long rareEnd = ms.Position;

            // Nesta versão do BIN estes dois blocos existem apenas como count=0.
            int mysteryItemCount =
                ReadCount(
                    br,
                    "GotchaMysteryItems.Count",
                    1_000_000);

            if (mysteryItemCount != 0)
            {
                throw new InvalidDataException(
                    $"GotchaMysteryItems possui {mysteryItemCount} records. " +
                    "Esta versão do converter só possui estrutura confirmada para count=0, " +
                    "porque o BIN/XML de referência não contém exemplos desta tabela.");
            }

            int mysteryCoinCount =
                ReadCount(
                    br,
                    "GotchaMysteryCoins.Count",
                    1_000_000);

            if (mysteryCoinCount != 0)
            {
                throw new InvalidDataException(
                    $"GotchaMysteryCoins possui {mysteryCoinCount} records. " +
                    "Esta versão do converter só possui estrutura confirmada para count=0, " +
                    "porque o BIN/XML de referência não contém exemplos desta tabela.");
            }

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Gotcha.bin contém {extra:N0} bytes extra. " +
                    $"Leitura terminou no offset {ms.Position:N0}, " +
                    $"ficheiro possui {ms.Length:N0} bytes.");
            }

            SaveXml(
                gotchas,
                Path.Combine(folder, "Gotcha.xml"));

            SaveXml(
                items,
                Path.Combine(folder, "GotchaItems.xml"));

            SaveXml(
                rare,
                Path.Combine(folder, "GotchaRareItems.xml"));

            SaveXml(
                Xml(new XElement("GotchaMysteryItems")),
                Path.Combine(folder, "GotchaMysteryItems.xml"));

            SaveXml(
                Xml(new XElement("GotchaMysteryCoins")),
                Path.Combine(folder, "GotchaMysteryCoins.xml"));

            AppLogger.Log(
                "Gotcha: BIN -> XML concluído. 5 XMLs gerados.");

            AppLogger.Log(
                $"Gotcha: secções em bytes -> " +
                $"Gotcha={gotchaEnd - gotchaStart:N0}, " +
                $"Items={itemsEnd - itemsStart:N0}, " +
                $"RareItems={rareEnd - rareStart:N0}, " +
                $"MysteryCounts=8.");

            AppLogger.Log(
                $"Gotcha: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Gotcha.");

            string gotchaPath =
                Path.Combine(folder, "Gotcha.xml");

            string itemsPath =
                Path.Combine(folder, "GotchaItems.xml");

            string rarePath =
                Path.Combine(folder, "GotchaRareItems.xml");

            string mysteryItemsPath =
                Path.Combine(folder, "GotchaMysteryItems.xml");

            string mysteryCoinsPath =
                Path.Combine(folder, "GotchaMysteryCoins.xml");

            string[] required =
            {
                gotchaPath,
                itemsPath,
                rarePath,
                mysteryItemsPath,
                mysteryCoinsPath
            };

            foreach (string path in required)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Gotcha: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument gotchas = LoadXml(gotchaPath);
            XDocument items = LoadXml(itemsPath);
            XDocument rare = LoadXml(rarePath);
            XDocument mysteryItems = LoadXml(mysteryItemsPath);
            XDocument mysteryCoins = LoadXml(mysteryCoinsPath);

            ValidateMysteryEmpty(
                mysteryItems,
                "GotchaMysteryItems",
                "GotchaMysteryItems.xml");

            ValidateMysteryEmpty(
                mysteryCoins,
                "GotchaMysteryCoins",
                "GotchaMysteryCoins.xml");

            long expectedSize =
                CalculateExpectedSize(
                    gotchas,
                    items,
                    rare);

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Gotcha."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteGotchas(bw, gotchas);
            WriteGotchaItems(bw, items);
            WriteRareItems(bw, rare);

            // Tabelas Mystery vazias nesta versão.
            bw.Write(0);
            bw.Write(0);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Gotcha.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "Gotcha: XML -> BIN concluído. 5 XMLs validados.");

            AppLogger.Log(
                $"Gotcha: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        // ============================================================
        // GOTCHA
        // ============================================================

        private static XDocument ReadGotchas(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "Gotcha.Count",
                    1_000_000);

            XElement root = new("Gotchas");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                XElement row =
                    new(
                        "Gotcha",
                        new XElement("s_dwNpc_Id", br.ReadUInt32()),
                        new XElement("s_dwUseItem_Code", br.ReadUInt32()),
                        new XElement("s_nUseItem_Cnt", br.ReadUInt16()),
                        new XElement("s_bLimit", br.ReadUInt16()),
                        new XElement("s_nStart_Date", br.ReadUInt32()),
                        new XElement("s_nEnd_Date", br.ReadUInt32()),
                        new XElement("s_nStart_Time", br.ReadUInt32()),
                        new XElement("s_nEnd_Time", br.ReadUInt32()),
                        new XElement("s_nMin_Lv", br.ReadUInt16()),
                        new XElement("s_nMax_Lv", br.ReadUInt16()),
                        new XElement("nRareItemCnt", br.ReadUInt32()));

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != GotchaRecordSize)
                {
                    throw new InvalidDataException(
                        $"Gotcha record #{i} ocupa {consumed} bytes; " +
                        $"esperado={GotchaRecordSize}.");
                }

                root.Add(row);
            }

            return Xml(root);
        }

        private static void WriteGotchas(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "Gotchas",
                    "Gotcha.xml");

            List<XElement> rows =
                root.Elements("Gotcha").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint npc =
                    RequiredUInt(
                        row,
                        "s_dwNpc_Id",
                        "Gotcha.xml");

                string ctx = $"Gotcha NpcID={npc}";

                bw.Write(npc);
                bw.Write(RequiredUInt(row, "s_dwUseItem_Code", ctx));
                bw.Write(RequiredUInt16(row, "s_nUseItem_Cnt", ctx));
                bw.Write(RequiredUInt16(row, "s_bLimit", ctx));
                bw.Write(RequiredUInt(row, "s_nStart_Date", ctx));
                bw.Write(RequiredUInt(row, "s_nEnd_Date", ctx));
                bw.Write(RequiredUInt(row, "s_nStart_Time", ctx));
                bw.Write(RequiredUInt(row, "s_nEnd_Time", ctx));
                bw.Write(RequiredUInt16(row, "s_nMin_Lv", ctx));
                bw.Write(RequiredUInt16(row, "s_nMax_Lv", ctx));
                bw.Write(RequiredUInt(row, "nRareItemCnt", ctx));
            }
        }

        // ============================================================
        // GOTCHA ITEMS
        // ============================================================

        private static XDocument ReadGotchaItems(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "GotchaItems.Count",
                    1_000_000);

            XElement root = new("GotchaItems");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                ushort groupId = br.ReadUInt16();
                ushort level = br.ReadUInt16();

                uint[] codes =
                    new uint[GotchaItemSlots];

                ushort[] counts =
                    new ushort[GotchaItemSlots];

                for (int j = 0; j < GotchaItemSlots; j++)
                    codes[j] = br.ReadUInt32();

                for (int j = 0; j < GotchaItemSlots; j++)
                    counts[j] = br.ReadUInt16();

                XElement itemCode =
                    new("ItemCode");

                for (int j = 0; j < GotchaItemSlots; j++)
                {
                    itemCode.Add(
                        new XElement(
                            "ItemCodeValue",
                            codes[j]));

                    itemCode.Add(
                        new XElement(
                            "itemCodeCount",
                            counts[j]));
                }

                root.Add(
                    new XElement(
                        "GotchaItem",
                        new XAttribute("Group_Id", groupId),
                        new XAttribute("Level", level),
                        itemCode));

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != GotchaItemRecordSize)
                {
                    throw new InvalidDataException(
                        $"GotchaItem record #{i} ocupa {consumed} bytes; " +
                        $"esperado={GotchaItemRecordSize}.");
                }
            }

            return Xml(root);
        }

        private static void WriteGotchaItems(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "GotchaItems",
                    "GotchaItems.xml");

            List<XElement> rows =
                root.Elements("GotchaItem").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                ushort groupId =
                    RequiredAttrUInt16(
                        row,
                        "Group_Id",
                        "GotchaItems.xml");

                ushort level =
                    RequiredAttrUInt16(
                        row,
                        "Level",
                        $"GotchaItems Group_Id={groupId}");

                XElement? container =
                    row.Element("ItemCode");

                if (container == null)
                {
                    throw new InvalidDataException(
                        $"GotchaItems Group_Id={groupId}: falta <ItemCode>.");
                }

                List<XElement> codeElements =
                    container.Elements("ItemCodeValue").ToList();

                List<XElement> countElements =
                    container.Elements("itemCodeCount").ToList();

                if (codeElements.Count != GotchaItemSlots ||
                    countElements.Count != GotchaItemSlots)
                {
                    throw new InvalidDataException(
                        $"GotchaItems Group_Id={groupId}: são obrigatórios exatamente " +
                        $"{GotchaItemSlots} <ItemCodeValue> e {GotchaItemSlots} <itemCodeCount>. " +
                        $"Encontrados Codes={codeElements.Count}, Counts={countElements.Count}.");
                }

                bw.Write(groupId);
                bw.Write(level);

                foreach (XElement code in codeElements)
                {
                    bw.Write(
                        ParseUInt(
                            code.Value,
                            $"GotchaItems Group_Id={groupId} <ItemCodeValue>"));
                }

                foreach (XElement count in countElements)
                {
                    bw.Write(
                        ParseUInt16(
                            count.Value,
                            $"GotchaItems Group_Id={groupId} <itemCodeCount>"));
                }
            }
        }

        // ============================================================
        // RARE ITEMS
        // ============================================================

        private static XDocument ReadRareItems(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "GotchaRareItems.Count",
                    1_000_000);

            XElement root =
                new("GotchaRareItems");

            for (int i = 0; i < count; i++)
            {
                long start =
                    br.BaseStream.Position;

                uint npcId =
                    br.ReadUInt32();

                string name =
                    ReadFixedUnicode(
                        br,
                        RareNameChars);

                uint rareItem =
                    br.ReadUInt32();

                uint rareItemCnt =
                    br.ReadUInt32();

                uint rareItemGive =
                    br.ReadUInt32();

                root.Add(
                    new XElement(
                        "GotchaRareItem",
                        new XAttribute("NpcID", npcId),
                        new XAttribute("SzNameRareItem", name),
                        new XAttribute("RareItem", rareItem),
                        new XAttribute("RareItemCnt", rareItemCnt),
                        new XAttribute("RareItemGive", rareItemGive)));

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != RareRecordSize)
                {
                    throw new InvalidDataException(
                        $"GotchaRareItem record #{i} ocupa {consumed} bytes; " +
                        $"esperado={RareRecordSize}.");
                }
            }

            return Xml(root);
        }

        private static void WriteRareItems(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "GotchaRareItems",
                    "GotchaRareItems.xml");

            List<XElement> rows =
                root.Elements("GotchaRareItem").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                uint npc =
                    RequiredAttrUInt(
                        row,
                        "NpcID",
                        "GotchaRareItems.xml");

                string name =
                    RequiredAttrText(
                        row,
                        "SzNameRareItem",
                        $"GotchaRareItem NpcID={npc}",
                        allowEmpty: true);

                bw.Write(npc);

                WriteFixedUnicode(
                    bw,
                    name,
                    RareNameChars,
                    $"GotchaRareItem NpcID={npc} <SzNameRareItem>");

                bw.Write(
                    RequiredAttrUInt(
                        row,
                        "RareItem",
                        $"GotchaRareItem NpcID={npc}"));

                bw.Write(
                    RequiredAttrUInt(
                        row,
                        "RareItemCnt",
                        $"GotchaRareItem NpcID={npc}"));

                bw.Write(
                    RequiredAttrUInt(
                        row,
                        "RareItemGive",
                        $"GotchaRareItem NpcID={npc}"));
            }
        }

        // ============================================================
        // MYSTERY TABLES
        // ============================================================

        private static void ValidateMysteryEmpty(
            XDocument doc,
            string rootName,
            string fileName)
        {
            XElement root =
                RequireRoot(
                    doc,
                    rootName,
                    fileName);

            if (root.Elements().Any())
            {
                throw new InvalidDataException(
                    $"{fileName}: esta tabela contém records, mas o BIN/XML de " +
                    "referência fornecido possui count=0. " +
                    "A estrutura de records desta tabela ainda não pode ser " +
                    "implementada com segurança sem uma amostra não-vazia.");
            }
        }

        // ============================================================
        // SIZE
        // ============================================================

        private static long CalculateExpectedSize(
            XDocument gotchas,
            XDocument items,
            XDocument rare)
        {
            XElement gotchaRoot =
                RequireRoot(
                    gotchas,
                    "Gotchas",
                    "Gotcha.xml");

            XElement itemRoot =
                RequireRoot(
                    items,
                    "GotchaItems",
                    "GotchaItems.xml");

            XElement rareRoot =
                RequireRoot(
                    rare,
                    "GotchaRareItems",
                    "GotchaRareItems.xml");

            long gotchaCount =
                gotchaRoot.Elements("Gotcha").LongCount();

            long itemCount =
                itemRoot.Elements("GotchaItem").LongCount();

            long rareCount =
                rareRoot.Elements("GotchaRareItem").LongCount();

            return
                4L + gotchaCount * GotchaRecordSize +
                4L + itemCount * GotchaItemRecordSize +
                4L + rareCount * RareRecordSize +
                4L + // MysteryItems Count
                4L;  // MysteryCoins Count
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value =
                br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max}.");
            }

            return value;
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            int byteCount =
                wcharCount * 2;

            byte[] raw =
                br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"Esperados {byteCount} bytes UTF-16LE, " +
                    $"recebidos {raw.Length}.");
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
                Encoding.Unicode.GetBytes(
                    value ?? string.Empty);

            int maxBytes =
                (wcharCount - 1) * 2;

            if (raw.Length > maxBytes)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes UTF-16LE. " +
                    $"O limite útil é {maxBytes} bytes " +
                    $"({wcharCount - 1} caracteres + terminador).");
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
            XElement? root =
                doc.Root;

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

            string value =
                element.Value;

            if (!allowEmpty &&
                string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> está vazio.");
            }

            return value;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context) =>
            ParseUInt(
                RequiredText(parent, name, context),
                $"{context} <{name}>");

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context) =>
            ParseUInt16(
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

        private static ushort ParseUInt16(
            string value,
            string context)
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

        private static string RequiredAttrText(
            XElement element,
            string name,
            string context,
            bool allowEmpty = false)
        {
            XAttribute? attr =
                element.Attribute(name);

            if (attr == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o atributo '{name}'.");
            }

            string value =
                attr.Value;

            if (!allowEmpty &&
                string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"{context}: atributo '{name}' está vazio.");
            }

            return value;
        }

        private static uint RequiredAttrUInt(
            XElement element,
            string name,
            string context) =>
            ParseUInt(
                RequiredAttrText(
                    element,
                    name,
                    context),
                $"{context} atributo {name}");

        private static ushort RequiredAttrUInt16(
            XElement element,
            string name,
            string context) =>
            ParseUInt16(
                RequiredAttrText(
                    element,
                    name,
                    context),
                $"{context} atributo {name}");

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
    }
}
