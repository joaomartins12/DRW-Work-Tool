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
    public sealed class MapObjectConverter : IGameDataConverter
    {
        public string Name => "MapObject";

        private const int MapHeaderSize = 8;
        private const int SourceHeaderSize = 8;
        private const int OrderHeaderSize = 8;
        private const int FactorRecordSize = 12;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapObject", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapObject", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            uint mapCount =
                ReadUInt32(
                    br,
                    "MapObject.MapCount");

            XElement root = new("MapObjects");

            long sourceCount = 0;
            long orderCount = 0;
            long factorCount = 0;

            for (uint mapIndex = 0; mapIndex < mapCount; mapIndex++)
            {
                uint mapId =
                    ReadUInt32(
                        br,
                        $"MapObject #{mapIndex + 1}.MapId");

                uint size =
                    ReadUInt32(
                        br,
                        $"MapId={mapId}.Size");

                XElement map =
                    new(
                        "MapObject",
                        new XElement("MapId", mapId),
                        new XElement("Size", size));

                for (uint sourceIndex = 0; sourceIndex < size; sourceIndex++)
                {
                    uint objectId =
                        ReadUInt32(
                            br,
                            $"MapId={mapId}, MapSourceObject #{sourceIndex + 1}.ObjectId");

                    // Count físico de OrderObject.
                    // Não existe uma tag separada no XML.
                    uint physicalOrderCount =
                        ReadUInt32(
                            br,
                            $"MapId={mapId}, ObjectId={objectId}.OrderObjectCount");

                    XElement source =
                        new(
                            "MapSourceObject",
                            new XElement("ObjectId", objectId));

                    for (uint orderIndex = 0; orderIndex < physicalOrderCount; orderIndex++)
                    {
                        uint orderId =
                            ReadUInt32(
                                br,
                                $"MapId={mapId}, ObjectId={objectId}, OrderObject #{orderIndex + 1}.OrderId");

                        uint factorSize =
                            ReadUInt32(
                                br,
                                $"MapId={mapId}, ObjectId={objectId}, OrderId={orderId}.FactorSize");

                        XElement order =
                            new(
                                "OrderObject",
                                new XElement("OrderId", orderId),
                                new XElement("FactorSize", factorSize));

                        for (uint factorIndex = 0; factorIndex < factorSize; factorIndex++)
                        {
                            uint openType =
                                ReadUInt32(
                                    br,
                                    $"MapId={mapId}, ObjectId={objectId}, OrderId={orderId}, Object #{factorIndex + 1}.s_nOpenType");

                            // IMPORTANTE:
                            // a ordem física no BIN é Count antes de Factor.
                            uint factorCnt =
                                ReadUInt32(
                                    br,
                                    $"MapId={mapId}, ObjectId={objectId}, OrderId={orderId}, Object #{factorIndex + 1}.s_nFactorCnt");

                            uint factor =
                                ReadUInt32(
                                    br,
                                    $"MapId={mapId}, ObjectId={objectId}, OrderId={orderId}, Object #{factorIndex + 1}.s_nFactor");

                            order.Add(
                                new XElement(
                                    "Object",
                                    new XElement("s_nOpenType", openType),
                                    new XElement("s_nFactor", factor),
                                    new XElement("s_nFactorCnt", factorCnt)));

                            factorCount++;
                        }

                        source.Add(order);
                        orderCount++;
                    }

                    map.Add(source);
                    sourceCount++;
                }

                root.Add(map);
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"MapObject.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            SaveXml(
                Xml(root),
                outputXml);

            AppLogger.Log(
                $"MapObject: BIN -> XML concluído. " +
                $"Maps={mapCount:N0}, MapSourceObject={sourceCount:N0}, " +
                $"OrderObject={orderCount:N0}, Factors={factorCount:N0}.");

            AppLogger.Log(
                $"MapObject: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "MapObjects",
                    "MapObject.xml");

            List<XElement> maps =
                root.Elements("MapObject").ToList();

            ValidateXml(maps);

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDocument(
                    test,
                    maps);

                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "MapObject: pasta Output inválida.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteDocument(
                bw,
                maps);

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"MapObject.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            long sourceCount =
                maps.Sum(
                    m => (long)m.Elements("MapSourceObject").Count());

            long orderCount =
                maps
                    .SelectMany(m => m.Elements("MapSourceObject"))
                    .Sum(s => (long)s.Elements("OrderObject").Count());

            long factorCount =
                maps
                    .SelectMany(m => m.Elements("MapSourceObject"))
                    .SelectMany(s => s.Elements("OrderObject"))
                    .Sum(o => (long)o.Elements("Object").Count());

            AppLogger.Log(
                $"MapObject: XML -> BIN concluído. " +
                $"Maps={maps.Count:N0}, MapSourceObject={sourceCount:N0}, " +
                $"OrderObject={orderCount:N0}, Factors={factorCount:N0}.");

            AppLogger.Log(
                $"MapObject: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteDocument(
            BinaryWriter bw,
            IReadOnlyList<XElement> maps)
        {
            if ((ulong)maps.Count > uint.MaxValue)
            {
                throw new InvalidDataException(
                    $"MapObject.xml contém {maps.Count:N0} mapas; " +
                    "o MapCount físico é UInt32.");
            }

            bw.Write((uint)maps.Count);

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                XElement map =
                    maps[mapIndex];

                uint mapId =
                    RequiredUInt(
                        map,
                        "MapId",
                        $"MapObject #{mapIndex + 1}");

                string mapContext =
                    $"MapObject MapId={mapId}";

                List<XElement> sources =
                    map.Elements("MapSourceObject").ToList();

                uint declaredSize =
                    RequiredUInt(
                        map,
                        "Size",
                        mapContext);

                if (declaredSize != sources.Count)
                {
                    throw new InvalidDataException(
                        $"{mapContext}: <Size>={declaredSize}, mas existem " +
                        $"{sources.Count} <MapSourceObject>. " +
                        $"Corrige <Size> para {sources.Count} ou ajusta os objetos.");
                }

                bw.Write(mapId);
                bw.Write(declaredSize);

                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    XElement source =
                        sources[sourceIndex];

                    uint objectId =
                        RequiredUInt(
                            source,
                            "ObjectId",
                            $"{mapContext}, MapSourceObject #{sourceIndex + 1}");

                    string sourceContext =
                        $"{mapContext}, ObjectId={objectId}";

                    List<XElement> orders =
                        source.Elements("OrderObject").ToList();

                    if ((ulong)orders.Count > uint.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"{sourceContext}: existem {orders.Count:N0} OrderObject; " +
                            "o count físico é UInt32.");
                    }

                    bw.Write(objectId);

                    // Count físico que não aparece como campo XML.
                    bw.Write((uint)orders.Count);

                    for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
                    {
                        XElement order =
                            orders[orderIndex];

                        uint orderId =
                            RequiredUInt(
                                order,
                                "OrderId",
                                $"{sourceContext}, OrderObject #{orderIndex + 1}");

                        string orderContext =
                            $"{sourceContext}, OrderId={orderId}";

                        List<XElement> factors =
                            order.Elements("Object").ToList();

                        uint factorSize =
                            RequiredUInt(
                                order,
                                "FactorSize",
                                orderContext);

                        if (factorSize != factors.Count)
                        {
                            throw new InvalidDataException(
                                $"{orderContext}: <FactorSize>={factorSize}, mas existem " +
                                $"{factors.Count} <Object>. " +
                                $"Corrige <FactorSize> para {factors.Count} ou ajusta os fatores.");
                        }

                        bw.Write(orderId);
                        bw.Write(factorSize);

                        for (int factorIndex = 0; factorIndex < factors.Count; factorIndex++)
                        {
                            XElement factor =
                                factors[factorIndex];

                            string factorContext =
                                $"{orderContext}, Object #{factorIndex + 1}";

                            uint openType =
                                RequiredUInt(
                                    factor,
                                    "s_nOpenType",
                                    factorContext);

                            uint factorValue =
                                RequiredUInt(
                                    factor,
                                    "s_nFactor",
                                    factorContext);

                            uint factorCnt =
                                RequiredUInt(
                                    factor,
                                    "s_nFactorCnt",
                                    factorContext);

                            bw.Write(openType);

                            // Ordem física confirmada:
                            // s_nFactorCnt vem ANTES de s_nFactor.
                            bw.Write(factorCnt);
                            bw.Write(factorValue);
                        }
                    }
                }
            }
        }

        private static void ValidateXml(
            IReadOnlyList<XElement> maps)
        {
            Dictionary<uint, int> seenMaps = new();

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                XElement map =
                    maps[mapIndex];

                uint mapId =
                    RequiredUInt(
                        map,
                        "MapId",
                        $"MapObject #{mapIndex + 1}");

                if (seenMaps.TryGetValue(mapId, out int previous))
                {
                    throw new InvalidDataException(
                        $"MapObject.xml contém MapId duplicado {mapId}. " +
                        $"Entradas #{previous + 1} e #{mapIndex + 1}.");
                }

                seenMaps[mapId] = mapIndex;

                string mapContext =
                    $"MapObject MapId={mapId}";

                List<XElement> sources =
                    map.Elements("MapSourceObject").ToList();

                uint declaredSize =
                    RequiredUInt(
                        map,
                        "Size",
                        mapContext);

                if (declaredSize != sources.Count)
                {
                    throw new InvalidDataException(
                        $"{mapContext}: <Size>={declaredSize}, mas existem " +
                        $"{sources.Count} <MapSourceObject>. " +
                        $"Valor esperado={sources.Count}.");
                }

                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    XElement source =
                        sources[sourceIndex];

                    uint objectId =
                        RequiredUInt(
                            source,
                            "ObjectId",
                            $"{mapContext}, MapSourceObject #{sourceIndex + 1}");

                    string sourceContext =
                        $"{mapContext}, ObjectId={objectId}";

                    List<XElement> orders =
                        source.Elements("OrderObject").ToList();

                    if (orders.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"{sourceContext}: não existe nenhum <OrderObject>. " +
                            "O BIN exige um count físico seguido dos respetivos OrderObject.");
                    }

                    for (int orderIndex = 0; orderIndex < orders.Count; orderIndex++)
                    {
                        XElement order =
                            orders[orderIndex];

                        uint orderId =
                            RequiredUInt(
                                order,
                                "OrderId",
                                $"{sourceContext}, OrderObject #{orderIndex + 1}");

                        string orderContext =
                            $"{sourceContext}, OrderId={orderId}";

                        List<XElement> factors =
                            order.Elements("Object").ToList();

                        uint factorSize =
                            RequiredUInt(
                                order,
                                "FactorSize",
                                orderContext);

                        if (factorSize != factors.Count)
                        {
                            throw new InvalidDataException(
                                $"{orderContext}: <FactorSize>={factorSize}, " +
                                $"mas existem {factors.Count} <Object>. " +
                                $"Valor esperado={factors.Count}.");
                        }

                        for (int factorIndex = 0; factorIndex < factors.Count; factorIndex++)
                        {
                            XElement factor =
                                factors[factorIndex];

                            string factorContext =
                                $"{orderContext}, Object #{factorIndex + 1}";

                            RequiredUInt(
                                factor,
                                "s_nOpenType",
                                factorContext);

                            RequiredUInt(
                                factor,
                                "s_nFactor",
                                factorContext);

                            RequiredUInt(
                                factor,
                                "s_nFactorCnt",
                                factorContext);
                        }
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
