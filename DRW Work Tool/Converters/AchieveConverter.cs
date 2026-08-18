using DRW_Work_Tool.Core;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class AchieveConverter : IGameDataConverter
    {
        public string Name => "Achieve";

        private const int SInfoCount = 18;
        private const int SInfoNameChars = 32;
        private const int SInfoRecordSize = 68;

        private const int AchieveNameChars = 64;
        private const int AchieveCommentChars = 256;
        private const int AchieveTitleChars = 64;
        private const int AchieveRecordSize = 796;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Achieve", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Achieve", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            const int fixedHeaderSize = SInfoCount * SInfoRecordSize + 4;

            if (data.Length < fixedHeaderSize)
                throw new InvalidDataException(
                    $"Achieve.bin demasiado pequeno: {data.Length} bytes.");

            int offset = 0;

            // ---------------------------------------------------------
            // Parte 1: 18 sINFO fixos, sem count no início.
            // Cada registo = wchar[32] (64 bytes) + int32 (4 bytes) = 68.
            // ---------------------------------------------------------
            XElement sInfosRoot = new("sINFOs");

            for (int i = 0; i < SInfoCount; i++)
            {
                string name = ReadWideBuffer(data, offset, SInfoNameChars);
                offset += SInfoNameChars * 2;

                int child = ReadInt32(data, offset);
                offset += 4;

                sInfosRoot.Add(
                    new XElement("sINFO",
                        new XElement("s_szName", name),
                        new XElement("s_listChild", child)));
            }

            // ---------------------------------------------------------
            // Parte 2: quantidade de AchieveSINFO
            // ---------------------------------------------------------
            int count = ReadInt32(data, offset);
            offset += 4;

            if (count < 0)
                throw new InvalidDataException(
                    $"Achieve count inválido: {count}.");

            long expectedSize =
                (long)SInfoCount * SInfoRecordSize +
                4L +
                (long)count * AchieveRecordSize;

            if (data.Length != expectedSize)
            {
                throw new InvalidDataException(
                    $"Tamanho Achieve.bin inválido. " +
                    $"Atual={data.Length} bytes, Esperado={expectedSize} bytes, " +
                    $"Achieves={count}.");
            }

            XElement achieveRoot = new("AchieveSINFOs");

            for (int i = 0; i < count; i++)
            {
                int recordStart = offset;

                int questId = ReadInt32(data, offset);
                offset += 4;

                int icon = ReadInt32(data, offset);
                offset += 4;

                ushort point = ReadUInt16(data, offset);
                offset += 2;

                byte display = data[offset++];
                byte display2 = data[offset++];

                string name = ReadWideBuffer(data, offset, AchieveNameChars);
                offset += AchieveNameChars * 2;

                string comment = ReadWideBuffer(data, offset, AchieveCommentChars);
                offset += AchieveCommentChars * 2;

                string title = ReadWideBuffer(data, offset, AchieveTitleChars);
                offset += AchieveTitleChars * 2;

                int group = ReadInt32(data, offset);
                offset += 4;

                int subGroup = ReadInt32(data, offset);
                offset += 4;

                int type = ReadInt32(data, offset);
                offset += 4;

                int buffCode = ReadInt32(data, offset);
                offset += 4;

                if (offset - recordStart != AchieveRecordSize)
                    throw new InvalidDataException(
                        $"Erro interno no record Achieve #{i}: " +
                        $"{offset - recordStart} bytes em vez de {AchieveRecordSize}.");

                achieveRoot.Add(
                    new XElement("AchieveSINFO",
                        new XElement("s_nQuestID", questId),
                        new XElement("s_nIcon", icon),
                        new XElement("s_nPoint", point),
                        new XElement("s_bDisplay", display),
                        new XElement("s_bDisplay2", display2),
                        new XElement("s_szName", name),
                        new XElement("s_szComment", comment),
                        new XElement("s_szTitle", title),
                        new XElement("s_nGroup", group),
                        new XElement("s_nSubGroup", subGroup),
                        new XElement("s_nType", type),
                        new XElement("s_nBuffCode", buffCode)));
            }

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidOperationException("Pasta XML inválida.");

            Directory.CreateDirectory(folder);

            string sInfosPath = Path.Combine(folder, "Achieve_sINFOs.xml");

            SaveXml(new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                achieveRoot), outputXml);

            SaveXml(new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                sInfosRoot), sInfosPath);

            AppLogger.Log(
                $"Achieve: BIN -> XML concluído. " +
                $"sINFOs={SInfoCount}, Achieves={count}.");

            AppLogger.Log(
                $"Achieve: tamanho BIN verificado: " +
                $"{data.Length} / {expectedSize} bytes (OK).");

            AppLogger.Log($"Achieve XML: {outputXml}");
            AppLogger.Log($"Achieve sINFOs XML: {sInfosPath}");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidOperationException("Pasta XML inválida.");

            string sInfosPath = Path.Combine(folder, "Achieve_sINFOs.xml");

            if (!File.Exists(inputXml))
                throw new FileNotFoundException(
                    "Achieve.xml não encontrado.", inputXml);

            if (!File.Exists(sInfosPath))
                throw new FileNotFoundException(
                    "Achieve_sINFOs.xml não encontrado.", sInfosPath);

            XDocument achieveDoc = XDocument.Load(inputXml);
            XDocument sInfoDoc = XDocument.Load(sInfosPath);

            XElement achieveRoot =
                achieveDoc.Root
                ?? throw new InvalidDataException("Achieve.xml sem root.");

            XElement sInfoRoot =
                sInfoDoc.Root
                ?? throw new InvalidDataException(
                    "Achieve_sINFOs.xml sem root.");

            var sInfos = sInfoRoot.Elements("sINFO").ToList();
            var achieves = achieveRoot.Elements("AchieveSINFO").ToList();

            if (sInfos.Count != SInfoCount)
            {
                throw new InvalidDataException(
                    $"Achieve_sINFOs.xml tem {sInfos.Count} registos; " +
                    $"o BIN exige exatamente {SInfoCount}.");
            }

            long expectedSize =
                (long)SInfoCount * SInfoRecordSize +
                4L +
                (long)achieves.Count * AchieveRecordSize;

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidOperationException("Output inválido."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs);

            // 18 sINFO fixos
            foreach (XElement info in sInfos)
            {
                WriteWideBuffer(
                    bw,
                    Value(info, "s_szName", string.Empty),
                    SInfoNameChars);

                bw.Write(ParseInt(Value(info, "s_listChild")));
            }

            // Count dos achievements
            bw.Write(achieves.Count);

            // Registos de 796 bytes
            foreach (XElement a in achieves)
            {
                bw.Write(ParseInt(Value(a, "s_nQuestID")));
                bw.Write(ParseInt(Value(a, "s_nIcon")));

                bw.Write(checked(
                    (ushort)ParseInt(Value(a, "s_nPoint"))));

                bw.Write(checked(
                    (byte)ParseInt(Value(a, "s_bDisplay"))));

                bw.Write(checked(
                    (byte)ParseInt(Value(a, "s_bDisplay2"))));

                WriteWideBuffer(
                    bw,
                    Value(a, "s_szName", string.Empty),
                    AchieveNameChars);

                WriteWideBuffer(
                    bw,
                    Value(a, "s_szComment", string.Empty),
                    AchieveCommentChars);

                WriteWideBuffer(
                    bw,
                    Value(a, "s_szTitle", string.Empty),
                    AchieveTitleChars);

                bw.Write(ParseInt(Value(a, "s_nGroup")));
                bw.Write(ParseInt(Value(a, "s_nSubGroup")));
                bw.Write(ParseInt(Value(a, "s_nType")));
                bw.Write(ParseInt(Value(a, "s_nBuffCode")));
            }

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"BIN gerado com tamanho incorreto. " +
                    $"Atual={actualSize}, Esperado={expectedSize}.");
            }

            AppLogger.Log(
                $"Achieve: XML -> BIN concluído. " +
                $"sINFOs={sInfos.Count}, Achieves={achieves.Count}.");

            AppLogger.Log(
                $"Achieve: tamanho BIN gerado: " +
                $"{actualSize} bytes. Esperado={expectedSize} bytes (OK).");
        }

        private static string Value(
            XElement element,
            string name,
            string defaultValue = "0")
        {
            return element.Element(name)?.Value ?? defaultValue;
        }

        private static int ParseInt(string value)
        {
            return int.Parse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);
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

        private static string ReadWideBuffer(
            byte[] data,
            int offset,
            int wcharCount)
        {
            int bytes = wcharCount * 2;

            string value =
                Encoding.Unicode.GetString(data, offset, bytes);

            int zero = value.IndexOf('\0');

            return zero >= 0
                ? value[..zero]
                : value;
        }

        private static int ReadInt32(byte[] data, int offset) =>
            BitConverter.ToInt32(data, offset);

        private static ushort ReadUInt16(byte[] data, int offset) =>
            BitConverter.ToUInt16(data, offset);

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
