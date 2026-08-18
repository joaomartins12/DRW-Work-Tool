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
    public sealed class NpcConverter : IGameDataConverter
    {
        public string Name => "Npc";

        private const int NpcTagChars = 32;
        private const int NpcNameChars = 32;
        private const int NpcDescChars = 512;
        private const int NpcFixedCoreSize = 1176;

        private const int ModelRecordSize = 140;
        private const int ModelCommentChars = 60;

        private const int PortalReqItems = 3;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Npc", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Npc", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Npc.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            long npcStart = ms.Position;
            XDocument npcDoc = ReadNpcTable(br);
            long npcEnd = ms.Position;

            long modelStart = ms.Position;
            XDocument modelDoc = ReadModelTable(br);
            long modelEnd = ms.Position;

            long eventStart = ms.Position;
            XDocument eventDoc = ReadEventTable(br);
            long eventEnd = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Npc.bin contém {extra:N0} bytes extra após a estrutura conhecida. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"ficheiro possui {ms.Length:N0} bytes.");
            }

            SaveXml(npcDoc, Path.Combine(folder, "Npc.xml"));
            SaveXml(modelDoc, Path.Combine(folder, "ModelNpc.xml"));
            SaveXml(eventDoc, Path.Combine(folder, "EventNpc.xml"));

            AppLogger.Log(
                "Npc: BIN -> XML concluído. 3 XMLs gerados.");

            AppLogger.Log(
                $"Npc: secções em bytes -> " +
                $"Npc={npcEnd - npcStart:N0}, " +
                $"ModelNpc={modelEnd - modelStart:N0}, " +
                $"EventNpc={eventEnd - eventStart:N0}.");

            AppLogger.Log(
                $"Npc: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Npc.");

            string npcPath = Path.Combine(folder, "Npc.xml");
            string modelPath = Path.Combine(folder, "ModelNpc.xml");
            string eventPath = Path.Combine(folder, "EventNpc.xml");

            foreach (string path in new[] { npcPath, modelPath, eventPath })
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Npc: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument npcDoc = LoadXml(npcPath);
            XDocument modelDoc = LoadXml(modelPath);
            XDocument eventDoc = LoadXml(eventPath);

            long expectedSize;

            // Validação integral antes de criar o Output.
            using (MemoryStream counter = new())
            using (BinaryWriter test =
                new(counter, Encoding.UTF8, leaveOpen: true))
            {
                WriteNpcTable(test, npcDoc);
                WriteModelTable(test, modelDoc);
                WriteEventTable(test, eventDoc);
                test.Flush();

                expectedSize = counter.Length;
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Npc."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteNpcTable(bw, npcDoc);
            WriteModelTable(bw, modelDoc);
            WriteEventTable(bw, eventDoc);

            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Npc.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "Npc: XML -> BIN concluído. 3 XMLs validados.");

            AppLogger.Log(
                $"Npc: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        // ============================================================
        // NPC PRINCIPAL
        // ============================================================

        private static XDocument ReadNpcTable(BinaryReader br)
        {
            int count = ReadCount(br, "Npc.Count", 1_000_000);
            XElement root = new("NPCs");

            for (int index = 0; index < count; index++)
            {
                long recordStart = br.BaseStream.Position;

                int npcId = br.ReadInt32();
                int mapId = br.ReadInt32();
                int npcType = br.ReadInt32();
                int npcMove = br.ReadInt32();
                int displayFlag = br.ReadInt32();
                int model = br.ReadInt32();

                string tag = ReadFixedUnicode(br, NpcTagChars);
                string name = ReadFixedUnicode(br, NpcNameChars);
                string desc = ReadFixedUnicode(br, NpcDescChars);

                long fixedConsumed =
                    br.BaseStream.Position - recordStart;

                if (fixedConsumed != NpcFixedCoreSize)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: core fixo ocupou {fixedConsumed} bytes; " +
                        $"esperado={NpcFixedCoreSize}.");
                }

                XElement npc =
                    new(
                        "NPC",
                        new XElement("NpcID", npcId),
                        new XElement("MapID", mapId),
                        new XElement("NPCType", npcType),
                        new XElement("NPCMOVE", npcMove),
                        new XElement("s_nDisplayPlag", displayFlag),
                        new XElement("NPCTag", tag),
                        new XElement("NPCName", name),
                        new XElement("Model", model),
                        new XElement("NPCDesc", desc));

                ReadTypeSpecificData(br, npc, npcType, npcId);

                int extra = br.ReadInt32();
                npc.Add(new XElement("nExtraData", extra));

                if (extra == 1)
                {
                    ReadQuestData(br, npc, npcId);
                }
                else if (extra != 0)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: nExtraData={extra}. " +
                        "O formato confirmado aceita apenas 0 ou 1.");
                }

                root.Add(npc);
            }

            return Xml(root);
        }

        private static void ReadTypeSpecificData(
            BinaryReader br,
            XElement npc,
            int npcType,
            int npcId)
        {
            switch (npcType)
            {
                // Shop/Exchange NPCs: count + ItemID[]
                case 1:
                case 8:
                case 9:
                case 12:
                case 14:
                {
                    int itemCount =
                        ReadCount(
                            br,
                            $"NPC {npcId}.ItemCount",
                            100_000);

                    XElement items = new("ItemIDs");

                    for (int i = 0; i < itemCount; i++)
                    {
                        items.Add(
                            new XElement(
                                "ItemID",
                                br.ReadInt32()));
                    }

                    npc.Add(items);
                    break;
                }

                // Portal NPC
                case 3:
                {
                    int portalType = br.ReadInt32();
                    int portalCount =
                        ReadCount(
                            br,
                            $"NPC {npcId}.PortalCount",
                            100_000);

                    XElement portalTypes = new("PortalsType");

                    for (int p = 0; p < portalCount; p++)
                    {
                        int eventId = br.ReadInt32();

                        XElement req = new("Req");

                        for (int r = 0; r < PortalReqItems; r++)
                        {
                            req.Add(
                                new XElement(
                                    "ReqItem",
                                    new XElement("s_eEnableType", br.ReadInt32()),
                                    new XElement("s_nEnableID", br.ReadInt32()),
                                    new XElement("s_nEnableCount", br.ReadInt32())));
                        }

                        portalTypes.Add(
                            new XElement(
                                "PortalType",
                                new XElement("s_dwEventID", eventId),
                                req));
                    }

                    npc.Add(
                        new XElement(
                            "Portals",
                            new XElement(
                                "Portal",
                                new XElement("s_nPortalType", portalType),
                                new XElement("s_nPortalCount", portalCount),
                                portalTypes)));

                    break;
                }

                // Masters Match
                case 16:
                {
                    int itemCount =
                        ReadCount(
                            br,
                            $"NPC {npcId}.MatchItemCount",
                            100_000);

                    XElement items = new("MatchItemIDs");

                    for (int i = 0; i < itemCount; i++)
                    {
                        items.Add(
                            new XElement(
                                "ItemID",
                                br.ReadInt32()));
                    }

                    npc.Add(items);
                    break;
                }

                // Special Event
                case 19:
                {
                    int nvType = br.ReadInt32();

                    if (nvType != 0)
                    {
                        int itemCount =
                            ReadCount(
                                br,
                                $"NPC {npcId}.SpecialEventItemCount",
                                100_000);

                        XElement special =
                            new(
                                "SpecialEventItems",
                                new XElement("nvType", nvType));

                        for (int i = 0; i < itemCount; i++)
                        {
                            special.Add(
                                new XElement(
                                    "ItemID",
                                    br.ReadInt32()));
                        }

                        npc.Add(special);
                    }

                    break;
                }
            }
        }

        private static void ReadQuestData(
            BinaryReader br,
            XElement npc,
            int npcId)
        {
            int reserved = br.ReadInt32();

            if (reserved != 0)
            {
                throw new InvalidDataException(
                    $"NPC {npcId}: DWORD reservado antes de Quest é {reserved}; " +
                    "esperado=0.");
            }

            int initialState = br.ReadInt32();

            int actionCount =
                ReadCount(
                    br,
                    $"NPC {npcId}.Quest.ActionCount",
                    100_000);

            XElement quest =
                new(
                    "Quest",
                    new XElement("s_nEInitSate", initialState),
                    new XElement("nActcnt", actionCount));

            for (int a = 0; a < actionCount; a++)
            {
                int actionType = br.ReadInt32();
                int compState = br.ReadInt32();

                int questCount =
                    ReadCount(
                        br,
                        $"NPC {npcId}.Quest.Action[{a}].QuestCount",
                        100_000);

                XElement ids = new("QuestIds");

                for (int q = 0; q < questCount; q++)
                {
                    ids.Add(
                        new XElement(
                            "QuestId",
                            br.ReadInt32()));
                }

                quest.Add(
                    new XElement(
                        "Action",
                        new XElement("ActionType", actionType),
                        new XElement("ECompState", compState),
                        new XElement("QuestCount", questCount),
                        ids));
            }

            npc.Add(quest);
        }

        private static void WriteNpcTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "NPCs",
                    "Npc.xml");

            List<XElement> rows =
                root.Elements("NPC").ToList();

            bw.Write(rows.Count);

            foreach (XElement npc in rows)
            {
                int npcId =
                    RequiredInt(
                        npc,
                        "NpcID",
                        "Npc.xml");

                int npcType =
                    RequiredInt(
                        npc,
                        "NPCType",
                        $"NPC {npcId}");

                long recordStart =
                    bw.BaseStream.Position;

                bw.Write(npcId);
                bw.Write(RequiredInt(npc, "MapID", $"NPC {npcId}"));
                bw.Write(npcType);
                bw.Write(RequiredInt(npc, "NPCMOVE", $"NPC {npcId}"));
                bw.Write(RequiredInt(npc, "s_nDisplayPlag", $"NPC {npcId}"));
                bw.Write(RequiredInt(npc, "Model", $"NPC {npcId}"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(npc, "NPCTag", $"NPC {npcId}", true),
                    NpcTagChars,
                    $"NPC {npcId} <NPCTag>");

                WriteFixedUnicode(
                    bw,
                    RequiredText(npc, "NPCName", $"NPC {npcId}", true),
                    NpcNameChars,
                    $"NPC {npcId} <NPCName>");

                WriteFixedUnicode(
                    bw,
                    RequiredText(npc, "NPCDesc", $"NPC {npcId}", true),
                    NpcDescChars,
                    $"NPC {npcId} <NPCDesc>");

                long fixedConsumed =
                    bw.BaseStream.Position - recordStart;

                if (fixedConsumed != NpcFixedCoreSize)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: core gerado ocupou {fixedConsumed} bytes; " +
                        $"esperado={NpcFixedCoreSize}.");
                }

                WriteTypeSpecificData(
                    bw,
                    npc,
                    npcType,
                    npcId);

                int extra =
                    RequiredInt(
                        npc,
                        "nExtraData",
                        $"NPC {npcId}");

                if (extra != 0 && extra != 1)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: <nExtraData>={extra}. " +
                        "O formato confirmado aceita apenas 0 ou 1.");
                }

                bw.Write(extra);

                XElement? quest =
                    npc.Element("Quest");

                if (extra == 1)
                {
                    if (quest == null)
                    {
                        throw new InvalidDataException(
                            $"NPC {npcId}: nExtraData=1 mas falta <Quest>.");
                    }

                    WriteQuestData(
                        bw,
                        quest,
                        npcId);
                }
                else if (quest != null)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: existe <Quest>, mas nExtraData=0. " +
                        "Define <nExtraData>1</nExtraData> ou remove a Quest.");
                }
            }
        }

        private static void WriteTypeSpecificData(
            BinaryWriter bw,
            XElement npc,
            int npcType,
            int npcId)
        {
            switch (npcType)
            {
                case 1:
                case 8:
                case 9:
                case 12:
                case 14:
                {
                    XElement? container = npc.Element("ItemIDs");

                    List<XElement> items =
                        container?
                            .Elements("ItemID")
                            .ToList()
                        ?? new List<XElement>();

                    bw.Write(items.Count);

                    foreach (XElement item in items)
                    {
                        bw.Write(
                            ParseInt(
                                item.Value,
                                $"NPC {npcId} <ItemID>"));
                    }

                    break;
                }

                case 3:
                {
                    XElement? portals =
                        npc.Element("Portals");

                    XElement? portal =
                        portals?.Element("Portal");

                    if (portal == null)
                    {
                        throw new InvalidDataException(
                            $"NPC {npcId}: NPCType=3 exige " +
                            "<Portals><Portal>...</Portal></Portals>.");
                    }

                    int portalType =
                        RequiredInt(
                            portal,
                            "s_nPortalType",
                            $"NPC {npcId} Portal");

                    int declaredCount =
                        RequiredInt(
                            portal,
                            "s_nPortalCount",
                            $"NPC {npcId} Portal");

                    XElement? typesContainer =
                        portal.Element("PortalsType");

                    List<XElement> portalTypes =
                        typesContainer?
                            .Elements("PortalType")
                            .ToList()
                        ?? new List<XElement>();

                    if (declaredCount != portalTypes.Count)
                    {
                        throw new InvalidDataException(
                            $"NPC {npcId}: <s_nPortalCount>={declaredCount}, " +
                            $"mas existem {portalTypes.Count} <PortalType>.");
                    }

                    bw.Write(portalType);
                    bw.Write(portalTypes.Count);

                    foreach (XElement portalTypeElement in portalTypes)
                    {
                        bw.Write(
                            RequiredInt(
                                portalTypeElement,
                                "s_dwEventID",
                                $"NPC {npcId} PortalType"));

                        XElement? req =
                            portalTypeElement.Element("Req");

                        List<XElement> reqItems =
                            req?
                                .Elements("ReqItem")
                                .ToList()
                            ?? new List<XElement>();

                        if (reqItems.Count != PortalReqItems)
                        {
                            throw new InvalidDataException(
                                $"NPC {npcId}: cada PortalType exige exatamente " +
                                $"{PortalReqItems} <ReqItem>; encontrados {reqItems.Count}.");
                        }

                        foreach (XElement item in reqItems)
                        {
                            bw.Write(
                                RequiredInt(
                                    item,
                                    "s_eEnableType",
                                    $"NPC {npcId} ReqItem"));

                            bw.Write(
                                RequiredInt(
                                    item,
                                    "s_nEnableID",
                                    $"NPC {npcId} ReqItem"));

                            bw.Write(
                                RequiredInt(
                                    item,
                                    "s_nEnableCount",
                                    $"NPC {npcId} ReqItem"));
                        }
                    }

                    break;
                }

                case 16:
                {
                    XElement? container =
                        npc.Element("MatchItemIDs");

                    List<XElement> items =
                        container?
                            .Elements("ItemID")
                            .ToList()
                        ?? new List<XElement>();

                    bw.Write(items.Count);

                    foreach (XElement item in items)
                    {
                        bw.Write(
                            ParseInt(
                                item.Value,
                                $"NPC {npcId} <MatchItemIDs>/<ItemID>"));
                    }

                    break;
                }

                case 19:
                {
                    XElement? special =
                        npc.Element("SpecialEventItems");

                    if (special == null)
                    {
                        // O formato grava sempre nvType.
                        bw.Write(0);
                        break;
                    }

                    int nvType =
                        RequiredInt(
                            special,
                            "nvType",
                            $"NPC {npcId} SpecialEventItems");

                    if (nvType == 0)
                    {
                        throw new InvalidDataException(
                            $"NPC {npcId}: existe <SpecialEventItems>, " +
                            "mas <nvType> é 0. Remove a secção ou usa nvType != 0.");
                    }

                    List<XElement> items =
                        special.Elements("ItemID").ToList();

                    bw.Write(nvType);
                    bw.Write(items.Count);

                    foreach (XElement item in items)
                    {
                        bw.Write(
                            ParseInt(
                                item.Value,
                                $"NPC {npcId} SpecialEventItems <ItemID>"));
                    }

                    break;
                }

                default:
                {
                    ValidateUnexpectedTypeSpecificElements(
                        npc,
                        npcId,
                        npcType);
                    break;
                }
            }
        }

        private static void ValidateUnexpectedTypeSpecificElements(
            XElement npc,
            int npcId,
            int npcType)
        {
            foreach (string name in new[]
            {
                "ItemIDs",
                "Portals",
                "MatchItemIDs",
                "SpecialEventItems"
            })
            {
                if (npc.Element(name) != null)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: NPCType={npcType} não possui layout " +
                        $"binário confirmado para <{name}>.");
                }
            }
        }

        private static void WriteQuestData(
            BinaryWriter bw,
            XElement quest,
            int npcId)
        {
            // DWORD físico omitido do XML antigo.
            bw.Write(0);

            bw.Write(
                RequiredInt(
                    quest,
                    "s_nEInitSate",
                    $"NPC {npcId} Quest"));

            List<XElement> actions =
                quest.Elements("Action").ToList();

            int declaredActions =
                RequiredInt(
                    quest,
                    "nActcnt",
                    $"NPC {npcId} Quest");

            if (declaredActions != actions.Count)
            {
                throw new InvalidDataException(
                    $"NPC {npcId}: <nActcnt>={declaredActions}, " +
                    $"mas existem {actions.Count} <Action>.");
            }

            bw.Write(actions.Count);

            foreach (XElement action in actions)
            {
                bw.Write(
                    RequiredInt(
                        action,
                        "ActionType",
                        $"NPC {npcId} Quest Action"));

                bw.Write(
                    RequiredInt(
                        action,
                        "ECompState",
                        $"NPC {npcId} Quest Action"));

                XElement? idsContainer =
                    action.Element("QuestIds");

                List<XElement> ids =
                    idsContainer?
                        .Elements("QuestId")
                        .ToList()
                    ?? new List<XElement>();

                int declaredQuests =
                    RequiredInt(
                        action,
                        "QuestCount",
                        $"NPC {npcId} Quest Action");

                if (declaredQuests != ids.Count)
                {
                    throw new InvalidDataException(
                        $"NPC {npcId}: <QuestCount>={declaredQuests}, " +
                        $"mas existem {ids.Count} <QuestId>.");
                }

                bw.Write(ids.Count);

                foreach (XElement id in ids)
                {
                    bw.Write(
                        ParseInt(
                            id.Value,
                            $"NPC {npcId} <QuestId>"));
                }
            }
        }

        // ============================================================
        // MODEL NPC
        // ============================================================

        private static XDocument ReadModelTable(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "ModelNpc.Count",
                    1_000_000);

            XElement root = new("NPCs");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                int modelId = br.ReadInt32();
                int offset = br.ReadInt32();
                int offset1 = br.ReadInt32();
                int offset2 = br.ReadInt32();

                string comment =
                    ReadFixedUnicode(
                        br,
                        ModelCommentChars);

                int unknown =
                    br.ReadInt32();

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != ModelRecordSize)
                {
                    throw new InvalidDataException(
                        $"ModelNpc {modelId}: record ocupa {consumed} bytes; " +
                        $"esperado={ModelRecordSize}.");
                }

                root.Add(
                    new XElement(
                        "NPC",
                        new XElement("s_nModelID", modelId),
                        new XElement("s_nOffset", offset),
                        new XElement("s_nOffset1", offset1),
                        new XElement("s_nOffset2", offset2),
                        new XElement("s_szComment", comment),
                        new XElement("unknowvalue", unknown)));
            }

            return Xml(root);
        }

        private static void WriteModelTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "NPCs",
                    "ModelNpc.xml");

            List<XElement> rows =
                root.Elements("NPC").ToList();

            bw.Write(rows.Count);

            foreach (XElement npc in rows)
            {
                int modelId =
                    RequiredInt(
                        npc,
                        "s_nModelID",
                        "ModelNpc.xml");

                long start =
                    bw.BaseStream.Position;

                bw.Write(modelId);
                bw.Write(RequiredInt(npc, "s_nOffset", $"ModelNpc {modelId}"));
                bw.Write(RequiredInt(npc, "s_nOffset1", $"ModelNpc {modelId}"));
                bw.Write(RequiredInt(npc, "s_nOffset2", $"ModelNpc {modelId}"));

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        npc,
                        "s_szComment",
                        $"ModelNpc {modelId}",
                        true),
                    ModelCommentChars,
                    $"ModelNpc {modelId} <s_szComment>");

                bw.Write(
                    RequiredInt(
                        npc,
                        "unknowvalue",
                        $"ModelNpc {modelId}"));

                long consumed =
                    bw.BaseStream.Position - start;

                if (consumed != ModelRecordSize)
                {
                    throw new InvalidDataException(
                        $"ModelNpc {modelId}: record gerado ocupa {consumed} bytes; " +
                        $"esperado={ModelRecordSize}.");
                }
            }
        }

        // ============================================================
        // EVENT NPC
        // ============================================================

        private static XDocument ReadEventTable(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "EventNpc.Count",
                    100_000);

            XElement root = new("NPCs");

            for (int i = 0; i < count; i++)
            {
                int npcId = br.ReadInt32();
                int tries = br.ReadInt32();
                int money = br.ReadInt32();
                int exhaustItem = br.ReadInt32();

                int itemCount =
                    ReadCount(
                        br,
                        $"EventNpc {npcId}.ItemCount",
                        100_000);

                XElement items = new("s_maxItems");

                for (int item = 0; item < itemCount; item++)
                {
                    items.Add(
                        new XElement(
                            "Item",
                            new XElement("s_nItemID", br.ReadInt32()),
                            new XElement("s_nMaxCount", br.ReadInt32())));
                }

                root.Add(
                    new XElement(
                        "NPC",
                        new XElement("s_nNpcID", npcId),
                        new XElement("s_nTry", tries),
                        new XElement("s_nExhaustMoney", money),
                        new XElement("s_dwExhaustItem", exhaustItem),
                        new XElement("s_unItemCount", itemCount),
                        items));
            }

            return Xml(root);
        }

        private static void WriteEventTable(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "NPCs",
                    "EventNpc.xml");

            List<XElement> rows =
                root.Elements("NPC").ToList();

            bw.Write(rows.Count);

            foreach (XElement npc in rows)
            {
                int npcId =
                    RequiredInt(
                        npc,
                        "s_nNpcID",
                        "EventNpc.xml");

                XElement? itemsContainer =
                    npc.Element("s_maxItems");

                List<XElement> items =
                    itemsContainer?
                        .Elements("Item")
                        .ToList()
                    ?? new List<XElement>();

                int declared =
                    RequiredInt(
                        npc,
                        "s_unItemCount",
                        $"EventNpc {npcId}");

                if (declared != items.Count)
                {
                    throw new InvalidDataException(
                        $"EventNpc {npcId}: <s_unItemCount>={declared}, " +
                        $"mas existem {items.Count} <Item>.");
                }

                bw.Write(npcId);
                bw.Write(RequiredInt(npc, "s_nTry", $"EventNpc {npcId}"));
                bw.Write(RequiredInt(npc, "s_nExhaustMoney", $"EventNpc {npcId}"));
                bw.Write(RequiredInt(npc, "s_dwExhaustItem", $"EventNpc {npcId}"));
                bw.Write(items.Count);

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredInt(
                            item,
                            "s_nItemID",
                            $"EventNpc {npcId} Item"));

                    bw.Write(
                        RequiredInt(
                            item,
                            "s_nMaxCount",
                            $"EventNpc {npcId} Item"));
                }
            }
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] bytes =
                br.ReadBytes(count);

            if (bytes.Length != count)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {count} bytes; recebidos {bytes.Length}.");
            }

            return bytes;
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            byte[] raw =
                ReadExact(
                    br,
                    wcharCount * 2,
                    "UTF-16LE fixed string");

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
            string text,
            int wcharCount,
            string field)
        {
            byte[] raw =
                Encoding.Unicode.GetBytes(
                    text ?? string.Empty);

            int maxBytes =
                (wcharCount - 1) * 2;

            if (raw.Length > maxBytes)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes UTF-16LE; " +
                    $"o buffer suporta no máximo {maxBytes} bytes úteis " +
                    $"({wcharCount - 1} caracteres + terminador NUL).");
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

        private static int RequiredInt(
            XElement parent,
            string name,
            string context) =>
            ParseInt(
                RequiredText(
                    parent,
                    name,
                    context),
                $"{context} <{name}>");

        private static int ParseInt(
            string value,
            string context)
        {
            if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não é Int32 válido.");
            }

            return result;
        }

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
