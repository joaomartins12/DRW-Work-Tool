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
    public sealed class MapMonsterListConverter : IGameDataConverter
    {
        public string Name => "MapMonsterList";

        private const int MapHeaderSize = 8;
        private const int MapInformationHeaderSize = 8;
        private const int MonsterRecordSize = 48;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapMonsterList", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("MapMonsterList", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            uint fileCount =
                ReadUInt32(
                    br,
                    "MapMonsterList.Count");

            XElement root = new("MapMonsters");

            long mapInfoCount = 0;
            long monsterCount = 0;

            HashSet<uint> fileIds = new();

            for (uint fileIndex = 0; fileIndex < fileCount; fileIndex++)
            {
                long mapStart = ms.Position;

                uint fileId =
                    ReadUInt32(
                        br,
                        $"Map #{fileIndex + 1}.FileID");

                uint nSize =
                    ReadUInt32(
                        br,
                        $"Map FileID={fileId}.nSize");

                if (!fileIds.Add(fileId))
                {
                    throw new InvalidDataException(
                        $"MapMonsterList.bin contém FileID duplicado: {fileId}. " +
                        $"Entrada #{fileIndex + 1}.");
                }

                XElement mapElement =
                    new(
                        "Map",
                        new XElement("FileID", fileId),
                        new XElement("nSize", nSize));

                for (uint infoIndex = 0; infoIndex < nSize; infoIndex++)
                {
                    long infoStart = ms.Position;

                    uint mapId =
                        ReadUInt32(
                            br,
                            $"FileID={fileId}, MapInformation #{infoIndex + 1}.Map");

                    uint mapNum =
                        ReadUInt32(
                            br,
                            $"FileID={fileId}, Map={mapId}.MapNum");

                    XElement infoElement =
                        new(
                            "MapInformation",
                            new XElement("Map", mapId),
                            new XElement("MapNum", mapNum));

                    for (uint monsterIndex = 0; monsterIndex < mapNum; monsterIndex++)
                    {
                        long monsterStart = ms.Position;

                        uint monsterMapId =
                            ReadUInt32(
                                br,
                                $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}.MapID");

                        uint monsterId =
                            ReadUInt32(
                                br,
                                $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}.MonsterID");

                        uint centerX = ReadUInt32(br, "CenterX");
                        uint centerY = ReadUInt32(br, "CenterY");
                        uint radius = ReadUInt32(br, "Radius");
                        uint count = ReadUInt32(br, "Count");
                        uint respawnTime = ReadUInt32(br, "RespawnTime");
                        uint killGenMonFtId = ReadUInt32(br, "KillGenMonFTID");
                        uint killgenCount = ReadUInt32(br, "KillgenCount");
                        uint killgenViewCnt = ReadUInt32(br, "KillgenViewCnt");
                        uint moveType = ReadUInt32(br, "MoveType");

                        byte instRespawn =
                            ReadByte(
                                br,
                                $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}.InstRespawn");

                        byte u10 =
                            ReadByte(
                                br,
                                $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}.u10");

                        ushort u2 =
                            ReadUInt16(
                                br,
                                $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}.u2");

                        if (monsterMapId != mapId)
                        {
                            throw new InvalidDataException(
                                $"MapMonsterList.bin inconsistente em FileID={fileId}, " +
                                $"MapInformation Map={mapId}, Monsters #{monsterIndex + 1}: " +
                                $"MapID={monsterMapId}. Esperado={mapId}.");
                        }

                        if (monsterId != fileId)
                        {
                            throw new InvalidDataException(
                                $"MapMonsterList.bin inconsistente em FileID={fileId}, " +
                                $"Map={mapId}, Monsters #{monsterIndex + 1}: " +
                                $"MonsterID={monsterId}. Esperado={fileId}.");
                        }

                        ValidateRecordSize(
                            ms.Position - monsterStart,
                            MonsterRecordSize,
                            $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}");

                        infoElement.Add(
                            new XElement(
                                "Monsters",
                                new XElement("MapID", monsterMapId),
                                new XElement("MonsterID", monsterId),
                                new XElement("CenterX", centerX),
                                new XElement("CenterY", centerY),
                                new XElement("Radius", radius),
                                new XElement("Count", count),
                                new XElement("RespawnTime", respawnTime),
                                new XElement("KillGenMonFTID", killGenMonFtId),
                                new XElement("KillgenCount", killgenCount),
                                new XElement("KillgenViewCnt", killgenViewCnt),
                                new XElement("MoveType", moveType),
                                new XElement("InstRespawn", instRespawn),
                                new XElement("u10", u10),
                                new XElement("u2", u2)));

                        monsterCount++;
                    }

                    ValidateRecordSize(
                        ms.Position - infoStart,
                        MapInformationHeaderSize +
                        ((long)mapNum * MonsterRecordSize),
                        $"FileID={fileId}, MapInformation Map={mapId}");

                    mapElement.Add(infoElement);
                    mapInfoCount++;
                }

                ValidateRecordSize(
                    ms.Position - mapStart,
                    MapHeaderSize +
                    CalculateMapPayloadSize(mapElement),
                    $"Map FileID={fileId}");

                root.Add(mapElement);
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"MapMonsterList.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou em {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            SaveXml(
                Xml(root),
                outputXml);

            AppLogger.Log(
                $"MapMonsterList: BIN -> XML concluído. " +
                $"{fileCount:N0} FileIDs, {mapInfoCount:N0} MapInformation, " +
                $"{monsterCount:N0} Monsters.");

            AppLogger.Log(
                $"MapMonsterList: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "MapMonsters",
                    "MapMonsterList.xml");

            List<XElement> maps =
                root.Elements("Map").ToList();

            ValidateXmlStructure(maps);

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDocument(test, maps);
                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "MapMonsterList: pasta Output inválida.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteDocument(bw, maps);
            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"MapMonsterList.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            long mapInfoCount =
                maps.Sum(x => (long)x.Elements("MapInformation").Count());

            long monsterCount =
                maps
                    .SelectMany(x => x.Elements("MapInformation"))
                    .Sum(x => (long)x.Elements("Monsters").Count());

            AppLogger.Log(
                $"MapMonsterList: XML -> BIN concluído. " +
                $"{maps.Count:N0} FileIDs, {mapInfoCount:N0} MapInformation, " +
                $"{monsterCount:N0} Monsters.");

            AppLogger.Log(
                $"MapMonsterList: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteDocument(
            BinaryWriter bw,
            IReadOnlyList<XElement> maps)
        {
            if ((ulong)maps.Count > uint.MaxValue)
            {
                throw new InvalidDataException(
                    $"MapMonsterList.xml contém {maps.Count:N0} <Map>; " +
                    "o Count físico é UInt32.");
            }

            bw.Write((uint)maps.Count);

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                XElement map = maps[mapIndex];

                uint fileId =
                    RequiredUInt(
                        map,
                        "FileID",
                        $"Map #{mapIndex + 1}");

                string context =
                    $"Map FileID={fileId}";

                List<XElement> infos =
                    map.Elements("MapInformation").ToList();

                uint declaredSize =
                    RequiredUInt(
                        map,
                        "nSize",
                        context);

                if (declaredSize != infos.Count)
                {
                    throw new InvalidDataException(
                        $"{context}: <nSize>={declaredSize}, mas existem " +
                        $"{infos.Count} <MapInformation>. " +
                        $"Corrige <nSize> para {infos.Count} ou ajusta os blocos.");
                }

                long mapStart =
                    bw.BaseStream.Position;

                bw.Write(fileId);
                bw.Write(declaredSize);

                for (int infoIndex = 0; infoIndex < infos.Count; infoIndex++)
                {
                    XElement info =
                        infos[infoIndex];

                    uint mapId =
                        RequiredUInt(
                            info,
                            "Map",
                            $"{context}, MapInformation #{infoIndex + 1}");

                    string infoContext =
                        $"{context}, Map={mapId}";

                    List<XElement> monsters =
                        info.Elements("Monsters").ToList();

                    uint mapNum =
                        RequiredUInt(
                            info,
                            "MapNum",
                            infoContext);

                    if (mapNum != monsters.Count)
                    {
                        throw new InvalidDataException(
                            $"{infoContext}: <MapNum>={mapNum}, mas existem " +
                            $"{monsters.Count} <Monsters>. " +
                            $"Corrige <MapNum> para {monsters.Count} ou ajusta os spawns.");
                    }

                    long infoStart =
                        bw.BaseStream.Position;

                    bw.Write(mapId);
                    bw.Write(mapNum);

                    for (int monsterIndex = 0; monsterIndex < monsters.Count; monsterIndex++)
                    {
                        XElement monster =
                            monsters[monsterIndex];

                        string monsterContext =
                            $"{infoContext}, Monsters #{monsterIndex + 1}";

                        uint monsterMapId =
                            RequiredUInt(
                                monster,
                                "MapID",
                                monsterContext);

                        if (monsterMapId != mapId)
                        {
                            throw new InvalidDataException(
                                $"{monsterContext}: <MapID>={monsterMapId}, " +
                                $"mas o MapInformation pai possui <Map>{mapId}</Map>. " +
                                $"Usa MapID={mapId} neste spawn.");
                        }

                        uint monsterId =
                            RequiredUInt(
                                monster,
                                "MonsterID",
                                monsterContext);

                        if (monsterId != fileId)
                        {
                            throw new InvalidDataException(
                                $"{monsterContext}: <MonsterID>={monsterId}, " +
                                $"mas o bloco pai possui <FileID>{fileId}</FileID>. " +
                                $"Usa MonsterID={fileId} ou move o spawn para o FileID correto.");
                        }

                        long monsterStart =
                            bw.BaseStream.Position;

                        bw.Write(monsterMapId);
                        bw.Write(monsterId);
                        bw.Write(RequiredUInt(monster, "CenterX", monsterContext));
                        bw.Write(RequiredUInt(monster, "CenterY", monsterContext));
                        bw.Write(RequiredUInt(monster, "Radius", monsterContext));
                        bw.Write(RequiredUInt(monster, "Count", monsterContext));
                        bw.Write(RequiredUInt(monster, "RespawnTime", monsterContext));
                        bw.Write(RequiredUInt(monster, "KillGenMonFTID", monsterContext));
                        bw.Write(RequiredUInt(monster, "KillgenCount", monsterContext));
                        bw.Write(RequiredUInt(monster, "KillgenViewCnt", monsterContext));
                        bw.Write(RequiredUInt(monster, "MoveType", monsterContext));
                        bw.Write(RequiredByte(monster, "InstRespawn", monsterContext));
                        bw.Write(RequiredByte(monster, "u10", monsterContext));
                        bw.Write(RequiredUInt16(monster, "u2", monsterContext));

                        ValidateRecordSize(
                            bw.BaseStream.Position - monsterStart,
                            MonsterRecordSize,
                            monsterContext);
                    }

                    ValidateRecordSize(
                        bw.BaseStream.Position - infoStart,
                        MapInformationHeaderSize +
                        ((long)monsters.Count * MonsterRecordSize),
                        infoContext);
                }

                ValidateRecordSize(
                    bw.BaseStream.Position - mapStart,
                    MapHeaderSize +
                    CalculateMapPayloadSize(map),
                    context);
            }
        }

        private static void ValidateXmlStructure(
            IReadOnlyList<XElement> maps)
        {
            Dictionary<uint, int> seenFileIds = new();

            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                XElement map = maps[mapIndex];

                uint fileId =
                    RequiredUInt(
                        map,
                        "FileID",
                        $"Map #{mapIndex + 1}");

                if (seenFileIds.TryGetValue(fileId, out int previousIndex))
                {
                    throw new InvalidDataException(
                        $"MapMonsterList.xml contém FileID duplicado {fileId}. " +
                        $"Entradas #{previousIndex + 1} e #{mapIndex + 1}.");
                }

                seenFileIds[fileId] = mapIndex;

                List<XElement> infos =
                    map.Elements("MapInformation").ToList();

                uint declaredSize =
                    RequiredUInt(
                        map,
                        "nSize",
                        $"Map FileID={fileId}");

                if (declaredSize != infos.Count)
                {
                    throw new InvalidDataException(
                        $"Map FileID={fileId}: <nSize>={declaredSize}, " +
                        $"mas existem {infos.Count} <MapInformation>. " +
                        $"Valor correto esperado: {infos.Count}.");
                }

                for (int infoIndex = 0; infoIndex < infos.Count; infoIndex++)
                {
                    XElement info = infos[infoIndex];

                    uint mapId =
                        RequiredUInt(
                            info,
                            "Map",
                            $"FileID={fileId}, MapInformation #{infoIndex + 1}");

                    List<XElement> monsters =
                        info.Elements("Monsters").ToList();

                    uint mapNum =
                        RequiredUInt(
                            info,
                            "MapNum",
                            $"FileID={fileId}, Map={mapId}");

                    if (mapNum != monsters.Count)
                    {
                        throw new InvalidDataException(
                            $"FileID={fileId}, Map={mapId}: <MapNum>={mapNum}, " +
                            $"mas existem {monsters.Count} <Monsters>. " +
                            $"Valor correto esperado: {monsters.Count}.");
                    }

                    for (int monsterIndex = 0; monsterIndex < monsters.Count; monsterIndex++)
                    {
                        XElement monster =
                            monsters[monsterIndex];

                        string context =
                            $"FileID={fileId}, Map={mapId}, Monsters #{monsterIndex + 1}";

                        uint monsterMap =
                            RequiredUInt(
                                monster,
                                "MapID",
                                context);

                        uint monsterId =
                            RequiredUInt(
                                monster,
                                "MonsterID",
                                context);

                        if (monsterMap != mapId)
                        {
                            throw new InvalidDataException(
                                $"{context}: MapID={monsterMap}, esperado={mapId}.");
                        }

                        if (monsterId != fileId)
                        {
                            throw new InvalidDataException(
                                $"{context}: MonsterID={monsterId}, esperado={fileId}.");
                        }

                        RequiredUInt(monster, "CenterX", context);
                        RequiredUInt(monster, "CenterY", context);
                        RequiredUInt(monster, "Radius", context);
                        RequiredUInt(monster, "Count", context);
                        RequiredUInt(monster, "RespawnTime", context);
                        RequiredUInt(monster, "KillGenMonFTID", context);
                        RequiredUInt(monster, "KillgenCount", context);
                        RequiredUInt(monster, "KillgenViewCnt", context);
                        RequiredUInt(monster, "MoveType", context);
                        RequiredByte(monster, "InstRespawn", context);
                        RequiredByte(monster, "u10", context);
                        RequiredUInt16(monster, "u2", context);
                    }
                }
            }
        }

        private static long CalculateMapPayloadSize(
            XElement map)
        {
            long size = 0;

            foreach (XElement info in map.Elements("MapInformation"))
            {
                long monsters =
                    info.Elements("Monsters").LongCount();

                size +=
                    MapInformationHeaderSize +
                    (monsters * MonsterRecordSize);
            }

            return size;
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

        private static ushort ReadUInt16(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(
                br,
                2,
                field);

            return br.ReadUInt16();
        }

        private static byte ReadByte(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(
                br,
                1,
                field);

            return br.ReadByte();
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
                    $"{context}: estrutura ocupa {actual:N0} bytes; " +
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

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!ushort.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não cabe em UInt16 " +
                    "(0..65535).");
            }

            return value;
        }

        private static byte RequiredByte(
            XElement parent,
            string name,
            string context)
        {
            string raw =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!byte.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out byte value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{raw}' não cabe em Byte " +
                    "(0..255).");
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
