using DRW_Work_Tool.Core;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class BuffConverter : IGameDataConverter
    {
        public string Name => "Buff";

        private const int RecordSize = 476;

        private const int NameChars = 64;       // wchar_t[64] = 128 bytes
        private const int CommentChars = 128;   // wchar_t[128] = 256 bytes
        private const int EffectFileBytes = 64; // char[64], CP949

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Buff", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Buff", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            if (data.Length < 4)
                throw new InvalidDataException("Buff.bin demasiado pequeno.");

            int count = BitConverter.ToInt32(data, 0);

            if (count < 0)
                throw new InvalidDataException($"Buff count inválido: {count}.");

            long expectedSize = 4L + (long)count * RecordSize;

            if (data.Length != expectedSize)
            {
                throw new InvalidDataException(
                    $"Tamanho Buff.bin inválido. " +
                    $"Atual={data.Length} bytes, Esperado={expectedSize} bytes, " +
                    $"Buffs={count}.");
            }

            XElement root = new("BuffDataArray");

            int offset = 4;

            for (int i = 0; i < count; i++)
            {
                int recordStart = offset;

                ushort id = ReadUInt16(data, offset);
                offset += 2;

                string name = ReadWideBuffer(data, offset, NameChars);
                offset += NameChars * 2;

                string comment = ReadWideBuffer(data, offset, CommentChars);
                offset += CommentChars * 2;

                ushort buffIcon = ReadUInt16(data, offset);
                offset += 2;

                ushort buffType = ReadUInt16(data, offset);
                offset += 2;

                ushort buffLifeType = ReadUInt16(data, offset);
                offset += 2;

                ushort buffTimeType = ReadUInt16(data, offset);
                offset += 2;

                ushort minLv = ReadUInt16(data, offset);
                offset += 2;

                ushort buffClass = ReadUInt16(data, offset);
                offset += 2;

                ushort unknown = ReadUInt16(data, offset);
                offset += 2;

                uint skillCode = ReadUInt32(data, offset);
                offset += 4;

                uint digimonSkillCode = ReadUInt32(data, offset);
                offset += 4;

                byte delete = data[offset++];

                string effectFile = ReadAnsiBuffer(
                    data,
                    offset,
                    EffectFileBytes);

                offset += EffectFileBytes;

                ushort conditionLv = ReadUInt16(data, offset);
                offset += 2;

                byte u = data[offset++];

                if (offset - recordStart != RecordSize)
                {
                    throw new InvalidDataException(
                        $"Erro interno no Buff #{i}: " +
                        $"{offset - recordStart} bytes em vez de {RecordSize}.");
                }

                root.Add(
                    new XElement("BuffData",
                        new XElement("s_dwID", id),
                        new XElement("s_szName", name),
                        new XElement("s_szComment", comment),
                        new XElement("s_nBuffIcon", buffIcon),
                        new XElement("s_nBuffType", buffType),
                        new XElement("s_nBuffLifeType", buffLifeType),
                        new XElement("s_nBuffTimeType", buffTimeType),
                        new XElement("s_nMinLv", minLv),
                        new XElement("s_nBuffClass", buffClass),
                        new XElement("unknow", unknown),
                        new XElement("s_dwSkillCode", skillCode),
                        new XElement("s_dwDigimonSkillCode", digimonSkillCode),
                        new XElement("s_bDelete", delete),
                        new XElement("s_szEffectFile", effectFile),
                        new XElement("s_nConditionLv", conditionLv),
                        new XElement("u", u)));
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidOperationException("Pasta XML inválida."));

            SaveXml(
                new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root),
                outputXml);

            AppLogger.Log(
                $"Buff: BIN -> XML concluído. Buffs={count}.");

            AppLogger.Log(
                $"Buff: tamanho BIN verificado: " +
                $"{data.Length} / {expectedSize} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc = XDocument.Load(inputXml);

            XElement root =
                doc.Root
                ?? throw new InvalidDataException("Buff.xml sem root.");

            if (root.Name.LocalName != "BuffDataArray")
            {
                throw new InvalidDataException(
                    $"Root inválido em Buff.xml: '{root.Name.LocalName}'.");
            }

            var rows = root.Elements("BuffData").ToList();

            long expectedSize =
                4L + (long)rows.Count * RecordSize;

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidOperationException("Output inválido."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs);

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_dwID"))));

                WriteWideBuffer(
                    bw,
                    Value(row, "s_szName", string.Empty),
                    NameChars);

                WriteWideBuffer(
                    bw,
                    Value(row, "s_szComment", string.Empty),
                    CommentChars);

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nBuffIcon"))));

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nBuffType"))));

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nBuffLifeType"))));

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nBuffTimeType"))));

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nMinLv"))));

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nBuffClass"))));

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "unknow"))));

                bw.Write(ParseUInt(Value(row, "s_dwSkillCode")));

                bw.Write(ParseUInt(Value(row, "s_dwDigimonSkillCode")));

                bw.Write(checked(
                    (byte)ParseUInt(Value(row, "s_bDelete"))));

                WriteAnsiBuffer(
                    bw,
                    Value(row, "s_szEffectFile", string.Empty),
                    EffectFileBytes);

                bw.Write(checked(
                    (ushort)ParseUInt(Value(row, "s_nConditionLv"))));

                bw.Write(checked(
                    (byte)ParseUInt(Value(row, "u"))));
            }

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Buff.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize}, Esperado={expectedSize}.");
            }

            AppLogger.Log(
                $"Buff: XML -> BIN concluído. Buffs={rows.Count}.");

            AppLogger.Log(
                $"Buff: tamanho BIN gerado: " +
                $"{actualSize} bytes. Esperado={expectedSize} bytes (OK).");
        }

        private static string Value(
            XElement element,
            string name,
            string defaultValue = "0")
        {
            return element.Element(name)?.Value ?? defaultValue;
        }

        private static uint ParseUInt(string value)
        {
            return uint.Parse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
        }

        private static ushort ReadUInt16(byte[] data, int offset) =>
            BitConverter.ToUInt16(data, offset);

        private static uint ReadUInt32(byte[] data, int offset) =>
            BitConverter.ToUInt32(data, offset);

        private static string ReadWideBuffer(
            byte[] data,
            int offset,
            int wcharCount)
        {
            string value =
                Encoding.Unicode.GetString(
                    data,
                    offset,
                    wcharCount * 2);

            int zero = value.IndexOf('\0');

            return zero >= 0
                ? value[..zero]
                : value;
        }

        private static void WriteWideBuffer(
            BinaryWriter bw,
            string text,
            int wcharCount)
        {
            byte[] buffer = new byte[wcharCount * 2];

            byte[] raw =
                Encoding.Unicode.GetBytes(text ?? string.Empty);

            int maxBytes = (wcharCount - 1) * 2;
            int copy = Math.Min(raw.Length, maxBytes);

            Buffer.BlockCopy(
                raw, 0,
                buffer, 0,
                copy);

            bw.Write(buffer);
        }

        private static string ReadAnsiBuffer(
            byte[] data,
            int offset,
            int byteCount)
        {
            int length = 0;

            while (length < byteCount &&
                   data[offset + length] != 0)
            {
                length++;
            }

            return GetCp949().GetString(
                data,
                offset,
                length);
        }

        private static void WriteAnsiBuffer(
            BinaryWriter bw,
            string text,
            int byteCount)
        {
            byte[] buffer = new byte[byteCount];

            byte[] raw =
                GetCp949().GetBytes(text ?? string.Empty);

            int copy = Math.Min(
                raw.Length,
                byteCount - 1);

            Buffer.BlockCopy(
                raw, 0,
                buffer, 0,
                copy);

            bw.Write(buffer);
        }

        private static Encoding GetCp949()
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(949);
        }

        private static void SaveXml(
            XDocument document,
            string path)
        {
            using var writer =
                System.Xml.XmlWriter.Create(
                    path,
                    new System.Xml.XmlWriterSettings
                    {
                        Indent = true,
                        Encoding = new UTF8Encoding(false),
                        OmitXmlDeclaration = false
                    });

            document.Save(writer);
        }
    }
}
