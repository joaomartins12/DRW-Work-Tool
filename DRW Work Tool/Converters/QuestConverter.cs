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
    public sealed class QuestConverter : IGameDataConverter
    {
        public string Name => "Quest";

        private const uint MaxClientUniqId = 6144;
        private const int MaxItemRewardsPerQuest = 6;

        private const int TitleTabChars = 80;
        private const int TitleTextChars = 80;
        private const int BodyChars = 2048;
        private const int SimpleChars = 128;
        private const int HelperChars = 512;
        private const int ProcessChars = 320;
        private const int CompleteChars = 700;
        private const int ExpertChars = 320;

        // 49 bytes de cabeçalho + 8 buffers wchar_t fixos.
        private const int FixedRecordPrefixSize = 8425;

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Quest", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Quest", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br =
                new(ms, Encoding.UTF8, leaveOpen: true);

            int questCount =
                ReadCount(
                    br,
                    "Quest.Count",
                    1_000_000);

            XElement root = new("QuestInfos");

            int overRewardLimit = 0;
            int overUniqIdLimit = 0;
            int maxItemRewardsSeen = 0;
            uint maxUniqIdSeen = 0;

            for (int i = 0; i < questCount; i++)
            {
                long recordStart = ms.Position;

                uint uniqId = br.ReadUInt32();
                uint model = br.ReadUInt32();
                uint model2 = br.ReadUInt32();

                ushort level = br.ReadUInt16();

                int pos = br.ReadInt32();
                int pos2 = br.ReadInt32();
                int managedId = br.ReadInt32();

                byte active = br.ReadByte();
                byte immediate = br.ReadByte();
                byte unknown = br.ReadByte();

                int type = br.ReadInt32();
                int startTargetType = br.ReadInt32();
                int startTargetId = br.ReadInt32();
                int target = br.ReadInt32();
                int targetValue = br.ReadInt32();

                string titleTab =
                    ReadFixedUnicode(br, TitleTabChars, $"Quest UniqID={uniqId} TitleTab");

                string titleText =
                    ReadFixedUnicode(br, TitleTextChars, $"Quest UniqID={uniqId} TitleText");

                string body =
                    ReadFixedUnicode(br, BodyChars, $"Quest UniqID={uniqId} Body");

                string simple =
                    ReadFixedUnicode(br, SimpleChars, $"Quest UniqID={uniqId} Simple");

                string helper =
                    ReadFixedUnicode(br, HelperChars, $"Quest UniqID={uniqId} Helper");

                string process =
                    ReadFixedUnicode(br, ProcessChars, $"Quest UniqID={uniqId} Process");

                string complete =
                    ReadFixedUnicode(br, CompleteChars, $"Quest UniqID={uniqId} Complete");

                string expert =
                    ReadFixedUnicode(br, ExpertChars, $"Quest UniqID={uniqId} Expert");

                long fixedConsumed = ms.Position - recordStart;

                if (fixedConsumed != FixedRecordPrefixSize)
                {
                    throw new InvalidDataException(
                        $"Quest UniqID={uniqId}: prefixo fixo ocupa {fixedConsumed:N0} bytes; " +
                        $"esperado={FixedRecordPrefixSize:N0}.");
                }

                // ---------------- Quest Items ----------------
                int itemGivenCount =
                    ReadCount(
                        br,
                        $"Quest UniqID={uniqId}.Itemgiven",
                        100_000);

                XElement questItems = new("QuestItems");

                for (int q = 0; q < itemGivenCount; q++)
                {
                    questItems.Add(
                        new XElement(
                            "QuestItem",
                            new XElement("Itemgiven", br.ReadInt32()),
                            new XElement("ItemgivenType", br.ReadInt32()),
                            new XElement("ItemgivenAmount", br.ReadInt32())));
                }

                // ---------------- Conditions ----------------
                int conditionCount =
                    ReadCount(
                        br,
                        $"Quest UniqID={uniqId}.condition",
                        100_000);

                XElement questConditions = new("QuestConditions");

                for (int c = 0; c < conditionCount; c++)
                {
                    questConditions.Add(
                        new XElement(
                            "QuestCondition",
                            new XElement("ConditionType", br.ReadInt32()),
                            new XElement("ConditionId", br.ReadInt32()),
                            new XElement("ConditionCount", br.ReadInt32())));
                }

                // ---------------- Goals ----------------
                int goalCount =
                    ReadCount(
                        br,
                        $"Quest UniqID={uniqId}.Goals",
                        100_000);

                XElement questGoals = new("QuestGoals");

                for (int g = 0; g < goalCount; g++)
                {
                    questGoals.Add(
                        new XElement(
                            "QuestGoal",
                            new XElement("GoalType", br.ReadInt32()),
                            new XElement("GoalId", br.ReadInt32()),

                            // GoalCount existe no XML, mas NÃO possui bytes
                            // físicos neste formato do Quest.bin.
                            new XElement("GoalCount", 0),

                            new XElement("goalAmount", br.ReadInt32()),
                            new XElement("CurTypeCount", br.ReadInt32()),
                            new XElement("SubValue", br.ReadInt32()),
                            new XElement("SubValue1", br.ReadInt32())));
                }

                // ---------------- Rewards ----------------
                int rewardCount =
                    ReadCount(
                        br,
                        $"Quest UniqID={uniqId}.RewardNumber",
                        100_000);

                XElement rewardQuantities = new("RewardQuantities");
                int itemRewardCount = 0;

                for (int r = 0; r < rewardCount; r++)
                {
                    int reward = br.ReadInt32();
                    int rewardType = br.ReadInt32();
                    int value1 = br.ReadInt32();
                    int value2 = br.ReadInt32();

                    XElement money = new("QuestRewardMoney");
                    XElement items = new("QuestRewardItems");

                    if (rewardType == 0)
                    {
                        money.Add(
                            new XElement(
                                "QuestRewardMoneyItem",
                                new XElement("RewardMoney", value1),
                                new XElement("RewardUnk", value2)));
                    }
                    else
                    {
                        items.Add(
                            new XElement(
                                "QuestRewardItemsItem",
                                new XElement("RewardItem", value1),
                                new XElement("RewardAmount", value2)));

                        if (rewardType == 2)
                        {
                            itemRewardCount++;
                        }
                    }

                    rewardQuantities.Add(
                        new XElement(
                            "RewardQuantity",
                            new XElement("Reward", reward),
                            new XElement("RewardType", rewardType),
                            money,
                            items));
                }

                // ---------------- Events ----------------
                int eventCount =
                    ReadCount(
                        br,
                        $"Quest UniqID={uniqId}.EventCount",
                        100_000);

                XElement eventRoot = new("Event");

                for (int e = 0; e < eventCount; e++)
                {
                    eventRoot.Add(
                        new XElement("EventId", br.ReadInt32()));
                }

                XElement quest =
                    new(
                        "QuestInfo",
                        new XElement("UniqID", uniqId),
                        new XElement("Model", model),
                        new XElement("Model2", model2),
                        new XElement("Level", level),
                        new XElement("Pos", pos),
                        new XElement("Pos2", pos2),
                        new XElement("ManagedID", managedId),
                        new XElement("Active", active),
                        new XElement("Unknown", unknown),
                        new XElement("Immediate", immediate),

                        // ResetQuest existe apenas no XML desta versão.
                        // Não há bytes físicos correspondentes.
                        new XElement("ResetQuest", 0),

                        new XElement("Type", type),
                        new XElement("StartTargetType", startTargetType),
                        new XElement("StartTargetID", startTargetId),
                        new XElement("Target", target),
                        new XElement("TargetValue", targetValue),

                        new XElement("TitleTab", titleTab),
                        new XElement("TitleText", titleText),
                        new XElement("Body", body),
                        new XElement("Simple", simple),
                        new XElement("Helper", helper),
                        new XElement("Process", process),
                        new XElement("Complete", complete),
                        new XElement("Expert", expert),

                        new XElement("Itemgiven", itemGivenCount),
                        questItems,

                        new XElement("condition", conditionCount),
                        questConditions,

                        new XElement("Goals", goalCount),
                        questGoals,

                        new XElement("RewardNumber", rewardCount),
                        rewardQuantities,

                        eventRoot);

                root.Add(quest);

                if (uniqId > maxUniqIdSeen)
                    maxUniqIdSeen = uniqId;

                if (itemRewardCount > maxItemRewardsSeen)
                    maxItemRewardsSeen = itemRewardCount;

                if (uniqId > MaxClientUniqId)
                    overUniqIdLimit++;

                if (itemRewardCount > MaxItemRewardsPerQuest)
                    overRewardLimit++;
            }

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Quest.bin contém {extra:N0} bytes extra no final. " +
                    $"Leitura terminou no offset {ms.Position:N0}; " +
                    $"tamanho total={ms.Length:N0}.");
            }

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar XML\\Quest.");

            Directory.CreateDirectory(folder);

            SaveXml(
                new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root),
                outputXml);

            AppLogger.Log(
                $"Quest: BIN -> XML concluído. {questCount:N0} quests exportadas.");

            AppLogger.Log(
                $"Quest: maior UniqID encontrado={maxUniqIdSeen:N0}. " +
                $"Limite do client={MaxClientUniqId:N0}.");

            AppLogger.Log(
                $"Quest: maior número de rewards Item (RewardType=2) numa quest=" +
                $"{maxItemRewardsSeen}. Limite configurado={MaxItemRewardsPerQuest}.");

            if (overUniqIdLimit > 0)
            {
                AppLogger.Warning(
                    $"Quest: ATENÇÃO - {overUniqIdLimit:N0} quests no BIN têm UniqID acima " +
                    $"do limite do client ({MaxClientUniqId}). O EXPORT foi permitido para " +
                    "não perder dados, mas o PACK irá bloquear.");
            }

            if (overRewardLimit > 0)
            {
                AppLogger.Warning(
                    $"Quest: ATENÇÃO - {overRewardLimit:N0} quests no BIN têm mais de " +
                    $"{MaxItemRewardsPerQuest} rewards de item. O EXPORT foi permitido, " +
                    "mas o PACK irá bloquear até serem corrigidas.");
            }

            AppLogger.Log(
                $"Quest: tamanho BIN verificado: " +
                $"{data.LongLength:N0} / {data.LongLength:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            XDocument doc = LoadXml(inputXml);

            XElement root =
                RequireRoot(
                    doc,
                    "QuestInfos",
                    "Quest.xml");

            List<XElement> quests =
                root.Elements("QuestInfo").ToList();

            if (quests.Count == 0)
            {
                throw new InvalidDataException(
                    "Quest.xml: não existe nenhum <QuestInfo>.");
            }

            // Primeiro fazemos TODAS as validações de client.
            // Assim o utilizador recebe todas as quests problemáticas
            // numa única tentativa, e não apenas a primeira.
            ValidateClientLimits(quests);

            // Depois validamos a estrutura e calculamos o tamanho real.
            long expectedSize;

            using (MemoryStream testStream = new())
            using (BinaryWriter test =
                new(testStream, Encoding.UTF8, leaveOpen: true))
            {
                WriteTable(test, quests);
                test.Flush();
                expectedSize = testStream.Length;
            }

            string outputFolder =
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Quest.");

            Directory.CreateDirectory(outputFolder);

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw =
                new(fs, Encoding.UTF8, leaveOpen: true);

            WriteTable(bw, quests);
            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Quest.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            uint maxUniqId =
                quests
                    .Select(
                        q => RequiredUInt(
                            q,
                            "UniqID",
                            "Quest.xml"))
                    .Max();

            int maxItemRewards =
                quests.Max(CountItemRewards);

            AppLogger.Log(
                $"Quest: XML -> BIN concluído. {quests.Count:N0} quests serializadas.");

            AppLogger.Log(
                $"Quest: validação client OK. MaxUniqID={maxUniqId:N0}/{MaxClientUniqId:N0}, " +
                $"MaxItemRewards={maxItemRewards}/{MaxItemRewardsPerQuest}.");

            AppLogger.Log(
                $"Quest: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        private static void ValidateClientLimits(
            IReadOnlyList<XElement> quests)
        {
            List<string> problems = new();

            for (int index = 0; index < quests.Count; index++)
            {
                XElement quest = quests[index];

                uint uniqId =
                    RequiredUInt(
                        quest,
                        "UniqID",
                        $"QuestInfo #{index + 1}");

                string title =
                    TextOrEmpty(quest, "TitleText");

                if (uniqId > MaxClientUniqId)
                {
                    problems.Add(
                        $"QuestInfo #{index + 1}: UniqID={uniqId} | " +
                        $"TitleText=\"{Shorten(title, 70)}\" | " +
                        $"ERRO: UniqID acima do limite do client. " +
                        $"Máximo permitido={MaxClientUniqId}; atual={uniqId}.");
                }

                int itemRewards = CountItemRewards(quest);

                if (itemRewards > MaxItemRewardsPerQuest)
                {
                    List<string> itemIds =
                        quest
                            .Elements("RewardQuantities")
                            .Elements("RewardQuantity")
                            .Where(
                                r => RequiredInt(
                                    r,
                                    "RewardType",
                                    $"Quest UniqID={uniqId} RewardQuantity") == 2)
                            .Elements("QuestRewardItems")
                            .Elements("QuestRewardItemsItem")
                            .Select(
                                i =>
                                    $"{RequiredInt(i, "RewardItem", $"Quest UniqID={uniqId} RewardItem")}" +
                                    $"x{RequiredInt(i, "RewardAmount", $"Quest UniqID={uniqId} RewardAmount")}")
                            .ToList();

                    problems.Add(
                        $"QuestInfo #{index + 1}: UniqID={uniqId} | " +
                        $"TitleText=\"{Shorten(title, 70)}\" | " +
                        $"ERRO: {itemRewards} rewards de ITEM (RewardType=2). " +
                        $"O client suporta no máximo {MaxItemRewardsPerQuest}. " +
                        $"Items=[{string.Join(", ", itemIds)}]. " +
                        $"Remove/reorganiza {itemRewards - MaxItemRewardsPerQuest} reward(s) de item.");
                }
            }

            if (problems.Count > 0)
            {
                throw new InvalidDataException(
                    "Quest.xml falhou a validação de limites do CLIENT.\n" +
                    $"Foram encontrados {problems.Count:N0} problema(s):\n" +
                    string.Join("\n", problems) +
                    "\nNenhum Quest.bin foi gerado. Corrige todos os problemas acima e volta a fazer PACK.");
            }
        }

        private static int CountItemRewards(XElement quest)
        {
            int count = 0;

            XElement? rewardRoot =
                quest.Element("RewardQuantities");

            if (rewardRoot == null)
                return 0;

            foreach (XElement reward in
                rewardRoot.Elements("RewardQuantity"))
            {
                int type =
                    RequiredInt(
                        reward,
                        "RewardType",
                        "Quest RewardQuantity");

                if (type != 2)
                    continue;

                XElement? items =
                    reward.Element("QuestRewardItems");

                if (items != null)
                {
                    count +=
                        items
                            .Elements("QuestRewardItemsItem")
                            .Count();
                }
            }

            return count;
        }

        private static void WriteTable(
            BinaryWriter bw,
            IReadOnlyList<XElement> quests)
        {
            bw.Write(quests.Count);

            for (int i = 0; i < quests.Count; i++)
            {
                XElement quest = quests[i];

                uint uniqId =
                    RequiredUInt(
                        quest,
                        "UniqID",
                        $"QuestInfo #{i + 1}");

                string context =
                    $"Quest UniqID={uniqId}";

                long recordStart = bw.BaseStream.Position;

                // ====================================================
                // FIXED PREFIX - 8425 bytes
                // ====================================================

                bw.Write(uniqId);

                bw.Write(
                    RequiredUInt(
                        quest,
                        "Model",
                        context));

                bw.Write(
                    RequiredUInt(
                        quest,
                        "Model2",
                        context));

                bw.Write(
                    RequiredUInt16(
                        quest,
                        "Level",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "Pos",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "Pos2",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "ManagedID",
                        context));

                bw.Write(
                    RequiredByte(
                        quest,
                        "Active",
                        context));

                bw.Write(
                    RequiredByte(
                        quest,
                        "Immediate",
                        context));

                bw.Write(
                    RequiredByte(
                        quest,
                        "Unknown",
                        context));

                int resetQuest =
                    RequiredInt(
                        quest,
                        "ResetQuest",
                        context);

                if (resetQuest != 0)
                {
                    throw new InvalidDataException(
                        $"{context}: <ResetQuest>={resetQuest}, mas este campo " +
                        "não possui bytes físicos no formato Quest.bin analisado. " +
                        "Mantém <ResetQuest>0</ResetQuest>. " +
                        "Alterá-lo não produziria qualquer efeito no client.");
                }

                bw.Write(
                    RequiredInt(
                        quest,
                        "Type",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "StartTargetType",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "StartTargetID",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "Target",
                        context));

                bw.Write(
                    RequiredInt(
                        quest,
                        "TargetValue",
                        context));

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "TitleTab", context),
                    TitleTabChars,
                    context + " <TitleTab>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "TitleText", context),
                    TitleTextChars,
                    context + " <TitleText>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "Body", context),
                    BodyChars,
                    context + " <Body>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "Simple", context),
                    SimpleChars,
                    context + " <Simple>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "Helper", context),
                    HelperChars,
                    context + " <Helper>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "Process", context),
                    ProcessChars,
                    context + " <Process>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "Complete", context),
                    CompleteChars,
                    context + " <Complete>");

                WriteFixedUnicode(
                    bw,
                    TextOrEmptyRequired(quest, "Expert", context),
                    ExpertChars,
                    context + " <Expert>");

                long fixedConsumed =
                    bw.BaseStream.Position - recordStart;

                if (fixedConsumed != FixedRecordPrefixSize)
                {
                    throw new InvalidDataException(
                        $"{context}: prefixo fixo gerado ocupa {fixedConsumed:N0} bytes; " +
                        $"esperado={FixedRecordPrefixSize:N0}.");
                }

                // ====================================================
                // QUEST ITEMS
                // ====================================================

                XElement questItemsRoot =
                    RequireChild(
                        quest,
                        "QuestItems",
                        context);

                List<XElement> questItems =
                    questItemsRoot
                        .Elements("QuestItem")
                        .ToList();

                int declaredItemGiven =
                    RequiredInt(
                        quest,
                        "Itemgiven",
                        context);

                ValidateDeclaredCount(
                    context,
                    "Itemgiven",
                    declaredItemGiven,
                    questItems.Count,
                    "QuestItem");

                bw.Write(declaredItemGiven);

                for (int q = 0; q < questItems.Count; q++)
                {
                    XElement item = questItems[q];
                    string itemContext =
                        $"{context}, QuestItem[{q}]";

                    bw.Write(
                        RequiredInt(
                            item,
                            "Itemgiven",
                            itemContext));

                    bw.Write(
                        RequiredInt(
                            item,
                            "ItemgivenType",
                            itemContext));

                    bw.Write(
                        RequiredInt(
                            item,
                            "ItemgivenAmount",
                            itemContext));
                }

                // ====================================================
                // CONDITIONS
                // ====================================================

                XElement conditionsRoot =
                    RequireChild(
                        quest,
                        "QuestConditions",
                        context);

                List<XElement> conditions =
                    conditionsRoot
                        .Elements("QuestCondition")
                        .ToList();

                int declaredConditions =
                    RequiredInt(
                        quest,
                        "condition",
                        context);

                ValidateDeclaredCount(
                    context,
                    "condition",
                    declaredConditions,
                    conditions.Count,
                    "QuestCondition");

                bw.Write(declaredConditions);

                for (int c = 0; c < conditions.Count; c++)
                {
                    XElement condition = conditions[c];
                    string cc =
                        $"{context}, QuestCondition[{c}]";

                    bw.Write(
                        RequiredInt(
                            condition,
                            "ConditionType",
                            cc));

                    bw.Write(
                        RequiredInt(
                            condition,
                            "ConditionId",
                            cc));

                    bw.Write(
                        RequiredInt(
                            condition,
                            "ConditionCount",
                            cc));
                }

                // ====================================================
                // GOALS
                // ====================================================

                XElement goalsRoot =
                    RequireChild(
                        quest,
                        "QuestGoals",
                        context);

                List<XElement> goals =
                    goalsRoot
                        .Elements("QuestGoal")
                        .ToList();

                int declaredGoals =
                    RequiredInt(
                        quest,
                        "Goals",
                        context);

                ValidateDeclaredCount(
                    context,
                    "Goals",
                    declaredGoals,
                    goals.Count,
                    "QuestGoal");

                bw.Write(declaredGoals);

                for (int g = 0; g < goals.Count; g++)
                {
                    XElement goal = goals[g];
                    string gc =
                        $"{context}, QuestGoal[{g}]";

                    int xmlGoalCount =
                        RequiredInt(
                            goal,
                            "GoalCount",
                            gc);

                    if (xmlGoalCount != 0)
                    {
                        throw new InvalidDataException(
                            $"{gc}: <GoalCount>={xmlGoalCount}, mas este campo " +
                            "não possui bytes físicos no Quest.bin analisado. " +
                            "Mantém <GoalCount>0</GoalCount>.");
                    }

                    bw.Write(
                        RequiredInt(
                            goal,
                            "GoalType",
                            gc));

                    bw.Write(
                        RequiredInt(
                            goal,
                            "GoalId",
                            gc));

                    bw.Write(
                        RequiredInt(
                            goal,
                            "goalAmount",
                            gc));

                    bw.Write(
                        RequiredInt(
                            goal,
                            "CurTypeCount",
                            gc));

                    bw.Write(
                        RequiredInt(
                            goal,
                            "SubValue",
                            gc));

                    bw.Write(
                        RequiredInt(
                            goal,
                            "SubValue1",
                            gc));
                }

                // ====================================================
                // REWARDS
                // ====================================================

                XElement rewardsRoot =
                    RequireChild(
                        quest,
                        "RewardQuantities",
                        context);

                List<XElement> rewards =
                    rewardsRoot
                        .Elements("RewardQuantity")
                        .ToList();

                int declaredRewards =
                    RequiredInt(
                        quest,
                        "RewardNumber",
                        context);

                ValidateDeclaredCount(
                    context,
                    "RewardNumber",
                    declaredRewards,
                    rewards.Count,
                    "RewardQuantity");

                bw.Write(declaredRewards);

                for (int r = 0; r < rewards.Count; r++)
                {
                    XElement reward = rewards[r];
                    string rc =
                        $"{context}, RewardQuantity[{r}]";

                    int rewardFlag =
                        RequiredInt(
                            reward,
                            "Reward",
                            rc);

                    int rewardType =
                        RequiredInt(
                            reward,
                            "RewardType",
                            rc);

                    bw.Write(rewardFlag);
                    bw.Write(rewardType);

                    XElement moneyRoot =
                        RequireChild(
                            reward,
                            "QuestRewardMoney",
                            rc);

                    XElement itemsRoot =
                        RequireChild(
                            reward,
                            "QuestRewardItems",
                            rc);

                    List<XElement> moneyRows =
                        moneyRoot
                            .Elements("QuestRewardMoneyItem")
                            .ToList();

                    List<XElement> itemRows =
                        itemsRoot
                            .Elements("QuestRewardItemsItem")
                            .ToList();

                    if (rewardType == 0)
                    {
                        if (moneyRows.Count != 1)
                        {
                            throw new InvalidDataException(
                                $"{rc}: RewardType=0 exige exatamente 1 " +
                                $"<QuestRewardMoneyItem>; encontrados {moneyRows.Count}.");
                        }

                        if (itemRows.Count != 0)
                        {
                            throw new InvalidDataException(
                                $"{rc}: RewardType=0 é reward de dinheiro, mas existem " +
                                $"{itemRows.Count} <QuestRewardItemsItem>. Remove-os.");
                        }

                        XElement money = moneyRows[0];

                        bw.Write(
                            RequiredInt(
                                money,
                                "RewardMoney",
                                rc));

                        bw.Write(
                            RequiredInt(
                                money,
                                "RewardUnk",
                                rc));
                    }
                    else
                    {
                        if (moneyRows.Count != 0)
                        {
                            throw new InvalidDataException(
                                $"{rc}: RewardType={rewardType} não usa " +
                                "<QuestRewardMoneyItem>, mas foi encontrado um.");
                        }

                        if (itemRows.Count > 1)
                        {
                            throw new InvalidDataException(
                                $"{rc}: o formato binário só possui um par " +
                                "(RewardItem, RewardAmount) por RewardQuantity. " +
                                $"Foram encontrados {itemRows.Count} QuestRewardItemsItem.");
                        }

                        if (itemRows.Count == 0)
                        {
                            bw.Write(0);
                            bw.Write(0);
                        }
                        else
                        {
                            XElement item = itemRows[0];

                            bw.Write(
                                RequiredInt(
                                    item,
                                    "RewardItem",
                                    rc));

                            bw.Write(
                                RequiredInt(
                                    item,
                                    "RewardAmount",
                                    rc));
                        }
                    }
                }

                // ====================================================
                // EVENTS
                // ====================================================

                XElement eventRoot =
                    RequireChild(
                        quest,
                        "Event",
                        context);

                List<XElement> eventIds =
                    eventRoot
                        .Elements("EventId")
                        .ToList();

                bw.Write(eventIds.Count);

                for (int e = 0; e < eventIds.Count; e++)
                {
                    string value =
                        eventIds[e].Value;

                    if (!int.TryParse(
                        value.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int eventId))
                    {
                        throw new InvalidDataException(
                            $"{context}, EventId[{e}]='{value}' não é Int32 válido.");
                    }

                    bw.Write(eventId);
                }
            }
        }

        private static void ValidateDeclaredCount(
            string context,
            string countField,
            int declared,
            int actual,
            string childName)
        {
            if (declared < 0)
            {
                throw new InvalidDataException(
                    $"{context}: <{countField}>={declared} é negativo.");
            }

            if (declared != actual)
            {
                throw new InvalidDataException(
                    $"{context}: <{countField}>={declared}, mas existem " +
                    $"{actual} <{childName}>. " +
                    $"Corrige <{countField}> para {actual} ou ajusta os elementos.");
            }
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount,
            string field)
        {
            int byteCount =
                checked(wcharCount * 2);

            byte[] raw =
                ReadExact(
                    br,
                    byteCount,
                    field);

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
            string text = value ?? string.Empty;

            byte[] raw =
                Encoding.Unicode.GetBytes(text);

            int capacity =
                checked(wcharCount * 2);

            if (raw.Length > capacity)
            {
                throw new InvalidDataException(
                    $"{field}: texto demasiado longo. " +
                    $"Atual={text.Length:N0} caracteres / {raw.Length:N0} bytes UTF-16LE. " +
                    $"Máximo físico={wcharCount:N0} caracteres / {capacity:N0} bytes. " +
                    $"Reduz o texto em pelo menos {text.Length - wcharCount:N0} caracteres.");
            }

            bw.Write(raw);

            int padding =
                capacity - raw.Length;

            if (padding > 0)
            {
                bw.Write(new byte[padding]);
            }
        }

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] raw =
                br.ReadBytes(count);

            if (raw.Length != count)
            {
                throw new EndOfStreamException(
                    $"{field}: BIN truncado. Esperados={count:N0} bytes, " +
                    $"recebidos={raw.Length:N0}. Offset atual={br.BaseStream.Position:N0}.");
            }

            return raw;
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            if (br.BaseStream.Position + 4 > br.BaseStream.Length)
            {
                throw new EndOfStreamException(
                    $"{field}: faltam 4 bytes para ler o Count.");
            }

            int value = br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: Count inválido ({value}). " +
                    $"Esperado entre 0 e {max:N0}.");
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
            XElement? root = doc.Root;

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

        private static XElement RequireChild(
            XElement parent,
            string name,
            string context)
        {
            XElement? child =
                parent.Element(name);

            if (child == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            return child;
        }

        private static string TextOrEmpty(
            XElement parent,
            string name)
        {
            return parent.Element(name)?.Value
                ?? string.Empty;
        }

        private static string TextOrEmptyRequired(
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
                    "O texto pode estar vazio, mas a tag tem de existir.");
            }

            return element.Value;
        }

        private static int RequiredInt(
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

            string value = element.Value;

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

            string value = element.Value;

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
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            string value = element.Value;

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

        private static byte RequiredByte(
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

            string value = element.Value;

            if (!byte.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out byte result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em byte (0..255).");
            }

            return result;
        }

        private static string Shorten(
            string value,
            int max)
        {
            string normalized =
                (value ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ");

            if (normalized.Length <= max)
                return normalized;

            return normalized[..max] + "...";
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
