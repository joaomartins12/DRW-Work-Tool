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
    public sealed class RideConverter : IGameDataConverter
    {
        public string Name => "Ride";

        private const int CommentChars = 512;
        private const int RecordSize = 1060;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Ride", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Ride", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            int count = ReadCount(br, "Ride.Count", 1_000_000);

            long expectedSize =
                4L + (long)count * RecordSize;

            if (data.LongLength != expectedSize)
            {
                throw new InvalidDataException(
                    $"Ride.bin possui {data.LongLength:N0} bytes, " +
                    $"mas Count={count} exige exatamente {expectedSize:N0} bytes. " +
                    $"Diferença={(data.LongLength - expectedSize):+#;-#;0} bytes.");
            }

            XElement root = new("Rides");

            for (int i = 0; i < count; i++)
            {
                long start = ms.Position;

                uint digimonId = br.ReadUInt32();
                uint changeRide = br.ReadUInt32();
                float moveSpeed = br.ReadSingle();

                string comment =
                    ReadFixedUnicode(
                        br,
                        CommentChars);

                int rideType = br.ReadInt32();
                float aniRate = br.ReadSingle();
                int section = br.ReadInt32();
                int needCount = br.ReadInt32();
                int section2 = br.ReadInt32();
                int needCount2 = br.ReadInt32();

                long consumed =
                    ms.Position - start;

                if (consumed != RecordSize)
                {
                    throw new InvalidDataException(
                        $"Ride #{i} / DigimonID={digimonId}: " +
                        $"record ocupa {consumed} bytes; esperado={RecordSize}.");
                }

                root.Add(
                    new XElement(
                        "Ride",
                        new XElement("s_dwDigimonID", digimonId),
                        new XElement("s_dwChangeRide", changeRide),
                        new XElement(
                            "s_fMoveSpeed",
                            FloatText(moveSpeed)),
                        new XElement("s_szComment", comment),
                        new XElement("s_nRideType", rideType),
                        new XElement(
                            "s_fAniRate_Run",
                            FloatText(aniRate)),
                        new XElement("s_nSection", section),
                        new XElement("s_nNeedCount", needCount),
                        new XElement("s_nSection_2", section2),
                        new XElement("s_nNeedCount_2", needCount2)));
            }

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Ride.bin contém {extra:N0} bytes extra no final.");
            }

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Ride.");

            Directory.CreateDirectory(folder);

            SaveXml(
                new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root),
                outputXml);

            AppLogger.Log(
                $"Ride: BIN -> XML concluído. {count:N0} rides exportadas.");

            AppLogger.Log(
                $"Ride: estrutura = 4 bytes Count + " +
                $"{count:N0} × {RecordSize:N0} bytes.");

            AppLogger.Log(
                $"Ride: tamanho BIN verificado: " +
                $"{data.Length:N0} / {expectedSize:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc = LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "Rides",
                    "Ride.xml");

            List<XElement> rides =
                root.Elements("Ride").ToList();

            long expectedSize =
                4L + (long)rides.Count * RecordSize;

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Ride."));

            // Valida tudo antes de substituir o BIN existente.
            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteRideTable(test, rides);
                test.Flush();

                if (testStream.Length != expectedSize)
                {
                    throw new InvalidDataException(
                        $"Ride: validação interna gerou {testStream.Length:N0} bytes; " +
                        $"esperado={expectedSize:N0}.");
                }
            }

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteRideTable(bw, rides);
            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Ride.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"Ride: XML -> BIN concluído. {rides.Count:N0} rides serializadas.");

            AppLogger.Log(
                $"Ride: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteRideTable(
            BinaryWriter bw,
            IReadOnlyList<XElement> rides)
        {
            bw.Write(rides.Count);

            for (int i = 0; i < rides.Count; i++)
            {
                XElement ride = rides[i];

                uint digimonId =
                    RequiredUInt(
                        ride,
                        "s_dwDigimonID",
                        $"Ride #{i}");

                string context =
                    $"Ride DigimonID={digimonId}";

                long start =
                    bw.BaseStream.Position;

                bw.Write(digimonId);

                bw.Write(
                    RequiredUInt(
                        ride,
                        "s_dwChangeRide",
                        context));

                bw.Write(
                    RequiredFloat(
                        ride,
                        "s_fMoveSpeed",
                        context));

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        ride,
                        "s_szComment",
                        context,
                        allowEmpty: true),
                    CommentChars,
                    $"{context} <s_szComment>");

                bw.Write(
                    RequiredInt(
                        ride,
                        "s_nRideType",
                        context));

                bw.Write(
                    RequiredFloat(
                        ride,
                        "s_fAniRate_Run",
                        context));

                bw.Write(
                    RequiredInt(
                        ride,
                        "s_nSection",
                        context));

                bw.Write(
                    RequiredInt(
                        ride,
                        "s_nNeedCount",
                        context));

                bw.Write(
                    RequiredInt(
                        ride,
                        "s_nSection_2",
                        context));

                bw.Write(
                    RequiredInt(
                        ride,
                        "s_nNeedCount_2",
                        context));

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != RecordSize)
                {
                    throw new InvalidDataException(
                        $"{context}: record gerado ocupa {consumed:N0} bytes; " +
                        $"esperado={RecordSize:N0}.");
                }
            }
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            int byteCount =
                checked(wcharCount * 2);

            byte[] raw =
                br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"String UTF-16LE truncada: esperados {byteCount} bytes, " +
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

            int maxUsefulBytes =
                (wcharCount - 1) * 2;

            if (raw.Length > maxUsefulBytes)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length:N0} bytes UTF-16LE, " +
                    $"mas o buffer wchar[{wcharCount}] suporta no máximo " +
                    $"{maxUsefulBytes:N0} bytes úteis " +
                    $"({wcharCount - 1} caracteres + terminador NUL). " +
                    $"Reduz o comentário.");
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
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

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

        private static int RequiredInt(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

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

        private static float RequiredFloat(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!float.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é Single/float válido. " +
                    "Usa ponto como separador decimal, por exemplo 1.5.");
            }

            if (float.IsNaN(result) ||
                float.IsInfinity(result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> não pode ser NaN ou Infinity.");
            }

            return result;
        }

        private static string FloatText(float value) =>
            value.ToString(
                "R",
                CultureInfo.InvariantCulture);

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
