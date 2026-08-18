using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed record NpcQuestReference(
        int QuestId,
        string Title,
        string Relation);

    public sealed class NpcQuestReferenceService
    {
        private readonly Dictionary<int, XElement> _quests = new();

        public string QuestFilePath { get; }

        public NpcQuestReferenceService(string? questFilePath = null)
        {
            QuestFilePath =
                questFilePath ??
                Path.Combine(
                    AppPaths.Xml,
                    "Quest",
                    "Quest.xml");

            if (!File.Exists(QuestFilePath))
                return;

            XDocument doc = XDocument.Load(QuestFilePath);

            foreach (XElement q in doc.Root?.Elements("QuestInfo")
                ?? Enumerable.Empty<XElement>())
            {
                if (int.TryParse(
                    q.Element("UniqID")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int id))
                {
                    _quests[id] = q;
                }
            }
        }

        public bool Exists(int questId) =>
            _quests.ContainsKey(questId);

        public IReadOnlyList<NpcQuestReference> FindNpcReferences(int npcId)
        {
            var result = new List<NpcQuestReference>();

            foreach (var pair in _quests)
            {
                XElement q = pair.Value;
                string title = q.Element("TitleText")?.Value ?? string.Empty;

                if (ReadInt(q, "StartTargetType") == 0 &&
                    ReadInt(q, "StartTargetID") == npcId)
                {
                    result.Add(new NpcQuestReference(pair.Key, title, "Quest Start NPC"));
                }

                if (ReadInt(q, "Target") == 1 &&
                    ReadInt(q, "TargetValue") == npcId)
                {
                    result.Add(new NpcQuestReference(pair.Key, title, "Quest Target NPC"));
                }

                if (q.Descendants("QuestGoal").Any(g =>
                    ReadInt(g, "GoalType") == 4 &&
                    ReadInt(g, "GoalId") == npcId))
                {
                    result.Add(new NpcQuestReference(pair.Key, title, "Talk-to NPC Goal"));
                }
            }

            return result
                .OrderBy(x => x.QuestId)
                .ThenBy(x => x.Relation)
                .ToArray();
        }

        private static int ReadInt(XElement parent, string name)
        {
            _ = int.TryParse(
                parent.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value);

            return value;
        }
    }
}
