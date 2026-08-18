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
    public sealed class MapPortalConverter : IGameDataConverter
    {
        public string Name => "MapPortal";

        private const int PortalRecordSize = 60;

        private static readonly string[] PortalFields =
        {
            "s_dwPortalID",
            "s_dwPortalType",
            "s_dwSrcMapID",
            "s_nSrcTargetX",
            "s_nSrcTargetY",
            "s_nSrcRadius",
            "s_dwDestMapID",
            "s_nDestTargetX",
            "s_nDestTargetY",
            "s_nDestRadius",
            "s_ePortalType",
            "s_dwUniqObjectID",
            "s_nPortalKindIndex",
            "s_nViewTargetX",
            "s_nViewTargetY"
        };

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapPortal", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapPortal", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            uint groupCount =
                ReadUInt32(
                    br,
                    "MapPortal.GroupCount");

            XElement root = new("Portal");

            long portalCount = 0;
            HashSet<uint> portalIds = new();

            for (uint groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                uint pMapGroup =
                    ReadUInt32(
                        br,
                        $"Portal block #{groupIndex + 1}.pMapGroup");

                // O nome XML é enganador:
                // fisicamente este valor é o número de PortalInfo
                // que vêm imediatamente a seguir.
                XElement portalGroup =
                    new(
                        "Portal",
                        new XElement("pMapGroup", pMapGroup));

                XElement infos =
                    new("PortalInfos");

                for (uint portalIndex = 0; portalIndex < pMapGroup; portalIndex++)
                {
                    long recordStart =
                        ms.Position;

                    uint portalId =
                        ReadUInt32(
                            br,
                            $"Block #{groupIndex + 1}, Portal #{portalIndex + 1}.s_dwPortalID");

                    if (!portalIds.Add(portalId))
                    {
                        throw new InvalidDataException(
                            $"MapPortal.bin contém s_dwPortalID duplicado: {portalId}. " +
                            $"Bloco #{groupIndex + 1}, entrada #{portalIndex + 1}.");
                    }

                    XElement info =
                        new(
                            "PortalInfo",
                            new XElement("s_dwPortalID", portalId));

                    for (int fieldIndex = 1; fieldIndex < PortalFields.Length; fieldIndex++)
                    {
                        string field =
                            PortalFields[fieldIndex];

                        uint value =
                            ReadUInt32(
                                br,
                                $"PortalID={portalId}.{field}");

                        info.Add(
                            new XElement(field, value));
                    }

                    ValidateRecordSize(
                        ms.Position - recordStart,
                        PortalRecordSize,
                        $"PortalID={portalId}");

                    infos.Add(info);
                    portalCount++;
                }

                portalGroup.Add(infos);
                root.Add(portalGroup);
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"MapPortal.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou em {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            SaveXml(
                Xml(root),
                outputXml);

            AppLogger.Log(
                $"MapPortal: BIN -> XML concluído. " +
                $"{groupCount:N0} blocos, {portalCount:N0} PortalInfo.");

            AppLogger.Log(
                $"MapPortal: estrutura = 4 bytes GroupCount + " +
                $"{groupCount:N0} × 4 bytes pMapGroup + " +
                $"{portalCount:N0} × {PortalRecordSize} bytes.");

            AppLogger.Log(
                $"MapPortal: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "Portal",
                    "MapPortal.xml");

            List<XElement> groups =
                root.Elements("Portal").ToList();

            ValidateXml(groups);

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDocument(test, groups);
                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "MapPortal: pasta Output inválida.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteDocument(bw, groups);
            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"MapPortal.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            long portalCount =
                groups.Sum(
                    x => (long)(
                        x.Element("PortalInfos")?
                            .Elements("PortalInfo")
                            .Count() ?? 0));

            AppLogger.Log(
                $"MapPortal: XML -> BIN concluído. " +
                $"{groups.Count:N0} blocos, {portalCount:N0} PortalInfo.");

            AppLogger.Log(
                $"MapPortal: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteDocument(
            BinaryWriter bw,
            IReadOnlyList<XElement> groups)
        {
            if ((ulong)groups.Count > uint.MaxValue)
            {
                throw new InvalidDataException(
                    $"MapPortal.xml contém {groups.Count:N0} blocos <Portal>; " +
                    "o GroupCount físico é UInt32.");
            }

            bw.Write((uint)groups.Count);

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                XElement group =
                    groups[groupIndex];

                string context =
                    $"Portal block #{groupIndex + 1}";

                XElement infosRoot =
                    RequiredElement(
                        group,
                        "PortalInfos",
                        context);

                List<XElement> infos =
                    infosRoot.Elements("PortalInfo").ToList();

                uint declaredCount =
                    RequiredUInt(
                        group,
                        "pMapGroup",
                        context);

                if (declaredCount != infos.Count)
                {
                    throw new InvalidDataException(
                        $"{context}: <pMapGroup>={declaredCount}, mas existem " +
                        $"{infos.Count} <PortalInfo>. " +
                        $"Neste BIN, pMapGroup é fisicamente o COUNT de PortalInfo. " +
                        $"Corrige <pMapGroup> para {infos.Count} ou ajusta os portais.");
                }

                bw.Write(declaredCount);

                for (int portalIndex = 0; portalIndex < infos.Count; portalIndex++)
                {
                    XElement info =
                        infos[portalIndex];

                    uint portalId =
                        RequiredUInt(
                            info,
                            "s_dwPortalID",
                            $"{context}, PortalInfo #{portalIndex + 1}");

                    string portalContext =
                        $"{context}, PortalID={portalId}";

                    long recordStart =
                        bw.BaseStream.Position;

                    foreach (string field in PortalFields)
                    {
                        bw.Write(
                            RequiredUInt(
                                info,
                                field,
                                portalContext));
                    }

                    ValidateRecordSize(
                        bw.BaseStream.Position - recordStart,
                        PortalRecordSize,
                        portalContext);
                }
            }
        }

        private static void ValidateXml(
            IReadOnlyList<XElement> groups)
        {
            Dictionary<uint, string> seenPortalIds = new();

            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                XElement group =
                    groups[groupIndex];

                string groupContext =
                    $"Portal block #{groupIndex + 1}";

                XElement infosRoot =
                    RequiredElement(
                        group,
                        "PortalInfos",
                        groupContext);

                List<XElement> infos =
                    infosRoot.Elements("PortalInfo").ToList();

                uint pMapGroup =
                    RequiredUInt(
                        group,
                        "pMapGroup",
                        groupContext);

                if (pMapGroup != infos.Count)
                {
                    throw new InvalidDataException(
                        $"{groupContext}: <pMapGroup>={pMapGroup}, " +
                        $"mas existem {infos.Count} <PortalInfo>. " +
                        $"Valor correto esperado: {infos.Count}. " +
                        "Apesar do nome, pMapGroup é o count físico desta lista.");
                }

                for (int portalIndex = 0; portalIndex < infos.Count; portalIndex++)
                {
                    XElement info =
                        infos[portalIndex];

                    string preliminary =
                        $"{groupContext}, PortalInfo #{portalIndex + 1}";

                    uint portalId =
                        RequiredUInt(
                            info,
                            "s_dwPortalID",
                            preliminary);

                    if (seenPortalIds.TryGetValue(portalId, out string? previous))
                    {
                        throw new InvalidDataException(
                            $"MapPortal.xml contém s_dwPortalID duplicado {portalId}. " +
                            $"Primeira ocorrência: {previous}. " +
                            $"Segunda ocorrência: {preliminary}. " +
                            "Cada PortalID deve ser único.");
                    }

                    seenPortalIds[portalId] = preliminary;

                    string context =
                        $"{groupContext}, PortalID={portalId}";

                    foreach (string field in PortalFields)
                    {
                        RequiredUInt(
                            info,
                            field,
                            context);
                    }
                }
            }
        }

        private static uint ReadUInt32(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(
                br,
                4,
                field);

            return br.ReadUInt32();
        }

        private static void EnsureRemaining(
            BinaryReader br,
            int required,
            string field)
        {
            long remaining =
                br.BaseStream.Length -
                br.BaseStream.Position;

            if (remaining < required)
            {
                throw new EndOfStreamException(
                    $"{field}: BIN truncado. " +
                    $"São necessários {required} bytes, mas restam apenas " +
                    $"{remaining:N0}. Offset={br.BaseStream.Position:N0}.");
            }
        }

        private static void ValidateRecordSize(
            long actual,
            long expected,
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

        private static XDocument LoadXml(
            string path)
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

        private static XElement RequiredElement(
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

            return element;
        }

        private static string RequiredText(
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

            return element.Value.Trim();
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!uint.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não é UInt32 válido " +
                    "(0..4294967295).");
            }

            return value;
        }

        private static XDocument Xml(
            XElement root) =>
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
            string? folder =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

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
