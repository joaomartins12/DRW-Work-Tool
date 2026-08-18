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
    public sealed class ExtraExchangeConverter : IGameDataConverter
    {
        public string Name => "ExtraExchange";

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("ExtraExchange", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("ExtraExchange", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            int npcCount =
                ReadCount(
                    br,
                    "ExtraExchange.NpcCount",
                    1_000_000);

            XElement root =
                new("ExtraExchangeNPCs");

            int totalGroups = 0;
            int totalExchanges = 0;
            int totalMaterials = 0;
            int totalSubMaterials = 0;

            for (int n = 0; n < npcCount; n++)
            {
                uint npcId = br.ReadUInt32();

                int groupCount =
                    ReadCount(
                        br,
                        $"ExtraExchange NPC {npcId}.GroupCount",
                        100_000);

                XElement npc =
                    new(
                        "ExtraExchangeNPC",
                        new XElement("NpcId", npcId));

                for (int g = 0; g < groupCount; g++)
                {
                    totalGroups++;

                    ushort id = br.ReadUInt16();
                    ushort exchangeCount = br.ReadUInt16();

                    ushort groupUnknown = br.ReadUInt16();

                    if (groupUnknown != 0)
                    {
                        throw new InvalidDataException(
                            $"ExtraExchange NPC {npcId}, Group {id}: " +
                            $"campo ushort reservado do grupo é {groupUnknown}; esperado=0.");
                    }

                    XElement group =
                        new(
                            "ExtraExchangesId",
                            new XElement("Id", id));

                    for (int e = 0; e < exchangeCount; e++)
                    {
                        totalExchanges++;

                        uint digimonId = br.ReadUInt32();
                        ushort unknown = br.ReadUInt16();
                        ushort requiredLevel = br.ReadUInt16();

                        ushort reserved1 = br.ReadUInt16();

                        if (reserved1 != 0)
                        {
                            throw new InvalidDataException(
                                $"ExtraExchange DigimonID={digimonId}: " +
                                $"reserved1={reserved1}; esperado=0.");
                        }

                        uint price = br.ReadUInt32();
                        ushort unknown1 = br.ReadUInt16();
                        ushort itemCount = br.ReadUInt16();

                        ushort reserved2 = br.ReadUInt16();

                        if (reserved2 != 0)
                        {
                            throw new InvalidDataException(
                                $"ExtraExchange DigimonID={digimonId}: " +
                                $"reserved2={reserved2}; esperado=0.");
                        }

                        XElement exchange =
                            new(
                                "ExtraExchange",
                                new XElement("DigimonID", digimonId),
                                new XElement("Unknow", unknown),
                                new XElement("RequiredLevel", requiredLevel),
                                new XElement("Unknow1", unknown1),
                                new XElement("Price", price),
                                new XElement("ItemCount", itemCount));

                        for (int i = 0; i < itemCount; i++)
                        {
                            totalMaterials++;

                            exchange.Add(
                                new XElement(
                                    "MaterialData",
                                    new XElement("ItemId", br.ReadUInt32()),
                                    new XElement("ItemCount", br.ReadUInt32())));
                        }

                        uint materialCount = br.ReadUInt32();

                        exchange.Add(
                            new XElement(
                                "MaterialCount",
                                materialCount));

                        if (materialCount > 100_000)
                        {
                            throw new InvalidDataException(
                                $"ExtraExchange DigimonID={digimonId}: " +
                                $"MaterialCount absurdo ({materialCount}).");
                        }

                        for (uint i = 0; i < materialCount; i++)
                        {
                            totalSubMaterials++;

                            exchange.Add(
                                new XElement(
                                    "SubMaterialData",
                                    new XElement("ItemId", br.ReadUInt32()),
                                    new XElement("ItemCount", br.ReadUInt32())));
                        }

                        group.Add(exchange);
                    }

                    npc.Add(group);
                }

                root.Add(npc);
            }

            if (ms.Position != ms.Length)
            {
                long extra =
                    ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"ExtraExchange.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho={ms.Length:N0}.");
            }

            string? folder =
                Path.GetDirectoryName(outputXml);

            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidDataException(
                    "Não foi possível determinar XML\\ExtraExchange.");
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
                $"ExtraExchange: BIN -> XML concluído. " +
                $"NPCs={npcCount:N0}, Groups={totalGroups:N0}, " +
                $"Exchanges={totalExchanges:N0}, " +
                $"Materials={totalMaterials:N0}, " +
                $"SubMaterials={totalSubMaterials:N0}.");

            AppLogger.Log(
                $"ExtraExchange: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc =
                LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "ExtraExchangeNPCs",
                    "ExtraExchange.xml");

            List<XElement> npcs =
                root.Elements("ExtraExchangeNPC").ToList();

            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteTable(
                    test,
                    npcs);

                test.Flush();
                expectedSize = testStream.Length;
            }

            string? outputFolder =
                Path.GetDirectoryName(outputBin);

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                throw new InvalidDataException(
                    "Pasta Output inválida para ExtraExchange.");
            }

            Directory.CreateDirectory(outputFolder);

            using FileStream fs =
                File.Create(outputBin);

            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteTable(
                bw,
                npcs);

            bw.Flush();

            long actualSize =
                fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"ExtraExchange.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            int groups =
                npcs.Sum(
                    n => n.Elements("ExtraExchangesId").Count());

            int exchanges =
                npcs.Sum(
                    n => n.Elements("ExtraExchangesId")
                          .Sum(g => g.Elements("ExtraExchange").Count()));

            AppLogger.Log(
                $"ExtraExchange: XML -> BIN concluído. " +
                $"NPCs={npcs.Count:N0}, Groups={groups:N0}, " +
                $"Exchanges={exchanges:N0}.");

            AppLogger.Log(
                $"ExtraExchange: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void WriteTable(
            BinaryWriter bw,
            IReadOnlyList<XElement> npcs)
        {
            bw.Write(npcs.Count);

            for (int n = 0; n < npcs.Count; n++)
            {
                XElement npc =
                    npcs[n];

                uint npcId =
                    RequiredUInt(
                        npc,
                        "NpcId",
                        $"ExtraExchangeNPC #{n}");

                List<XElement> groups =
                    npc.Elements("ExtraExchangesId").ToList();

                bw.Write(npcId);
                bw.Write(groups.Count);

                for (int g = 0; g < groups.Count; g++)
                {
                    XElement group =
                        groups[g];

                    ushort id =
                        RequiredUInt16(
                            group,
                            "Id",
                            $"ExtraExchange NPC {npcId}, Group #{g}");

                    List<XElement> exchanges =
                        group.Elements("ExtraExchange").ToList();

                    if (exchanges.Count > ushort.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"ExtraExchange NPC {npcId}, Group {id}: " +
                            $"{exchanges.Count:N0} exchanges excedem UInt16.");
                    }

                    bw.Write(id);
                    bw.Write((ushort)exchanges.Count);

                    // Campo físico reservado confirmado sempre 0.
                    bw.Write((ushort)0);

                    for (int e = 0; e < exchanges.Count; e++)
                    {
                        XElement exchange =
                            exchanges[e];

                        uint digimonId =
                            RequiredUInt(
                                exchange,
                                "DigimonID",
                                $"ExtraExchange NPC {npcId}, Group {id}, #{e}");

                        string context =
                            $"ExtraExchange DigimonID={digimonId}";

                        List<XElement> materials =
                            exchange.Elements("MaterialData").ToList();

                        List<XElement> subMaterials =
                            exchange.Elements("SubMaterialData").ToList();

                        ushort declaredItemCount =
                            RequiredUInt16(
                                exchange,
                                "ItemCount",
                                context);

                        uint declaredMaterialCount =
                            RequiredUInt(
                                exchange,
                                "MaterialCount",
                                context);

                        if (declaredItemCount != materials.Count)
                        {
                            throw new InvalidDataException(
                                $"{context}: <ItemCount>={declaredItemCount}, " +
                                $"mas existem {materials.Count} <MaterialData>.");
                        }

                        if (declaredMaterialCount != subMaterials.Count)
                        {
                            throw new InvalidDataException(
                                $"{context}: <MaterialCount>={declaredMaterialCount}, " +
                                $"mas existem {subMaterials.Count} <SubMaterialData>.");
                        }

                        bw.Write(digimonId);

                        bw.Write(
                            RequiredUInt16(
                                exchange,
                                "Unknow",
                                context));

                        bw.Write(
                            RequiredUInt16(
                                exchange,
                                "RequiredLevel",
                                context));

                        // reserved1
                        bw.Write((ushort)0);

                        bw.Write(
                            RequiredUInt(
                                exchange,
                                "Price",
                                context));

                        bw.Write(
                            RequiredUInt16(
                                exchange,
                                "Unknow1",
                                context));

                        bw.Write(declaredItemCount);

                        // reserved2
                        bw.Write((ushort)0);

                        foreach (XElement material in materials)
                        {
                            WriteItemData(
                                bw,
                                material,
                                context + " MaterialData");
                        }

                        bw.Write(declaredMaterialCount);

                        foreach (XElement material in subMaterials)
                        {
                            WriteItemData(
                                bw,
                                material,
                                context + " SubMaterialData");
                        }
                    }
                }
            }
        }

        private static void WriteItemData(
            BinaryWriter bw,
            XElement row,
            string context)
        {
            bw.Write(
                RequiredUInt(
                    row,
                    "ItemId",
                    context));

            bw.Write(
                RequiredUInt(
                    row,
                    "ItemCount",
                    context));
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

            string value =
                element.Value;

            if (string.IsNullOrWhiteSpace(value))
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

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!ushort.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em UInt16 (0..65535).");
            }

            return result;
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
                        OmitXmlDeclaration = false
                    });

            document.Save(writer);
        }
    }
}
