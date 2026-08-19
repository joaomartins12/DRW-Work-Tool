using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private const int AchievementCardsPerPage = 30;

        private sealed class AchievementService
        {
            public string FilePath { get; }
            public string QuestPath { get; }
            public string BuffPath { get; }
            public XDocument Document { get; private set; }
            public XDocument? QuestDocument { get; private set; }
            public XDocument? BuffDocument { get; private set; }

            public AchievementService(string filePath)
            {
                FilePath = Path.GetFullPath(filePath);
                QuestPath = Path.Combine(AppPaths.Xml, "Quest", "Quest.xml");
                BuffPath = Path.Combine(AppPaths.Xml, "Buff", "Buff.xml");
                Document = LoadAchievement(FilePath);
                ReloadReferences();
            }

            public IReadOnlyList<XElement> Records =>
                Document.Root?.Elements("AchieveSINFO").ToList()
                ?? new List<XElement>();

            public void ReloadReferences()
            {
                QuestDocument = File.Exists(QuestPath)
                    ? XDocument.Load(QuestPath, LoadOptions.PreserveWhitespace)
                    : null;
                BuffDocument = File.Exists(BuffPath)
                    ? XDocument.Load(BuffPath, LoadOptions.PreserveWhitespace)
                    : null;
            }

            public uint SuggestAvailableId(uint preferred)
            {
                var used = Records
                    .Select(x => UInt(x, "s_nQuestID"))
                    .ToHashSet();
                uint id = Math.Max(1, preferred);
                while (used.Contains(id) && id < uint.MaxValue)
                    id++;
                return id;
            }

            public XElement CreateNewNode()
            {
                uint id = SuggestAvailableId(
                    Records.Select(x => UInt(x, "s_nQuestID")).DefaultIfEmpty(1499u).Max() + 1);
                return new XElement("AchieveSINFO",
                    new XElement("s_nQuestID", id),
                    new XElement("s_nIcon", 0),
                    new XElement("s_nPoint", 10),
                    new XElement("s_bDisplay", 1),
                    new XElement("s_bDisplay2", 0),
                    new XElement("s_szName", "New Achievement"),
                    new XElement("s_szComment", string.Empty),
                    new XElement("s_szTitle", "New Title"),
                    new XElement("s_nGroup", 1),
                    new XElement("s_nSubGroup", 8),
                    new XElement("s_nType", 700),
                    new XElement("s_nBuffCode", 0));
            }

            public void Save(XElement working, XElement? original)
            {
                XElement root = Document.Root
                    ?? throw new InvalidDataException("Achieve.xml has no root.");
                uint id = UInt(working, "s_nQuestID");
                if (id == 0)
                    throw new InvalidDataException("s_nQuestID must be greater than zero.");

                bool duplicate = root.Elements("AchieveSINFO")
                    .Any(x => !ReferenceEquals(x, original) && UInt(x, "s_nQuestID") == id);
                if (duplicate)
                    throw new InvalidDataException($"Achievement QuestID {id} already exists.");

                if (original == null)
                    root.Add(new XElement(working));
                else
                    original.ReplaceWith(new XElement(working));

                SaveDocumentWithBackup(Document, FilePath);
                Document = LoadAchievement(FilePath);
            }

            public void Delete(XElement node)
            {
                XElement? target = Document.Root?.Elements("AchieveSINFO")
                    .FirstOrDefault(x => UInt(x, "s_nQuestID") == UInt(node, "s_nQuestID") &&
                                         AchievementText(x, "s_szName") == AchievementText(node, "s_szName"));
                target?.Remove();
                SaveDocumentWithBackup(Document, FilePath);
                Document = LoadAchievement(FilePath);
            }

            public string BuffSummary(uint buffId)
            {
                if (buffId == 0) return "No Buff";
                XElement? buff = BuffDocument?.Root?.Elements("BuffData")
                    .FirstOrDefault(x => UInt(x, "s_dwID") == buffId);
                return buff == null
                    ? $"Buff {buffId} (missing in Buff.xml)"
                    : $"{buffId} — {AchievementText(buff, "s_szName")}";
            }

            public IReadOnlyList<XElement> Buffs(string query)
            {
                string q = (query ?? string.Empty).Trim();
                return BuffDocument?.Root?.Elements("BuffData")
                    .Where(x => q.Length == 0 ||
                        x.ToString(SaveOptions.DisableFormatting).Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(600).ToList()
                    ?? new List<XElement>();
            }

            public XElement? Quest(uint id) =>
                QuestDocument?.Root?.Elements("QuestInfo")
                    .FirstOrDefault(x => UInt(x, "UniqID") == id);

            public IReadOnlyList<XElement> RelatedTitleQuests(string query)
            {
                string q = (query ?? string.Empty).Trim();
                var achievementIds = Records.Select(x => UInt(x, "s_nQuestID")).ToHashSet();
                return QuestDocument?.Root?.Elements("QuestInfo")
                    .Where(IsTitleQuest)
                    .Where(x => q.Length == 0 ||
                        x.ToString(SaveOptions.DisableFormatting).Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(800).ToList()
                    ?? new List<XElement>();

                bool IsTitleQuest(XElement x)
                {
                    uint id = UInt(x, "UniqID");
                    string tab = AchievementText(x, "TitleTab");
                    string title = AchievementText(x, "TitleText");
                    int type = Int(x, "Type");
                    XElement? goal = x.Element("QuestGoals")?.Elements("QuestGoal").FirstOrDefault();
                    int goalType = goal == null ? -1 : Int(goal, "GoalType");
                    return achievementIds.Contains(id) ||
                           type == 5 ||
                           goalType == 2 ||
                           tab.Contains("achievement", StringComparison.OrdinalIgnoreCase) ||
                           title.Contains("[Achievement]", StringComparison.OrdinalIgnoreCase);
                }
            }

            public XElement CreateQuestTemplate(XElement achievement, XElement? source = null)
            {
                if (QuestDocument?.Root == null)
                    throw new FileNotFoundException("Quest.xml was not found.", QuestPath);

                uint questId = UInt(achievement, "s_nQuestID");
                if (questId == 0)
                    throw new InvalidDataException("Save a valid s_nQuestID before creating the quest.");
                if (Quest(questId) != null)
                    throw new InvalidOperationException($"Quest {questId} already exists in Quest.xml.");

                XElement? template = source;
                if (template == null)
                {
                    int achievementType = Int(achievement, "s_nType");
                    template = RelatedTitleQuests(string.Empty)
                        .FirstOrDefault(x =>
                        {
                            XElement? goal = x.Element("QuestGoals")?.Elements("QuestGoal").FirstOrDefault();
                            return Int(x, "Type") == 5 && goal != null &&
                                   Int(goal, "GoalType") == 2 && Int(goal, "GoalId") == achievementType;
                        })
                        ?? RelatedTitleQuests(string.Empty).FirstOrDefault(x => Int(x, "Type") == 5)
                        ?? RelatedTitleQuests(string.Empty).FirstOrDefault();
                }

                XElement quest = template != null
                    ? new XElement(template)
                    : BuildMinimalTitleQuest();

                Set(quest, "UniqID", questId.ToString(CultureInfo.InvariantCulture));
                Set(quest, "Active", "1");
                Set(quest, "Type", "5");
                Set(quest, "TitleTab", string.Empty);
                Set(quest, "TitleText", AchievementText(achievement, "s_szTitle"));
                Set(quest, "Body", string.Empty);
                Set(quest, "Simple", string.Empty);
                Set(quest, "Helper", string.Empty);
                Set(quest, "Process", string.Empty);
                Set(quest, "Complete", string.Empty);
                Set(quest, "Expert", string.Empty);

                XElement goals = quest.Element("QuestGoals") ?? new XElement("QuestGoals");
                if (goals.Parent == null) quest.Add(goals);
                XElement goal = goals.Elements("QuestGoal").FirstOrDefault() ?? new XElement("QuestGoal");
                if (goal.Parent == null) goals.Add(goal);
                Set(goal, "GoalType", "2");
                Set(goal, "GoalId", AchievementText(achievement, "s_nType"));
                if (goal.Element("goalAmount") == null) Set(goal, "goalAmount", "0");
                Set(quest, "Goals", "1");

                QuestDocument.Root.Add(quest);
                SaveDocumentWithBackup(QuestDocument, QuestPath);
                ReloadReferences();
                return Quest(questId) ?? quest;
            }

            private static XElement BuildMinimalTitleQuest() =>
                new XElement("QuestInfo",
                    new XElement("UniqID", 0), new XElement("Model", 0), new XElement("Model2", 0),
                    new XElement("Level", 1), new XElement("Pos", 0), new XElement("Pos2", 0),
                    new XElement("ManagedID", 0), new XElement("Active", 1), new XElement("Unknown", 0),
                    new XElement("Immediate", 0), new XElement("ResetQuest", 0), new XElement("Type", 5),
                    new XElement("StartTargetType", 0), new XElement("StartTargetID", 0),
                    new XElement("Target", 0), new XElement("TargetValue", 0),
                    new XElement("TitleTab", string.Empty), new XElement("TitleText", string.Empty),
                    new XElement("Body", string.Empty), new XElement("Simple", string.Empty),
                    new XElement("Helper", string.Empty), new XElement("Process", string.Empty),
                    new XElement("Complete", string.Empty), new XElement("Expert", string.Empty),
                    new XElement("Itemgiven", 0), new XElement("QuestItems"),
                    new XElement("condition", 0), new XElement("QuestConditions"), new XElement("Goals", 1),
                    new XElement("QuestGoals", new XElement("QuestGoal",
                        new XElement("GoalType", 2), new XElement("GoalId", 700), new XElement("GoalCount", 0),
                        new XElement("goalAmount", 0), new XElement("CurTypeCount", 0),
                        new XElement("SubValue", 0), new XElement("SubValue1", 0))),
                    new XElement("RewardNumber", 0), new XElement("RewardQuantities"),
                    new XElement("Event", new XElement("EventId", 0), new XElement("EventId", 0),
                        new XElement("EventId", 0), new XElement("EventId", 0)));

            private static XDocument LoadAchievement(string path)
            {
                XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                if (doc.Root?.Name.LocalName != "AchieveSINFOs")
                    throw new InvalidDataException($"Unexpected Achieve.xml root <{doc.Root?.Name.LocalName}>.");
                return doc;
            }

            private static void SaveDocumentWithBackup(XDocument doc, string path)
            {
                if (File.Exists(path)) File.Copy(path, path + ".editor.bak", true);
                doc.Save(path, SaveOptions.None);
            }

            private static void Set(XElement node, string name, string value)
            {
                XElement? e = node.Element(name);
                if (e == null) node.Add(new XElement(name, value)); else e.Value = value;
            }
        }

        private sealed class AchievementBrowseState
        {
            public required AchievementService Service { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required TextBox Search { get; init; }
            public required Label Count { get; init; }
            public required Button Previous { get; init; }
            public required Button Next { get; init; }
            public required Label PageInfo { get; init; }
            public int PageIndex { get; set; }
            public List<XElement> Filtered { get; set; } = new();
        }

        private sealed class AchievementEditState
        {
            public required AchievementService Service { get; init; }
            public required XElement Working { get; set; }
            public XElement? Original { get; set; }
            public bool IsNew { get; set; }
            public bool Dirty { get; set; }
            public required Dictionary<string, TextBox> Fields { get; init; }
            public required PictureBox Icon { get; init; }
            public required Label BuffStatus { get; init; }
            public required Label QuestStatus { get; init; }
            public required TabPage Page { get; init; }
        }

        private async void OpenAchievementBrowser(string xmlPath)
        {
            string full = Path.GetFullPath(xmlPath);
            var page = CreateDarkTab("Achieve.xml");
            page.Name = full;
            page.Controls.Add(new EditorLoadingView(
                "Loading Achievement Database",
                "Reading titles, achievement icons, Buff.xml and related Quest.xml entries."));
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            await System.Threading.Tasks.Task.Yield();

            try
            {
                BuildAchievementBrowser(page, new AchievementService(full));
            }
            catch (Exception ex)
            {
                page.Controls.Clear();
                page.Controls.Add(CreateInfoLabel("Achieve.xml could not be loaded.\r\n\r\n" + ex.Message));
            }
        }

        private void BuildAchievementBrowser(TabPage page, AchievementService service)
        {
            page.Controls.Clear();
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var header = new Panel { Dock = DockStyle.Top, Height = 146, BackColor = CEditor };
            var title = new Label { Text = "Achievement / Title Editor", ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold), Location = new Point(8, 4), AutoSize = true };
            var sub = new Label { Text = $"{service.Records.Count:N0} titles • Achieve icons • Buff.xml • title quests only",
                ForeColor = CMuted, Location = new Point(10, 35), AutoSize = true };
            var search = new TextBox { Location = new Point(8, 68), Height = 28, BackColor = Color.FromArgb(10,10,10),
                ForeColor = CText, BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Search QuestID, title, name, type, group, BuffID...",
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var create = CreateEditorActionButton("NEW TITLE");
            create.Size = new Size(130, 34); create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var count = new Label { ForeColor = CMuted, AutoSize = true, Location = new Point(10, 104) };
            var previous = CreateEditorActionButton("◀ PREVIOUS");
            previous.Size = new Size(112, 30);
            previous.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var pageInfo = new Label { ForeColor = CText, Size = new Size(82, 30), TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var next = CreateEditorActionButton("NEXT ▶");
            next.Size = new Size(96, 30);
            next.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var results = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, BackColor = CEditor, Padding = new Padding(4, 8, 16, 8) };
            DarkUi.ApplyDarkScrollBar(results);
            header.Controls.AddRange(new Control[] { title, sub, search, create, count, previous, pageInfo, next });
            root.Controls.Add(results); root.Controls.Add(header); page.Controls.Add(root);
            var state = new AchievementBrowseState { Service = service, Results = results, Search = search, Count = count, Previous = previous, Next = next, PageInfo = pageInfo };
            page.Tag = state;

            void Layout()
            {
                create.Location = new Point(Math.Max(150, header.ClientSize.Width - create.Width - 8), 6);
                search.Width = Math.Max(220, header.ClientSize.Width - 16);
                next.Location = new Point(Math.Max(300, header.ClientSize.Width - next.Width - 8), 108);
                pageInfo.Location = new Point(next.Left - pageInfo.Width - 8, 108);
                previous.Location = new Point(pageInfo.Left - previous.Width - 8, 108);
            }
            header.Resize += (_, _) => Layout();
            results.Resize += (_, _) => ResizeAchievementCards(results);
            search.TextChanged += (_, _) => { state.PageIndex = 0; RefreshAchievementBrowser(state); };
            previous.Click += (_, _) =>
            {
                if (state.PageIndex <= 0) return;
                state.PageIndex--;
                RefreshAchievementBrowser(state);
                state.Results.AutoScrollPosition = Point.Empty;
            };
            next.Click += (_, _) =>
            {
                int pages = Math.Max(1, (int)Math.Ceiling(state.Filtered.Count / (double)AchievementCardsPerPage));
                if (state.PageIndex >= pages - 1) return;
                state.PageIndex++;
                RefreshAchievementBrowser(state);
                state.Results.AutoScrollPosition = Point.Empty;
            };
            create.Click += (_, _) => OpenAchievementEditTab(service, service.CreateNewNode(), null, true);
            Layout(); RefreshAchievementBrowser(state);
        }

        private void RefreshAchievementBrowser(AchievementBrowseState state)
        {
            string q = state.Search.Text.Trim();
            state.Filtered = state.Service.Records
                .Where(x => q.Length == 0 || x.ToString(SaveOptions.DisableFormatting)
                    .Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
            int pages = Math.Max(1, (int)Math.Ceiling(state.Filtered.Count / (double)AchievementCardsPerPage));
            state.PageIndex = Math.Clamp(state.PageIndex, 0, pages - 1);
            state.Results.SuspendLayout();
            DisposeAchievementCardImages(state.Results);
            state.Results.Controls.Clear();
            foreach (XElement node in state.Filtered.Skip(state.PageIndex * AchievementCardsPerPage).Take(AchievementCardsPerPage))
                state.Results.Controls.Add(CreateAchievementCard(state, node));
            state.Results.ResumeLayout();
            state.Count.Text = $"Results: {state.Filtered.Count:N0} / {state.Service.Records.Count:N0} • 30 cards per page";
            state.PageInfo.Text = $"{state.PageIndex + 1} / {pages}";
            state.Previous.Enabled = state.PageIndex > 0;
            state.Next.Enabled = state.PageIndex < pages - 1;
            ResizeAchievementCards(state.Results);
        }

        private Control CreateAchievementCard(AchievementBrowseState state, XElement node)
        {
            uint questId = UInt(node, "s_nQuestID");
            uint iconId = UInt(node, "s_nIcon");
            uint buffId = UInt(node, "s_nBuffCode");
            string name = AchievementText(node, "s_szName");
            string titleText = AchievementText(node, "s_szTitle");
            XElement? quest = state.Service.Quest(questId);

            var card = new Panel { Height = 104, Width = Math.Max(560, state.Results.ClientSize.Width - 26),
                BackColor = Color.FromArgb(29,29,29), Margin = new Padding(0,0,0,8) };
            card.Paint += (_, e) => { using var p = new Pen(Color.FromArgb(70,70,70)); e.Graphics.DrawRectangle(p,0,0,card.Width-1,card.Height-1); };
            var icon = new PictureBox { Location = new Point(12,12), Size = new Size(78,78), BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom, Image = AchievementIconAtlasCache.TryLoad(iconId) };
            var main = new Label { Text = string.IsNullOrWhiteSpace(titleText) ? name : titleText, ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold), Location = new Point(104,10), Size = new Size(430,24), AutoEllipsis = true };
            var info = new Label { Text = $"Quest {questId} • Icon {iconId} • Type {AchievementText(node,"s_nType")} • Group {AchievementText(node,"s_nGroup")}/{AchievementText(node,"s_nSubGroup")}",
                ForeColor = Color.FromArgb(120,220,145), Location = new Point(104,36), Size = new Size(480,20), AutoEllipsis = true };
            var refs = new Label { Text = $"{state.Service.BuffSummary(buffId)} • Quest: {(quest == null ? "missing" : AchievementText(quest,"TitleText"))}",
                ForeColor = CMuted, Location = new Point(104,58), Size = new Size(500,20), AutoEllipsis = true };
            var desc = new Label { Text = AchievementText(node,"s_szComment"), ForeColor = CMuted, Location = new Point(104,79), Size = new Size(500,18), AutoEllipsis = true };
            var edit = CreateEditorActionButton("EDIT"); edit.Size = new Size(88,30); edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var clone = CreateEditorActionButton("CLONE"); clone.Size = new Size(88,30); clone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            void LayoutCard()
            {
                clone.Location = new Point(card.ClientSize.Width - clone.Width - 12, 54);
                edit.Location = new Point(card.ClientSize.Width - edit.Width - 12, 16);
                int w = Math.Max(140, edit.Left - main.Left - 12);
                main.Width = info.Width = refs.Width = desc.Width = w;
            }
            card.Resize += (_, _) => LayoutCard();
            edit.Click += (_, _) => OpenAchievementEditTab(state.Service, new XElement(node), node, false);
            clone.Click += (_, _) =>
            {
                XElement copy = new XElement(node);
                uint next = state.Service.SuggestAvailableId(questId + 1);
                copy.Element("s_nQuestID")!.Value = next.ToString(CultureInfo.InvariantCulture);
                copy.Element("s_szName")!.Value += " [Clone]";
                OpenAchievementEditTab(state.Service, copy, null, true);
            };
            card.Controls.AddRange(new Control[] { icon, main, info, refs, desc, edit, clone }); LayoutCard();
            return card;
        }

        private void ResizeAchievementCards(FlowLayoutPanel panel)
        {
            int width = Math.Max(560, panel.ClientSize.Width - 26);
            foreach (Control c in panel.Controls) c.Width = width;
        }

        private void OpenAchievementEditTab(AchievementService service, XElement working, XElement? original, bool isNew)
        {
            var page = CreateDarkTab((AchievementText(working,"s_szTitle") is string t && t.Length > 0 ? t : "Achievement") + " [Edit]");
            var top = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = CEditor, Padding = new Padding(16,12,16,10) };
            var save = CreateEditorActionButton("SAVE"); save.Size = new Size(100,34);
            var raw = CreateEditorActionButton("VIEW XML"); raw.Size = new Size(108,34); raw.Location = new Point(110,0);
            top.Controls.AddRange(new Control[] { save, raw });
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CEditor, Padding = new Padding(18) };
            DarkUi.ApplyDarkScrollBar(scroll);
            var form = new Panel { Location = new Point(0,0), Width = 760, Height = 820, BackColor = CEditor };
            scroll.Controls.Add(form); page.Controls.Add(scroll); page.Controls.Add(top);

            var icon = new PictureBox { Location = new Point(16,16), Size = new Size(96,96), BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            form.Controls.Add(icon);
            var fields = new Dictionary<string,TextBox>();
            int y = 16;
            AddField("Quest ID", "s_nQuestID", 132, y, 210); AddField("Icon ID", "s_nIcon", 360, y, 170); y += 60;
            AddField("Name", "s_szName", 132, y, 398); y += 60;
            AddField("Title", "s_szTitle", 132, y, 398); y += 60;
            AddField("Comment", "s_szComment", 16, y, 686, true, 72); y += 112;
            AddField("Points", "s_nPoint", 16, y, 150); AddField("Display", "s_bDisplay", 182, y, 120); AddField("Display2", "s_bDisplay2", 318, y, 120); y += 60;
            AddField("Group", "s_nGroup", 16, y, 150); AddField("SubGroup", "s_nSubGroup", 182, y, 150); AddField("Type", "s_nType", 348, y, 150); y += 72;
            AddField("Buff ID", "s_nBuffCode", 16, y, 210);
            var selectBuff = CreateEditorActionButton("SELECT BUFF"); selectBuff.Location = new Point(238,y+20); selectBuff.Size = new Size(130,28); form.Controls.Add(selectBuff);
            var buffStatus = new Label { Location = new Point(382,y+20), Size = new Size(320,28), ForeColor = CMuted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }; form.Controls.Add(buffStatus); y += 72;
            var selectQuest = CreateEditorActionButton("SELECT QUEST"); selectQuest.Location = new Point(16,y); selectQuest.Size = new Size(142,32);
            var createQuest = CreateEditorActionButton("CREATE QUEST"); createQuest.Location = new Point(166,y); createQuest.Size = new Size(142,32);
            var questStatus = new Label { Location = new Point(322,y), Size = new Size(380,32), ForeColor = CMuted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            form.Controls.AddRange(new Control[] { selectQuest, createQuest, questStatus }); y += 54;
            var hint = new Label { Location = new Point(16,y), Size = new Size(686,56), ForeColor = CMuted,
                Text = "SELECT QUEST only lists title/achievement quests (existing Achievement QuestIDs, Type=5, GoalType=2, or Achievement-labelled quests).\r\nCREATE QUEST clones a Type=5 title quest and assigns this achievement QuestID/Type." };
            form.Controls.Add(hint);

            var state = new AchievementEditState { Service = service, Working = working, Original = original, IsNew = isNew,
                Dirty = isNew, Fields = fields, Icon = icon, BuffStatus = buffStatus, QuestStatus = questStatus, Page = page };
            page.Tag = state;

            foreach (TextBox box in fields.Values)
            {
                box.TextChanged += (_, _) => { state.Dirty = true; Pull(); RefreshRefs(); };
            }
            save.Click += (_, _) =>
            {
                try
                {
                    Pull(); service.Save(state.Working, state.Original); state.Original = service.Records.FirstOrDefault(x => UInt(x,"s_nQuestID") == UInt(state.Working,"s_nQuestID"));
                    state.IsNew = false; state.Dirty = false; page.Text = (AchievementText(state.Working,"s_szTitle").Length > 0 ? AchievementText(state.Working,"s_szTitle") : AchievementText(state.Working,"s_szName")) + " [Edit]";
                    RefreshAllAchievementBrowsers(service); MessageBox.Show("Achieve.xml saved successfully.", "Achievement Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { ShowEditorError("Save Achievement", ex); }
            };
            raw.Click += (_, _) => OpenRawBlockTab(service.FilePath, new XElement(state.Working));
            selectBuff.Click += (_, _) =>
            {
                uint? selected = OpenAchievementReferencePicker("Select Buff", "Search Buff ID or name...",
                    service.Buffs, x => $"{UInt(x,"s_dwID")} — {AchievementText(x,"s_szName")} — {AchievementText(x,"s_szComment")}", x => UInt(x,"s_dwID"));
                if (selected.HasValue) fields["s_nBuffCode"].Text = selected.Value.ToString(CultureInfo.InvariantCulture);
            };
            selectQuest.Click += (_, _) =>
            {
                uint? selected = OpenAchievementReferencePicker("Select related title quest", "Search related achievement/title quests...",
                    service.RelatedTitleQuests, x => $"{UInt(x,"UniqID")} — {AchievementText(x,"TitleText")} — Type {AchievementText(x,"Type")}", x => UInt(x,"UniqID"));
                if (selected.HasValue) fields["s_nQuestID"].Text = selected.Value.ToString(CultureInfo.InvariantCulture);
            };
            createQuest.Click += (_, _) =>
            {
                try { Pull(); XElement q = service.CreateQuestTemplate(state.Working); RefreshRefs(); MessageBox.Show($"Quest {UInt(q,"UniqID")} created in Quest.xml.\r\nReview GoalId/goalAmount and NPC assignment before using it in game.", "Achievement Quest", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { ShowEditorError("Create Achievement Quest", ex); }
            };
            scroll.Resize += (_, _) => form.Width = Math.Max(720, scroll.ClientSize.Width - 40);
            RefreshRefs(); editorTabs.TabPages.Add(page); editorTabs.SelectedTab = page;

            void AddField(string label, string element, int x, int topY, int width, bool multiline = false, int height = 24)
            {
                form.Controls.Add(new Label { Text = label, ForeColor = CText, Location = new Point(x,topY), Size = new Size(width,18), Font = new Font("Segoe UI Semibold",8F) });
                var box = new TextBox { Text = working.Element(element)?.Value ?? string.Empty, Location = new Point(x,topY+20), Size = new Size(width,height),
                    BackColor = Color.FromArgb(10,10,10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
                fields[element] = box; form.Controls.Add(box);
            }
            void Pull()
            {
                foreach ((string key, TextBox box) in fields)
                {
                    XElement? e = state.Working.Element(key); if (e == null) state.Working.Add(new XElement(key, box.Text)); else e.Value = box.Text;
                }
            }
            void RefreshRefs()
            {
                uint iconId = uint.TryParse(fields["s_nIcon"].Text, out uint i) ? i : 0;
                Image? old = icon.Image; icon.Image = AchievementIconAtlasCache.TryLoad(iconId); old?.Dispose();
                uint buffId = uint.TryParse(fields["s_nBuffCode"].Text, out uint b) ? b : 0; buffStatus.Text = service.BuffSummary(buffId);
                uint qid = uint.TryParse(fields["s_nQuestID"].Text, out uint q) ? q : 0; XElement? quest = service.Quest(qid);
                questStatus.Text = quest == null ? $"Quest {qid}: not found" : $"Quest {qid}: {AchievementText(quest,"TitleText")} • Type {AchievementText(quest,"Type")}";
                questStatus.ForeColor = quest == null ? Color.FromArgb(255,180,90) : Color.FromArgb(120,220,145);
            }
        }

        private uint? OpenAchievementReferencePicker(string title, string placeholder,
            Func<string,IReadOnlyList<XElement>> search, Func<XElement,string> display, Func<XElement,uint> id)
        {
            using var dialog = new Form { Text = title, Size = new Size(760,560), StartPosition = FormStartPosition.CenterParent,
                BackColor = CEditor, ForeColor = CText, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
            var box = new TextBox { Location = new Point(14,14), Size = new Size(714,28), PlaceholderText = placeholder,
                BackColor = Color.FromArgb(10,10,10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle };
            var list = new ListBox { Location = new Point(14,52), Size = new Size(714,410), BackColor = Color.FromArgb(18,18,18), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI",9F) };
            var ok = new Button { Text = "SELECT", Location = new Point(608,474), Size = new Size(120,34), BackColor = Color.FromArgb(40,40,40), ForeColor = CText, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            dialog.Controls.AddRange(new Control[] { box,list,ok }); dialog.AcceptButton = ok;
            List<XElement> current = new();
            void Refresh()
            {
                current = search(box.Text).ToList(); list.BeginUpdate(); list.Items.Clear(); foreach (XElement x in current) list.Items.Add(display(x)); list.EndUpdate(); if (list.Items.Count > 0) list.SelectedIndex = 0;
            }
            box.TextChanged += (_, _) => Refresh(); list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK; };
            Refresh(); return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0 ? id(current[list.SelectedIndex]) : null;
        }

        private void RefreshAllAchievementBrowsers(AchievementService service)
        {
            foreach (TabPage page in editorTabs.TabPages)
                if (page.Tag is AchievementBrowseState state && ReferenceEquals(state.Service, service)) RefreshAchievementBrowser(state);
        }

        private static uint UInt(XElement node, string name) =>
            uint.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v) ? v : 0;
        private static int Int(XElement node, string name) =>
            int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        private static string AchievementText(XElement node, string name) => node.Element(name)?.Value ?? string.Empty;
    }
}
