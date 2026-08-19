using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private bool _dmBaseBridgeScheduled;
        private bool _dmBaseBridgeInitialized;
        private Dictionary<uint, DMBaseItemRef>? _dmBaseItems;
        private List<DMBaseSimpleRef>? _dmBaseMaps;
        private List<DMBaseSimpleRef>? _dmBaseEvolutions;

        private sealed class DMBaseVisualState
        {
            public required string XmlPath { get; init; }
            public required string FileName { get; init; }
            public required TabPage Page { get; init; }
            public required Panel Host { get; init; }
            public required TextBox Search { get; init; }
            public required Label Count { get; init; }
            public required Label PageLabel { get; init; }
            public required FlowLayoutPanel Cards { get; init; }
            public required Button Previous { get; init; }
            public required Button Next { get; init; }
            public XDocument Document { get; set; } = null!;
            public List<XElement> Records { get; set; } = new();
            public int PageIndex { get; set; }
            public int PageSize { get; set; } = 18;
        }

        private sealed class DMBaseItemRef
        {
            public uint Id { get; init; }
            public uint IconId { get; init; }
            public string Name { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
        }

        private sealed class DMBaseSimpleRef
        {
            public uint Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public string Extra { get; init; } = string.Empty;
        }

        private enum DMBaseReferenceKind
        {
            None,
            Item,
            Digimon,
            Map,
            Evolution
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            if (_dmBaseBridgeScheduled)
                return;

            _dmBaseBridgeScheduled = true;
            BeginInvoke(new Action(InitializeDMBaseEditorBridge));
        }

        private void InitializeDMBaseEditorBridge()
        {
            if (_dmBaseBridgeInitialized || editorTabs == null || editorTabs.IsDisposed)
                return;

            _dmBaseBridgeInitialized = true;
            editorTabs.SelectedIndexChanged += (_, _) => BeginInvoke(new Action(RefreshDMBaseEditorBridge));
            editorTabs.ControlAdded += (_, _) => BeginInvoke(new Action(RefreshDMBaseEditorBridge));
            RefreshDMBaseEditorBridge();
        }

        private void RefreshDMBaseEditorBridge()
        {
            if (editorTabs == null || editorTabs.IsDisposed)
                return;

            TabPage? page = editorTabs.SelectedTab;
            if (page == null || page.IsDisposed || page.Tag is DMBaseVisualState)
                return;

            string candidate = page.Name ?? string.Empty;
            if (candidate.Length == 0 || !candidate.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return;

            string full;
            try { full = Path.GetFullPath(candidate); }
            catch { return; }

            if (!File.Exists(full) || !DMBaseIsPath(full))
                return;

            editorTabs.TabPages.Remove(page);
            page.Dispose();
            OpenDMBaseVisualEditor(full);
        }

        private static bool DMBaseIsPath(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (Path.GetFileName(dir).Equals("DMBase", StringComparison.OrdinalIgnoreCase))
                    return true;
                dir = Path.GetDirectoryName(dir);
            }
            return false;
        }

        private void OpenDMBaseVisualEditor(string xmlPath)
        {
            string full = Path.GetFullPath(xmlPath);
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>()
                .FirstOrDefault(x => x.Tag is DMBaseVisualState s &&
                                     s.XmlPath.Equals(full, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page = CreateDarkTab(Path.GetFileName(full));
            page.Name = "dmbase-visual:" + full;
            var loading = new EditorLoadingView(
                "Loading DMBase Editor",
                $"Reading {Path.GetFileName(full)}, resolving references and preparing visual cards...");
            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            BeginInvoke(new Action(() =>
            {
                if (page.IsDisposed) return;
                try
                {
                    XDocument doc = XDocument.Load(full, LoadOptions.PreserveWhitespace);
                    BuildDMBaseVisualPage(page, full, doc);
                }
                catch (Exception ex)
                {
                    loading.SetError("DMBase XML could not be opened", ex.Message);
                }
            }));
        }

        private void BuildDMBaseVisualPage(TabPage page, string xmlPath, XDocument document)
        {
            page.Controls.Clear();
            string file = Path.GetFileName(xmlPath);

            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(16) };
            var header = new Panel { Dock = DockStyle.Top, Height = 116, BackColor = CEditor };
            var title = new Label
            {
                Text = $"DMBase — {Path.GetFileNameWithoutExtension(file)}",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                Location = new Point(4, 2),
                Size = new Size(520, 30),
                AutoEllipsis = true
            };
            var subtitle = new Label
            {
                Text = DMBaseSubtitle(file),
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(6, 34),
                Size = new Size(720, 22),
                AutoEllipsis = true
            };
            var search = new TextBox
            {
                Location = new Point(4, 70),
                Size = new Size(360, 26),
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle
            };
            var count = new Label
            {
                ForeColor = CMuted,
                Location = new Point(376, 70),
                Size = new Size(190, 26),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var newTemplate = CreateEditorActionButton("NEW TEMPLATE");
            newTemplate.Size = new Size(120, 34);
            newTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var settings = CreateEditorActionButton(file.Equals("Store.xml", StringComparison.OrdinalIgnoreCase) ? "STORE SETTINGS" : "XML INFO");
            settings.Size = new Size(120, 34);
            settings.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            header.Controls.AddRange(new Control[] { title, subtitle, search, count, newTemplate, settings });

            var nav = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = CEditor };
            var previous = CreateEditorActionButton("◀ PREVIOUS"); previous.Size = new Size(108, 32); previous.Location = new Point(4, 6);
            var pageLabel = new Label { ForeColor = CText, Size = new Size(90, 32), Location = new Point(120, 6), TextAlign = ContentAlignment.MiddleCenter };
            var next = CreateEditorActionButton("NEXT ▶"); next.Size = new Size(108, 32); next.Location = new Point(216, 6);
            nav.Controls.AddRange(new Control[] { previous, pageLabel, next });

            var cards = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = CEditor,
                Padding = new Padding(0, 8, 8, 26)
            };
            DarkUi.ApplyDarkScrollBar(cards);

            root.Controls.Add(cards);
            root.Controls.Add(nav);
            root.Controls.Add(header);
            page.Controls.Add(root);

            var state = new DMBaseVisualState
            {
                XmlPath = xmlPath,
                FileName = file,
                Page = page,
                Host = root,
                Search = search,
                Count = count,
                PageLabel = pageLabel,
                Cards = cards,
                Previous = previous,
                Next = next,
                Document = document,
                Records = DMBaseExtractRecords(file, document)
            };
            page.Tag = state;

            void LayoutHeader()
            {
                int right = header.ClientSize.Width - 4;
                newTemplate.Location = new Point(Math.Max(600, right - newTemplate.Width), 4);
                settings.Location = new Point(Math.Max(470, right - newTemplate.Width - settings.Width - 10), 4);
                title.Width = Math.Max(260, settings.Left - title.Left - 12);
                subtitle.Width = Math.Max(300, header.ClientSize.Width - subtitle.Left - 10);
                search.Width = Math.Max(220, Math.Min(430, header.ClientSize.Width - 360));
                count.Left = search.Right + 12;
            }
            header.Resize += (_, _) => LayoutHeader();
            LayoutHeader();

            search.TextChanged += (_, _) => { state.PageIndex = 0; DMBaseRenderCards(state); };
            previous.Click += (_, _) => { if (state.PageIndex > 0) { state.PageIndex--; DMBaseRenderCards(state); } };
            next.Click += (_, _) => { state.PageIndex++; DMBaseRenderCards(state); };
            newTemplate.Click += (_, _) => DMBaseCreateTemplate(state);
            settings.Click += (_, _) => DMBaseOpenRootSettings(state);

            cards.Resize += (_, _) =>
            {
                foreach (Control card in cards.Controls)
                    card.Width = Math.Max(500, cards.ClientSize.Width - 32);
            };

            DMBaseRenderCards(state);
        }

        private static string DMBaseSubtitle(string file) => file.ToLowerInvariant() switch
        {
            "csbasemapinfo.xml" => "Map base rules • MapList-linked selector • macro/shout settings",
            "digimonbase.xml" or "digimonbaseinfo.xml" => "Digimon level/stat growth curves • HP / DS / AT / DE / CT / HT / MS",
            "tamerbase.xml" or "tamerbaseinfo.xml" => "Tamer level/stat growth curves • searchable and pageable",
            "digimonevomaxskill.xml" or "digimonevomaxskilllevel.xml" => "Evolution skill level rules • EvolutionBaseApply-linked types",
            "evolutionbaseapply.xml" => "Evolution type dictionary • names and apply values",
            "expansioncondition.xml" or "expansiondata.xml" => "Expansion ranks • evolution type collections",
            "guild.xml" => "Guild progression • ItemList-linked requirements",
            "jumpbooster.xml" => "Jump Booster items • ItemList icons • MapList multi-map routing",
            "limit.xml" => "Global capacity and XG limits",
            "paneltyinfo.xml" => "Penalty level multipliers for EXP and drops",
            "party.xml" => "Party distance/range configuration",
            "store.xml" => "Consignment store visuals • ItemList + DigimonList dual references",
            _ => "DMBase visual XML editor • cross references preserved"
        };

        private static List<XElement> DMBaseExtractRecords(string file, XDocument document)
        {
            if (file.Equals("Store.xml", StringComparison.OrdinalIgnoreCase))
                return document.Descendants("StoreItem").ToList();
            return document.Root?.Elements().ToList() ?? new List<XElement>();
        }

        private void DMBaseRenderCards(DMBaseVisualState state)
        {
            if (state.Page.IsDisposed || state.Cards.IsDisposed) return;
            string q = state.Search.Text.Trim();
            List<XElement> filtered = state.Records
                .Where(x => q.Length == 0 || DMBaseSearchText(x).Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();

            int pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)state.PageSize));
            state.PageIndex = Math.Max(0, Math.Min(state.PageIndex, pages - 1));
            List<XElement> visible = filtered.Skip(state.PageIndex * state.PageSize).Take(state.PageSize).ToList();

            state.Cards.SuspendLayout();
            foreach (Control old in state.Cards.Controls.Cast<Control>().ToArray()) old.Dispose();
            state.Cards.Controls.Clear();
            foreach (XElement record in visible)
                state.Cards.Controls.Add(DMBaseCreateRecordCard(state, record));
            state.Cards.ResumeLayout(true);

            state.Count.Text = $"{filtered.Count:N0} / {state.Records.Count:N0} records";
            state.PageLabel.Text = $"{state.PageIndex + 1} / {pages}";
            state.Previous.Enabled = state.PageIndex > 0;
            state.Next.Enabled = state.PageIndex + 1 < pages;
            state.Cards.AutoScrollPosition = Point.Empty;
        }

        private Control DMBaseCreateRecordCard(DMBaseVisualState state, XElement record)
        {
            string file = state.FileName;
            int h = file.Equals("JumpBooster.xml", StringComparison.OrdinalIgnoreCase) ||
                    file.Equals("Store.xml", StringComparison.OrdinalIgnoreCase) ? 154 : 126;
            var card = new Panel
            {
                Width = Math.Max(520, state.Cards.ClientSize.Width - 32),
                Height = h,
                BackColor = Color.FromArgb(29, 29, 33),
                Margin = new Padding(0, 0, 0, 10)
            };
            card.Paint += (_, e) =>
            {
                using var p = new Pen(Color.FromArgb(62, 62, 68));
                e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1);
            };

            string title = DMBaseCardTitle(state, record);
            string meta = DMBaseCardMeta(state, record);
            string detail = DMBaseCardDetail(state, record);

            int left = 18;
            if (DMBaseTryBuildPreview(state, record, card, out int previewRight))
                left = previewRight + 14;

            var titleLabel = new Label
            {
                Text = title,
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 10.2F, FontStyle.Bold),
                Location = new Point(left, 14),
                Size = new Size(Math.Max(220, card.Width - left - 310), 25),
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var metaLabel = new Label
            {
                Text = meta,
                ForeColor = Color.FromArgb(100, 230, 145),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Location = new Point(left, 42),
                Size = new Size(Math.Max(220, card.Width - left - 310), 22),
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            var detailLabel = new Label
            {
                Text = detail,
                ForeColor = CMuted,
                Font = new Font("Segoe UI", 8.3F),
                Location = new Point(left, 68),
                Size = new Size(Math.Max(220, card.Width - left - 310), h - 78),
                AutoEllipsis = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            var edit = CreateEditorActionButton("EDIT"); edit.Size = new Size(88, 30); edit.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var clone = CreateEditorActionButton("CLONE"); clone.Size = new Size(88, 30); clone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var delete = CreateEditorActionButton("DELETE"); delete.Size = new Size(88, 30); delete.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            void LayoutButtons()
            {
                int x = card.ClientSize.Width - 104;
                edit.Location = new Point(x, 14);
                clone.Location = new Point(x, 50);
                delete.Location = new Point(x, 86);
            }
            card.Resize += (_, _) => LayoutButtons();
            LayoutButtons();

            edit.Click += (_, _) => DMBaseOpenRecordEditor(state, record);
            clone.Click += (_, _) => DMBaseCloneRecord(state, record);
            delete.Click += (_, _) => DMBaseDeleteRecord(state, record);

            card.Controls.AddRange(new Control[] { titleLabel, metaLabel, detailLabel, edit, clone, delete });
            return card;
        }

        private string DMBaseCardTitle(DMBaseVisualState state, XElement r)
        {
            string f = state.FileName.ToLowerInvariant();
            if (f is "digimonbase.xml" or "digimonbaseinfo.xml" or "tamerbase.xml" or "tamerbaseinfo.xml")
                return $"Level {DMBaseText(r, "Level")} • Curve ID {DMBaseText(r, "Id")}";
            if (f == "csbasemapinfo.xml")
            {
                uint id = DMBaseUInt(r, "s_nMapID");
                return $"Map {id} — {DMBaseMapName(id)}";
            }
            if (f is "digimonevomaxskill.xml" or "digimonevomaxskilllevel.xml")
            {
                uint id = DMBaseUInt(r, "nEvoType");
                return $"{DMBaseEvolutionName(id)} — Skill Level Rule";
            }
            if (f == "evolutionbaseapply.xml")
                return $"{DMBaseText(r, "EvolutionName")} • Evolution Type {DMBaseText(r, "EvolutionType")}";
            if (f is "expansioncondition.xml" or "expansiondata.xml")
                return $"Expansion Rank {DMBaseText(r, "nExpansionRank")} • SubType {DMBaseText(r, "nOpenItemSubType")}";
            if (f == "guild.xml")
                return $"Guild Level {DMBaseText(r, "s_nLevel")}";
            if (f == "jumpbooster.xml")
            {
                uint id = DMBaseUInt(r, "dwItemID");
                return $"{DMBaseItemName(id)} • Jump Booster {id}";
            }
            if (f == "paneltyinfo.xml")
                return $"Penalty Level {DMBaseText(r, "s_nPaneltyLevel")}";
            if (f == "store.xml")
            {
                uint item = DMBaseUInt(r, "s_nItemID");
                return $"{DMBaseItemName(item)} • Store Item {item}";
            }
            if (f == "limit.xml") return "Global Limits";
            if (f == "party.xml") return "Party Configuration";
            return r.Name.LocalName;
        }

        private string DMBaseCardMeta(DMBaseVisualState state, XElement r)
        {
            string f = state.FileName.ToLowerInvariant();
            if (f is "digimonbase.xml" or "digimonbaseinfo.xml" or "tamerbase.xml" or "tamerbaseinfo.xml")
                return $"HP {DMBaseText(r, "Hp")}  •  DS {DMBaseText(r, "Ds")}  •  AT {DMBaseText(r, "At")}  •  DE {DMBaseText(r, "De")}";
            if (f == "csbasemapinfo.xml")
                return $"Shout {DMBaseText(r, "s_nShoutSec")} ms • Macro check {(DMBaseText(r, "s_bEnableCheckMacro") == "1" ? "ENABLED" : "DISABLED")}";
            if (f is "digimonevomaxskill.xml" or "digimonevomaxskilllevel.xml")
                return $"Start Level {DMBaseText(r, "s_SkillExpStartLv")} • {DMBaseText(r, "nSubCount")} stages";
            if (f == "evolutionbaseapply.xml")
                return $"Apply value {DMBaseText(r, "EvolutionApplyValue")} • Name size {DMBaseText(r, "NameSize")}";
            if (f is "expansioncondition.xml" or "expansiondata.xml")
                return "Evolution Types: " + string.Join(", ", r.Element("nEvoType")?.Elements("Type").Select(x => $"{x.Value} {DMBaseEvolutionName(DMBaseParseUInt(x.Value))}") ?? Enumerable.Empty<string>());
            if (f == "guild.xml")
                return $"Fame {DMBaseText(r, "s_nFame")} • Max members {DMBaseText(r, "s_nMaxGuildPerson")} • Master Lv {DMBaseText(r, "s_nMasterLevel")}";
            if (f == "jumpbooster.xml")
                return $"{r.Element("dwMapIDs")?.Elements("dwMapID").Count() ?? 0} destination maps";
            if (f == "paneltyinfo.xml")
                return $"EXP {DMBaseText(r, "s_nExp")}% • Drop {DMBaseText(r, "s_nDrop")}%";
            if (f == "store.xml")
            {
                uint digi = DMBaseUInt(r, "s_nDigimonID");
                return $"Digimon {digi} • {DMBaseDigimonName(digi)} • Slots {DMBaseText(r, "s_nSlotCount")}";
            }
            if (f == "limit.xml") return $"Warehouse {DMBaseText(r, "s_nMaxWareHouse")} • Tactics {DMBaseText(r, "s_nMaxTacticsHouse")} • Share Stash {DMBaseText(r, "s_nMaxShareStash")}";
            if (f == "party.xml") return $"Distance {DMBaseText(r, "distc")}";
            return string.Join(" • ", r.Elements().Where(x => !x.HasElements).Take(4).Select(x => $"{x.Name.LocalName} {x.Value}"));
        }

        private string DMBaseCardDetail(DMBaseVisualState state, XElement r)
        {
            string f = state.FileName.ToLowerInvariant();
            if (f is "digimonbase.xml" or "digimonbaseinfo.xml" or "tamerbase.xml" or "tamerbaseinfo.xml")
                return $"EXP {DMBaseText(r, "Exp")} • MS {DMBaseText(r, "Ms")} • EV {DMBaseText(r, "Ev")} • CT {DMBaseText(r, "Ct")} • HT {DMBaseText(r, "Ht")}";
            if (f == "jumpbooster.xml")
                return "Maps: " + string.Join(", ", r.Element("dwMapIDs")?.Elements("dwMapID").Take(12).Select(x => $"{x.Value} {DMBaseMapName(DMBaseParseUInt(x.Value))}") ?? Enumerable.Empty<string>());
            if (f == "guild.xml")
                return $"Item 1: {DMBaseText(r, "s_nItemNo1")} ×{DMBaseText(r, "s_nItemCount1")} • Item 2: {DMBaseText(r, "s_nItemNo2")} ×{DMBaseText(r, "s_nItemCount2")}";
            if (f == "store.xml")
                return $"Scale {DMBaseText(r, "s_fScale")} • File {DMBaseText(r, "s_szFileName")}";
            if (f is "digimonevomaxskill.xml" or "digimonevomaxskilllevel.xml")
                return "Max Skill Levels: " + string.Join(" → ", r.Element("s_SkillMaxLvs")?.Elements("SkillMaxLv").Select(x => x.Value) ?? Enumerable.Empty<string>());
            return string.Join(" • ", r.Elements().Where(x => !x.HasElements).Skip(4).Take(6).Select(x => $"{x.Name.LocalName} {x.Value}"));
        }

        private bool DMBaseTryBuildPreview(DMBaseVisualState state, XElement r, Panel card, out int right)
        {
            right = 0;
            string f = state.FileName.ToLowerInvariant();
            if (f == "jumpbooster.xml" || f == "guild.xml")
            {
                uint item = f == "jumpbooster.xml" ? DMBaseUInt(r, "dwItemID") : DMBaseUInt(r, "s_nItemNo1");
                if (item == 0) return false;
                var pic = DMBasePictureBox(18, 22, 70, 70);
                pic.Image = DMBaseLoadItemIcon(item);
                card.Controls.Add(pic);
                right = pic.Right;
                return true;
            }
            if (f == "store.xml")
            {
                uint item = DMBaseUInt(r, "s_nItemID");
                uint digi = DMBaseUInt(r, "s_nDigimonID");
                var itemPic = DMBasePictureBox(14, 22, 64, 64); itemPic.Image = DMBaseLoadItemIcon(item);
                var digiPic = DMBasePictureBox(84, 22, 64, 64); digiPic.Image = digi == 0 ? null : DigimonBookDigimonCatalog.TryLoadIcon(digi);
                card.Controls.Add(itemPic); card.Controls.Add(digiPic);
                right = digiPic.Right;
                return true;
            }
            return false;
        }

        private static PictureBox DMBasePictureBox(int x, int y, int w, int h) => new()
        {
            Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        private void DMBaseOpenRecordEditor(DMBaseVisualState state, XElement record)
        {
            int index = state.Records.IndexOf(record);
            string key = $"dmbase-edit:{state.XmlPath}:{index}";
            TabPage? existing = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Name == key);
            if (existing != null) { editorTabs.SelectedTab = existing; return; }

            var page = CreateDarkTab($"{Path.GetFileNameWithoutExtension(state.FileName)} #{index + 1}");
            page.Name = key;
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(18) };
            var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = CEditor };
            header.Controls.Add(new Label
            {
                Text = DMBaseCardTitle(state, record), ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                Location = new Point(0, 0), Size = new Size(650, 28), AutoEllipsis = true
            });
            header.Controls.Add(new Label
            {
                Text = "All XML fields remain editable • SELECT buttons resolve linked XML records.", ForeColor = CMuted,
                Location = new Point(2, 31), Size = new Size(700, 22), AutoEllipsis = true
            });
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = CEditor };
            var save = CreateEditorActionButton("SAVE"); save.Size = new Size(110, 34); save.Dock = DockStyle.Right;
            footer.Padding = new Padding(0, 10, 0, 10); footer.Controls.Add(save);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CEditor, Padding = new Padding(0, 8, 10, 30) };
            DarkUi.ApplyDarkScrollBar(scroll);
            root.Controls.Add(scroll); root.Controls.Add(footer); root.Controls.Add(header); page.Controls.Add(root);

            var fields = new Dictionary<XElement, TextBox>();
            var lists = new Dictionary<XElement, DataGridView>();
            int y = 8;
            foreach (XElement child in record.Elements())
            {
                if (!child.HasElements)
                {
                    var label = new Label { Text = child.Name.LocalName, ForeColor = CText, Location = new Point(6, y), Size = new Size(210, 24), TextAlign = ContentAlignment.MiddleLeft };
                    var box = new TextBox { Text = child.Value, Location = new Point(220, y), Size = new Size(360, 25), BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                    scroll.Controls.Add(label); scroll.Controls.Add(box); fields[child] = box;
                    DMBaseReferenceKind kind = DMBaseReferenceFor(child);
                    if (kind != DMBaseReferenceKind.None)
                    {
                        var select = CreateEditorActionButton("SELECT"); select.Location = new Point(590, y - 3); select.Size = new Size(88, 30); select.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                        select.Click += async (_, _) =>
                        {
                            uint current = DMBaseParseUInt(box.Text);
                            uint? chosen = await DMBaseOpenReferencePickerAsync(kind, current, $"Select {child.Name.LocalName}");
                            if (chosen.HasValue && !page.IsDisposed) box.Text = chosen.Value.ToString(CultureInfo.InvariantCulture);
                        };
                        scroll.Controls.Add(select);
                    }
                    y += 36;
                }
                else if (child.Elements().All(x => !x.HasElements))
                {
                    var group = new Panel { Location = new Point(6, y), Size = new Size(680, 190), BackColor = Color.FromArgb(25, 25, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
                    group.Controls.Add(new Label { Text = child.Name.LocalName, ForeColor = CText, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Location = new Point(10, 8), Size = new Size(300, 24) });
                    var grid = new DataGridView
                    {
                        Location = new Point(10, 38), Size = new Size(520, 140), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                        BackgroundColor = Color.FromArgb(18, 18, 18), ForeColor = Color.Black, RowHeadersVisible = false,
                        AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
                    };
                    string childName = child.Elements().FirstOrDefault()?.Name.LocalName ?? "Value";
                    grid.Columns.Add("Value", childName);
                    foreach (XElement value in child.Elements()) grid.Rows.Add(value.Value);
                    var add = CreateEditorActionButton("ADD"); add.Location = new Point(542, 42); add.Size = new Size(80, 30); add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    var remove = CreateEditorActionButton("REMOVE"); remove.Location = new Point(542, 78); remove.Size = new Size(80, 30); remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    var select = CreateEditorActionButton("SELECT"); select.Location = new Point(542, 114); select.Size = new Size(80, 30); select.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                    DMBaseReferenceKind listKind = DMBaseReferenceForList(child, childName);
                    select.Visible = listKind != DMBaseReferenceKind.None;
                    add.Click += (_, _) => grid.Rows.Add(string.Empty);
                    remove.Click += (_, _) => { if (grid.SelectedRows.Count > 0 && !grid.SelectedRows[0].IsNewRow) grid.Rows.Remove(grid.SelectedRows[0]); };
                    select.Click += async (_, _) =>
                    {
                        uint current = 0;
                        if (grid.CurrentRow != null) current = DMBaseParseUInt(Convert.ToString(grid.CurrentRow.Cells[0].Value, CultureInfo.InvariantCulture));
                        uint? chosen = await DMBaseOpenReferencePickerAsync(listKind, current, $"Select {childName}");
                        if (!chosen.HasValue || page.IsDisposed) return;
                        if (grid.CurrentRow == null || grid.CurrentRow.IsNewRow) grid.Rows.Add(chosen.Value);
                        else grid.CurrentRow.Cells[0].Value = chosen.Value;
                    };
                    group.Controls.Add(grid); group.Controls.Add(add); group.Controls.Add(remove); group.Controls.Add(select);
                    scroll.Controls.Add(group); lists[child] = grid; y += 202;
                }
                else
                {
                    var note = new Label { Text = $"{child.Name.LocalName}: complex collection managed by its own cards.", ForeColor = CMuted, Location = new Point(6, y), Size = new Size(650, 28) };
                    scroll.Controls.Add(note); y += 34;
                }
            }
            scroll.AutoScrollMinSize = new Size(0, y + 30);

            save.Click += (_, _) =>
            {
                foreach ((XElement element, TextBox box) in fields) element.Value = box.Text;
                foreach ((XElement container, DataGridView grid) in lists)
                {
                    string childName = container.Elements().FirstOrDefault()?.Name.LocalName ?? "Value";
                    container.RemoveNodes();
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (row.IsNewRow) continue;
                        string value = Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                        if (value.Length > 0) container.Add(new XElement(childName, value));
                    }
                    XElement? countField = record.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("mapcount", StringComparison.OrdinalIgnoreCase) || x.Name.LocalName.Equals("nSubCount", StringComparison.OrdinalIgnoreCase));
                    if (countField != null) countField.Value = container.Elements().Count().ToString(CultureInfo.InvariantCulture);
                }
                DMBaseSaveState(state);
                DMBaseRenderCards(state);
                editorTabs.SelectedTab = state.Page;
            };

            editorTabs.TabPages.Add(page); editorTabs.SelectedTab = page;
        }

        private void DMBaseOpenRootSettings(DMBaseVisualState state)
        {
            if (state.Document.Root == null) return;
            XElement? target = state.FileName.Equals("Store.xml", StringComparison.OrdinalIgnoreCase)
                ? state.Document.Root.Element("Store")
                : state.Document.Root.Elements().FirstOrDefault();
            if (target == null) return;
            DMBaseOpenRecordEditor(state, target);
        }

        private void DMBaseCreateTemplate(DMBaseVisualState state)
        {
            XElement? source = state.Records.FirstOrDefault();
            if (source == null || source.Parent == null) return;
            XElement clone = new XElement(source);
            DMBaseResetTemplateIds(state.FileName, clone);
            source.Parent.Add(clone);
            DMBaseSaveState(state);
            DMBaseReloadState(state);
            state.PageIndex = Math.Max(0, (state.Records.Count - 1) / state.PageSize);
            DMBaseRenderCards(state);
            DMBaseOpenRecordEditor(state, state.Records.Last());
        }

        private void DMBaseCloneRecord(DMBaseVisualState state, XElement record)
        {
            if (record.Parent == null) return;
            XElement clone = new XElement(record);
            record.AddAfterSelf(clone);
            DMBaseSaveState(state);
            DMBaseReloadState(state);
            DMBaseRenderCards(state);
        }

        private void DMBaseDeleteRecord(DMBaseVisualState state, XElement record)
        {
            DialogResult result = MessageBox.Show(this,
                $"Delete this {record.Name.LocalName} record from {state.FileName}?\r\n\r\nA .editor.bak backup will be created before saving.",
                "Delete DMBase record", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
            record.Remove();
            DMBaseSaveState(state);
            DMBaseReloadState(state);
            DMBaseRenderCards(state);
        }

        private static void DMBaseResetTemplateIds(string file, XElement node)
        {
            string[] names = file.Equals("Store.xml", StringComparison.OrdinalIgnoreCase)
                ? new[] { "s_nItemID", "s_nDigimonID" }
                : new[] { "Id", "s_nMapID", "dwItemID", "s_nLevel" };
            foreach (string n in names)
                if (node.Element(n) is XElement e) e.Value = "0";
        }

        private void DMBaseSaveState(DMBaseVisualState state)
        {
            File.Copy(state.XmlPath, state.XmlPath + ".editor.bak", true);
            state.Document.Save(state.XmlPath);
        }

        private void DMBaseReloadState(DMBaseVisualState state)
        {
            state.Document = XDocument.Load(state.XmlPath, LoadOptions.PreserveWhitespace);
            state.Records = DMBaseExtractRecords(state.FileName, state.Document);
        }

        private static string DMBaseSearchText(XElement node) =>
            string.Join(" ", node.DescendantsAndSelf().Select(x => x.HasElements ? x.Name.LocalName : x.Value));

        private static string DMBaseText(XElement node, string name) => node.Element(name)?.Value?.Trim() ?? string.Empty;
        private static uint DMBaseUInt(XElement node, string name) => DMBaseParseUInt(DMBaseText(node, name));
        private static uint DMBaseParseUInt(string? value) => uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint n) ? n : 0;

        private DMBaseReferenceKind DMBaseReferenceFor(XElement element)
        {
            string n = element.Name.LocalName;
            if (n is "dwItemID" or "s_nItemID" or "s_nItemNo1" or "s_nItemNo2") return DMBaseReferenceKind.Item;
            if (n == "s_nDigimonID") return DMBaseReferenceKind.Digimon;
            if (n is "s_nMapID" or "dwMapID") return DMBaseReferenceKind.Map;
            if (n is "nEvoType" or "EvolutionType") return DMBaseReferenceKind.Evolution;
            return DMBaseReferenceKind.None;
        }

        private DMBaseReferenceKind DMBaseReferenceForList(XElement container, string childName)
        {
            if (childName == "dwMapID") return DMBaseReferenceKind.Map;
            if (childName == "Type" && container.Name.LocalName == "nEvoType") return DMBaseReferenceKind.Evolution;
            return DMBaseReferenceKind.None;
        }

        private async Task<uint?> DMBaseOpenReferencePickerAsync(DMBaseReferenceKind kind, uint current, string title)
        {
            if (kind == DMBaseReferenceKind.None) return null;
            var completion = new TaskCompletionSource<uint?>();
            var page = CreateDarkTab(title);
            page.Name = "dmbase-picker:" + Guid.NewGuid().ToString("N");
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(16) };
            var toolbar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = CEditor };
            var search = new TextBox { Location = new Point(0, 10), Size = new Size(360, 26), BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle };
            var confirm = CreateEditorActionButton("CONFIRM"); confirm.Size = new Size(110, 34); confirm.Dock = DockStyle.Right; confirm.Enabled = false;
            toolbar.Controls.Add(search); toolbar.Controls.Add(confirm);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BackColor = CEditor };
            split.Panel1MinSize = 300; split.Panel2MinSize = 180;
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(18, 18, 18), ForeColor = Color.Black,
                RowHeadersVisible = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.Columns.Add("ID", "ID"); grid.Columns.Add("Name", "Name"); grid.Columns.Add("Extra", "Details");
            split.Panel1.Controls.Add(grid);
            var preview = new PictureBox { Dock = DockStyle.Top, Height = 180, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom };
            var details = new Label { Dock = DockStyle.Fill, ForeColor = CText, Padding = new Padding(10), TextAlign = ContentAlignment.TopLeft };
            split.Panel2.Controls.Add(details); split.Panel2.Controls.Add(preview);
            root.Controls.Add(split); root.Controls.Add(toolbar); page.Controls.Add(root);
            editorTabs.TabPages.Add(page); editorTabs.SelectedTab = page;

            uint? selected = null;
            void Fill()
            {
                grid.Rows.Clear();
                string q = search.Text.Trim();
                if (kind == DMBaseReferenceKind.Item)
                {
                    foreach (DMBaseItemRef x in DMBaseItems().Values.Where(x => q.Length == 0 || x.Id.ToString().Contains(q) || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Id).Take(800))
                    { int i = grid.Rows.Add(x.Id, x.Name, $"Icon {x.IconId}"); grid.Rows[i].Tag = x; }
                }
                else if (kind == DMBaseReferenceKind.Digimon)
                {
                    foreach (DigimonBookDigimonEntry x in DigimonBookDigimonCatalog.Search(q, 800))
                    { int i = grid.Rows.Add(x.Id, x.Name, $"Model {x.ModelId}"); grid.Rows[i].Tag = x; }
                }
                else
                {
                    IEnumerable<DMBaseSimpleRef> source = kind == DMBaseReferenceKind.Map ? DMBaseMaps() : DMBaseEvolutions();
                    foreach (DMBaseSimpleRef x in source.Where(x => q.Length == 0 || x.Id.ToString().Contains(q) || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(800))
                    { int i = grid.Rows.Add(x.Id, x.Name, x.Extra); grid.Rows[i].Tag = x; }
                }
                if (grid.Rows.Count > 0)
                {
                    int idx = grid.Rows.Cast<DataGridViewRow>().ToList().FindIndex(r => DMBaseParseUInt(Convert.ToString(r.Cells[0].Value, CultureInfo.InvariantCulture)) == current);
                    grid.ClearSelection(); grid.Rows[Math.Max(0, idx)].Selected = true;
                }
            }
            void SelectRow()
            {
                Image? old = preview.Image; preview.Image = null; old?.Dispose();
                if (grid.SelectedRows.Count == 0) { selected = null; confirm.Enabled = false; return; }
                selected = DMBaseParseUInt(Convert.ToString(grid.SelectedRows[0].Cells[0].Value, CultureInfo.InvariantCulture));
                confirm.Enabled = selected.HasValue && selected.Value > 0;
                object? tag = grid.SelectedRows[0].Tag;
                if (tag is DMBaseItemRef item)
                { preview.Image = DMBaseLoadItemIcon(item.Id); details.Text = $"Item {item.Id}\r\n{item.Name}\r\nIcon {item.IconId}\r\n\r\n{item.Description}"; }
                else if (tag is DigimonBookDigimonEntry digi)
                { preview.Image = DigimonBookDigimonCatalog.TryLoadIcon(digi.Id); details.Text = $"Digimon {digi.Id}\r\n{digi.Name}\r\nModel {digi.ModelId}"; }
                else if (tag is DMBaseSimpleRef simple)
                { details.Text = $"{simple.Id}\r\n{simple.Name}\r\n{simple.Extra}"; }
                preview.Visible = preview.Image != null;
            }
            void Close(uint? value)
            {
                if (!completion.Task.IsCompleted) completion.TrySetResult(value);
                if (editorTabs.TabPages.Contains(page)) editorTabs.TabPages.Remove(page);
                page.Dispose();
            }
            search.TextChanged += (_, _) => Fill();
            grid.SelectionChanged += (_, _) => SelectRow();
            grid.CellDoubleClick += (_, _) => { if (selected.HasValue) Close(selected); };
            confirm.Click += (_, _) => Close(selected);
            page.Disposed += (_, _) => { if (!completion.Task.IsCompleted) completion.TrySetResult(null); };
            page.Resize += (_, _) =>
            {
                int width = split.ClientSize.Width;
                if (width > 520)
                {
                    split.Panel1MinSize = Math.Min(300, width / 2);
                    split.Panel2MinSize = Math.Min(180, width / 3);
                    int max = Math.Max(split.Panel1MinSize, width - split.Panel2MinSize - split.SplitterWidth);
                    split.SplitterDistance = Math.Min(max, Math.Max(split.Panel1MinSize, width - 280));
                }
            };
            Fill(); SelectRow();
            return await completion.Task;
        }

        private Dictionary<uint, DMBaseItemRef> DMBaseItems()
        {
            if (_dmBaseItems != null) return _dmBaseItems;
            _dmBaseItems = new Dictionary<uint, DMBaseItemRef>();
            string path = Directory.Exists(AppPaths.Xml)
                ? Directory.EnumerateFiles(AppPaths.Xml, "ItemList.xml", SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault() ?? string.Empty
                : string.Empty;
            if (!File.Exists(path)) return _dmBaseItems;
            try
            {
                XDocument doc = XDocument.Load(path);
                string[] ids = { "s_dwItemID", "s_nItemID", "ItemId", "ItemID", "ID" };
                foreach (XElement node in doc.Descendants())
                {
                    XElement? idNode = ids.Select(x => node.Element(x)).FirstOrDefault(x => x != null);
                    if (idNode == null) continue;
                    uint id = DMBaseParseUInt(idNode.Value); if (id == 0 || _dmBaseItems.ContainsKey(id)) continue;
                    _dmBaseItems[id] = new DMBaseItemRef
                    {
                        Id = id,
                        Name = DMBaseFirst(node, "s_szName", "s_szItemName", "ItemName", "Name"),
                        IconId = DMBaseFirstUInt(node, "s_nIcon", "s_nIconID", "s_dwIcon", "IconID", "Icon"),
                        Description = DMBaseFirst(node, "s_szComment", "s_szDescription", "Description", "Desc")
                    };
                }
            }
            catch { }
            return _dmBaseItems;
        }

        private Image? DMBaseLoadItemIcon(uint itemId)
        {
            if (!DMBaseItems().TryGetValue(itemId, out DMBaseItemRef? item) || item.IconId == 0) return null;
            return ImageDatabasePreview.TryLoadInterfaceIcon(item.IconId, "Item");
        }

        private string DMBaseItemName(uint itemId) => DMBaseItems().TryGetValue(itemId, out DMBaseItemRef? x) && x.Name.Length > 0 ? x.Name : (itemId == 0 ? "None" : "Item " + itemId);

        private List<DMBaseSimpleRef> DMBaseMaps()
        {
            if (_dmBaseMaps != null) return _dmBaseMaps;
            _dmBaseMaps = new List<DMBaseSimpleRef>();
            if (!Directory.Exists(AppPaths.Xml)) return _dmBaseMaps;
            string path = Directory.EnumerateFiles(AppPaths.Xml, "MapList.xml", SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault() ?? string.Empty;
            if (!File.Exists(path)) return _dmBaseMaps;
            try
            {
                XDocument doc = XDocument.Load(path);
                foreach (XElement node in doc.Descendants())
                {
                    uint id = DMBaseFirstUInt(node, "s_nMapID", "MapID", "MapId", "ID", "Id"); if (id == 0) continue;
                    if (_dmBaseMaps.Any(x => x.Id == id)) continue;
                    string name = DMBaseFirst(node, "s_szMapName", "MapName", "Name", "s_szName");
                    _dmBaseMaps.Add(new DMBaseSimpleRef { Id = id, Name = name.Length > 0 ? name : "Map " + id, Extra = node.Name.LocalName });
                }
            }
            catch { }
            return _dmBaseMaps.OrderBy(x => x.Id).ToList();
        }

        private string DMBaseMapName(uint id) => DMBaseMaps().FirstOrDefault(x => x.Id == id)?.Name ?? (id == 0 ? "None" : "Map " + id);

        private List<DMBaseSimpleRef> DMBaseEvolutions()
        {
            if (_dmBaseEvolutions != null) return _dmBaseEvolutions;
            _dmBaseEvolutions = new List<DMBaseSimpleRef>();
            string path = Path.Combine(AppPaths.Xml, "DMBase", "EvolutionBaseApply.xml");
            if (!File.Exists(path) && Directory.Exists(AppPaths.Xml))
                path = Directory.EnumerateFiles(AppPaths.Xml, "EvolutionBaseApply.xml", SearchOption.AllDirectories).FirstOrDefault() ?? string.Empty;
            if (!File.Exists(path)) return _dmBaseEvolutions;
            try
            {
                XDocument doc = XDocument.Load(path);
                foreach (XElement x in doc.Root?.Elements("EvolutionBaseApply") ?? Enumerable.Empty<XElement>())
                {
                    uint id = DMBaseUInt(x, "EvolutionType"); if (id == 0) continue;
                    _dmBaseEvolutions.Add(new DMBaseSimpleRef { Id = id, Name = DMBaseText(x, "EvolutionName"), Extra = "Apply " + DMBaseText(x, "EvolutionApplyValue") });
                }
            }
            catch { }
            return _dmBaseEvolutions;
        }

        private string DMBaseEvolutionName(uint id) => DMBaseEvolutions().FirstOrDefault(x => x.Id == id)?.Name ?? (id == 0 ? "None" : "Type " + id);

        private string DMBaseDigimonName(uint id)
        {
            if (id == 0) return "None";
            return DigimonBookDigimonCatalog.Search(id.ToString(CultureInfo.InvariantCulture), 20).FirstOrDefault(x => x.Id == id)?.Name ?? "Digimon " + id;
        }

        private static string DMBaseFirst(XElement node, params string[] names)
        {
            foreach (string n in names)
            {
                string v = node.Element(n)?.Value?.Trim() ?? string.Empty;
                if (v.Length > 0) return v;
            }
            return string.Empty;
        }

        private static uint DMBaseFirstUInt(XElement node, params string[] names)
        {
            foreach (string n in names)
            {
                uint v = DMBaseParseUInt(node.Element(n)?.Value);
                if (v > 0) return v;
            }
            return 0;
        }
    }
}
