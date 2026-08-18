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
    public sealed class UITextConverter : IGameDataConverter
    {
        public string Name => "UIText";

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("UIText", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("UIText", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            int count =
                ReadCount(
                    br,
                    "UIText.Count",
                    10_000_000);

            XElement root =
                new("UITexts");

            long textBytes = 0;

            for (int i = 0; i < count; i++)
            {
                if (ms.Position + 12 > ms.Length)
                {
                    throw new EndOfStreamException(
                        $"UIText #{i}: BIN terminou antes do cabeçalho da entrada. " +
                        $"Offset atual={ms.Position:N0}, tamanho={ms.Length:N0}.");
                }

                uint id =
                    br.ReadUInt32();

                uint unknown =
                    br.ReadUInt32();

                int charCount =
                    br.ReadInt32();

                if (charCount < 0)
                {
                    throw new InvalidDataException(
                        $"UIText ID={id}: CharacterCount negativo ({charCount}).");
                }

                long byteCount =
                    checked((long)charCount * 2L);

                if (byteCount > int.MaxValue)
                {
                    throw new InvalidDataException(
                        $"UIText ID={id}: texto demasiado grande. " +
                        $"CharacterCount={charCount:N0}.");
                }

                byte[] raw =
                    br.ReadBytes((int)byteCount);

                if (raw.Length != (int)byteCount)
                {
                    throw new EndOfStreamException(
                        $"UIText ID={id}: texto UTF-16LE truncado. " +
                        $"Esperados={byteCount:N0} bytes, recebidos={raw.Length:N0}. " +
                        $"Offset={ms.Position:N0}.");
                }

                string text =
                    Encoding.Unicode.GetString(raw);

                textBytes += byteCount;

                // O XML legado não expõe este uint32.
                // Nesta amostra todos os 1.534 records têm valor 0.
                if (unknown != 0)
                {
                    throw new InvalidDataException(
                        $"UIText ID={id}: campo uint32 desconhecido possui valor " +
                        $"{unknown}, mas o UIText.xml não contém um elemento para " +
                        "preservá-lo. Esta versão de referência usa 0 em todas as entradas.");
                }

                root.Add(
                    new XElement(
                        "UIText",
                        new XElement("ID_Maybe", id),
                        new XElement("Text", text)));
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"UIText.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            string? folder =
                Path.GetDirectoryName(outputXml);

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidDataException(
                    "Não foi possível determinar XML\\UIText.");
            }

            Directory.CreateDirectory(folder);

            SaveXml(
                new XDocument(
                    new XDeclaration(
                        "1.0",
                        "utf-8",
                        null),
                    root),
                outputXml);

            AppLogger.Log(
                $"UIText: BIN -> XML concluído. " +
                $"{count:N0} textos exportados.");

            AppLogger.Log(
                $"UIText: bytes de texto UTF-16LE={textBytes:N0}. " +
                $"Estrutura dinâmica validada até EOF.");

            AppLogger.Log(
                $"UIText: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "UITexts",
                    "UIText.xml");

            List<XElement> rows =
                root.Elements("UIText").ToList();

            long expectedSize =
                CalculateExpectedSize(rows);

            // Validação completa antes de criar Output.
            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteTable(
                    test,
                    rows);

                test.Flush();

                if (testStream.Length != expectedSize)
                {
                    throw new InvalidDataException(
                        $"UIText: validação interna gerou " +
                        $"{testStream.Length:N0} bytes; " +
                        $"esperado={expectedSize:N0}.");
                }
            }

            string? outputFolder =
                Path.GetDirectoryName(outputBin);

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new InvalidDataException(
                    "Pasta Output inválida para UIText.");
            }

            Directory.CreateDirectory(outputFolder);

            using FileStream fs =
                File.Create(outputBin);

            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteTable(
                bw,
                rows);

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"UIText.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"UIText: XML -> BIN concluído. " +
                $"{rows.Count:N0} textos serializados.");

            AppLogger.Log(
                $"UIText: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static long CalculateExpectedSize(
            IReadOnlyList<XElement> rows)
        {
            long total = 4;

            for (int i = 0; i < rows.Count; i++)
            {
                XElement row =
                    rows[i];

                uint id =
                    RequiredUInt(
                        row,
                        "ID_Maybe",
                        $"UIText #{i}");

                string text =
                    RequiredTextAllowEmpty(
                        row,
                        "Text",
                        $"UIText ID={id}");

                // 4 ID + 4 unknown + 4 CharacterCount + UTF16 chars.
                total = checked(
                    total +
                    12L +
                    Encoding.Unicode.GetByteCount(text));
            }

            return total;
        }

        private static void WriteTable(
            BinaryWriter bw,
            IReadOnlyList<XElement> rows)
        {
            bw.Write(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                XElement row =
                    rows[i];

                uint id =
                    RequiredUInt(
                        row,
                        "ID_Maybe",
                        $"UIText #{i}");

                string context =
                    $"UIText ID={id}";

                string text =
                    RequiredTextAllowEmpty(
                        row,
                        "Text",
                        context);

                byte[] raw =
                    Encoding.Unicode.GetBytes(text);

                // CharacterCount, NÃO ByteCount.
                // Para strings .NET válidas, UTF-16LE ocupa 2 bytes por char.
                int charCount =
                    text.Length;

                if (raw.Length != checked(charCount * 2))
                {
                    throw new InvalidDataException(
                        $"{context}: inconsistência UTF-16LE. " +
                        $"Chars={charCount:N0}, bytes={raw.Length:N0}.");
                }

                bw.Write(id);

                // Campo físico presente em todos os records.
                // Na amostra fornecida é 0 em 1.534 / 1.534 entradas.
                bw.Write((uint)0);

                bw.Write(charCount);
                bw.Write(raw);
            }
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value =
                br.ReadInt32();

            if (value < 0 ||
                value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max:N0}.");
            }

            return value;
        }

        private static XDocument LoadXml(
            string path)
        {
            try
            {
                return XDocument.Load(
                    path,
                    LoadOptions.PreserveWhitespace |
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

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
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

            if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é UInt32 válido " +
                    "(0..4294967295).");
            }

            return result;
        }

        private static string RequiredTextAllowEmpty(
            XElement parent,
            string name,
            string context)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>. " +
                    "Textos vazios são permitidos, mas o elemento <Text> tem de existir.");
            }

            return element.Value;
        }

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
