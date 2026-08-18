using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using DRW_Work_Tool.Core;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private const int DigimonPageSize = 24;
        private readonly Dictionary<uint, Bitmap?> digimonEditorIcons = new();

        private sealed class DigimonRow
        {
            public required XElement Node { get; init; }
            public uint Id { get; init; }
            public uint ModelId { get; init; }
            public string Name { get; init; } = "";
            public string Form { get; init; } = "";
            public int EvolutionType { get; init; }
            public int AttributeType { get; init; }
            public int Rank { get; init; }
            public int Hp { get; init; }
            public int At { get; init; }
            public int De { get; init; }
            public int SkillCount { get; init; }
        }

        private sealed class DigimonBrowseState
        {
            public required string Path { get; init; }
            public required XDocument Document { get; init; }
            public required List<DigimonRow> Rows { get; init; }
            public required TextBox Search { get; init; }
            public required ComboBox Evolution { get; init; }
            public required ComboBox Attribute { get; init; }
            public required ComboBox Rank { get; init; }
            public required Label Count { get; init; }
            public required Label Page { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required Button Prev { get; init; }
            public required Button Next { get; init; }
            public required System.Windows.Forms.Timer Timer { get; init; }
            public List<DigimonRow> Filtered { get; set; } = new();
            public int PageIndex { get; set; }
        }

        private sealed class DigimonListEditState
        {
            public required string Path { get; init; }
            public required XDocument Document { get; init; }
            public required XElement Working { get; init; }
            public XElement? Original { get; set; }
            public bool Dirty { get; set; }
            public bool IsNew { get; set; }
            public required Dictionary<string, Control> Fields { get; init; }
            public required TextBox[] SkillIds { get; init; }
            public required string[] SkillReqValues { get; init; }
            public required Label[] SkillNames { get; init; }
            public required PictureBox[] SkillIcons { get; init; }
            public required Label[] SkillIdLabels { get; init; }
            public required Label[] SkillComments { get; init; }
            public required Button[] SkillSelectButtons { get; init; }
            public required Button[] SkillClearButtons { get; init; }
            public required PictureBox Icon { get; init; }
            public required Label HeroName { get; init; }
            public required Label HeroMeta { get; init; }
            public required Label IdStatus { get; init; }
        }

        private sealed class DigimonOption
        {
            public int Value { get; }
            public string Label { get; }
            public DigimonOption(int value, string label) { Value = value; Label = label; }
            public override string ToString() => Label;
        }

        private async void OpenDigimonListBrowser(string xmlPath)
        {
            string full = System.IO.Path.GetFullPath(xmlPath);
            TabPage? current =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                full,
                                StringComparison.OrdinalIgnoreCase));

            if (current != null)
            {
                // A previous generic XML route may already have opened the
                // same physical file as "Block Browser". Keep a real Digimon
                // browser tab, but discard the stale generic one.
                if (current.Tag is DigimonBrowseState)
                {
                    editorTabs.SelectedTab = current;
                    return;
                }

                editorTabs.TabPages.Remove(current);
                current.Dispose();
            }

            var page = CreateDarkTab("Digimon_List.xml");
            page.Name = full;
            var loading =
                new EditorLoadingView(
                    "Loading Digimon Database",
                    "Indexing Digimon_List.xml, filters, skill references and cached previews.");
            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            try
            {
                DigimonListEditorService service =
                    await EditorPreloadService.GetDigimonListAsync(full);

                var rows =
                    service.Document.Root!
                        .Elements("Digimon")
                        .Select(ParseDigimonRow)
                        .OrderBy(x => x.Id)
                        .ToList();

                if (!page.IsDisposed)
                    BuildDigimonBrowser(
                        page,
                        full,
                        service.Document,
                        rows);
            }
            catch (Exception ex)
            {
                loading.SetError(
                    "Digimon_List.xml could not be loaded",
                    ex.Message);
            }
        }

        private static DigimonRow ParseDigimonRow(XElement d)
        {
            XElement s = d.Element("Stats") ?? new XElement("Stats");
            return new DigimonRow
            {
                Node = d,
                Id = UInt(d.Attribute("ID")?.Value),
                ModelId = UInt(d.Element("ModelID")?.Value),
                Name = d.Attribute("Name")?.Value ?? "",
                Form = d.Element("Form")?.Value ?? "",
                EvolutionType = Int(d.Element("EvolutionType")?.Value),
                AttributeType = Int(d.Element("AttributeType")?.Value),
                Rank = Int(d.Element("DigimonRank")?.Value),
                Hp = Int(s.Attribute("HP")?.Value),
                At = Int(s.Attribute("AttPower")?.Value),
                De = Int(s.Attribute("DefPower")?.Value),
                SkillCount = d.Element("Skills")?.Elements("Skill").Count(x => UInt(x.Attribute("ID")?.Value) != 0) ?? 0
            };
        }

        private void BuildDigimonBrowser(TabPage page, string filePath, XDocument doc, List<DigimonRow> rows)
        {
            EditorLoadingView? loading =
                page.Controls.OfType<EditorLoadingView>().FirstOrDefault();

            page.SuspendLayout();

            foreach (Control control in page.Controls.Cast<Control>().ToArray())
            {
                if (!ReferenceEquals(control, loading))
                {
                    page.Controls.Remove(control);
                    control.Dispose();
                }
            }

            var host =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    Padding = new Padding(12,10,18,12)
                };

            // A fixed two-row layout prevents the expandable FILTERS header
            // from ever drawing over the first Digimon card.
            var browserLayout =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    ColumnCount = 1,
                    RowCount = 2,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };

            browserLayout.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Percent,
                    100F));

            browserLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Absolute,
                    156F));

            browserLayout.RowStyles.Add(
                new RowStyle(
                    SizeType.Percent,
                    100F));

            var header =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    Height = 156,
                    Margin = Padding.Empty,
                    BackColor = Color.FromArgb(24,24,24)
                };

            var title = new Label
            {
                Text = "Digimon Database",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold",14F,FontStyle.Bold),
                Location = new Point(14,10),
                Size = new Size(330,28)
            };

            var sub = new Label
            {
                Text = $"Digimon_List.xml  •  {rows.Count:N0} Digimon  •  SkillSlots={doc.Root?.Attribute("SkillSlots")?.Value ?? "5"}",
                ForeColor = CMuted,
                Location = new Point(16,40),
                Size = new Size(560,22)
            };

            var create = CreateEditorActionButton("NEW DIGIMON");
            create.Size = new Size(148,34);
            create.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var importer = CreateEditorActionButton("IMPORTER");
            importer.Size = new Size(116,34);
            importer.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var filterButton = CreateEditorActionButton("FILTERS");
            filterButton.Size = new Size(106,30);
            filterButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var search = DarkText("Search Digimon ID, Model ID, Name, Form or Skill ID...");
            search.Location = new Point(14,72);
            search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            var filterPanel = new Panel
            {
                Height = 48,
                Visible = false,
                BackColor = Color.FromArgb(29,29,29),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var evo = FilterCombo("All Evolution Types");
            var attr = FilterCombo("All Attributes");
            var rank = FilterCombo("All Ranks");
            var reset = CreateEditorActionButton("RESET");
            reset.Size = new Size(82,28);

            filterPanel.Controls.Add(evo);
            filterPanel.Controls.Add(attr);
            filterPanel.Controls.Add(rank);
            filterPanel.Controls.Add(reset);

            var count = new Label
            {
                ForeColor = CMuted,
                Location = new Point(16,116),
                Size = new Size(500,24)
            };

            var prev = CreateEditorActionButton("◀ PREVIOUS");
            prev.Size = new Size(105,30);
            prev.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var next = CreateEditorActionButton("NEXT ▶");
            next.Size = new Size(105,30);
            next.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var pg = new Label
            {
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold",8F,FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(90,30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var resultsViewport =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    BackColor = Color.FromArgb(18,18,18),
                    Padding = new Padding(10,8,16,12)
                };

            var results = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(18,18,18),
                Padding = new Padding(8,8,8,34)
            };

            DarkUi.ApplyDarkScrollBar(results);
            resultsViewport.Controls.Add(results);

            var timer = new System.Windows.Forms.Timer { Interval = 180 };
            var state = new DigimonBrowseState
            {
                Path=filePath,Document=doc,Rows=rows,Search=search,
                Evolution=evo,Attribute=attr,Rank=rank,Count=count,Page=pg,
                Results=results,Prev=prev,Next=next,Timer=timer
            };

            AddFilters(state);

            void Position()
            {
                int width = header.ClientSize.Width;
                create.Location = new Point(Math.Max(400,width-create.Width-14),12);
                importer.Location =
                    new Point(
                        Math.Max(
                            270,
                            create.Left - importer.Width - 8),
                        12);
                filterButton.Location = new Point(Math.Max(400,width-filterButton.Width-14),72);
                search.Width = Math.Max(220,filterButton.Left-search.Left-10);

                next.Location = new Point(width-next.Width-14,116);
                pg.Location = new Point(next.Left-pg.Width-8,116);
                prev.Location = new Point(pg.Left-prev.Width-8,116);

                count.Width =
                    Math.Max(
                        140,
                        prev.Left - count.Left - 14);

                filterPanel.Location = new Point(14,108);
                filterPanel.Width = Math.Max(420,width-28);

                int available = filterPanel.ClientSize.Width-20;
                evo.Location = new Point(8,9);
                evo.Width = Math.Max(160,(available-110)/3);
                attr.Location = new Point(evo.Right+8,9);
                attr.Width = evo.Width;
                rank.Location = new Point(attr.Right+8,9);
                rank.Width = evo.Width;
                reset.Location = new Point(filterPanel.ClientSize.Width-reset.Width-8,9);
                rank.Width = Math.Max(120,reset.Left-rank.Left-8);
            }

            void SetFilterPanel()
            {
                int headerHeight;

                if (filterPanel.Visible)
                {
                    headerHeight = 205;
                    count.Top = 166;
                    prev.Top = 162;
                    pg.Top = 162;
                    next.Top = 162;
                }
                else
                {
                    headerHeight = 156;
                    count.Top = 116;
                    prev.Top = 116;
                    pg.Top = 116;
                    next.Top = 116;
                }

                header.Height = headerHeight;

                browserLayout.RowStyles[0].SizeType =
                    SizeType.Absolute;

                browserLayout.RowStyles[0].Height =
                    headerHeight;

                Position();

                // Force a complete layout pass before the user can interact
                // with the card list. This removes the one-frame overlap that
                // could cover EDIT on the first visible card.
                browserLayout.PerformLayout();
                host.PerformLayout();
                resultsViewport.PerformLayout();
                results.PerformLayout();

                header.Invalidate();
                resultsViewport.Invalidate();
            }

            header.Resize += (_,_)=>Position();

            filterButton.Click += (_,_) =>
            {
                filterPanel.Visible =
                    !filterPanel.Visible;

                filterButton.Text =
                    filterPanel.Visible
                        ? "HIDE FILTERS"
                        : "FILTERS";

                if (filterPanel.Visible)
                    filterPanel.BringToFront();

                SetFilterPanel();

                // Keep keyboard/mouse focus away from any card that was under
                // the old header rectangle before the layout expansion.
                filterButton.Focus();
            };

            reset.Click += (_,_) =>
            {
                evo.SelectedIndex=0;
                attr.SelectedIndex=0;
                rank.SelectedIndex=0;
                state.PageIndex=0;
                RefreshDigimonBrowser(state);
            };

            timer.Tick += (_,_)=>{timer.Stop();state.PageIndex=0;RefreshDigimonBrowser(state);};
            search.TextChanged += (_,_)=>{timer.Stop();timer.Start();};
            evo.SelectedIndexChanged += (_,_)=>{state.PageIndex=0;RefreshDigimonBrowser(state);};
            attr.SelectedIndexChanged += (_,_)=>{state.PageIndex=0;RefreshDigimonBrowser(state);};
            rank.SelectedIndexChanged += (_,_)=>{state.PageIndex=0;RefreshDigimonBrowser(state);};

            prev.Click += (_,_)=>{if(state.PageIndex>0){state.PageIndex--;RenderDigimonPage(state);}};
            next.Click += (_,_)=>
            {
                int pages=Math.Max(1,(int)Math.Ceiling(state.Filtered.Count/(double)DigimonPageSize));
                if(state.PageIndex<pages-1){state.PageIndex++;RenderDigimonPage(state);}
            };

            create.Click += (_,_)=>OpenDigimonEdit(state,null);

            importer.Click +=
                async (_, _) =>
                    await OpenDigimonCoreDatabaseImportTabAndRunAsync();

            editorToolTip.SetToolTip(
                importer,
                "Valida e importa os três ficheiros pela ordem: Digimon_List.xml -> DigimonEvo.xml -> Skill.xml.");

            foreach(Control c in new Control[]{title,sub,importer,create,search,filterButton,filterPanel,count,prev,pg,next})
                header.Controls.Add(c);

            browserLayout.Controls.Add(
                header,
                0,
                0);

            browserLayout.Controls.Add(
                resultsViewport,
                0,
                1);

            host.Controls.Add(
                browserLayout);

            page.Controls.Add(host);
            page.Tag=state;

            Position();
            RefreshDigimonBrowser(state);
            results.Resize += (_,_)=>ResizeDigimonRowCards(results);

            if (loading != null && !loading.IsDisposed)
            {
                loading.BringToFront();
                page.ResumeLayout(true);
                loading.Refresh();

                page.Controls.Remove(loading);
                loading.Dispose();
                page.PerformLayout();
                page.Update();
            }
            else
            {
                page.ResumeLayout(true);
            }
        }

        private static TextBox DarkText(string placeholder) => new()
        {
            BackColor=Color.FromArgb(10,10,10), ForeColor=Color.FromArgb(235,235,235), BorderStyle=BorderStyle.FixedSingle,
            Font=new Font("Segoe UI",9F), PlaceholderText=placeholder, Height=28
        };

        private static ComboBox FilterCombo(string first)
        {
            var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(22,22,22),ForeColor=Color.White,Font=new Font("Segoe UI",8.5F),Height=28};
            c.Items.Add(new DigimonOption(int.MinValue,first)); c.SelectedIndex=0; return c;
        }

        private static void AddFilters(DigimonBrowseState s)
        {
            foreach(int v in s.Rows.Select(x=>x.EvolutionType).Distinct().OrderBy(x=>x))
                s.Evolution.Items.Add(new DigimonOption(v,EvoLabel(v)));

            foreach(int v in s.Rows.Select(x=>x.AttributeType).Distinct().OrderBy(x=>x))
                s.Attribute.Items.Add(new DigimonOption(v,AttributeLabel(v)));

            foreach(int v in s.Rows.Select(x=>x.Rank).Distinct().OrderBy(x=>x))
                s.Rank.Items.Add(new DigimonOption(v,RankLabel(v)));
        }

        private void RefreshDigimonBrowser(DigimonBrowseState s)
        {
            string q=s.Search.Text.Trim(); int? ev=FilterValue(s.Evolution), at=FilterValue(s.Attribute), rk=FilterValue(s.Rank);
            IEnumerable<DigimonRow> set=s.Rows;
            if(ev.HasValue)set=set.Where(x=>x.EvolutionType==ev.Value);
            if(at.HasValue)set=set.Where(x=>x.AttributeType==at.Value);
            if(rk.HasValue)set=set.Where(x=>x.Rank==rk.Value);
            if(q.Length>0)set=set.Where(x=>x.Id.ToString().Contains(q,StringComparison.OrdinalIgnoreCase)||x.ModelId.ToString().Contains(q,StringComparison.OrdinalIgnoreCase)||x.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Form.Contains(q,StringComparison.OrdinalIgnoreCase)||(x.Node.Element("Skills")?.Elements("Skill").Any(z=>(z.Attribute("ID")?.Value??"").Contains(q,StringComparison.OrdinalIgnoreCase))??false));
            s.Filtered=set.OrderBy(x=>x.Id).ToList(); int pages=Math.Max(1,(int)Math.Ceiling(s.Filtered.Count/(double)DigimonPageSize)); if(s.PageIndex>=pages)s.PageIndex=pages-1; RenderDigimonPage(s);
        }

        private void RenderDigimonPage(DigimonBrowseState s)
        {
            s.Results.SuspendLayout();
            s.Results.Controls.Clear();

            foreach(var row in s.Filtered.Skip(s.PageIndex*DigimonPageSize).Take(DigimonPageSize))
                s.Results.Controls.Add(DigimonCard(s,row));

            ResizeDigimonRowCards(s.Results);
            s.Results.ResumeLayout();

            int pages=Math.Max(1,(int)Math.Ceiling(s.Filtered.Count/(double)DigimonPageSize));
            s.Count.Text=$"Total: {s.Rows.Count:N0}   •   Results: {s.Filtered.Count:N0}";
            s.Page.Text=$"{s.PageIndex+1} / {pages}";
            s.Prev.Enabled=s.PageIndex>0;
            s.Next.Enabled=s.PageIndex<pages-1;
        }

        private static void ResizeDigimonRowCards(FlowLayoutPanel flow)
        {
            int width=Math.Max(
                520,
                flow.ClientSize.Width-flow.Padding.Horizontal-
                (flow.VerticalScroll.Visible?SystemInformation.VerticalScrollBarWidth+10:10));

            foreach(Control control in flow.Controls)
                control.Width=width;
        }

        private Panel DigimonCard(DigimonBrowseState s, DigimonRow r)
        {
            var p=new Panel
            {
                Width=780,
                Height=104,
                BackColor=Color.FromArgb(28,28,28),
                Margin=new Padding(4,4,4,5),
                BorderStyle=BorderStyle.FixedSingle
            };

            var pic=new PictureBox
            {
                Location=new Point(14,16),
                Size=new Size(68,68),
                BackColor=Color.FromArgb(8,8,8),
                SizeMode=PictureBoxSizeMode.Zoom,
                Image=DigimonIcon(r.Id,r.ModelId)
            };

            var name=new Label
            {
                Text=r.Name,
                ForeColor=Color.White,
                Font=new Font("Segoe UI Semibold",10.5F,FontStyle.Bold),
                Location=new Point(96,13),
                Size=new Size(230,24),
                AutoEllipsis=true
            };

            var ids=new Label
            {
                Text=$"ID {r.Id}  •  Model {r.ModelId}",
                ForeColor=CMuted,
                Location=new Point(96,39),
                Size=new Size(230,20)
            };

            var cl=new Label
            {
                Text=$"{EvoLabel(r.EvolutionType)}  •  {AttributeLabel(r.AttributeType)}  •  {RankLabel(r.Rank)}",
                ForeColor=Color.FromArgb(126,220,150),
                Font=new Font("Segoe UI Semibold",8F,FontStyle.Bold),
                Location=new Point(96,63),
                Size=new Size(420,20),
                AutoEllipsis=true
            };

            var details=new Label
            {
                Text=$"{(string.IsNullOrWhiteSpace(r.Form)?"—":r.Form)}   |   HP {r.Hp:N0}   AT {r.At:N0}   DE {r.De:N0}   Skills {r.SkillCount}/5",
                ForeColor=CMuted,
                Font=new Font("Consolas",7.4F),
                TextAlign=ContentAlignment.MiddleRight,
                Anchor=AnchorStyles.Top|AnchorStyles.Right,
                Size=new Size(315,22)
            };

            var edit=CreateEditorActionButton("EDIT");
            edit.Size=new Size(92,31);
            edit.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            edit.BackColor=Color.FromArgb(24,24,24);
            edit.FlatAppearance.MouseOverBackColor=Color.FromArgb(42,42,42);
            edit.FlatAppearance.MouseDownBackColor=Color.FromArgb(55,55,55);
            edit.UseVisualStyleBackColor=false;

            var del=CreateEditorActionButton("REMOVE");
            del.Size=new Size(92,31);
            del.ForeColor=Color.FromArgb(255,130,130);
            del.Anchor=AnchorStyles.Top|AnchorStyles.Right;
            del.BackColor=Color.FromArgb(24,24,24);
            del.FlatAppearance.MouseOverBackColor=Color.FromArgb(48,35,35);
            del.FlatAppearance.MouseDownBackColor=Color.FromArgb(62,40,40);
            del.UseVisualStyleBackColor=false;

            void PositionRight()
            {
                del.Location =
                    new Point(
                        p.ClientSize.Width -
                        del.Width -
                        12,
                        54);

                edit.Location =
                    new Point(
                        del.Left -
                        edit.Width -
                        8,
                        54);

                // IMPORTANT:
                // The details label must NEVER extend underneath EDIT.
                // Previously it had a fixed width (315px) combined with
                // Math.Max(340, ...), so on narrower cards it physically
                // overlapped the left side of the EDIT button.
                const int detailsLeft = 340;
                const int gapBeforeButtons = 14;

                int availableDetailsWidth =
                    edit.Left -
                    detailsLeft -
                    gapBeforeButtons;

                details.Location =
                    new Point(
                        detailsLeft,
                        17);

                details.Width =
                    Math.Max(
                        80,
                        availableDetailsWidth);

                // If the window becomes extremely narrow, hide the secondary
                // details instead of allowing them to paint over the buttons.
                details.Visible =
                    availableDetailsWidth >= 80;
            }

            p.Resize +=
                (_,_) =>
                {
                    PositionRight();
                    edit.BringToFront();
                    del.BringToFront();
                };

            p.MouseEnter +=
                (_,_) =>
                    p.BackColor =
                        Color.FromArgb(32,32,32);

            p.MouseLeave +=
                (_,_) =>
                    p.BackColor =
                        Color.FromArgb(28,28,28);

            edit.Click += (_,_)=>OpenDigimonEdit(s,r);
            del.Click += (_,_)=>DeleteDigimon(s,r);

            foreach(Control c in new Control[]{pic,name,ids,cl,details,edit,del})
                p.Controls.Add(c);

            // Buttons must always be the topmost controls in the card.
            edit.BringToFront();
            del.BringToFront();

            PositionRight();
            return p;
        }

        private void DeleteDigimon(DigimonBrowseState s, DigimonRow r)
        {
            if(MessageBox.Show($"Remove {r.Id} — {r.Name}?\r\n\r\nThis edits Digimon_List.xml.","Remove Digimon",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
            r.Node.Remove(); SaveDigimonDoc(s.Path,s.Document); s.Rows.Remove(r); RefreshDigimonBrowser(s);
        }

        private async void OpenDigimonEdit(DigimonBrowseState browse, DigimonRow? row)
        {
            XElement w = row == null ? NewDigimon(browse.Document) : new XElement(row.Node);
            string key = row == null
                ? $"digimon:new:{Guid.NewGuid():N}"
                : $"digimon:{browse.Path}:{row.Id}";

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(x => x.Name == key);

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page =
                CreateDarkTab(
                    row == null
                        ? "New Digimon [Unsaved]"
                        : $"{row.Name} [Edit]");

            page.Name = key;

            var opening=new EditorLoadingView(
                "Loading Digimon Editor",
                "Preparing model references, fields, stats, skill slots and icon previews.");
            page.Controls.Add(opening);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab=page;
            await Task.Yield();
            if(page.IsDisposed)return;
            page.SuspendLayout();

            // ONE scrolling container only.
            var scroll =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = CEditor,
                    Padding = new Padding(14, 10, 30, 48)
                };

            DarkUi.ApplyDarkScrollBar(scroll);

            var hero =
                new Panel
                {
                    Height = 126,
                    BackColor = Color.FromArgb(25, 25, 25),
                    Margin = new Padding(0, 0, 0, 10),
                    Tag = "DigimonFullWidth"
                };

            var icon =
                new PictureBox
                {
                    Location = new Point(16, 14),
                    Size = new Size(92, 92),
                    BackColor = Color.FromArgb(8, 8, 8),
                    SizeMode = PictureBoxSizeMode.Zoom
                };

            var heroName =
                new Label
                {
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                    Location = new Point(126, 18),
                    Size = new Size(360, 32),
                    AutoEllipsis = true
                };

            var heroMeta =
                new Label
                {
                    ForeColor = Color.FromArgb(126, 220, 150),
                    Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                    Location = new Point(128, 56),
                    Size = new Size(420, 50)
                };

            var save = CreateEditorActionButton("SAVE");
            save.Size = new Size(138, 34);

            var view = CreateEditorActionButton("VIEW XML BLOCK");
            view.Size = new Size(138, 34);

            foreach (Control c in new Control[] { icon, heroName, heroMeta, save, view })
                hero.Controls.Add(c);

            var fields =
                new Dictionary<string, Control>(
                    StringComparer.OrdinalIgnoreCase);

            TextBox id = Field(Attr(w, "ID"));
            TextBox name = Field(Attr(w, "Name"));
            TextBox model = Field(Txt(w, "ModelID"));
            model.ReadOnly = true;
            model.TabStop = false;
            TextBox sound = Field(Txt(w, "SoundDir"));
            TextBox scale = Field(Txt(w, "SelectScale"));
            TextBox effect = Field(Txt(w, "EvoEffectDir"));
            TextBox baseLevel = Field(Txt(w, "BaseLevel"));
            TextBox charSize = Field(Txt(w, "CharSize"));
            TextBox form = Field(Txt(w, "Form"));
            TextBox walk = Field(Txt(w, "WalkLen"));
            TextBox run = Field(Txt(w, "RunLen"));
            TextBox arun = Field(Txt(w, "ARunLen"));

            var idStatus =
                new Label
                {
                    ForeColor = Color.LightGreen,
                    Font = new Font("Segoe UI Semibold", 7.4F, FontStyle.Bold),
                    Height = 20,
                    AutoEllipsis = true
                };

            ComboBox evo = ValueCombo(EvolutionOptions(), Int(Txt(w, "EvolutionType")));
            ComboBox attr = ValueCombo(AttributeOptions(), Int(Txt(w, "AttributeType")));
            ComboBox nature = ValueCombo(NatureOptions(), Int(Txt(w, "BaseNatureType")));
            ComboBox dtype = ValueCombo(NumberOptions(0, 4, "Digimon Type"), Int(Txt(w, "DigimonType")));
            ComboBox rank = ValueCombo(RankOptions(), Int(Txt(w, "DigimonRank")));

            int[] fam = Triple(Txt(w, "FamilyTypes"));
            int[] nat = Triple(Txt(w, "BaseNatureTypes"));

            ComboBox[] families =
                fam.Select(x => ValueCombo(NumberOptions(0, 31, "Family"), x)).ToArray();

            ComboBox[] natures =
                nat.Select(x => ValueCombo(NatureOptions(), x)).ToArray();

            fields["ID"] = id;
            fields["Name"] = name;
            fields["ModelID"] = model;
            fields["SoundDir"] = sound;
            fields["SelectScale"] = scale;
            fields["EvoEffectDir"] = effect;
            fields["EvolutionType"] = evo;
            fields["AttributeType"] = attr;
            fields["BaseNatureType"] = nature;
            fields["DigimonType"] = dtype;
            fields["DigimonRank"] = rank;
            fields["BaseLevel"] = baseLevel;
            fields["CharSize"] = charSize;
            fields["Form"] = form;
            fields["WalkLen"] = walk;
            fields["RunLen"] = run;
            fields["ARunLen"] = arun;

            for (int i = 0; i < 3; i++)
            {
                fields[$"Family{i}"] = families[i];
                fields[$"Nature{i}"] = natures[i];
            }

            var identity = Section(
                "IDENTITY / MODEL",
                "Core identity, model, sound and evolution references.");

            identity.Controls.Add(Card("Digimon ID", "Must be unique.", id, idStatus));
            identity.Controls.Add(Card("Name", "Digimon display name.", name));
            var selectModel =
                CreateEditorActionButton("SELECT MODEL");

            selectModel.Size = new Size(116,30);

            var modelInfo =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI",7F),
                    AutoEllipsis = true
                };

            identity.Controls.Add(
                ModelSelectionCard(
                    model,
                    modelInfo,
                    selectModel));
            identity.Controls.Add(Card("Sound Directory", "Sound folder/reference.", sound));
            identity.Controls.Add(Card("Selection Scale", "Decimal display/selection scale.", scale));
            identity.Controls.Add(Card(
                "Evolution Effect Resource",
                "NIF/resource path used on evolution.",
                effect,
                null,
                true));

            var classification = Section(
                "CLASSIFICATION",
                "Evolution, attribute, family, nature, rank and base level.");

            classification.Controls.Add(Card("Evolution Type", "Evolution stage/type.", evo));
            classification.Controls.Add(Card("Attribute", "Data / Vaccine / Virus / Unknown.", attr));
            classification.Controls.Add(Card("Digimon Type", "Internal classification code.", dtype));
            classification.Controls.Add(Card("Digimon Rank", "B → U+ rank mapping.", rank));
            classification.Controls.Add(TripleCard(
                "Family Types",
                "Three family IDs stored as A,B,C.",
                families));
            classification.Controls.Add(Card("Base Nature", "Primary nature/element.", nature));
            classification.Controls.Add(TripleCard(
                "Base Nature Types",
                "Three nature/element slots.",
                natures));
            classification.Controls.Add(Card("Base Level", "Base level from Digimon_List.", baseLevel));

            XElement statsXml = w.Element("Stats") ?? new XElement("Stats");

            string[] statKeys =
            {
                "HP", "DS", "DefPower", "Evasion", "MoveSpeed",
                "CriticalRate", "AttPower", "AttSpeed", "AttRange", "HitRate"
            };

            var statSection = Section(
                "BASE STATS",
                "Raw UInt16 values stored by Digimon_List.");

            foreach (string stat in statKeys)
            {
                var box = Field(statsXml.Attribute(stat)?.Value ?? "0");
                fields["STAT:" + stat] = box;
                statSection.Controls.Add(StatCard(StatLabel(stat), stat, box));
            }

            var skills =
                w.Element("Skills")?
                    .Elements("Skill")
                    .ToDictionary(
                        x => Int(x.Attribute("Slot")?.Value),
                        x => x)
                ?? new Dictionary<int, XElement>();

            var skillIds = new TextBox[5];
            var skillReqValues = new string[5];
            var skillNames = new Label[5];
            var skillIcons = new PictureBox[5];
            var skillIdLabels = new Label[5];
            var skillComments = new Label[5];
            var skillSelectButtons = new Button[5];
            var skillClearButtons = new Button[5];

            var skillSection = Section(
                "SKILLS",
                "Select each slot directly from Skill.xml. Search by Skill ID or Skill Name.");

            for (int i = 0; i < 5; i++)
            {
                skills.TryGetValue(i, out XElement? skillXml);

                // Kept internally for XML serialization. The user no longer
                // types this value directly.
                skillIds[i] =
                    Field(
                        skillXml?.Attribute("ID")?.Value ??
                        "0");

                skillIds[i].Visible = false;

                skillReqValues[i] =
                    skillXml?.Attribute("ReqPrevSkillLevel")?.Value ??
                    (i % 2 == 1 ? "3" : "0");

                skillNames[i] =
                    new Label
                    {
                        Text = "Loading skill...",
                        ForeColor = CText,
                        Font = new Font(
                            "Segoe UI Semibold",
                            9.5F,
                            FontStyle.Bold),
                        AutoEllipsis = true
                    };

                skillIcons[i] =
                    new PictureBox
                    {
                        Size = new Size(58,58),
                        BackColor = Color.FromArgb(8,8,8),
                        SizeMode = PictureBoxSizeMode.Zoom
                    };

                skillIdLabels[i] =
                    new Label
                    {
                        Text = "Skill ID: 0",
                        ForeColor = CMuted,
                        Font = new Font("Consolas",7.5F),
                        AutoEllipsis = true
                    };

                skillComments[i] =
                    new Label
                    {
                        Text = "Empty skill slot",
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI",7.4F),
                        AutoEllipsis = true
                    };

                skillSelectButtons[i] =
                    CreateEditorActionButton("SELECT SKILL");

                skillSelectButtons[i].Size =
                    new Size(112,30);

                skillClearButtons[i] =
                    CreateEditorActionButton("CLEAR");

                skillClearButtons[i].Size =
                    new Size(74,30);

                skillClearButtons[i].ForeColor =
                    Color.FromArgb(255,150,150);

                skillSection.Controls.Add(
                    SkillSelectionCard(
                        i,
                        skillIds[i],
                        skillIcons[i],
                        skillNames[i],
                        skillIdLabels[i],
                        skillComments[i],
                        skillSelectButtons[i],
                        skillClearButtons[i]));
            }

            var movement = Section(
                "SIZE / MOVEMENT / FORM",
                "Character size, movement values and species/form text.");

            movement.Controls.Add(Card("Character Size", "Model size value.", charSize));
            movement.Controls.Add(Card("Walk Length", "Walk movement/animation length.", walk));
            movement.Controls.Add(Card("Run Length", "Run movement/animation length.", run));
            movement.Controls.Add(Card("Alternative Run Length", "ARunLen value.", arun));
            movement.Controls.Add(Card("Form", "Species/form description.", form, null, true));

            foreach (Control c in new Control[]
                     {
                         hero,
                         identity,
                         classification,
                         statSection,
                         skillSection,
                         movement
                     })
            {
                scroll.Controls.Add(c);
            }

            page.Controls.Add(scroll);

            var state =
                new DigimonListEditState
                {
                    Path = browse.Path,
                    Document = browse.Document,
                    Working = w,
                    Original = row?.Node,
                    IsNew = row == null,
                    Fields = fields,
                    SkillIds = skillIds,
                    SkillReqValues = skillReqValues,
                    SkillNames = skillNames,
                    SkillIcons = skillIcons,
                    SkillIdLabels = skillIdLabels,
                    SkillComments = skillComments,
                    SkillSelectButtons = skillSelectButtons,
                    SkillClearButtons = skillClearButtons,
                    Icon = icon,
                    HeroName = heroName,
                    HeroMeta = heroMeta,
                    IdStatus = idStatus
                };

            page.Tag = state;

            void ResizeEditor()
            {
                int usable =
                    Math.Max(
                        480,
                        scroll.ClientSize.Width -
                        scroll.Padding.Horizontal -
                        SystemInformation.VerticalScrollBarWidth -
                        24);

                foreach (Control child in scroll.Controls)
                    child.Width = usable;

                save.Location =
                    new Point(
                        hero.ClientSize.Width - save.Width - 16,
                        18);

                view.Location =
                    new Point(
                        hero.ClientSize.Width - view.Width - 16,
                        60);

                heroName.Width =
                    Math.Max(
                        150,
                        save.Left - heroName.Left - 16);

                heroMeta.Width =
                    Math.Max(
                        150,
                        save.Left - heroMeta.Left - 16);

                // WinForms sometimes remembers a previous horizontal extent.
                // Reset it after all child widths have been constrained.
                scroll.HorizontalScroll.Value = 0;
                scroll.HorizontalScroll.Enabled = false;
                scroll.HorizontalScroll.Visible = false;
                scroll.HorizontalScroll.Maximum = 0;
            }

            scroll.Resize +=
                (_, _) =>
                    ResizeEditor();

            void Dirty()
            {
                state.Dirty = true;

                if (!page.Text.EndsWith(" *"))
                    page.Text += " *";

                UpdateHero(state);
            }

            foreach (Control c in fields.Values)
            {
                if (c is TextBox tb)
                    tb.TextChanged += (_, _) => Dirty();
                else if (c is ComboBox cb)
                    cb.SelectedIndexChanged += (_, _) => Dirty();
            }

            foreach (TextBox tb in skillIds)
                tb.TextChanged += (_, _) => Dirty();

            id.TextChanged += (_, _) => ValidateDigimonId(state);
            model.TextChanged +=
                async (_, _) =>
                {
                    UpdateIcon(state);
                    await RefreshSelectedModelInfoAsync(model, modelInfo);
                };

            selectModel.Click +=
                async (_, _) =>
                    await OpenDigimonModelPickerAsync(page, model);

            name.TextChanged += (_, _) => UpdateHero(state);

            for (int i = 0; i < 5; i++)
            {
                int slot = i;

                skillIds[i].TextChanged +=
                    async (_, _) =>
                        await RefreshSkillSlotAsync(
                            state,
                            slot);

                skillSelectButtons[i].Click +=
                    async (_, _) =>
                        await OpenDigimonSkillPickerAsync(
                            page,
                            state,
                            slot);

                skillClearButtons[i].Click +=
                    (_, _) =>
                    {
                        if (skillIds[slot].Text == "0")
                            return;

                        skillIds[slot].Text = "0";
                    };

                _ =
                    RefreshSkillSlotAsync(
                        state,
                        slot);
            }

            save.Click +=
                (_, _) =>
                    SaveDigimonListEditPage(
                        page,
                        state,
                        true);

            view.Click +=
                (_, _) =>
                {
                    try
                    {
                        PullDigimon(state);
                        OpenRawBlockTab(
                            state.Path,
                            new XElement(state.Working));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "Digimon XML Preview");
                    }
                };

            ValidateDigimonId(state);
            UpdateHero(state);
            UpdateIcon(state);
            _ = RefreshSelectedModelInfoAsync(model, modelInfo);
            ResizeEditor();

            state.Dirty = false;

            opening.BringToFront();
            page.ResumeLayout(true);
            opening.Refresh();

            page.Controls.Remove(opening);
            opening.Dispose();
            page.PerformLayout();
            page.Update();
        }

        private bool SaveDigimonListEditPage(TabPage page, DigimonListEditState s, bool showSuccess)
        {
            try
            {
                PullDigimon(s); uint id=UInt(s.Fields["ID"].Text); if(id==0)throw new InvalidDataException("Digimon ID must be greater than 0.");
                XElement root=s.Document.Root??throw new InvalidDataException("DigimonList root missing.");
                if(root.Elements("Digimon").Any(x=>!ReferenceEquals(x,s.Original)&&UInt(x.Attribute("ID")?.Value)==id))throw new InvalidDataException($"Digimon ID {id} already exists.");
                XElement saved=new XElement(s.Working); if(s.Original!=null)s.Original.ReplaceWith(saved);else root.Add(saved); s.Original=saved;
                SaveDigimonDoc(s.Path,s.Document);
                EditorPreloadService.ReplaceDigimonListDocument(s.Path,s.Document);
                s.Dirty=false;s.IsNew=false;page.Name=$"digimon:{s.Path}:{id}";page.Text=$"{Attr(saved,"Name")} [Saved]";RefreshDigimonBrowsers(s.Path);if(showSuccess)MessageBox.Show($"{id} — {Attr(saved,"Name")} saved.","Digimon_List Editor",MessageBoxButtons.OK,MessageBoxIcon.Information);return true;
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Digimon Save Error",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}
        }

        private void PullDigimon(DigimonListEditState s)
        {
            uint id=ParseUInt(s.Fields["ID"].Text,"Digimon ID"), model=ParseUInt(s.Fields["ModelID"].Text,"Model ID");string name=s.Fields["Name"].Text.Trim();if(name.Length==0)throw new InvalidDataException("Name cannot be empty.");
            s.Working.SetAttributeValue("ID",id);s.Working.SetAttributeValue("Name",name);Set(s.Working,"ModelID",model.ToString());Set(s.Working,"SoundDir",s.Fields["SoundDir"].Text);Set(s.Working,"SelectScale",ParseFloat(s.Fields["SelectScale"].Text,"Selection Scale"));Set(s.Working,"EvoEffectDir",s.Fields["EvoEffectDir"].Text);Set(s.Working,"EvolutionType",ComboValue((ComboBox)s.Fields["EvolutionType"]).ToString());Set(s.Working,"AttributeType",ComboValue((ComboBox)s.Fields["AttributeType"]).ToString());Set(s.Working,"FamilyTypes",string.Join(",",Enumerable.Range(0,3).Select(i=>ComboValue((ComboBox)s.Fields[$"Family{i}"]))));Set(s.Working,"BaseNatureType",ComboValue((ComboBox)s.Fields["BaseNatureType"]).ToString());Set(s.Working,"BaseNatureTypes",string.Join(",",Enumerable.Range(0,3).Select(i=>ComboValue((ComboBox)s.Fields[$"Nature{i}"]))));Set(s.Working,"BaseLevel",ParseUShort(s.Fields["BaseLevel"].Text,"Base Level").ToString());
            XElement stats=s.Working.Element("Stats")??new XElement("Stats");if(stats.Parent==null)(s.Working.Element("BaseLevel")??s.Working.Elements().Last()).AddAfterSelf(stats);foreach(string k in new[]{"HP","DS","DefPower","Evasion","MoveSpeed","CriticalRate","AttPower","AttSpeed","AttRange","HitRate"})stats.SetAttributeValue(k,ParseUShort(s.Fields["STAT:"+k].Text,k));
            Set(s.Working,"DigimonType",ComboValue((ComboBox)s.Fields["DigimonType"]).ToString());Set(s.Working,"CharSize",ParseUShort(s.Fields["CharSize"].Text,"Character Size").ToString());XElement skills=s.Working.Element("Skills")??new XElement("Skills");if(skills.Parent==null)(s.Working.Element("CharSize")??s.Working.Elements().Last()).AddAfterSelf(skills);skills.RemoveNodes();for(int i=0;i<5;i++)skills.Add(new XElement("Skill",new XAttribute("Slot",i),new XAttribute("ID",ParseUInt(s.SkillIds[i].Text,$"Skill {i+1} ID")),new XAttribute("ReqPrevSkillLevel",ParseInt(s.SkillReqValues[i],$"Skill {i+1} requirement"))));Set(s.Working,"WalkLen",ParseFloat(s.Fields["WalkLen"].Text,"Walk Length"));Set(s.Working,"RunLen",ParseFloat(s.Fields["RunLen"].Text,"Run Length"));Set(s.Working,"ARunLen",ParseFloat(s.Fields["ARunLen"].Text,"ARunLen"));Set(s.Working,"Form",s.Fields["Form"].Text);Set(s.Working,"DigimonRank",ComboValue((ComboBox)s.Fields["DigimonRank"]).ToString());
        }

        private void RefreshDigimonBrowsers(string path)
        {
            foreach(TabPage tab in editorTabs.TabPages)if(tab.Tag is DigimonBrowseState b&&System.IO.Path.GetFullPath(b.Path).Equals(System.IO.Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)){b.Rows.Clear();b.Rows.AddRange((b.Document.Root?.Elements("Digimon")??Enumerable.Empty<XElement>()).Select(ParseDigimonRow).OrderBy(x=>x.Id));RefreshDigimonBrowser(b);}
        }

        private async Task RefreshSkillSlotAsync(
            DigimonListEditState state,
            int slot)
        {
            uint id =
                UInt(
                    state.SkillIds[slot].Text);

            if (id == 0)
            {
                state.SkillIcons[slot].Image = null;
                state.SkillNames[slot].Text = "Empty Skill Slot";
                state.SkillNames[slot].ForeColor = CMuted;
                state.SkillIdLabels[slot].Text = "Skill ID: 0";
                state.SkillComments[slot].Text =
                    "Click SELECT SKILL to choose a Skill.xml entry.";
                state.SkillComments[slot].ForeColor = CMuted;
                state.SkillClearButtons[slot].Enabled = false;
                return;
            }

            state.SkillNames[slot].Text =
                "Loading Skill.xml...";

            state.SkillNames[slot].ForeColor =
                CMuted;

            SkillReferencePickerService? catalog =
                EditorPreloadService
                    .TryGetSkillReferences();

            if (catalog == null)
            {
                try
                {
                    catalog =
                        await EditorPreloadService
                            .GetSkillReferencesAsync();
                }
                catch
                {
                    catalog = null;
                }
            }

            if (state.SkillNames[slot].IsDisposed)
                return;

            if (catalog == null ||
                !catalog.TryGet(
                    id,
                    out SkillReferenceRecord record))
            {
                state.SkillIcons[slot].Image = null;
                state.SkillNames[slot].Text =
                    $"Unknown Skill {id}";
                state.SkillNames[slot].ForeColor =
                    Color.FromArgb(255,190,90);
                state.SkillIdLabels[slot].Text =
                    $"Skill ID: {id}";
                state.SkillComments[slot].Text =
                    "This ID does not exist in XML\\Skill\\Skill.xml.";
                state.SkillComments[slot].ForeColor =
                    Color.FromArgb(255,160,100);
                state.SkillClearButtons[slot].Enabled = true;
                return;
            }

            Bitmap? icon =
                await Task.Run(
                    () =>
                        catalog.TryLoadIcon(
                            record));

            if (state.SkillNames[slot].IsDisposed)
            {
                icon?.Dispose();
                return;
            }

            Image? previous =
                state.SkillIcons[slot].Image;

            state.SkillIcons[slot].Image = icon;

            if (previous != null &&
                !ReferenceEquals(
                    previous,
                    icon))
            {
                previous.Dispose();
            }

            state.SkillNames[slot].Text =
                record.DisplayName;

            state.SkillNames[slot].ForeColor =
                Color.FromArgb(125,220,140);

            state.SkillIdLabels[slot].Text =
                $"Skill ID: {record.Id}   •   Icon: {record.IconId}";

            state.SkillComments[slot].Text =
                string.IsNullOrWhiteSpace(
                    record.Comment)
                    ? "No description in Skill.xml."
                    : record.Comment;

            state.SkillComments[slot].ForeColor =
                CMuted;

            state.SkillClearButtons[slot].Enabled = true;
        }




        private async Task OpenDigimonSkillPickerAsync(
            TabPage ownerPage,
            DigimonListEditState state,
            int slot)
        {
            if (ownerPage.IsDisposed)
                return;

            string key =
                $"digimon-skill-picker:{ownerPage.Name}:{slot}";

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        x =>
                            x.Name.Equals(
                                key,
                                StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page =
                CreateDarkTab(
                    $"Select Skill — Slot {slot + 1}");

            page.Name = key;

            var loading =
                new EditorLoadingView(
                    $"Loading Skill Catalog — Slot {slot + 1}",
                    "Preparing Skill.xml search index and cached skill icons.");

            page.Controls.Add(loading);

            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            SkillReferencePickerService catalog;

            try
            {
                catalog =
                    await EditorPreloadService
                        .GetSkillReferencesAsync();
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "Skill.xml could not be loaded",
                        ex.Message);
                }

                return;
            }

            if (page.IsDisposed ||
                ownerPage.IsDisposed)
            {
                return;
            }

            page.SuspendLayout();

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 134,
                    BackColor = Color.FromArgb(24,24,24),
                    Padding = new Padding(14,10,18,8)
                };

            var title =
                new Label
                {
                    Text =
                        $"Select Skill for Slot {slot + 1}",
                    ForeColor = CText,
                    Font = new Font(
                        "Segoe UI Semibold",
                        13F,
                        FontStyle.Bold),
                    Location = new Point(14,10),
                    Size = new Size(360,28)
                };

            var subtitle =
                new Label
                {
                    Text =
                        $"Skill.xml  •  {catalog.Skills.Count:N0} records  •  Search by Skill ID or Skill Name",
                    ForeColor = CMuted,
                    Location = new Point(16,40),
                    Size = new Size(600,20),
                    AutoEllipsis = true
                };

            var search =
                new TextBox
                {
                    BackColor = Color.FromArgb(10,10,10),
                    ForeColor = Color.White,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI",9.2F),
                    PlaceholderText =
                        "Search Skill ID or Skill Name...",
                    Location = new Point(14,70),
                    Height = 28
                };

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Location = new Point(16,104),
                    Size = new Size(380,20)
                };

            var clear =
                CreateEditorActionButton(
                    "CLEAR SLOT");

            clear.Size =
                new Size(106,30);

            var close =
                CreateEditorActionButton(
                    "CLOSE");

            close.Size =
                new Size(90,30);

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor = Color.FromArgb(18,18,18),
                    Padding = new Padding(12,10,28,42)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            IReadOnlyList<SkillReferenceRecord> filtered =
                catalog.Skills;

            const int PageSize = 80;
            int pageIndex = 0;

            var footer =
                new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 46,
                    BackColor = Color.FromArgb(23,23,23)
                };

            var previous =
                CreateEditorActionButton(
                    "◀ PREVIOUS");

            previous.Size =
                new Size(110,30);

            var pageLabel =
                new Label
                {
                    ForeColor = CText,
                    TextAlign =
                        ContentAlignment.MiddleCenter,
                    Size = new Size(90,30)
                };

            var next =
                CreateEditorActionButton(
                    "NEXT ▶");

            next.Size =
                new Size(110,30);

            footer.Controls.Add(previous);
            footer.Controls.Add(pageLabel);
            footer.Controls.Add(next);

            void ClosePicker()
            {
                if (page.IsDisposed)
                    return;

                editorTabs.TabPages.Remove(page);
                page.Dispose();

                if (!ownerPage.IsDisposed)
                    editorTabs.SelectedTab = ownerPage;
            }

            void SelectRecord(
                SkillReferenceRecord record)
            {
                state.SkillIds[slot].Text =
                    record.Id.ToString();

                ClosePicker();
            }

            Panel CreateSkillResultCard(
                SkillReferenceRecord record)
            {
                var card =
                    new Panel
                    {
                        Height = 92,
                        BackColor = Color.FromArgb(29,29,29),
                        BorderStyle =
                            BorderStyle.FixedSingle,
                        Margin =
                            new Padding(3,3,3,5)
                    };

                var picture =
                    new PictureBox
                    {
                        Location = new Point(12,14),
                        Size = new Size(62,62),
                        BackColor = Color.FromArgb(8,8,8),
                        SizeMode =
                            PictureBoxSizeMode.Zoom
                    };

                var skillName =
                    new Label
                    {
                        Text = record.DisplayName,
                        ForeColor = Color.White,
                        Font = new Font(
                            "Segoe UI Semibold",
                            9.5F,
                            FontStyle.Bold),
                        Location = new Point(88,11),
                        Height = 22,
                        AutoEllipsis = true
                    };

                var skillId =
                    new Label
                    {
                        Text =
                            $"ID {record.Id}  •  Icon {record.IconId}  •  Max Lv {record.MaxLevel}",
                        ForeColor =
                            Color.FromArgb(125,220,140),
                        Font =
                            new Font(
                                "Consolas",
                                7.4F),
                        Location =
                            new Point(
                                88,
                                36),
                        Height = 19,
                        AutoEllipsis = true
                    };

                var skillComment =
                    new Label
                    {
                        Text =
                            string.IsNullOrWhiteSpace(
                                record.Comment)
                                ? "No description in Skill.xml."
                                : record.Comment,
                        ForeColor = CMuted,
                        Font =
                            new Font(
                                "Segoe UI",
                                7.3F),
                        Location =
                            new Point(
                                88,
                                59),
                        Height = 19,
                        AutoEllipsis = true
                    };

                var select =
                    CreateEditorActionButton(
                        "SELECT");

                select.Size =
                    new Size(
                        92,
                        32);

                select.Anchor =
                    AnchorStyles.Top |
                    AnchorStyles.Right;

                void Relayout()
                {
                    int right =
                        card.ClientSize.Width -
                        12;

                    select.Location =
                        new Point(
                            right -
                            select.Width,
                            29);

                    int labelWidth =
                        Math.Max(
                            150,
                            select.Left -
                            skillName.Left -
                            14);

                    skillName.Width =
                        labelWidth;

                    skillId.Width =
                        labelWidth;

                    skillComment.Width =
                        labelWidth;
                }

                card.Resize +=
                    (_, _) =>
                        Relayout();

                select.Click +=
                    (_, _) =>
                        SelectRecord(
                            record);

                card.DoubleClick +=
                    (_, _) =>
                        SelectRecord(
                            record);

                picture.DoubleClick +=
                    (_, _) =>
                        SelectRecord(
                            record);

                skillName.DoubleClick +=
                    (_, _) =>
                        SelectRecord(
                            record);

                card.Controls.Add(picture);
                card.Controls.Add(skillName);
                card.Controls.Add(skillId);
                card.Controls.Add(skillComment);
                card.Controls.Add(select);

                Relayout();

                // Interface atlases are already cached at startup. We still
                // request/crop only visible result cards to keep UI light.
                _ =
                    Task.Run(
                        () =>
                            catalog.TryLoadIcon(
                                record))
                    .ContinueWith(
                        task =>
                        {
                            if (task.IsFaulted ||
                                task.IsCanceled ||
                                task.Result == null ||
                                picture.IsDisposed)
                            {
                                task.Result?.Dispose();
                                return;
                            }

                            try
                            {
                                picture.BeginInvoke(
                                    new Action(
                                        () =>
                                        {
                                            if (picture.IsDisposed)
                                            {
                                                task.Result?.Dispose();
                                                return;
                                            }

                                            Image? old =
                                                picture.Image;

                                            picture.Image =
                                                task.Result;

                                            old?.Dispose();
                                        }));
                            }
                            catch
                            {
                                task.Result?.Dispose();
                            }
                        });

                return card;
            }

            void ResizeCards()
            {
                int width =
                    Math.Max(
                        460,
                        results.ClientSize.Width -
                        results.Padding.Horizontal -
                        SystemInformation.VerticalScrollBarWidth -
                        18);

                foreach (Control control
                         in results.Controls)
                {
                    control.Width = width;
                }

                int footerWidth =
                    footer.ClientSize.Width;

                next.Location =
                    new Point(
                        footerWidth -
                        next.Width -
                        16,
                        8);

                pageLabel.Location =
                    new Point(
                        next.Left -
                        pageLabel.Width -
                        8,
                        8);

                previous.Location =
                    new Point(
                        pageLabel.Left -
                        previous.Width -
                        8,
                        8);
            }

            void Render()
            {
                results.SuspendLayout();
                results.Controls.Clear();

                int pages =
                    Math.Max(
                        1,
                        (int)Math.Ceiling(
                            filtered.Count /
                            (double)PageSize));

                pageIndex =
                    Math.Clamp(
                        pageIndex,
                        0,
                        pages - 1);

                foreach (SkillReferenceRecord record
                         in filtered
                             .Skip(
                                 pageIndex *
                                 PageSize)
                             .Take(
                                 PageSize))
                {
                    results.Controls.Add(
                        CreateSkillResultCard(
                            record));
                }

                results.ResumeLayout();

                count.Text =
                    $"Results: {filtered.Count:N0} / {catalog.Skills.Count:N0}";

                pageLabel.Text =
                    $"{pageIndex + 1} / {pages}";

                previous.Enabled =
                    pageIndex > 0;

                next.Enabled =
                    pageIndex < pages - 1;

                ResizeCards();
            }

            void ApplySearch()
            {
                filtered =
                    catalog.Search(
                        search.Text);

                pageIndex = 0;
                Render();
            }

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    ApplySearch();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            previous.Click +=
                (_, _) =>
                {
                    if (pageIndex <= 0)
                        return;

                    pageIndex--;
                    Render();
                };

            next.Click +=
                (_, _) =>
                {
                    int pages =
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                filtered.Count /
                                (double)PageSize));

                    if (pageIndex >= pages - 1)
                        return;

                    pageIndex++;
                    Render();
                };

            clear.Click +=
                (_, _) =>
                {
                    state.SkillIds[slot].Text =
                        "0";

                    ClosePicker();
                };

            close.Click +=
                (_, _) =>
                    ClosePicker();

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(search);
            header.Controls.Add(count);
            header.Controls.Add(clear);
            header.Controls.Add(close);

            void PositionHeader()
            {
                int width =
                    header.ClientSize.Width;

                close.Location =
                    new Point(
                        width -
                        close.Width -
                        14,
                        14);

                clear.Location =
                    new Point(
                        close.Left -
                        clear.Width -
                        8,
                        14);

                search.Width =
                    Math.Max(
                        260,
                        width -
                        28);

                subtitle.Width =
                    Math.Max(
                        260,
                        clear.Left -
                        subtitle.Left -
                        12);
            }

            header.Resize +=
                (_, _) =>
                    PositionHeader();

            results.Resize +=
                (_, _) =>
                    ResizeCards();

            footer.Resize +=
                (_, _) =>
                    ResizeCards();

            page.Controls.Add(results);
            page.Controls.Add(footer);
            page.Controls.Add(header);

            loading.BringToFront();
            page.ResumeLayout(true);
            loading.Refresh();

            loading.BringToFront();
            page.ResumeLayout(true);
            loading.Refresh();

            PositionHeader();
            Render();

            page.Controls.Remove(loading);
            loading.Dispose();
            page.PerformLayout();
            page.Update();

            page.Controls.Remove(loading);
            loading.Dispose();
            page.PerformLayout();
            page.Update();

            uint current =
                UInt(
                    state.SkillIds[slot].Text);

            if (current != 0 &&
                catalog.TryGet(
                    current,
                    out SkillReferenceRecord selected))
            {
                search.Text =
                    selected.DisplayName;

                search.SelectionStart =
                    search.TextLength;
            }

            search.Focus();
        }

        private void ValidateDigimonId(DigimonListEditState s)
        {
            if(!uint.TryParse(s.Fields["ID"].Text,out uint id)){s.IdStatus.Text="INVALID ID";s.IdStatus.ForeColor=Color.FromArgb(255,95,95);return;}bool exists=s.Document.Root?.Elements("Digimon").Any(x=>!ReferenceEquals(x,s.Original)&&UInt(x.Attribute("ID")?.Value)==id)==true;s.IdStatus.Text=exists?"ID ALREADY EXISTS":"ID AVAILABLE";s.IdStatus.ForeColor=exists?Color.FromArgb(255,190,90):Color.FromArgb(105,230,135);
        }
        private void UpdateHero(DigimonListEditState s){s.HeroName.Text=string.IsNullOrWhiteSpace(s.Fields["Name"].Text)?"Unnamed Digimon":s.Fields["Name"].Text.Trim();s.HeroMeta.Text=$"ID {s.Fields["ID"].Text}  •  Model {s.Fields["ModelID"].Text}\r\n{EvoLabel(ComboValue((ComboBox)s.Fields["EvolutionType"]))}  •  {AttributeLabel(ComboValue((ComboBox)s.Fields["AttributeType"]))}  •  {RankLabel(ComboValue((ComboBox)s.Fields["DigimonRank"]))}";}
        private void UpdateIcon(DigimonListEditState s)=>s.Icon.Image=DigimonIcon(UInt(s.Fields["ID"].Text),UInt(s.Fields["ModelID"].Text));

        private Bitmap? DigimonIcon(uint id,uint model)
        {
            if (digimonEditorIcons.TryGetValue(id, out Bitmap? b))
                return b;

            b = EditorPreloadService.TryGetDigimonIcon(id);

            if (b == null && model != 0 && model != id)
                b = EditorPreloadService.TryGetDigimonIcon(model);

            // Fallback if ImgDatabase changed after startup.
            b ??= LoadDigimonIcon(id);

            if (b == null && model != 0 && model != id)
                b = LoadDigimonIcon(model);

            digimonEditorIcons[id] = b;
            return b;
        }
        private static Bitmap? LoadDigimonIcon(uint id)
        {
            return DigimonListEditorService.TryLoadIconFromDatabase(id);
        }

        private static FlowLayoutPanel Section(string title,string subtitle)
        {
            var section =
                new FlowLayoutPanel
                {
                    Width = 700,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = true,
                    BackColor = Color.FromArgb(21,21,21),
                    Padding = new Padding(12,58,12,12),
                    Margin = new Padding(0,0,0,12),
                    Tag = "DigimonFullWidth"
                };

            var titleLabel =
                new Label
                {
                    Text = title,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold",9.5F,FontStyle.Bold),
                    Location = new Point(14,12),
                    Size = new Size(320,22)
                };

            var subtitleLabel =
                new Label
                {
                    Text = subtitle,
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI",7.5F),
                    Location = new Point(14,34),
                    Size = new Size(620,20)
                };

            section.Controls.Add(titleLabel);
            section.Controls.Add(subtitleLabel);
            section.SetFlowBreak(titleLabel,true);
            section.SetFlowBreak(subtitleLabel,true);

            void Relayout()
            {
                titleLabel.Location = new Point(14,12);
                subtitleLabel.Location = new Point(14,34);
                subtitleLabel.Width = Math.Max(200, section.ClientSize.Width - 28);

                titleLabel.BringToFront();
                subtitleLabel.BringToFront();

                int available =
                    Math.Max(
                        400,
                        section.ClientSize.Width -
                        section.Padding.Horizontal);

                int half =
                    Math.Max(
                        190,
                        (available - 20) / 2);

                foreach (Control child in section.Controls)
                {
                    if (child == titleLabel || child == subtitleLabel)
                        continue;

                    string? tag = child.Tag as string;

                    if (tag == "DigimonWideCard")
                        child.Width = Math.Max(280, available - 10);
                    else if (tag == "DigimonStatCard")
                        child.Width = Math.Max(110, (available - 40) / 4);
                    else
                        child.Width = half;
                }
            }

            section.Layout += (_, _) => Relayout();
            section.Resize += (_, _) => Relayout();

            return section;
        }
        private static Panel Card(
            string title,
            string hint,
            Control edit,
            Control? extra = null,
            bool wide = false)
        {
            var panel =
                new Panel
                {
                    Width = wide ? 704 : 346,
                    Height = extra == null ? 104 : 138,
                    BackColor = Color.FromArgb(29,29,29),
                    Margin = new Padding(5,5,5,7),
                    Tag = wide ? "DigimonWideCard" : null
                };

            var titleLabel =
                new Label
                {
                    Text = title,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold",8.3F,FontStyle.Bold),
                    Location = new Point(12,10),
                    Height = 22
                };

            var hintLabel =
                new Label
                {
                    Text = hint,
                    ForeColor = Color.FromArgb(150,150,150),
                    Font = new Font("Segoe UI",7F),
                    Location = new Point(12,68),
                    Height = 20,
                    AutoEllipsis = true
                };

            edit.Location = new Point(12,36);

            panel.Controls.Add(titleLabel);
            panel.Controls.Add(edit);
            panel.Controls.Add(hintLabel);

            if (extra != null)
            {
                extra.Location = new Point(12,101);
                extra.Height = 22;
                panel.Controls.Add(extra);
            }

            void Relayout()
            {
                int inner = Math.Max(70, panel.ClientSize.Width - 24);

                titleLabel.Width = inner;
                edit.Width = inner;
                hintLabel.Width = inner;

                if (extra != null)
                    extra.Width = inner;
            }

            panel.Resize += (_, _) => Relayout();
            Relayout();

            return panel;
        }
        private static Panel TripleCard(
            string title,
            string hint,
            ComboBox[] boxes)
        {
            var panel =
                new Panel
                {
                    Width = 704,
                    Height = 108,
                    BackColor = Color.FromArgb(29,29,29),
                    Margin = new Padding(5,5,5,7),
                    Tag = "DigimonWideCard"
                };

            var titleLabel =
                new Label
                {
                    Text = title,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold",8.3F,FontStyle.Bold),
                    Location = new Point(12,10),
                    Height = 22
                };

            var hintLabel =
                new Label
                {
                    Text = hint,
                    ForeColor = Color.FromArgb(150,150,150),
                    Font = new Font("Segoe UI",7F),
                    Location = new Point(12,70),
                    Height = 24
                };

            panel.Controls.Add(titleLabel);

            foreach (ComboBox box in boxes)
                panel.Controls.Add(box);

            panel.Controls.Add(hintLabel);

            void Relayout()
            {
                int inner = Math.Max(270, panel.ClientSize.Width - 24);
                titleLabel.Width = inner;
                hintLabel.Width = inner;

                const int gap = 10;
                int boxWidth =
                    Math.Max(
                        80,
                        (inner - gap * 2) / 3);

                for (int i = 0; i < 3; i++)
                {
                    boxes[i].Location =
                        new Point(
                            12 + i * (boxWidth + gap),
                            36);

                    boxes[i].Width = boxWidth;
                }
            }

            panel.Resize += (_, _) => Relayout();
            Relayout();

            return panel;
        }
        private static Panel StatCard(
            string title,
            string raw,
            TextBox box)
        {
            var panel =
                new Panel
                {
                    Width = 166,
                    Height = 92,
                    BackColor = Color.FromArgb(29,29,29),
                    Margin = new Padding(5,5,5,7),
                    Tag = "DigimonStatCard"
                };

            var titleLabel =
                new Label
                {
                    Text = title,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold",8F,FontStyle.Bold),
                    Location = new Point(10,9),
                    Height = 20
                };

            var rawLabel =
                new Label
                {
                    Text = raw,
                    ForeColor = Color.Gray,
                    Font = new Font("Consolas",6.8F),
                    Location = new Point(10,64),
                    Height = 18
                };

            box.Location = new Point(10,34);

            panel.Controls.Add(titleLabel);
            panel.Controls.Add(box);
            panel.Controls.Add(rawLabel);

            void Relayout()
            {
                int inner = Math.Max(60, panel.ClientSize.Width - 20);
                titleLabel.Width = inner;
                box.Width = inner;
                rawLabel.Width = inner;
            }

            panel.Resize += (_, _) => Relayout();
            Relayout();

            return panel;
        }
        private static Panel ModelSelectionCard(
            TextBox modelId,
            Label info,
            Button select)
        {
            var panel =
                new Panel
                {
                    Width = 346,
                    Height = 132,
                    BackColor = Color.FromArgb(29,29,29),
                    Margin = new Padding(5,5,5,7)
                };

            var title =
                new Label
                {
                    Text = "Model",
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI Semibold",8.3F,FontStyle.Bold),
                    Location = new Point(12,10),
                    Height = 22
                };

            modelId.Location = new Point(12,36);
            select.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            info.Location = new Point(12,75);
            info.Height = 42;

            panel.Controls.Add(title);
            panel.Controls.Add(modelId);
            panel.Controls.Add(select);
            panel.Controls.Add(info);

            void Relayout()
            {
                int right = panel.ClientSize.Width - 12;
                select.Location = new Point(right-select.Width,34);
                modelId.Width = Math.Max(100,select.Left-modelId.Left-10);
                info.Width = Math.Max(120,panel.ClientSize.Width-24);
                title.Width = info.Width;
            }

            panel.Resize += (_,_)=>Relayout();
            Relayout();
            return panel;
        }

        private async Task RefreshSelectedModelInfoAsync(
            TextBox modelId,
            Label info)
        {
            uint id = UInt(modelId.Text);

            if (id == 0)
            {
                info.Text = "No Digimon model selected.";
                info.ForeColor = CMuted;
                return;
            }

            DigimonModelReferenceService? models =
                EditorPreloadService.TryGetDigimonModels();

            if (models == null)
            {
                try
                {
                    models =
                        await EditorPreloadService
                            .GetDigimonModelsAsync();
                }
                catch (Exception ex)
                {
                    if (!info.IsDisposed)
                    {
                        info.Text = "Model.xml unavailable: " + ex.Message;
                        info.ForeColor = Color.FromArgb(255,150,120);
                    }
                    return;
                }
            }

            if (info.IsDisposed)
                return;

            if (!models.TryGet(id,out DigimonModelReference selected))
            {
                info.Text = $"Model {id} is not a Data\\Digimon model in Model.xml.";
                info.ForeColor = Color.FromArgb(255,170,90);
                return;
            }

            info.Text = $"{selected.DisplayName}  •  {selected.KfmPath}";
            info.ForeColor = Color.FromArgb(125,220,140);
        }

        private async Task OpenDigimonModelPickerAsync(
            TabPage owner,
            TextBox targetModelId)
        {
            string key =
                $"digimon-model-picker:{owner.Name}";

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        x => x.Name.Equals(
                            key,
                            StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab =
                    existing;

                return;
            }

            var page =
                CreateDarkTab(
                    "Select Digimon Model");

            page.Name =
                key;

            var loading =
                new EditorLoadingView(
                    "Loading Digimon Models",
                    "Using the startup-preloaded Model.xml Data\\Digimon catalog.");

            loading.Dock =
                DockStyle.Fill;

            page.Controls.Add(
                loading);

            editorTabs.TabPages.Add(
                page);

            editorTabs.SelectedTab =
                page;

            DigimonModelReferenceService catalog;

            try
            {
                // IMPORTANT:
                // LoadingForm should already have populated this catalog.
                // Use the in-memory instance immediately whenever possible.
                catalog =
                    EditorPreloadService
                        .TryGetDigimonModels()
                    ?? await EditorPreloadService
                        .GetDigimonModelsAsync();
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "Model.xml could not be loaded",
                        ex.Message);
                }

                return;
            }

            if (page.IsDisposed ||
                owner.IsDisposed)
            {
                return;
            }

            // Build the complete first usable frame behind the loading view.
            // The loading view is removed only after the first page has been
            // created and laid out successfully.
            var content =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor =
                        Color.FromArgb(
                            18,
                            18,
                            18),
                    Visible = false
                };

            var header =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 116,
                    BackColor =
                        Color.FromArgb(
                            24,
                            24,
                            24)
                };

            var title =
                new Label
                {
                    Text =
                        "Select Digimon Model",
                    ForeColor = CText,
                    Font =
                        new Font(
                            "Segoe UI Semibold",
                            13F,
                            FontStyle.Bold),
                    Location =
                        new Point(
                            14,
                            10),
                    Size =
                        new Size(
                            320,
                            28)
                };

            var subtitle =
                new Label
                {
                    Text =
                        $"Model.xml  •  {catalog.Models.Count:N0} Data\\Digimon models  •  PRELOADED",
                    ForeColor =
                        Color.FromArgb(
                            125,
                            220,
                            140),
                    Location =
                        new Point(
                            16,
                            40),
                    Size =
                        new Size(
                            560,
                            20)
                };

            var search =
                new TextBox
                {
                    BackColor =
                        Color.FromArgb(
                            10,
                            10,
                            10),
                    ForeColor =
                        Color.White,
                    BorderStyle =
                        BorderStyle.FixedSingle,
                    Font =
                        new Font(
                            "Segoe UI",
                            9F),
                    PlaceholderText =
                        "Search Model ID, Digimon folder/name or KFM path...",
                    Location =
                        new Point(
                            14,
                            72),
                    Height = 28
                };

            var close =
                CreateEditorActionButton(
                    "CLOSE");

            close.Size =
                new Size(
                    88,
                    30);

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    FlowDirection =
                        FlowDirection.TopDown,
                    WrapContents = false,
                    BackColor =
                        Color.FromArgb(
                            18,
                            18,
                            18),
                    Padding =
                        new Padding(
                            12,
                            10,
                            34,
                            44)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            var footer =
                new Panel
                {
                    Dock = DockStyle.Bottom,
                    Height = 46,
                    BackColor =
                        Color.FromArgb(
                            23,
                            23,
                            23)
                };

            var previous =
                CreateEditorActionButton(
                    "◀ PREVIOUS");

            previous.Size =
                new Size(
                    110,
                    30);

            var pageLabel =
                new Label
                {
                    ForeColor = CText,
                    TextAlign =
                        ContentAlignment.MiddleCenter,
                    Size =
                        new Size(
                            90,
                            30)
                };

            var next =
                CreateEditorActionButton(
                    "NEXT ▶");

            next.Size =
                new Size(
                    110,
                    30);

            footer.Controls.Add(
                previous);

            footer.Controls.Add(
                pageLabel);

            footer.Controls.Add(
                next);

            IReadOnlyList<DigimonModelReference> filtered =
                catalog.Models;

            // Keep the picker lightweight even on large Model.xml files.
            const int PageSize = 30;
            int pageIndex = 0;

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            void ClosePicker()
            {
                timer.Stop();
                timer.Dispose();

                if (page.IsDisposed)
                    return;

                editorTabs.TabPages.Remove(
                    page);

                page.Dispose();

                if (!owner.IsDisposed)
                {
                    editorTabs.SelectedTab =
                        owner;
                }
            }

            void Select(
                DigimonModelReference selected)
            {
                targetModelId.Text =
                    selected.Id.ToString(
                        CultureInfo.InvariantCulture);

                ClosePicker();
            }

            Panel CreateCard(
                DigimonModelReference item)
            {
                var card =
                    new Panel
                    {
                        Height = 102,
                        BackColor =
                            Color.FromArgb(
                                29,
                                29,
                                29),
                        BorderStyle =
                            BorderStyle.FixedSingle,
                        Margin =
                            new Padding(
                                3,
                                3,
                                3,
                                5)
                    };

                var pic =
                    new PictureBox
                    {
                        Location =
                            new Point(
                                12,
                                15),
                        Size =
                            new Size(
                                68,
                                68),
                        BackColor =
                            Color.FromArgb(
                                8,
                                8,
                                8),
                        SizeMode =
                            PictureBoxSizeMode.Zoom,

                        // Icons were already prepared by the startup preload.
                        // Do not force a new Model.xml parse or disk scan here.
                        Image =
                            EditorPreloadService
                                .TryGetDigimonIcon(
                                    item.Id)
                    };

                var name =
                    new Label
                    {
                        Text =
                            $"{item.Id} — {item.DisplayName}",
                        ForeColor =
                            Color.FromArgb(
                                125,
                                220,
                                140),
                        Font =
                            new Font(
                                "Segoe UI Semibold",
                                9.6F,
                                FontStyle.Bold),
                        Location =
                            new Point(
                                94,
                                12),
                        Height = 22,
                        AutoEllipsis = true
                    };

                var path =
                    new Label
                    {
                        Text =
                            item.KfmPath,
                        ForeColor =
                            CMuted,
                        Font =
                            new Font(
                                "Segoe UI",
                                7.4F),
                        Location =
                            new Point(
                                94,
                                39),
                        Height = 20,
                        AutoEllipsis = true
                    };

                var dims =
                    new Label
                    {
                        Text =
                            $"Scale {item.Scale:0.###}  •  Height {item.Height:0.###}  •  Width {item.Width:0.###}",
                        ForeColor =
                            Color.FromArgb(
                                155,
                                155,
                                155),
                        Font =
                            new Font(
                                "Consolas",
                                7F),
                        Location =
                            new Point(
                                94,
                                65),
                        Height = 18,
                        AutoEllipsis = true
                    };

                var select =
                    CreateEditorActionButton(
                        "SELECT");

                select.Size =
                    new Size(
                        94,
                        32);

                void Relayout()
                {
                    select.Location =
                        new Point(
                            Math.Max(
                                0,
                                card.ClientSize.Width -
                                select.Width -
                                12),
                            33);

                    int width =
                        Math.Max(
                            120,
                            select.Left -
                            name.Left -
                            14);

                    name.Width =
                        width;

                    path.Width =
                        width;

                    dims.Width =
                        width;
                }

                card.Resize +=
                    (_, _) =>
                        Relayout();

                select.Click +=
                    (_, _) =>
                        Select(item);

                card.DoubleClick +=
                    (_, _) =>
                        Select(item);

                pic.DoubleClick +=
                    (_, _) =>
                        Select(item);

                card.Controls.Add(
                    pic);

                card.Controls.Add(
                    name);

                card.Controls.Add(
                    path);

                card.Controls.Add(
                    dims);

                card.Controls.Add(
                    select);

                Relayout();

                return card;
            }

            void ResizeCards()
            {
                int width =
                    Math.Max(
                        360,
                        results.ClientSize.Width -
                        results.Padding.Horizontal -
                        SystemInformation
                            .VerticalScrollBarWidth -
                        18);

                foreach (Control control in
                         results.Controls)
                {
                    control.Width =
                        width;
                }

                next.Location =
                    new Point(
                        Math.Max(
                            0,
                            footer.ClientSize.Width -
                            next.Width -
                            16),
                        8);

                pageLabel.Location =
                    new Point(
                        Math.Max(
                            0,
                            next.Left -
                            pageLabel.Width -
                            8),
                        8);

                previous.Location =
                    new Point(
                        Math.Max(
                            0,
                            pageLabel.Left -
                            previous.Width -
                            8),
                        8);
            }

            void ResetScroll()
            {
                try
                {
                    results.AutoScrollPosition =
                        new Point(
                            0,
                            0);

                    results.VerticalScroll.Value =
                        results.VerticalScroll.Minimum;
                }
                catch
                {
                }
            }

            void Render()
            {
                results.SuspendLayout();

                try
                {
                    foreach (Control control in
                             results.Controls
                                 .Cast<Control>()
                                 .ToArray())
                    {
                        results.Controls.Remove(
                            control);

                        control.Dispose();
                    }

                    int pages =
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                filtered.Count /
                                (double)PageSize));

                    pageIndex =
                        Math.Clamp(
                            pageIndex,
                            0,
                            pages - 1);

                    foreach (DigimonModelReference item in
                             filtered
                                 .Skip(
                                     pageIndex *
                                     PageSize)
                                 .Take(
                                     PageSize))
                    {
                        results.Controls.Add(
                            CreateCard(
                                item));
                    }

                    pageLabel.Text =
                        $"{pageIndex + 1} / {pages}";

                    previous.Enabled =
                        pageIndex > 0;

                    next.Enabled =
                        pageIndex < pages - 1;

                    ResizeCards();
                }
                finally
                {
                    results.ResumeLayout(
                        true);
                }
            }

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();

                    filtered =
                        catalog.Search(
                            search.Text);

                    pageIndex = 0;

                    Render();

                    ResetScroll();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            previous.Click +=
                (_, _) =>
                {
                    if (pageIndex <= 0)
                        return;

                    pageIndex--;

                    Render();
                    ResetScroll();
                };

            next.Click +=
                (_, _) =>
                {
                    int pages =
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                filtered.Count /
                                (double)PageSize));

                    if (pageIndex >=
                        pages - 1)
                    {
                        return;
                    }

                    pageIndex++;

                    Render();
                    ResetScroll();
                };

            close.Click +=
                (_, _) =>
                    ClosePicker();

            void PositionHeader()
            {
                close.Location =
                    new Point(
                        Math.Max(
                            0,
                            header.ClientSize.Width -
                            close.Width -
                            14),
                        14);

                search.Width =
                    Math.Max(
                        250,
                        header.ClientSize.Width -
                        28);

                subtitle.Width =
                    Math.Max(
                        180,
                        close.Left -
                        subtitle.Left -
                        10);
            }

            header.Controls.Add(
                title);

            header.Controls.Add(
                subtitle);

            header.Controls.Add(
                search);

            header.Controls.Add(
                close);

            header.Resize +=
                (_, _) =>
                    PositionHeader();

            results.Resize +=
                (_, _) =>
                    ResizeCards();

            footer.Resize +=
                (_, _) =>
                    ResizeCards();

            content.Controls.Add(
                results);

            content.Controls.Add(
                footer);

            content.Controls.Add(
                header);

            page.Controls.Add(
                content);

            try
            {
                PositionHeader();
                Render();

                uint current =
                    UInt(
                        targetModelId.Text);

                // Do NOT populate the search TextBox with the current model.
                // Doing so immediately triggered a second timer/render during
                // the loading transition and made the tab look stuck.
                if (current != 0 &&
                    catalog.TryGet(
                        current,
                        out DigimonModelReference selected))
                {
                    subtitle.Text =
                        $"Model.xml  •  {catalog.Models.Count:N0} Data\\Digimon models  •  Current: {selected.Id} {selected.DisplayName}";
                }

                // Give WinForms one layout/message cycle with the finished
                // content still hidden, then swap atomically.
                await Task.Yield();

                if (page.IsDisposed)
                    return;

                page.Controls.Remove(
                    loading);

                loading.Dispose();

                content.Visible =
                    true;

                content.BringToFront();

                page.ResumeLayout(
                    true);

                content.PerformLayout();
                results.PerformLayout();

                search.Focus();
            }
            catch (Exception ex)
            {
                content.Visible =
                    false;

                if (!loading.IsDisposed)
                {
                    loading.BringToFront();

                    loading.SetError(
                        "Digimon model picker could not render",
                        ex.Message);
                }
            }
        }

        private static Panel SkillSelectionCard(
            int slot,
            TextBox hiddenId,
            PictureBox icon,
            Label name,
            Label idLabel,
            Label comment,
            Button select,
            Button clear)
        {
            var panel =
                new Panel
                {
                    Width = 704,
                    Height = 104,
                    BackColor = Color.FromArgb(29,29,29),
                    Margin = new Padding(5,5,5,7),
                    Tag = "DigimonWideCard"
                };

            var slotLabel =
                new Label
                {
                    Text = $"SLOT {slot + 1}",
                    ForeColor = Color.FromArgb(125,220,140),
                    Font = new Font(
                        "Segoe UI Semibold",
                        8.5F,
                        FontStyle.Bold),
                    Location = new Point(12,12),
                    Size = new Size(58,20)
                };

            icon.Location =
                new Point(
                    76,
                    20);

            name.Location =
                new Point(
                    148,
                    13);

            idLabel.Location =
                new Point(
                    148,
                    39);

            comment.Location =
                new Point(
                    148,
                    64);

            hiddenId.Location =
                new Point(
                    0,
                    0);

            select.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            clear.Anchor =
                AnchorStyles.Top |
                AnchorStyles.Right;

            panel.Controls.Add(slotLabel);
            panel.Controls.Add(icon);
            panel.Controls.Add(name);
            panel.Controls.Add(idLabel);
            panel.Controls.Add(comment);
            panel.Controls.Add(select);
            panel.Controls.Add(clear);
            panel.Controls.Add(hiddenId);

            void Relayout()
            {
                int right =
                    panel.ClientSize.Width -
                    12;

                clear.Location =
                    new Point(
                        right -
                        clear.Width,
                        54);

                select.Location =
                    new Point(
                        right -
                        select.Width,
                        14);

                int textRight =
                    Math.Min(
                        select.Left - 14,
                        panel.ClientSize.Width - 150);

                int textWidth =
                    Math.Max(
                        160,
                        textRight -
                        name.Left);

                name.Width = textWidth;
                idLabel.Width = textWidth;
                comment.Width = textWidth;
            }

            panel.Resize +=
                (_, _) =>
                    Relayout();

            Relayout();
            return panel;
        }


        private static TextBox Field(string value)=>new(){Text=value,BackColor=Color.FromArgb(10,10,10),ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Consolas",8.8F),Height=26};
        private static ComboBox ValueCombo(IEnumerable<DigimonOption> opts,int selected){var c=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(18,18,18),ForeColor=Color.White,Font=new Font("Segoe UI",8.4F),IntegralHeight=false,DropDownHeight=260,Height=27};var a=opts.ToArray();c.Items.AddRange(a);var m=a.FirstOrDefault(x=>x.Value==selected);if(m==null){m=new DigimonOption(selected,$"{selected} — Current XML value");c.Items.Add(m);}c.SelectedItem=m;return c;}

        private static IEnumerable<DigimonOption> EvolutionOptions(){for(int i=2;i<=18;i++)yield return new DigimonOption(i,EvoLabel(i));}
        private static string EvoLabel(int v)=>v switch
        {
            2=>"2 — In-Training",
            3=>"3 — Rookie",
            4=>"4 — Champion",
            5=>"5 — Ultimate",
            6=>"6 — Mega",
            7=>"7 — Burst Mode",
            8=>"8 — Jogress",
            9=>"9 — Armor",
            10=>"10 — Hybrid",
            11=>"11 — Rookie X",
            12=>"12 — Champion X",
            13=>"13 — Ultimate X",
            14=>"14 — Mega X",
            15=>"15 — Burst Mode X",
            16=>"16 — Jogress X",
            17=>"17 — Variant",
            _=>$"{v} — Evolution Type {v}"
        };
        private static IEnumerable<DigimonOption> AttributeOptions(){yield return new(1,"1 — None / Special");yield return new(2,"2 — Data");yield return new(3,"3 — Vaccine");yield return new(4,"4 — Virus");yield return new(5,"5 — Unknown");}
        private static IEnumerable<DigimonOption> RankOptions()
        {
            for(int i=0;i<=10;i++)yield return new DigimonOption(i,RankLabel(i));
        }

        private static string RankLabel(int v)=>v switch
        {
            0=>"0 — Rank B",
            1=>"1 — Rank A",
            2=>"2 — Rank A+",
            3=>"3 — Rank S",
            4=>"4 — Rank S+",
            5=>"5 — Rank SS",
            6=>"6 — Rank SS+",
            7=>"7 — Rank SSS",
            8=>"8 — Rank SSS+",
            9=>"9 — Rank U",
            10=>"10 — Rank U+",
            _=>$"{v} — Rank {v}"
        };

        private static string AttributeLabel(int v)=>v switch{1=>"None / Special",2=>"Data",3=>"Vaccine",4=>"Virus",5=>"Unknown",_=>$"Attribute {v}"};
        private static IEnumerable<DigimonOption> NatureOptions(){yield return new(0,"0 — None");yield return new(1,"1 — Special");yield return new(16,"16 — Ice");yield return new(17,"17 — Water");yield return new(18,"18 — Fire");yield return new(19,"19 — Earth");yield return new(20,"20 — Wind");yield return new(21,"21 — Wood");yield return new(22,"22 — Light");yield return new(23,"23 — Dark");yield return new(24,"24 — Thunder");yield return new(25,"25 — Steel");yield return new(26,"26 — None / Special");}
        private static IEnumerable<DigimonOption> NumberOptions(int min,int max,string label){for(int i=min;i<=max;i++)yield return new DigimonOption(i,$"{i} — {label} {i}");}
        private static int? FilterValue(ComboBox c)=>c.SelectedItem is DigimonOption o&&o.Value!=int.MinValue?o.Value:null; private static int ComboValue(ComboBox c)=>c.SelectedItem is DigimonOption o?o.Value:Int(c.Text.Split('—')[0].Trim());
        private static int[] Triple(string s){var a=(s??"").Split(',').Select(Int).Take(3).ToList();while(a.Count<3)a.Add(0);return a.ToArray();}
        private static string StatLabel(string s)=>s switch{"DefPower"=>"Defense","MoveSpeed"=>"Move Speed","CriticalRate"=>"Critical Rate","AttPower"=>"Attack","AttSpeed"=>"Attack Speed","AttRange"=>"Attack Range","HitRate"=>"Hit Rate",_=>s};
        private static string Attr(XElement e,string n)=>e.Attribute(n)?.Value??""; private static string Txt(XElement e,string n)=>e.Element(n)?.Value??""; private static void Set(XElement e,string n,string v){var x=e.Element(n);if(x!=null)x.Value=v;else e.Add(new XElement(n,v));}
        private static uint UInt(string? s)=>uint.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out uint v)?v:0; private static int Int(string? s)=>int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int v)?v:0;
        private static uint ParseUInt(string s,string f){if(!uint.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out uint v))throw new InvalidDataException($"{f}: invalid UInt32 '{s}'.");return v;} private static int ParseInt(string s,string f){if(!int.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out int v))throw new InvalidDataException($"{f}: invalid integer '{s}'.");return v;} private static ushort ParseUShort(string s,string f){if(!ushort.TryParse(s,NumberStyles.Integer,CultureInfo.InvariantCulture,out ushort v))throw new InvalidDataException($"{f}: value must be 0..65535.");return v;} private static string ParseFloat(string s,string f){if(!float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out float v))throw new InvalidDataException($"{f}: invalid decimal '{s}'. Use '.' as decimal separator.");return v.ToString("0.######",CultureInfo.InvariantCulture);}

        private static XElement NewDigimon(XDocument d)
        {
            uint id=(d.Root?.Elements("Digimon").Select(x=>UInt(x.Attribute("ID")?.Value)).DefaultIfEmpty(0u).Max()??0u)+1;
            return new XElement("Digimon",new XAttribute("ID",id),new XAttribute("Name","New Digimon"),new XElement("ModelID",id),new XElement("SoundDir","_"),new XElement("SelectScale","0"),new XElement("EvoEffectDir",@"System\Digimon_Tactics.nif"),new XElement("EvolutionType","3"),new XElement("AttributeType","3"),new XElement("FamilyTypes","0,0,0"),new XElement("BaseNatureType","26"),new XElement("BaseNatureTypes","0,0,0"),new XElement("BaseLevel","1"),new XElement("Stats",new XAttribute("HP","0"),new XAttribute("DS","0"),new XAttribute("DefPower","0"),new XAttribute("Evasion","0"),new XAttribute("MoveSpeed","580"),new XAttribute("CriticalRate","0"),new XAttribute("AttPower","0"),new XAttribute("AttSpeed","0"),new XAttribute("AttRange","0"),new XAttribute("HitRate","0")),new XElement("DigimonType","1"),new XElement("CharSize","100"),new XElement("Skills",Enumerable.Range(0,5).Select(i=>new XElement("Skill",new XAttribute("Slot",i),new XAttribute("ID","0"),new XAttribute("ReqPrevSkillLevel",i%2==1?"3":"0")))),new XElement("WalkLen","300"),new XElement("RunLen","300"),new XElement("ARunLen","300"),new XElement("Form","-"),new XElement("DigimonRank","0"));
        }

        private static void SaveDigimonDoc(string path,XDocument d)
        {
            string tmp=path+".tmp";var settings=new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,IndentChars="  ",NewLineChars=Environment.NewLine,NewLineHandling=NewLineHandling.Replace};using(var w=XmlWriter.Create(tmp,settings))d.Save(w);File.Copy(tmp,path,true);File.Delete(tmp);
        }
    }
}
