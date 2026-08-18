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
        private const int EvoBrowserPageSize = 18;
        private const int EvoMiniIconsPerRow = 6;

        private readonly Dictionary<uint, Bitmap?> digimonEvoIconCache = new();
        private readonly Dictionary<uint, Bitmap?> digimonEvoItemIconCache = new();

        private sealed class DigimonEvoBrowseState
        {
            public required DigimonEvoEditorService Service { get; init; }
            public required TabPage Page { get; init; }
            public required TextBox Search { get; init; }
            public required Label Count { get; init; }
            public required Label PageLabel { get; init; }
            public required Button Previous { get; init; }
            public required Button Next { get; init; }
            public required FlowLayoutPanel Results { get; init; }
            public required System.Windows.Forms.Timer SearchTimer { get; init; }
            public List<DigimonEvoChainInfo> Filtered { get; set; } = new();
            public int PageIndex { get; set; }
        }

        private sealed class DigimonEvoEditState
        {
            public required DigimonEvoEditorService Service { get; init; }
            public required TabPage Page { get; init; }
            public required uint RootId { get; init; }
            public required XElement WorkingChain { get; init; }
            public XElement? OriginalChain { get; init; }
            public required FlowLayoutPanel Body { get; init; }
            public bool Dirty { get; set; }
            public bool IsNew { get; init; }
            public uint? ExpandedEvolutionId { get; set; }
        }

        private async void OpenDigimonEvoBrowser(string xmlPath)
        {
            string full = Path.GetFullPath(xmlPath);

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        p =>
                            string.Equals(
                                p.Name,
                                "digimonevo-browser:" + full,
                                StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            var page = CreateDarkTab("DigimonEvo.xml");
            page.Name = "digimonevo-browser:" + full;

            var loading =
                new EditorLoadingView(
                    "Loading Digimon Evolution Database",
                    "Loading DigimonEvo.xml, Digimon_List.xml, ItemDisplay.xml, ItemList.xml and Quest.xml.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            try
            {
                DigimonEvoEditorService service =
                    await EditorPreloadService.GetDigimonEvoAsync(full);

                if (page.IsDisposed)
                    return;

                BuildDigimonEvoBrowser(page, service, loading);
            }
            catch (Exception ex)
            {
                if (!page.IsDisposed)
                {
                    loading.SetError(
                        "DigimonEvo.xml could not be loaded",
                        ex.Message);
                }
            }
        }

        private void BuildDigimonEvoBrowser(
            TabPage page,
            DigimonEvoEditorService service,
            EditorLoadingView loading)
        {
            page.SuspendLayout();

            var host =
                new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    Padding = new Padding(16, 12, 18, 12),
                    ColumnCount = 1,
                    RowCount = 2
                };

            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, 138F));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var header =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = CEditor,
                    Margin = Padding.Empty
                };

            var title =
                new Label
                {
                    Text = "Digimon Evolution Database",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(4, 4)
                };

            var info =
                new Label
                {
                    Text =
                        $"DigimonEvo.xml  •  {service.TotalTrees} total trees  •  " +
                        $"{service.TrueStarterTrees} first-evolution starters  •  " +
                        $"{service.TotalTrees - service.TrueStarterTrees} continuation/special groups hidden",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 9F),
                    AutoSize = true,
                    Location = new Point(5, 36)
                };

            var search =
                new TextBox
                {
                    BackColor = Color.FromArgb(15, 15, 15),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 9.5F),
                    PlaceholderText = "Search starter Digimon ID, name or any evolution ID/name...",
                    Location = new Point(5, 68),
                    Height = 26
                };

            var create =
                CreateEditorActionButton("NEW EVOLUTION TREE");
            create.Size = new Size(174, 34);
            create.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            var count =
                new Label
                {
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.8F),
                    AutoSize = true,
                    Location = new Point(5, 108)
                };

            var previous =
                CreateEditorActionButton("◀ PREVIOUS");
            previous.Size = new Size(120, 31);

            var pageLabel =
                new Label
                {
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Size = new Size(64, 31)
                };

            var next =
                CreateEditorActionButton("NEXT ▶");
            next.Size = new Size(120, 31);

            void LayoutHeader()
            {
                int width = Math.Max(500, header.ClientSize.Width);
                create.Location = new Point(width - create.Width - 6, 4);
                search.Width = Math.Max(220, width - 10);

                next.Location =
                    new Point(
                        width - next.Width - 6,
                        102);

                pageLabel.Location =
                    new Point(
                        next.Left - pageLabel.Width - 8,
                        102);

                previous.Location =
                    new Point(
                        pageLabel.Left - previous.Width - 8,
                        102);
            }

            header.Resize += (_, _) => LayoutHeader();

            header.Controls.Add(title);
            header.Controls.Add(info);
            header.Controls.Add(search);
            header.Controls.Add(count);
            header.Controls.Add(previous);
            header.Controls.Add(pageLabel);
            header.Controls.Add(next);
            header.Controls.Add(create);

            var results =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = CEditor,
                    Padding = new Padding(4, 4, 14, 28),
                    Margin = Padding.Empty
                };

            DarkUi.ApplyDarkScrollBar(results);

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 180
                };

            var state =
                new DigimonEvoBrowseState
                {
                    Service = service,
                    Page = page,
                    Search = search,
                    Count = count,
                    PageLabel = pageLabel,
                    Previous = previous,
                    Next = next,
                    Results = results,
                    SearchTimer = timer
                };

            page.Tag = state;

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    state.PageIndex = 0;
                    RefreshDigimonEvoBrowser(state);
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
                    if (state.PageIndex <= 0)
                        return;

                    state.PageIndex--;
                    RefreshDigimonEvoBrowser(state);
                };

            next.Click +=
                (_, _) =>
                {
                    int pages =
                        Math.Max(
                            1,
                            (int)Math.Ceiling(
                                state.Filtered.Count /
                                (double)EvoBrowserPageSize));

                    if (state.PageIndex + 1 >= pages)
                        return;

                    state.PageIndex++;
                    RefreshDigimonEvoBrowser(state);
                };

            create.Click +=
                (_, _) =>
                    ShowDigimonEvoDigimonPicker(
                        page,
                        service,
                        "Create New Evolution Tree",
                        d =>
                        {
                            try
                            {
                                XElement created =
                                    service.CreateChain(d.Id);

                                service.Save();
                                EditorPreloadService.ReplaceDigimonEvo(service.FilePath, service);

                                RefreshDigimonEvoBrowser(state);
                                OpenDigimonEvoEditor(
                                    service,
                                    DigimonEvoEditorService.U(
                                        created.Element("digiId")?.Value),
                                    isNew: false);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(
                                    ex.Message,
                                    "New Evolution Tree",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                            }
                        });

            host.Controls.Add(header, 0, 0);
            host.Controls.Add(results, 0, 1);

            page.Controls.Add(host);
            loading.BringToFront();

            page.ResumeLayout(true);
            LayoutHeader();
            RefreshDigimonEvoBrowser(state);

            page.Controls.Remove(loading);
            loading.Dispose();
            page.PerformLayout();
            page.Update();
        }

        private void RefreshDigimonEvoBrowser(
            DigimonEvoBrowseState state)
        {
            string q = state.Search.Text.Trim();

            List<DigimonEvoChainInfo> starters =
                state.Service
                    .GetChains(startersOnly: true)
                    .ToList();

            if (q.Length != 0)
            {
                starters =
                    starters
                        .Where(
                            chain =>
                            {
                                if (chain.RootId
                                    .ToString(CultureInfo.InvariantCulture)
                                    .Contains(q, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }

                                DigimonEvoDigimonRef root =
                                    state.Service.ResolveDigimon(chain.RootId);

                                if (root.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                                    return true;

                                foreach (uint id in chain.EvolutionIds)
                                {
                                    if (id.ToString(CultureInfo.InvariantCulture)
                                        .Contains(q, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return true;
                                    }

                                    if (state.Service.ResolveDigimon(id).Name
                                        .Contains(q, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return true;
                                    }
                                }

                                return false;
                            })
                        .ToList();
            }

            starters =
                starters
                    .OrderBy(
                        x => state.Service.ResolveDigimon(x.RootId).Name,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.RootId)
                    .ToList();

            state.Filtered = starters;

            int pages =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        starters.Count /
                        (double)EvoBrowserPageSize));

            state.PageIndex =
                Math.Clamp(
                    state.PageIndex,
                    0,
                    pages - 1);

            state.Count.Text =
                $"Starter trees: {starters.Count}  •  " +
                $"Showing {Math.Min(EvoBrowserPageSize, Math.Max(0, starters.Count - state.PageIndex * EvoBrowserPageSize))}";

            state.PageLabel.Text =
                $"{state.PageIndex + 1} / {pages}";

            state.Previous.Enabled = state.PageIndex > 0;
            state.Next.Enabled = state.PageIndex + 1 < pages;

            List<DigimonEvoChainInfo> pageRows =
                starters
                    .Skip(state.PageIndex * EvoBrowserPageSize)
                    .Take(EvoBrowserPageSize)
                    .ToList();

            state.Results.SuspendLayout();

            foreach (Control c in state.Results.Controls.Cast<Control>().ToArray())
            {
                state.Results.Controls.Remove(c);
                c.Dispose();
            }

            foreach (DigimonEvoChainInfo chain in pageRows)
                state.Results.Controls.Add(CreateDigimonEvoChainCard(state, chain));

            state.Results.ResumeLayout(true);
            ResizeDigimonEvoBrowserCards(state.Results);
            ResetDigimonEvoScrollToTop(state.Results);
        }

        private Control CreateDigimonEvoChainCard(
            DigimonEvoBrowseState state,
            DigimonEvoChainInfo chain)
        {
            int iconRows =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        chain.EvolutionIds.Count /
                        (double)EvoMiniIconsPerRow));

            int cardHeight =
                92 +
                iconRows * 38;

            var card =
                new Panel
                {
                    Height = cardHeight,
                    Width = Math.Max(650, state.Results.ClientSize.Width - 34),
                    BackColor = CPanel,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(2, 4, 2, 5),
                    Tag = "digimon-evo-card"
                };

            DigimonEvoDigimonRef root =
                state.Service.ResolveDigimon(chain.RootId);

            var mainIcon =
                new PictureBox
                {
                    Location = new Point(14, 14),
                    Size = new Size(66, 66),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Black,
                    Image = DigimonEvoIcon(state.Service, chain.RootId)
                };

            var name =
                new Label
                {
                    Text = root.Name,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(96, 14)
                };

            var meta =
                new Label
                {
                    Text =
                        $"Starter ID {chain.RootId}  •  BattleType {chain.BattleType}  •  " +
                        $"{chain.Count} evolution{(chain.Count == 1 ? "" : "s")}",
                    ForeColor = Color.FromArgb(115, 225, 145),
                    Font = new Font("Segoe UI", 8.8F),
                    AutoSize = true,
                    Location = new Point(97, 39)
                };

            var miniTitle =
                new Label
                {
                    Text = "EVOLUTION LINE",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI Semibold", 7.6F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(97, 65)
                };

            var edit = CreateEditorActionButton("EDIT");
            edit.Size = new Size(105, 31);

            var remove = CreateEditorActionButton("REMOVE");
            remove.ForeColor = Color.FromArgb(255, 105, 105);
            remove.Size = new Size(105, 31);

            void PositionButtons()
            {
                remove.Location =
                    new Point(
                        card.ClientSize.Width - remove.Width - 14,
                        14);

                edit.Location =
                    new Point(
                        remove.Left - edit.Width - 8,
                        14);
            }

            card.Resize +=
                (_, _) =>
                    PositionButtons();

            edit.Click +=
                (_, _) =>
                    OpenDigimonEvoEditor(
                        state.Service,
                        chain.RootId,
                        isNew: false);

            remove.Click +=
                (_, _) =>
                {
                    DigimonEvoDigimonRef d =
                        state.Service.ResolveDigimon(chain.RootId);

                    if (MessageBox.Show(
                            $"Remove the COMPLETE evolution tree for {chain.RootId} — {d.Name}?\r\n\r\n" +
                            $"This removes {chain.Count} evolution records from DigimonEvo.xml.",
                            "Remove Evolution Tree",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }

                    try
                    {
                        state.Service.RemoveChain(chain.RootId);
                        state.Service.Save();
                        EditorPreloadService.ReplaceDigimonEvo(
                            state.Service.FilePath,
                            state.Service);

                        RefreshDigimonEvoBrowser(state);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "Remove Evolution Tree",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };

            card.Controls.Add(mainIcon);
            card.Controls.Add(name);
            card.Controls.Add(meta);
            card.Controls.Add(miniTitle);
            card.Controls.Add(edit);
            card.Controls.Add(remove);

            int startX = 97;
            int startY = 88;

            for (int i = 0; i < chain.EvolutionIds.Count; ++i)
            {
                uint id = chain.EvolutionIds[i];
                DigimonEvoDigimonRef d = state.Service.ResolveDigimon(id);

                int row = i / EvoMiniIconsPerRow;
                int col = i % EvoMiniIconsPerRow;

                var mini =
                    new Panel
                    {
                        Size = new Size(90, 32),
                        Location =
                            new Point(
                                startX + col * 94,
                                startY + row * 38),
                        BackColor = Color.FromArgb(24, 24, 24)
                    };

                var pic =
                    new PictureBox
                    {
                        Location = new Point(2, 2),
                        Size = new Size(28, 28),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        BackColor = Color.Black,
                        Image = DigimonEvoIcon(state.Service, id)
                    };

                var label =
                    new Label
                    {
                        Text = d.Name,
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 6.8F),
                        AutoEllipsis = true,
                        Location = new Point(34, 3),
                        Size = new Size(53, 26),
                        TextAlign = ContentAlignment.MiddleLeft
                    };

                editorToolTip.SetToolTip(
                    mini,
                    $"{id} — {d.Name}");

                mini.Controls.Add(pic);
                mini.Controls.Add(label);
                card.Controls.Add(mini);
            }

            PositionButtons();
            return card;
        }

        private void ResizeDigimonEvoBrowserCards(
            FlowLayoutPanel results)
        {
            int width =
                Math.Max(
                    640,
                    results.ClientSize.Width -
                    results.Padding.Horizontal -
                    18);

            foreach (Control c in results.Controls)
            {
                if (Equals(c.Tag, "digimon-evo-card"))
                    c.Width = width;
            }
        }

        private void OpenDigimonEvoEditor(
            DigimonEvoEditorService service,
            uint rootId,
            bool isNew)
        {
            string key =
                $"digimonevo-edit:{service.FilePath}:{rootId}";

            TabPage? existing =
                editorTabs.TabPages
                    .Cast<TabPage>()
                    .FirstOrDefault(
                        p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                editorTabs.SelectedTab = existing;
                return;
            }

            XElement original = service.GetChain(rootId);
            XElement working = new XElement(original);

            DigimonEvoDigimonRef root = service.ResolveDigimon(rootId);

            var page =
                CreateDarkTab(
                    $"{root.Name} Evolution [Edit]");

            page.Name = key;

            var loading =
                new EditorLoadingView(
                    "Loading Evolution Tree",
                    $"Preparing {rootId} — {root.Name}, evolution icons and unlock references.");

            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;

            var top =
                new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 102,
                    BackColor = CPanel,
                    Padding = new Padding(14, 12, 14, 10)
                };

            var hero =
                new PictureBox
                {
                    Location = new Point(14, 13),
                    Size = new Size(68, 68),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Black,
                    Image = DigimonEvoIcon(service, rootId)
                };

            var heroName =
                new Label
                {
                    Text = root.Name,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(96, 12)
                };

            var heroMeta =
                new Label
                {
                    Text =
                        $"Root ID {rootId}  •  Evolution records {working.Elements("Evolution").Count()}",
                    ForeColor = Color.FromArgb(115, 225, 145),
                    Font = new Font("Segoe UI", 9F),
                    AutoSize = true,
                    Location = new Point(97, 40)
                };

            var save = CreateEditorActionButton("SAVE");
            save.Size = new Size(118, 34);

            var viewXml = CreateEditorActionButton("VIEW XML BLOCK");
            viewXml.Size = new Size(152, 34);

            void PositionTopButtons()
            {
                viewXml.Location =
                    new Point(
                        Math.Max(350, top.ClientSize.Width - viewXml.Width - 14),
                        18);

                save.Location =
                    new Point(
                        viewXml.Left - save.Width - 8,
                        18);
            }

            top.Resize += (_, _) => PositionTopButtons();
            top.Controls.Add(hero);
            top.Controls.Add(heroName);
            top.Controls.Add(heroMeta);
            top.Controls.Add(save);
            top.Controls.Add(viewXml);

            var body =
                new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = CEditor,
                    Padding = new Padding(14, 12, 18, 34)
                };

            DarkUi.ApplyDarkScrollBar(body);

            var state =
                new DigimonEvoEditState
                {
                    Service = service,
                    Page = page,
                    RootId = rootId,
                    WorkingChain = working,
                    OriginalChain = original,
                    Body = body,
                    IsNew = isNew
                };

            page.Tag = state;

            save.Click +=
                (_, _) =>
                    SaveDigimonEvoEditor(state, showSuccess: true);

            viewXml.Click +=
                (_, _) =>
                    OpenRawBlockTab(
                        state.Service.FilePath,
                        new XElement(state.WorkingChain));

            page.Controls.Add(body);
            page.Controls.Add(top);

            loading.BringToFront();
            page.PerformLayout();
            PositionTopButtons();
            RenderDigimonEvoEditBody(state);

            page.Controls.Remove(loading);
            loading.Dispose();
            page.Update();
        }

        private void RenderDigimonEvoEditBody(
            DigimonEvoEditState state)
        {
            FlowLayoutPanel body = state.Body;
            body.SuspendLayout();

            foreach (Control c in body.Controls.Cast<Control>().ToArray())
            {
                body.Controls.Remove(c);
                c.Dispose();
            }

            int targetWidth =
                Math.Max(
                    660,
                    body.ClientSize.Width -
                    body.Padding.Horizontal -
                    18);

            var section =
                new Label
                {
                    Text = "EVOLUTION LINE",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                    AutoSize = false,
                    Width = targetWidth,
                    Height = 32,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, 0, 4)
                };

            body.Controls.Add(section);

            List<XElement> evolutions =
                state.WorkingChain.Elements("Evolution").ToList();

            foreach (XElement evo in evolutions)
            {
                uint id =
                    DigimonEvoEditorService.U(
                        evo.Element("digiId")?.Value);

                bool expanded =
                    state.ExpandedEvolutionId == id;

                body.Controls.Add(
                    CreateDigimonEvoEvolutionCard(
                        state,
                        evo,
                        targetWidth,
                        expanded));
            }

            body.Controls.Add(
                CreateAddEvolutionCard(
                    state,
                    targetWidth));

            body.ResumeLayout(true);
        }

        private Control CreateDigimonEvoEvolutionCard(
            DigimonEvoEditState state,
            XElement evo,
            int width,
            bool expanded)
        {
            uint id =
                DigimonEvoEditorService.U(
                    evo.Element("digiId")?.Value);

            DigimonEvoDigimonRef d =
                state.Service.ResolveDigimon(id);

            int baseHeight = 112;
            int detailsHeight = expanded ? 890 : 0;

            var card =
                new Panel
                {
                    Width = width,
                    Height = baseHeight + detailsHeight,
                    BackColor = CPanel,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 4, 0, 7)
                };

            var icon =
                new PictureBox
                {
                    Location = new Point(14, 14),
                    Size = new Size(72, 72),
                    BackColor = Color.Black,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = DigimonEvoIcon(state.Service, id)
                };

            int level =
                DigimonEvoEditorService.I(
                    evo.Element("Level")?.Value);

            var name =
                new Label
                {
                    Text = d.Name,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                    AutoSize = false,
                    AutoEllipsis = true,
                    Location = new Point(101, 14),
                    Height = 23
                };

            var meta =
                new Label
                {
                    Text =
                        $"ID {id}  •  Level/Slot {level}  •  " +
                        BuildEvolutionUnlockSummary(state.Service, evo),
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.5F),
                    AutoSize = false,
                    AutoEllipsis = true,
                    Location = new Point(102, 41),
                    Height = 20
                };

            var links =
                new Label
                {
                    Text = BuildEvolutionLinksSummary(state.Service, state.WorkingChain, evo),
                    ForeColor = Color.FromArgb(115, 225, 145),
                    Font = new Font("Segoe UI", 8.2F),
                    AutoEllipsis = true,
                    Location = new Point(102, 66),
                    Height = 32
                };

            var edit =
                CreateEditorActionButton(
                    expanded
                        ? "HIDE DETAILS"
                        : "EDIT DETAILS");

            edit.Size = new Size(118, 31);

            var remove =
                CreateEditorActionButton("REMOVE");
            remove.ForeColor = Color.FromArgb(255, 105, 105);
            remove.Size = new Size(102, 31);

            void PositionButtons()
            {
                remove.Location =
                    new Point(
                        card.ClientSize.Width - remove.Width - 14,
                        16);

                edit.Location =
                    new Point(
                        remove.Left - edit.Width - 8,
                        16);

                // Reserve the whole right side for EDIT/REMOVE so long
                // Digimon names/unlock summaries can never render under
                // the action buttons.
                int textRight =
                    Math.Max(
                        180,
                        edit.Left - 18);

                name.Width =
                    Math.Max(
                        120,
                        textRight - name.Left);

                meta.Width =
                    Math.Max(
                        120,
                        textRight - meta.Left);

                links.Width =
                    Math.Max(
                        120,
                        textRight - links.Left);
            }

            card.Resize += (_, _) => PositionButtons();

            edit.Click +=
                (_, _) =>
                {
                    state.ExpandedEvolutionId =
                        expanded
                            ? null
                            : id;

                    RenderDigimonEvoEditBody(state);
                };

            bool isRoot =
                ReferenceEquals(
                    evo,
                    state.WorkingChain.Elements("Evolution").FirstOrDefault());

            remove.Enabled = !isRoot;

            remove.Click +=
                (_, _) =>
                {
                    if (isRoot)
                        return;

                    if (MessageBox.Show(
                            $"Remove evolution {id} — {d.Name} from this tree?\r\n\r\n" +
                            "All EvolutionType links pointing to this Digimon will be cleared.",
                            "Remove Evolution",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        return;
                    }

                    RemoveEvolutionFromWorkingChain(
                        state.WorkingChain,
                        id);

                    state.Dirty = true;
                    state.ExpandedEvolutionId = null;
                    RenderDigimonEvoEditBody(state);
                };

            card.Controls.Add(icon);
            card.Controls.Add(name);
            card.Controls.Add(meta);
            card.Controls.Add(links);
            card.Controls.Add(edit);
            card.Controls.Add(remove);

            if (expanded)
            {
                Control details =
                    CreateEvolutionDetailsEditor(
                        state,
                        evo,
                        width - 28);

                details.Location =
                    new Point(
                        14,
                        baseHeight - 2);

                card.Controls.Add(details);
            }

            PositionButtons();
            return card;
        }

        private Control CreateEvolutionDetailsEditor(
            DigimonEvoEditState state,
            XElement evo,
            int width)
        {
            var panel =
                new Panel
                {
                    Width = width,
                    Height = 870,
                    BackColor = Color.FromArgb(24, 24, 24)
                };

            uint evoId =
                DigimonEvoEditorService.U(
                    evo.Element("digiId")?.Value);

            var iconHeading =
                new Label
                {
                    Text = "ICON POSITION",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(12, 10)
                };

            var iconHint =
                new Label
                {
                    Text =
                        "Controls the Digimon icon coordinates inside the evolution-tree interface.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 7.8F),
                    AutoSize = true,
                    Location = new Point(12, 31)
                };

            panel.Controls.Add(iconHeading);
            panel.Controls.Add(iconHint);

            int colGap = 12;
            int fieldWidth =
                Math.Max(
                    220,
                    (width - 36) / 2);

            Control Field(
                string title,
                int x,
                int y,
                string elementName,
                string help = "")
            {
                var host =
                    new Panel
                    {
                        Location = new Point(x, y),
                        Size = new Size(fieldWidth, 72),
                        BackColor = Color.FromArgb(28, 28, 28)
                    };

                var l =
                    new Label
                    {
                        Text = title,
                        ForeColor = CText,
                        Font = new Font("Segoe UI Semibold", 8.4F, FontStyle.Bold),
                        AutoSize = true,
                        Location = new Point(10, 7)
                    };

                var box =
                    new TextBox
                    {
                        Text = evo.Element(elementName)?.Value ?? "0",
                        BackColor = Color.FromArgb(12, 12, 12),
                        ForeColor = CText,
                        BorderStyle = BorderStyle.FixedSingle,
                        Font = new Font("Consolas", 9F),
                        Location = new Point(10, 28),
                        Width = fieldWidth - 20
                    };

                box.TextChanged +=
                    (_, _) =>
                    {
                        DigimonEvoEditorService.Set(
                            evo,
                            elementName,
                            box.Text.Trim().Length == 0 ? "0" : box.Text.Trim());

                        state.Dirty = true;
                    };

                host.Controls.Add(l);
                host.Controls.Add(box);

                if (help.Length != 0)
                    editorToolTip.SetToolTip(host, help);

                panel.Controls.Add(host);
                return host;
            }

            int left = 12;
            int right = left + fieldWidth + colGap;

            Field(
                "Icon Position X (m_IconPos)",
                left,
                52,
                "m_IconPos",
                "Horizontal position of this evolution icon in the game's evolution-tree UI.");

            Field(
                "Icon Position Y (m_IconPos2)",
                right,
                52,
                "m_IconPos2",
                "Vertical position of this evolution icon in the game's evolution-tree UI.");

            var heading =
                new Label
                {
                    Text = "UNLOCK / REQUIREMENTS",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(12, 132)
                };

            panel.Controls.Add(heading);

            Field(
                "Open Level",
                left,
                158,
                "m_nOpenLevel",
                "Digimon level required before this evolution can be opened.");

            Field(
                "Qualification Mode",
                right,
                158,
                "m_nOpenQualification",
                "Observed values in the supplied DigimonEvo.xml are 0 and 3. The tool preserves the raw value.");

            AddQuestSelector(
                panel,
                state,
                evo,
                "Open Quest",
                "m_nOpenQuest",
                left,
                238,
                fieldWidth);

            AddOpenItemSelector(
                panel,
                state,
                evo,
                "Unlock Item (ItemDisplay nItemS)",
                "m_nOpenItemTypeS",
                "m_nOpenItemNum",
                right,
                238,
                fieldWidth);

            AddDirectItemSelector(
                panel,
                state,
                evo,
                "Consumed / Use Item (direct ItemID)",
                "m_nUseItem",
                "m_nUseItemNum",
                left,
                338,
                fieldWidth);

            AddQuestSelector(
                panel,
                state,
                evo,
                "Jogress Quest Check",
                "m_nJoGressQuestCheck",
                right,
                338,
                fieldWidth);

            Field(
                "Tamer DS / Requirement Value",
                left,
                438,
                "m_nEvoTamerDS",
                "Raw DigimonEvo value. Most normal evolutions use 0 or 1; special entries contain larger values.");

            Field(
                "Evolution Tree Mode",
                right,
                438,
                "m_nEvolutionTree",
                "Observed values: 0, 2 and 3. Kept explicit because it affects special evolution-tree behavior.");

            uint currentEvolutionId =
                DigimonEvoEditorService.U(
                    evo.Element("digiId")?.Value);

            bool isJogress =
                state.Service.IsJogressEvolution(
                    currentEvolutionId,
                    evo);

            int linksY = 526;

            if (isJogress)
            {
                var jogressTitle =
                    new Label
                    {
                        Text = "JOGRESS PARTNER REQUIREMENTS",
                        ForeColor = CText,
                        Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                        AutoSize = true,
                        Location = new Point(12, 526)
                    };

                var jogressHint =
                    new Label
                    {
                        Text =
                            "Required partner Digimon for this Jogress. Rookie-type candidates are prioritised; " +
                            "existing special/Xros partner types are also preserved. Count is automatic.",
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 7.7F),
                        AutoSize = false,
                        Location = new Point(12, 548),
                        Size = new Size(width - 24, 34)
                    };

                panel.Controls.Add(jogressTitle);
                panel.Controls.Add(jogressHint);

                var jogressPanel =
                    new FlowLayoutPanel
                    {
                        Location = new Point(12, 584),
                        Size = new Size(width - 24, 104),
                        FlowDirection = FlowDirection.LeftToRight,
                        WrapContents = true,
                        AutoScroll = false,
                        BackColor = Color.FromArgb(20, 20, 20),
                        Padding = new Padding(4)
                    };

                List<uint> currentPartners =
                    Enumerable.Range(1, 4)
                        .Select(
                            index =>
                                DigimonEvoEditorService.U(
                                    evo.Element(
                                        $"m_nJoGress_Tacticses{index}")?.Value))
                        .Where(id => id != 0)
                        .ToList();

                for (int index = 1; index <= 4; ++index)
                {
                    int requirementIndex = index;

                    uint partnerId =
                        DigimonEvoEditorService.U(
                            evo.Element(
                                $"m_nJoGress_Tacticses{requirementIndex}")?.Value);

                    if (partnerId == 0)
                        continue;

                    DigimonEvoDigimonRef partner =
                        state.Service.ResolveDigimon(partnerId);

                    var requirementCard =
                        CreateEditorActionButton(
                            $"{requirementIndex}. {partner.Name}  [{partnerId}]");

                    requirementCard.Size =
                        new Size(
                            Math.Max(200, (width - 52) / 2),
                            38);

                    requirementCard.Font =
                        new Font("Segoe UI", 7.8F);

                    editorToolTip.SetToolTip(
                        requirementCard,
                        $"Required Jogress partner #{requirementIndex}\r\n" +
                        $"{partnerId} — {partner.Name}\r\n" +
                        $"Digimon_List EvolutionType={partner.EvolutionType}\r\n" +
                        "Click to replace or clear this requirement.");

                    requirementCard.Click +=
                        (_, _) =>
                            ShowJogressRequirementEditor(
                                state,
                                evo,
                                requirementIndex);

                    jogressPanel.Controls.Add(
                        requirementCard);
                }

                if (currentPartners.Count < 4)
                {
                    int firstFree =
                        Enumerable.Range(1, 4)
                            .First(
                                index =>
                                    DigimonEvoEditorService.U(
                                        evo.Element(
                                            $"m_nJoGress_Tacticses{index}")?.Value) == 0);

                    var addPartner =
                        CreateEditorActionButton(
                            "+ ADD REQUIRED PARTNER");

                    addPartner.Size =
                        new Size(
                            Math.Max(200, (width - 52) / 2),
                            38);

                    addPartner.ForeColor =
                        Color.FromArgb(115, 225, 145);

                    addPartner.Click +=
                        (_, _) =>
                            ShowJogressRequirementEditor(
                                state,
                                evo,
                                firstFree);

                    jogressPanel.Controls.Add(
                        addPartner);
                }

                var countLabel =
                    new Label
                    {
                        Text =
                            $"Automatic count: {currentPartners.Count}  •  XML m_nJoGressesNum will be kept in sync.",
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 7.5F),
                        AutoSize = true,
                        Location = new Point(12, 694)
                    };

                panel.Controls.Add(jogressPanel);
                panel.Controls.Add(countLabel);

                linksY = 726;
            }

            var linksTitle =
                new Label
                {
                    Text = "EVOLUTION LINKS",
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(12, linksY)
                };

            panel.Controls.Add(linksTitle);

            var linksPanel =
                new FlowLayoutPanel
                {
                    Location = new Point(12, linksY + 24),
                    Size = new Size(width - 24, 112),
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = false,
                    BackColor = Color.FromArgb(20, 20, 20),
                    Padding = new Padding(4)
                };

            List<XElement> visibleLinks =
                evo.Elements("EvolutionType")
                    .Take(9)
                    .Where(
                        link =>
                        {
                            int rawSlot =
                                DigimonEvoEditorService.I(
                                    link.Element("nSlot")?.Value);

                            uint target =
                                DigimonEvoEditorService.U(
                                    link.Element("dwDigimonID")?.Value);

                            return target != 0 &&
                                   rawSlot != 65537;
                        })
                    .ToList();

            foreach (XElement link in visibleLinks)
            {
                uint target =
                    DigimonEvoEditorService.U(
                        link.Element("dwDigimonID")?.Value);

                XElement? targetEvolution =
                    state.WorkingChain
                        .Elements("Evolution")
                        .FirstOrDefault(
                            candidate =>
                                DigimonEvoEditorService.U(
                                    candidate.Element("digiId")?.Value) == target);

                int automaticSlot =
                    targetEvolution == null
                        ? DigimonEvoEditorService.I(
                            link.Element("nSlot")?.Value)
                        : DigimonEvoEditorService.I(
                            targetEvolution.Element("Level")?.Value);

                var button =
                    CreateEditorActionButton(
                        $"Level {automaticSlot} → {state.Service.ResolveDigimon(target).Name}");

                int linkButtonWidth =
                    Math.Max(
                        190,
                        (width - 48) / 3);

                button.Size =
                    new Size(
                        linkButtonWidth,
                        36);

                button.Font =
                    new Font(
                        "Segoe UI",
                        7.9F);

                editorToolTip.SetToolTip(
                    button,
                    $"{target} — {state.Service.ResolveDigimon(target).Name}\r\n" +
                    $"Slot is automatic from target <Level>: {automaticSlot}");

                button.Click +=
                    (_, _) =>
                        ShowEvolutionLinkEditor(
                            state,
                            evo,
                            link);

                linksPanel.Controls.Add(
                    button);
            }

            XElement? freePhysicalLink =
                evo.Elements("EvolutionType")
                    .Take(9)
                    .FirstOrDefault(
                        candidate =>
                            DigimonEvoEditorService.I(
                                candidate.Element("nSlot")?.Value) == 0 &&
                            DigimonEvoEditorService.U(
                                candidate.Element("dwDigimonID")?.Value) == 0);

            if (freePhysicalLink != null)
            {
                var addLink =
                    CreateEditorActionButton(
                        "+ ADD EVOLUTION LINK");

                addLink.Size =
                    new Size(
                        Math.Max(
                            190,
                            (width - 48) / 3),
                        36);

                addLink.ForeColor =
                    Color.FromArgb(
                        115,
                        225,
                        145);

                editorToolTip.SetToolTip(
                    addLink,
                    "Choose one Digimon already present in this evolution line.\r\n" +
                    "The target nSlot will be filled automatically from that Digimon <Level>.");

                addLink.Click +=
                    (_, _) =>
                        ShowNewEvolutionLinkPicker(
                            state,
                            evo,
                            freePhysicalLink);

                linksPanel.Controls.Add(
                    addLink);
            }

            if (visibleLinks.Count == 0 &&
                freePhysicalLink == null)
            {
                linksPanel.Controls.Add(
                    new Label
                    {
                        Text = "No forward evolution links and no free EvolutionType record.",
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 8F),
                        AutoSize = false,
                        Size = new Size(Math.Max(220, width - 40), 34),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Margin = new Padding(5)
                    });
            }
            else if (visibleLinks.Count == 0)
            {
                linksPanel.Controls.Add(
                    new Label
                    {
                        Text = "No forward evolution links yet. Use ADD EVOLUTION LINK.",
                        ForeColor = CMuted,
                        Font = new Font("Segoe UI", 8F),
                        AutoSize = false,
                        Size = new Size(Math.Max(220, width - 40), 34),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Margin = new Padding(5)
                    });
            }

            panel.Controls.Add(linksPanel);
            return panel;
        }

        private void AddQuestSelector(
            Panel parent,
            DigimonEvoEditState state,
            XElement evo,
            string title,
            string elementName,
            int x,
            int y,
            int width)
        {
            int id =
                DigimonEvoEditorService.I(
                    evo.Element(elementName)?.Value);

            DigimonEvoQuestRef? quest =
                state.Service.ResolveQuest(id);

            var host =
                new Panel
                {
                    Location = new Point(x, y),
                    Size = new Size(width, 92),
                    BackColor = Color.FromArgb(28, 28, 28)
                };

            var label =
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 8.4F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(10, 7)
                };

            var value =
                new Label
                {
                    Text =
                        id == 0
                            ? "None"
                            : quest != null
                                ? $"{quest.Id} — {quest.Title}"
                                : $"{id} — not found in Quest.xml",
                    ForeColor =
                        id == 0
                            ? CMuted
                            : quest != null
                                ? Color.FromArgb(115, 225, 145)
                                : Color.FromArgb(255, 170, 85),
                    AutoEllipsis = true,
                    Location = new Point(10, 30),
                    Size = new Size(width - 20, 22)
                };

            var select = CreateEditorActionButton("SELECT QUEST");
            select.Location = new Point(10, 57);
            select.Size = new Size(122, 27);

            var clear = CreateEditorActionButton("CLEAR");
            clear.Location = new Point(140, 57);
            clear.Size = new Size(78, 27);

            select.Click +=
                (_, _) =>
                    ShowDigimonEvoQuestPicker(
                        state.Page,
                        state.Service,
                        title,
                        q =>
                        {
                            DigimonEvoEditorService.Set(
                                evo,
                                elementName,
                                q.Id);

                            state.Dirty = true;
                            RenderDigimonEvoEditBody(state);
                        });

            clear.Click +=
                (_, _) =>
                {
                    DigimonEvoEditorService.Set(evo, elementName, 0);
                    state.Dirty = true;
                    RenderDigimonEvoEditBody(state);
                };

            host.Controls.Add(label);
            host.Controls.Add(value);
            host.Controls.Add(select);
            host.Controls.Add(clear);
            parent.Controls.Add(host);
        }

        private void AddOpenItemSelector(
            Panel parent,
            DigimonEvoEditState state,
            XElement evo,
            string title,
            string sectionElement,
            string countElement,
            int x,
            int y,
            int width)
        {
            int section =
                DigimonEvoEditorService.I(
                    evo.Element(sectionElement)?.Value);

            int amount =
                DigimonEvoEditorService.I(
                    evo.Element(countElement)?.Value);

            DigimonEvoItemRef? item =
                state.Service.ResolveOpenItem(section);

            var host =
                new Panel
                {
                    Location = new Point(x, y),
                    Size = new Size(width, 92),
                    BackColor = Color.FromArgb(28, 28, 28)
                };

            var icon =
                new PictureBox
                {
                    Location = new Point(10, 30),
                    Size = new Size(44, 44),
                    BackColor = Color.Black,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = item == null ? null : DigimonEvoItemIcon(item.IconId)
                };

            var label =
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 8.4F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(10, 7)
                };

            var value =
                new Label
                {
                    Text =
                        section == 0
                            ? "None"
                            : item != null
                                ? $"{section} → ItemID {item.ItemId} — {item.Name}  ×{amount}"
                                : $"{section} — not resolved by ItemDisplay.xml",
                    ForeColor =
                        section == 0
                            ? CMuted
                            : item != null
                                ? Color.FromArgb(115, 225, 145)
                                : Color.FromArgb(255, 170, 85),
                    AutoEllipsis = true,
                    Location = new Point(61, 29),
                    Size = new Size(Math.Max(100, width - 240), 42)
                };

            var select = CreateEditorActionButton("SELECT ITEM");
            select.Size = new Size(112, 27);
            select.Location = new Point(width - 122, 28);

            var amountBox =
                new TextBox
                {
                    Text = amount.ToString(CultureInfo.InvariantCulture),
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(width - 122, 60),
                    Width = 54
                };

            var clear = CreateEditorActionButton("CLEAR");
            clear.Size = new Size(60, 27);
            clear.Location = new Point(width - 64, 58);

            select.Click +=
                (_, _) =>
                    ShowDigimonEvoOpenItemPicker(
                        state.Page,
                        state.Service,
                        itemRef =>
                        {
                            DigimonEvoEditorService.Set(
                                evo,
                                sectionElement,
                                itemRef.Section);

                            if (DigimonEvoEditorService.I(
                                    evo.Element(countElement)?.Value) <= 0)
                            {
                                DigimonEvoEditorService.Set(evo, countElement, 1);
                            }

                            state.Dirty = true;
                            RenderDigimonEvoEditBody(state);
                        });

            amountBox.TextChanged +=
                (_, _) =>
                {
                    DigimonEvoEditorService.Set(
                        evo,
                        countElement,
                        amountBox.Text.Trim().Length == 0
                            ? "0"
                            : amountBox.Text.Trim());

                    state.Dirty = true;
                };

            clear.Click +=
                (_, _) =>
                {
                    DigimonEvoEditorService.Set(evo, sectionElement, 0);
                    DigimonEvoEditorService.Set(evo, countElement, 0);
                    state.Dirty = true;
                    RenderDigimonEvoEditBody(state);
                };

            host.Controls.Add(label);
            host.Controls.Add(icon);
            host.Controls.Add(value);
            host.Controls.Add(select);
            host.Controls.Add(amountBox);
            host.Controls.Add(clear);
            parent.Controls.Add(host);
        }

        private void AddDirectItemSelector(
            Panel parent,
            DigimonEvoEditState state,
            XElement evo,
            string title,
            string itemElement,
            string countElement,
            int x,
            int y,
            int width)
        {
            uint itemId =
                DigimonEvoEditorService.U(
                    evo.Element(itemElement)?.Value);

            int amount =
                DigimonEvoEditorService.I(
                    evo.Element(countElement)?.Value);

            DigimonEvoItemRef? item =
                state.Service.ResolveItem(itemId);

            var host =
                new Panel
                {
                    Location = new Point(x, y),
                    Size = new Size(width, 92),
                    BackColor = Color.FromArgb(28, 28, 28)
                };

            var label =
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 8.4F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(10, 7)
                };

            var icon =
                new PictureBox
                {
                    Location = new Point(10, 30),
                    Size = new Size(44, 44),
                    BackColor = Color.Black,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = item == null ? null : DigimonEvoItemIcon(item.IconId)
                };

            var value =
                new Label
                {
                    Text =
                        itemId == 0
                            ? "None"
                            : item != null
                                ? $"{item.ItemId} — {item.Name}  ×{amount}"
                                : $"{itemId} — not found in ItemList.xml",
                    ForeColor =
                        itemId == 0
                            ? CMuted
                            : item != null
                                ? Color.FromArgb(115, 225, 145)
                                : Color.FromArgb(255, 170, 85),
                    AutoEllipsis = true,
                    Location = new Point(61, 29),
                    Size = new Size(Math.Max(100, width - 240), 42)
                };

            var select = CreateEditorActionButton("SELECT ITEM");
            select.Size = new Size(112, 27);
            select.Location = new Point(width - 122, 28);

            var amountBox =
                new TextBox
                {
                    Text = amount.ToString(CultureInfo.InvariantCulture),
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(width - 122, 60),
                    Width = 54
                };

            var clear = CreateEditorActionButton("CLEAR");
            clear.Size = new Size(60, 27);
            clear.Location = new Point(width - 64, 58);

            select.Click +=
                (_, _) =>
                    ShowDigimonEvoDirectItemPicker(
                        state.Page,
                        state.Service,
                        selected =>
                        {
                            DigimonEvoEditorService.Set(evo, itemElement, selected.ItemId);

                            if (DigimonEvoEditorService.I(
                                    evo.Element(countElement)?.Value) <= 0)
                            {
                                DigimonEvoEditorService.Set(evo, countElement, 1);
                            }

                            state.Dirty = true;
                            RenderDigimonEvoEditBody(state);
                        });

            amountBox.TextChanged +=
                (_, _) =>
                {
                    DigimonEvoEditorService.Set(
                        evo,
                        countElement,
                        amountBox.Text.Trim().Length == 0
                            ? "0"
                            : amountBox.Text.Trim());

                    state.Dirty = true;
                };

            clear.Click +=
                (_, _) =>
                {
                    DigimonEvoEditorService.Set(evo, itemElement, 0);
                    DigimonEvoEditorService.Set(evo, countElement, 0);
                    state.Dirty = true;
                    RenderDigimonEvoEditBody(state);
                };

            host.Controls.Add(label);
            host.Controls.Add(icon);
            host.Controls.Add(value);
            host.Controls.Add(select);
            host.Controls.Add(amountBox);
            host.Controls.Add(clear);
            parent.Controls.Add(host);
        }

        private Control CreateAddEvolutionCard(
            DigimonEvoEditState state,
            int width)
        {
            var card =
                new Button
                {
                    Text = "+  ADD NEW EVOLUTION",
                    Width = width,
                    Height = 68,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(25, 25, 25),
                    ForeColor = Color.FromArgb(115, 225, 145),
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 4, 0, 24)
                };

            card.FlatAppearance.BorderColor =
                Color.FromArgb(70, 95, 75);

            card.FlatAppearance.MouseOverBackColor =
                Color.FromArgb(32, 42, 34);

            card.Click +=
                (_, _) =>
                    ShowAddEvolutionWizard(state);

            return card;
        }

        private void ShowAddEvolutionWizard(
            DigimonEvoEditState state)
        {
            ShowDigimonEvoDigimonPicker(
                state.Page,
                state.Service,
                "Select New Evolution Digimon",
                selected =>
                {
                    List<XElement> current =
                        state.WorkingChain.Elements("Evolution").ToList();

                    if (current.Any(
                            x =>
                                DigimonEvoEditorService.U(
                                    x.Element("digiId")?.Value) == selected.Id))
                    {
                        MessageBox.Show(
                            $"{selected.Id} — {selected.Name} already exists in this tree.",
                            "Add Evolution",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);

                        return;
                    }

                    ShowEvolutionParentPicker(
                        state,
                        selected);
                });
        }

        private void ShowEvolutionParentPicker(
            DigimonEvoEditState state,
            DigimonEvoDigimonRef target)
        {
            var overlay =
                CreateEvoOverlay(
                    state.Page,
                    $"Add {target.Id} — {target.Name}",
                    out Panel content,
                    out Action close);

            var info =
                new Label
                {
                    Text =
                        "Select the evolution that will lead to this Digimon.\r\n" +
                        "EvolutionType nSlot is automatic and always uses the new evolution <Level>. " +
                        "CountEvo and the 65537 root back-link are also automatic.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 9F),
                    AutoSize = false,
                    Location = new Point(20, 58),
                    Size = new Size(690, 45)
                };

            var parents =
                new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = Color.FromArgb(18, 18, 18),
                    ForeColor = CText,
                    FlatStyle = FlatStyle.Flat,
                    Location = new Point(20, 118),
                    Width = 420
                };

            foreach (XElement evo in state.WorkingChain.Elements("Evolution"))
            {
                uint id =
                    DigimonEvoEditorService.U(
                        evo.Element("digiId")?.Value);

                parents.Items.Add(
                    new EvoChoice(
                        id,
                        $"{id} — {state.Service.ResolveDigimon(id).Name}"));
            }

            if (parents.Items.Count > 0)
                parents.SelectedIndex = parents.Items.Count - 1;

            var add = CreateEditorActionButton("ADD EVOLUTION");
            add.Location = new Point(545, 114);
            add.Size = new Size(145, 32);

            add.Click +=
                (_, _) =>
                {
                    if (parents.SelectedItem is not EvoChoice parent)
                        return;

                    try
                    {
                        AddEvolutionToWorkingChain(
                            state,
                            (uint)parent.Value,
                            target.Id);

                        state.Dirty = true;
                        close();
                        RenderDigimonEvoEditBody(state);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            ex.Message,
                            "Add Evolution",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };

            content.Controls.Add(info);
            content.Controls.Add(parents);
            content.Controls.Add(add);

            overlay.BringToFront();
        }

        private void ShowNewEvolutionLinkPicker(
            DigimonEvoEditState state,
            XElement sourceEvolution,
            XElement freeLink)
        {
            uint sourceId =
                DigimonEvoEditorService.U(
                    sourceEvolution.Element("digiId")?.Value);

            HashSet<uint> alreadyLinked =
                sourceEvolution
                    .Elements("EvolutionType")
                    .Take(9)
                    .Where(
                        link =>
                            DigimonEvoEditorService.I(
                                link.Element("nSlot")?.Value) != 65537)
                    .Select(
                        link =>
                            DigimonEvoEditorService.U(
                                link.Element("dwDigimonID")?.Value))
                    .Where(id => id != 0)
                    .ToHashSet();

            var overlay =
                CreateEvoOverlay(
                    state.Page,
                    "Add Evolution Link",
                    out Panel content,
                    out Action close);

            var hint =
                new Label
                {
                    Text =
                        "Select which Digimon from THIS evolution line this evolution can go to.\r\n" +
                        "The link Slot is automatic: nSlot = selected target <Level>.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.8F),
                    AutoSize = false,
                    Location = new Point(20, 58),
                    Size = new Size(690, 44)
                };

            var search =
                new TextBox
                {
                    PlaceholderText = "Search Digimon ID or name inside this evolution line...",
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(20, 108),
                    Width = 700
                };

            var results =
                new FlowLayoutPanel
                {
                    Location = new Point(20, 142),
                    Size = new Size(700, 388),
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(20, 20, 20),
                    Padding = new Padding(4, 4, 12, 24)
                };

            DarkUi.ApplyDarkScrollBar(
                results);

            void Render()
            {
                results.SuspendLayout();

                foreach (Control control in results.Controls.Cast<Control>().ToArray())
                {
                    results.Controls.Remove(control);
                    control.Dispose();
                }

                string query =
                    search.Text.Trim();

                List<XElement> candidates =
                    state.WorkingChain
                        .Elements("Evolution")
                        .Where(
                            candidate =>
                            {
                                uint candidateId =
                                    DigimonEvoEditorService.U(
                                        candidate.Element("digiId")?.Value);

                                if (candidateId == 0 ||
                                    candidateId == sourceId ||
                                    alreadyLinked.Contains(candidateId))
                                {
                                    return false;
                                }

                                if (query.Length == 0)
                                    return true;

                                DigimonEvoDigimonRef digimon =
                                    state.Service.ResolveDigimon(
                                        candidateId);

                                return
                                    candidateId
                                        .ToString(CultureInfo.InvariantCulture)
                                        .Contains(
                                            query,
                                            StringComparison.OrdinalIgnoreCase) ||
                                    digimon.Name.Contains(
                                        query,
                                        StringComparison.OrdinalIgnoreCase);
                            })
                        .OrderBy(
                            candidate =>
                                DigimonEvoEditorService.I(
                                    candidate.Element("Level")?.Value))
                        .ThenBy(
                            candidate =>
                                state.Service.ResolveDigimon(
                                    DigimonEvoEditorService.U(
                                        candidate.Element("digiId")?.Value)).Name,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                foreach (XElement candidate in candidates)
                {
                    uint targetId =
                        DigimonEvoEditorService.U(
                            candidate.Element("digiId")?.Value);

                    int targetLevel =
                        DigimonEvoEditorService.I(
                            candidate.Element("Level")?.Value);

                    DigimonEvoDigimonRef digimon =
                        state.Service.ResolveDigimon(
                            targetId);

                    var card =
                        new Panel
                        {
                            Width = 660,
                            Height = 72,
                            BackColor = CPanel,
                            BorderStyle = BorderStyle.FixedSingle,
                            Margin = new Padding(2, 3, 2, 4)
                        };

                    var icon =
                        new PictureBox
                        {
                            Location = new Point(8, 8),
                            Size = new Size(54, 54),
                            BackColor = Color.Black,
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Image = DigimonEvoIcon(
                                state.Service,
                                targetId)
                        };

                    var name =
                        new Label
                        {
                            Text =
                                $"{targetId} — {digimon.Name}\r\n" +
                                $"Level {targetLevel}  •  Automatic nSlot {targetLevel}",
                            ForeColor = CText,
                            Font = new Font("Segoe UI", 8.6F),
                            Location = new Point(72, 10),
                            Size = new Size(430, 48)
                        };

                    var choose =
                        CreateEditorActionButton(
                            "SELECT");

                    choose.Location =
                        new Point(
                            548,
                            20);

                    choose.Size =
                        new Size(
                            92,
                            31);

                    choose.Click +=
                        (_, _) =>
                        {
                            if (targetLevel <= 0)
                            {
                                MessageBox.Show(
                                    $"Digimon {targetId} has invalid <Level> {targetLevel}.",
                                    "Evolution Link",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);

                                return;
                            }

                            DigimonEvoEditorService.Set(
                                freeLink,
                                "dwDigimonID",
                                targetId);

                            DigimonEvoEditorService.Set(
                                freeLink,
                                "nSlot",
                                targetLevel);

                            state.Dirty = true;
                            close();
                            RenderDigimonEvoEditBody(
                                state);
                        };

                    card.Controls.Add(icon);
                    card.Controls.Add(name);
                    card.Controls.Add(choose);
                    results.Controls.Add(card);
                }

                if (candidates.Count == 0)
                {
                    results.Controls.Add(
                        new Label
                        {
                            Text =
                                "No available Digimon in this evolution line for a new link.",
                            ForeColor = CMuted,
                            Font = new Font("Segoe UI", 8.5F),
                            AutoSize = false,
                            Width = 640,
                            Height = 40,
                            TextAlign = ContentAlignment.MiddleCenter
                        });
                }

                results.ResumeLayout(
                    true);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            content.Controls.Add(hint);
            content.Controls.Add(search);
            content.Controls.Add(results);

            Render();
            overlay.BringToFront();
        }

        private void ShowEvolutionLinkEditor(
            DigimonEvoEditState state,
            XElement evo,
            XElement link)
        {
            uint currentTarget =
                DigimonEvoEditorService.U(
                    link.Element("dwDigimonID")?.Value);

            var overlay =
                CreateEvoOverlay(
                    state.Page,
                    "Edit Evolution Link",
                    out Panel content,
                    out Action close);

            var explanation =
                new Label
                {
                    Text =
                        "Only Digimon already present in this evolution tree can be linked.\r\n" +
                        "nSlot is not editable: it is automatically copied from the selected Digimon <Level>.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.8F),
                    AutoSize = false,
                    Location = new Point(20, 62),
                    Size = new Size(650, 44)
                };

            uint selectedTarget =
                currentTarget;

            var target =
                new Label
                {
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                    Location = new Point(20, 116),
                    Size = new Size(480, 30),
                    TextAlign = ContentAlignment.MiddleLeft
                };

            var slotInfo =
                new Label
                {
                    ForeColor = Color.FromArgb(115, 225, 145),
                    Font = new Font("Segoe UI", 8.5F),
                    Location = new Point(20, 148),
                    Size = new Size(480, 26),
                    TextAlign = ContentAlignment.MiddleLeft
                };

            void RefreshSelection()
            {
                if (selectedTarget == 0)
                {
                    target.Text =
                        "No target Digimon";

                    slotInfo.Text =
                        "Automatic Slot: 0";

                    return;
                }

                DigimonEvoDigimonRef selected =
                    state.Service.ResolveDigimon(
                        selectedTarget);

                XElement? selectedEvolution =
                    state.WorkingChain
                        .Elements("Evolution")
                        .FirstOrDefault(
                            candidate =>
                                DigimonEvoEditorService.U(
                                    candidate.Element("digiId")?.Value) ==
                                selectedTarget);

                int level =
                    selectedEvolution == null
                        ? 0
                        : DigimonEvoEditorService.I(
                            selectedEvolution.Element("Level")?.Value);

                target.Text =
                    $"{selectedTarget} — {selected.Name}";

                slotInfo.Text =
                    $"Automatic Slot = target <Level> = {level}";
            }

            var select =
                CreateEditorActionButton(
                    "SELECT FROM TREE");

            select.Location =
                new Point(
                    520,
                    116);

            select.Size =
                new Size(
                    150,
                    31);

            var clear =
                CreateEditorActionButton(
                    "CLEAR LINK");

            clear.Location =
                new Point(
                    520,
                    154);

            clear.Size =
                new Size(
                    150,
                    31);

            select.Click +=
                (_, _) =>
                    ShowEvolutionTreeDigimonPicker(
                        state,
                        evo,
                        d =>
                        {
                            selectedTarget = d.Id;
                            RefreshSelection();
                        },
                        keepExistingOverlay: overlay);

            clear.Click +=
                (_, _) =>
                {
                    selectedTarget = 0;
                    RefreshSelection();
                };

            var save =
                CreateEditorActionButton(
                    "APPLY");

            save.Location =
                new Point(
                    20,
                    196);

            save.Size =
                new Size(
                    105,
                    32);

            save.Click +=
                (_, _) =>
                {
                    int automaticSlot = 0;

                    if (selectedTarget != 0)
                    {
                        XElement? targetEvolution =
                            state.WorkingChain
                                .Elements("Evolution")
                                .FirstOrDefault(
                                    candidate =>
                                        DigimonEvoEditorService.U(
                                            candidate.Element("digiId")?.Value) ==
                                        selectedTarget);

                        if (targetEvolution == null)
                        {
                            MessageBox.Show(
                                "The selected Digimon is not present in this evolution tree.",
                                "Evolution Link",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);

                            return;
                        }

                        automaticSlot =
                            DigimonEvoEditorService.I(
                                targetEvolution.Element("Level")?.Value);

                        if (automaticSlot <= 0)
                        {
                            MessageBox.Show(
                                "The selected evolution has an invalid <Level>.",
                                "Evolution Link",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);

                            return;
                        }
                    }

                    DigimonEvoEditorService.Set(
                        link,
                        "nSlot",
                        automaticSlot);

                    DigimonEvoEditorService.Set(
                        link,
                        "dwDigimonID",
                        selectedTarget);

                    state.Dirty = true;
                    close();
                    RenderDigimonEvoEditBody(state);
                };

            content.Controls.Add(explanation);
            content.Controls.Add(target);
            content.Controls.Add(slotInfo);
            content.Controls.Add(select);
            content.Controls.Add(clear);
            content.Controls.Add(save);

            RefreshSelection();
            overlay.BringToFront();
        }

        private void ShowJogressRequirementEditor(
            DigimonEvoEditState state,
            XElement evo,
            int requirementIndex)
        {
            requirementIndex =
                Math.Clamp(
                    requirementIndex,
                    1,
                    4);

            string field =
                $"m_nJoGress_Tacticses{requirementIndex}";

            uint currentId =
                DigimonEvoEditorService.U(
                    evo.Element(field)?.Value);

            var overlay =
                CreateEvoOverlay(
                    state.Page,
                    $"Jogress Required Partner #{requirementIndex}",
                    out Panel content,
                    out Action close);

            var hint =
                new Label
                {
                    Text =
                        "Select the Rookie/partner Digimon that must be present for this Jogress. " +
                        "The editor prioritises Digimon_List EvolutionType 3 (Rookie) and also keeps " +
                        "special partner types already used by the original DigimonEvo.xml.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8.6F),
                    AutoSize = false,
                    Location = new Point(20, 58),
                    Size = new Size(690, 54)
                };

            var search =
                new TextBox
                {
                    PlaceholderText = "Search Rookie/partner Digimon ID or name...",
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(20, 120),
                    Width = 700
                };

            var results =
                new FlowLayoutPanel
                {
                    Location = new Point(20, 154),
                    Size = new Size(700, 330),
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(20, 20, 20),
                    Padding = new Padding(4, 4, 12, 24)
                };

            DarkUi.ApplyDarkScrollBar(results);

            var clear =
                CreateEditorActionButton(
                    currentId == 0
                        ? "NO PARTNER SET"
                        : "CLEAR REQUIREMENT");

            clear.Location =
                new Point(
                    20,
                    496);

            clear.Size =
                new Size(
                    160,
                    31);

            clear.Enabled =
                currentId != 0;

            clear.Click +=
                (_, _) =>
                {
                    DigimonEvoEditorService.Set(
                        evo,
                        field,
                        0);

                    SyncJogressRequirementCount(
                        evo);

                    state.Dirty = true;
                    close();
                    RenderDigimonEvoEditBody(state);
                };

            void Render()
            {
                results.SuspendLayout();

                foreach (Control control in results.Controls.Cast<Control>().ToArray())
                {
                    results.Controls.Remove(control);
                    control.Dispose();
                }

                foreach (DigimonEvoDigimonRef digimon in
                    state.Service.SearchJogressPartners(search.Text, 120))
                {
                    var card =
                        new Panel
                        {
                            Width = 660,
                            Height = 68,
                            BackColor = CPanel,
                            BorderStyle = BorderStyle.FixedSingle,
                            Margin = new Padding(2, 3, 2, 4)
                        };

                    var pic =
                        new PictureBox
                        {
                            Location = new Point(8, 8),
                            Size = new Size(50, 50),
                            BackColor = Color.Black,
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Image = DigimonEvoIcon(state.Service, digimon.Id)
                        };

                    var text =
                        new Label
                        {
                            Text =
                                $"{digimon.Id} — {digimon.Name}\r\n" +
                                $"Digimon_List EvolutionType {digimon.EvolutionType}" +
                                (digimon.EvolutionType == 3
                                    ? " — Rookie"
                                    : " — existing special Jogress/Xros partner"),
                            ForeColor = CText,
                            Font = new Font("Segoe UI", 8.4F),
                            Location = new Point(70, 10),
                            Size = new Size(440, 46)
                        };

                    var choose =
                        CreateEditorActionButton(
                            "SELECT");

                    choose.Location =
                        new Point(
                            548,
                            18);

                    choose.Size =
                        new Size(
                            92,
                            31);

                    choose.Click +=
                        (_, _) =>
                        {
                            DigimonEvoEditorService.Set(
                                evo,
                                field,
                                digimon.Id);

                            SyncJogressRequirementCount(
                                evo);

                            state.Dirty = true;
                            close();
                            RenderDigimonEvoEditBody(state);
                        };

                    card.Controls.Add(pic);
                    card.Controls.Add(text);
                    card.Controls.Add(choose);
                    results.Controls.Add(card);
                }

                results.ResumeLayout(true);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            content.Controls.Add(hint);
            content.Controls.Add(search);
            content.Controls.Add(results);
            content.Controls.Add(clear);

            Render();
            overlay.BringToFront();
        }

        private static void SyncJogressRequirementCount(
            XElement evo)
        {
            int count =
                Enumerable.Range(1, 4)
                    .Count(
                        index =>
                            DigimonEvoEditorService.U(
                                evo.Element(
                                    $"m_nJoGress_Tacticses{index}")?.Value) != 0);

            DigimonEvoEditorService.Set(
                evo,
                "m_nJoGressesNum",
                count);
        }

        private sealed class EvoChoice
        {
            public int Value { get; }
            public string Text { get; }

            public EvoChoice(uint value, string text)
            {
                Value = unchecked((int)value);
                Text = text;
            }

            public override string ToString() => Text;
        }

        private Panel CreateEvoOverlay(
            TabPage page,
            string title,
            out Panel content,
            out Action close)
        {
            var overlay =
                new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18, 18, 18)
                };

            var contentPanel =
                new Panel
                {
                    Anchor = AnchorStyles.None,
                    Size = new Size(740, 560),
                    BackColor = CPanel,
                    BorderStyle = BorderStyle.FixedSingle
                };

            var heading =
                new Label
                {
                    Text = title,
                    ForeColor = CText,
                    Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(20, 18)
                };

            var closeButton = CreateEditorActionButton("CLOSE");
            closeButton.Size = new Size(92, 32);

            void LayoutOverlay()
            {
                contentPanel.Location =
                    new Point(
                        Math.Max(
                            8,
                            (overlay.ClientSize.Width - contentPanel.Width) / 2),
                        Math.Max(
                            8,
                            (overlay.ClientSize.Height - contentPanel.Height) / 2));

                closeButton.Location =
                    new Point(
                        contentPanel.Width - closeButton.Width - 16,
                        12);
            }

            void CloseOverlay()
            {
                if (overlay.Parent != null)
                    overlay.Parent.Controls.Remove(overlay);

                overlay.Dispose();
            }

            overlay.Resize += (_, _) => LayoutOverlay();
            closeButton.Click += (_, _) => CloseOverlay();

            contentPanel.Controls.Add(heading);
            contentPanel.Controls.Add(closeButton);
            overlay.Controls.Add(contentPanel);
            page.Controls.Add(overlay);

            LayoutOverlay();
            overlay.BringToFront();

            content = contentPanel;
            close = CloseOverlay;

            return overlay;
        }

        private void ShowDigimonEvoDigimonPicker(
            TabPage page,
            DigimonEvoEditorService service,
            string title,
            Action<DigimonEvoDigimonRef> selected,
            Control? keepExistingOverlay = null)
        {
            var overlay =
                CreateEvoOverlay(
                    page,
                    title,
                    out Panel content,
                    out Action close);

            var search =
                new TextBox
                {
                    PlaceholderText = "Search Digimon ID or name...",
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(20, 58),
                    Width = 700
                };

            var results =
                new FlowLayoutPanel
                {
                    Location = new Point(20, 92),
                    Size = new Size(700, 448),
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(20, 20, 20),
                    Padding = new Padding(4, 4, 12, 24)
                };

            DarkUi.ApplyDarkScrollBar(results);

            void Render()
            {
                results.SuspendLayout();

                foreach (Control c in results.Controls.Cast<Control>().ToArray())
                {
                    results.Controls.Remove(c);
                    c.Dispose();
                }

                foreach (DigimonEvoDigimonRef d in service.SearchDigimons(search.Text, 100))
                {
                    var card =
                        new Panel
                        {
                            Width = 660,
                            Height = 70,
                            BackColor = CPanel,
                            BorderStyle = BorderStyle.FixedSingle,
                            Margin = new Padding(2, 3, 2, 4)
                        };

                    var icon =
                        new PictureBox
                        {
                            Location = new Point(8, 8),
                            Size = new Size(52, 52),
                            BackColor = Color.Black,
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Image = DigimonEvoIcon(service, d.Id)
                        };

                    var label =
                        new Label
                        {
                            Text =
                                $"{d.Id} — {d.Name}\r\nEvolutionType {d.EvolutionType}  •  BaseLevel {d.BaseLevel}",
                            ForeColor = CText,
                            Font = new Font("Segoe UI", 8.6F),
                            Location = new Point(72, 10),
                            Size = new Size(440, 48)
                        };

                    var choose =
                        CreateEditorActionButton("SELECT");
                    choose.Location = new Point(548, 18);
                    choose.Size = new Size(92, 32);

                    choose.Click +=
                        (_, _) =>
                        {
                            close();
                            if (keepExistingOverlay != null &&
                                !keepExistingOverlay.IsDisposed)
                            {
                                keepExistingOverlay.BringToFront();
                            }

                            selected(d);
                        };

                    card.Controls.Add(icon);
                    card.Controls.Add(label);
                    card.Controls.Add(choose);
                    results.Controls.Add(card);
                }

                results.ResumeLayout(true);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            content.Controls.Add(search);
            content.Controls.Add(results);
            Render();
            overlay.BringToFront();
        }

        private void ShowDigimonEvoQuestPicker(
            TabPage page,
            DigimonEvoEditorService service,
            string title,
            Action<DigimonEvoQuestRef> selected)
        {
            ShowDigimonEvoSimplePicker(
                page,
                title,
                "Search Quest ID or title...",
                q => service.SearchQuests(q, 100),
                q => $"{q.Id} — {q.Title}",
                q => $"Quest Level {q.Level}",
                _ => null,
                selected);
        }

        private void ShowDigimonEvoOpenItemPicker(
            TabPage page,
            DigimonEvoEditorService service,
            Action<DigimonEvoItemRef> selected)
        {
            ShowDigimonEvoSimplePicker(
                page,
                "Select Evolution Unlock Item",
                "Search nItemS, ItemID or item name...",
                q => service.SearchOpenItems(q, 100),
                x => $"{x.Section} → {x.ItemId} — {x.Name}",
                x => $"DigimonEvo stores nItemS {x.Section}; ItemDisplay resolves it to ItemID {x.ItemId}.",
                x => DigimonEvoItemIcon(x.IconId),
                selected);
        }

        private void ShowDigimonEvoDirectItemPicker(
            TabPage page,
            DigimonEvoEditorService service,
            Action<DigimonEvoItemRef> selected)
        {
            ShowDigimonEvoSimplePicker(
                page,
                "Select Direct Use Item",
                "Search ItemID or item name...",
                q => service.SearchItems(q, 100),
                x => $"{x.ItemId} — {x.Name}",
                x => $"ItemList Section {x.Section}",
                x => DigimonEvoItemIcon(x.IconId),
                selected);
        }

        private void ShowDigimonEvoSimplePicker<T>(
            TabPage page,
            string title,
            string placeholder,
            Func<string?, IReadOnlyList<T>> searchFunction,
            Func<T, string> titleFunction,
            Func<T, string> detailFunction,
            Func<T, Bitmap?> iconFunction,
            Action<T> selected)
        {
            var overlay =
                CreateEvoOverlay(
                    page,
                    title,
                    out Panel content,
                    out Action close);

            var search =
                new TextBox
                {
                    PlaceholderText = placeholder,
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(20, 58),
                    Width = 700
                };

            var results =
                new FlowLayoutPanel
                {
                    Location = new Point(20, 92),
                    Size = new Size(700, 448),
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(20, 20, 20),
                    Padding = new Padding(4, 4, 12, 24)
                };

            DarkUi.ApplyDarkScrollBar(results);

            void Render()
            {
                results.SuspendLayout();

                foreach (Control c in results.Controls.Cast<Control>().ToArray())
                {
                    results.Controls.Remove(c);
                    c.Dispose();
                }

                foreach (T item in searchFunction(search.Text))
                {
                    var card =
                        new Panel
                        {
                            Width = 660,
                            Height = 66,
                            BackColor = CPanel,
                            BorderStyle = BorderStyle.FixedSingle,
                            Margin = new Padding(2, 3, 2, 4)
                        };

                    Bitmap? bitmap = iconFunction(item);

                    int textX = 12;

                    if (bitmap != null)
                    {
                        var pic =
                            new PictureBox
                            {
                                Location = new Point(8, 8),
                                Size = new Size(48, 48),
                                BackColor = Color.Black,
                                SizeMode = PictureBoxSizeMode.Zoom,
                                Image = bitmap
                            };

                        card.Controls.Add(pic);
                        textX = 68;
                    }

                    var name =
                        new Label
                        {
                            Text = titleFunction(item),
                            ForeColor = CText,
                            Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold),
                            AutoEllipsis = true,
                            Location = new Point(textX, 10),
                            Size = new Size(440, 21)
                        };

                    var detail =
                        new Label
                        {
                            Text = detailFunction(item),
                            ForeColor = CMuted,
                            Font = new Font("Segoe UI", 7.7F),
                            AutoEllipsis = true,
                            Location = new Point(textX, 34),
                            Size = new Size(440, 20)
                        };

                    var choose =
                        CreateEditorActionButton("SELECT");
                    choose.Location = new Point(548, 17);
                    choose.Size = new Size(92, 31);

                    choose.Click +=
                        (_, _) =>
                        {
                            close();
                            selected(item);
                        };

                    card.Controls.Add(name);
                    card.Controls.Add(detail);
                    card.Controls.Add(choose);
                    results.Controls.Add(card);
                }

                results.ResumeLayout(true);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 160
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            content.Controls.Add(search);
            content.Controls.Add(results);
            Render();
            overlay.BringToFront();
        }

        private void AddEvolutionToWorkingChain(
            DigimonEvoEditState state,
            uint parentId,
            uint targetId)
        {
            List<XElement> evolutions =
                state.WorkingChain.Elements("Evolution").ToList();

            XElement parent =
                evolutions.First(
                    x =>
                        DigimonEvoEditorService.U(
                            x.Element("digiId")?.Value) == parentId);

            XElement template =
                new XElement(
                    evolutions.Last());

            int level =
                evolutions
                    .Select(
                        x =>
                            DigimonEvoEditorService.I(
                                x.Element("Level")?.Value))
                    .DefaultIfEmpty(0)
                    .Max() + 1;

            ResetWorkingEvolution(
                template,
                targetId,
                state.RootId,
                level,
                state.Service.ResolveDigimon(targetId).BaseLevel);

            XElement? free =
                parent.Elements("EvolutionType")
                    .FirstOrDefault(
                        x =>
                            DigimonEvoEditorService.I(
                                x.Element("nSlot")?.Value) == 0 &&
                            DigimonEvoEditorService.U(
                                x.Element("dwDigimonID")?.Value) == 0);

            if (free == null)
                throw new InvalidOperationException(
                    "The selected parent has no free EvolutionType slot.");

            // EvolutionType.nSlot is the target evolution layer.
            // In this XML that layer is exactly the target <Level>.
            DigimonEvoEditorService.Set(free, "nSlot", level);
            DigimonEvoEditorService.Set(free, "dwDigimonID", targetId);

            state.WorkingChain.Add(template);
            DigimonEvoEditorService.Set(
                state.WorkingChain,
                "CountEvo",
                state.WorkingChain.Elements("Evolution").Count());
        }

        private static void ResetWorkingEvolution(
            XElement evo,
            uint id,
            uint rootId,
            int level,
            int baseLevel)
        {
            DigimonEvoEditorService.Set(evo, "digiId", id);
            DigimonEvoEditorService.Set(evo, "Level", level);
            DigimonEvoEditorService.Set(evo, "nType", 0);
            DigimonEvoEditorService.Set(evo, "uShort1", 0);

            List<XElement> links =
                evo.Elements("EvolutionType").Take(9).ToList();

            foreach (XElement l in links)
            {
                DigimonEvoEditorService.Set(l, "nSlot", 0);
                DigimonEvoEditorService.Set(l, "dwDigimonID", 0);
            }

            if (links.Count != 0)
            {
                XElement back = links.Last();
                DigimonEvoEditorService.Set(back, "nSlot", 65537);
                DigimonEvoEditorService.Set(back, "dwDigimonID", rootId);
            }

            foreach (string field in new[]
            {
                "m_nOpenQualification","m_nOpenQuest","m_nOpenItemTypeS",
                "m_nOpenItemNum","m_nUseItem","m_nUseItemNum","m_nIntimacy",
                "m_nOpenCrest","m_EvoCard1","m_EvoCard2","m_EvoCard3",
                "m_nEvoDigimental","m_nEvoTamerDS","m_nEvolutionTree",
                "m_nJoGressQuestCheck","m_nChipsetType","m_nChipsetTypeC",
                "m_nChipsetNum","m_nChipsetTypeP","m_nJoGressesNum","unknow1",
                "m_nJoGress_Tacticses1","m_nJoGress_Tacticses2",
                "m_nJoGress_Tacticses3","m_nJoGress_Tacticses4"
            })
            {
                DigimonEvoEditorService.Set(evo, field, 0);
            }

            DigimonEvoEditorService.Set(evo, "m_nEnableSlot", 1);
            DigimonEvoEditorService.Set(evo, "m_nOpenLevel", Math.Max(1, baseLevel));
        }

        private static void RemoveEvolutionFromWorkingChain(
            XElement chain,
            uint id)
        {
            XElement? target =
                chain.Elements("Evolution")
                    .FirstOrDefault(
                        x =>
                            DigimonEvoEditorService.U(
                                x.Element("digiId")?.Value) == id);

            if (target == null)
                return;

            foreach (XElement evo in chain.Elements("Evolution"))
            {
                foreach (XElement link in evo.Elements("EvolutionType"))
                {
                    if (DigimonEvoEditorService.U(
                            link.Element("dwDigimonID")?.Value) == id)
                    {
                        DigimonEvoEditorService.Set(link, "nSlot", 0);
                        DigimonEvoEditorService.Set(link, "dwDigimonID", 0);
                    }
                }
            }

            target.Remove();

            DigimonEvoEditorService.Set(
                chain,
                "CountEvo",
                chain.Elements("Evolution").Count());
        }

        private bool SaveDigimonEvoEditor(
            DigimonEvoEditState state,
            bool showSuccess)
        {
            try
            {
                foreach (XElement evolution in state.WorkingChain.Elements("Evolution"))
                {
                    SyncJogressRequirementCount(
                        evolution);
                }

                XElement actual =
                    state.Service.GetChain(state.RootId);

                actual.ReplaceWith(
                    new XElement(state.WorkingChain));

                state.Service.Save();

                EditorPreloadService.ReplaceDigimonEvo(
                    state.Service.FilePath,
                    state.Service);

                state.Dirty = false;

                RefreshDigimonEvoBrowsers(
                    state.Service.FilePath);

                if (showSuccess)
                {
                    MessageBox.Show(
                        $"Evolution tree {state.RootId} saved successfully.",
                        "DigimonEvo Editor",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "DigimonEvo Save Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return false;
            }
        }

        private void RefreshDigimonEvoBrowsers(
            string filePath)
        {
            foreach (TabPage page in editorTabs.TabPages)
            {
                if (page.Tag is DigimonEvoBrowseState browser &&
                    browser.Service.FilePath.Equals(
                        filePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    RefreshDigimonEvoBrowser(browser);
                }
            }
        }

        private Bitmap? DigimonEvoIcon(
            DigimonEvoEditorService service,
            uint id)
        {
            if (id == 0)
                return null;

            if (digimonEvoIconCache.TryGetValue(id, out Bitmap? cached))
                return cached;

            DigimonEvoDigimonRef reference =
                service.ResolveDigimon(id);

            Bitmap? icon =
                DigimonEvoIconResolver.TryLoad(
                    id,
                    reference.ModelId);

            digimonEvoIconCache[id] = icon;
            return icon;
        }

        private static void ResetDigimonEvoScrollToTop(
            ScrollableControl control)
        {
            if (control.IsDisposed)
                return;

            control.AutoScrollPosition = Point.Empty;

            if (control.VerticalScroll.Visible)
            {
                try
                {
                    control.VerticalScroll.Value =
                        control.VerticalScroll.Minimum;
                }
                catch
                {
                    // WinForms can recreate the scrollbar during layout.
                }
            }

            if (control.IsHandleCreated)
            {
                control.BeginInvoke(
                    new Action(
                        () =>
                        {
                            if (control.IsDisposed)
                                return;

                            control.AutoScrollPosition = Point.Empty;

                            if (control.VerticalScroll.Visible)
                            {
                                try
                                {
                                    control.VerticalScroll.Value =
                                        control.VerticalScroll.Minimum;
                                }
                                catch
                                {
                                }
                            }
                        }));
            }
        }

        private void ShowEvolutionTreeDigimonPicker(
            DigimonEvoEditState state,
            XElement sourceEvolution,
            Action<DigimonEvoDigimonRef> selected,
            Control? keepExistingOverlay = null)
        {
            uint sourceId =
                DigimonEvoEditorService.U(
                    sourceEvolution.Element("digiId")?.Value);

            List<DigimonEvoDigimonRef> treeDigimons =
                state.WorkingChain
                    .Elements("Evolution")
                    .Select(
                        x =>
                            DigimonEvoEditorService.U(
                                x.Element("digiId")?.Value))
                    .Where(x => x != 0 && x != sourceId)
                    .Distinct()
                    .Select(state.Service.ResolveDigimon)
                    .OrderBy(
                        x => x.Name,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Id)
                    .ToList();

            var overlay =
                CreateEvoOverlay(
                    state.Page,
                    "Select Link Target — Current Evolution Tree Only",
                    out Panel content,
                    out Action close);

            var search =
                new TextBox
                {
                    PlaceholderText = "Search Digimon ID or name inside this evolution tree...",
                    BackColor = Color.FromArgb(12, 12, 12),
                    ForeColor = CText,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(20, 58),
                    Width = 700
                };

            var info =
                new Label
                {
                    Text =
                        $"{treeDigimons.Count} valid target Digimon in this tree. " +
                        "External Digimon are intentionally hidden.",
                    ForeColor = CMuted,
                    Font = new Font("Segoe UI", 8F),
                    AutoSize = true,
                    Location = new Point(20, 86)
                };

            var results =
                new FlowLayoutPanel
                {
                    Location = new Point(20, 110),
                    Size = new Size(700, 430),
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    BackColor = Color.FromArgb(20, 20, 20),
                    Padding = new Padding(4, 4, 12, 24)
                };

            DarkUi.ApplyDarkScrollBar(results);

            void Render()
            {
                string query = search.Text.Trim();

                IEnumerable<DigimonEvoDigimonRef> filtered =
                    treeDigimons;

                if (query.Length != 0)
                {
                    filtered =
                        filtered.Where(
                            x =>
                                x.Id.ToString(CultureInfo.InvariantCulture)
                                    .Contains(
                                        query,
                                        StringComparison.OrdinalIgnoreCase) ||
                                x.Name.Contains(
                                    query,
                                    StringComparison.OrdinalIgnoreCase));
                }

                results.SuspendLayout();

                foreach (Control c in results.Controls.Cast<Control>().ToArray())
                {
                    results.Controls.Remove(c);
                    c.Dispose();
                }

                foreach (DigimonEvoDigimonRef d in filtered)
                {
                    var card =
                        new Panel
                        {
                            Width = 660,
                            Height = 70,
                            BackColor = CPanel,
                            BorderStyle = BorderStyle.FixedSingle,
                            Margin = new Padding(2, 3, 2, 4)
                        };

                    var icon =
                        new PictureBox
                        {
                            Location = new Point(8, 8),
                            Size = new Size(52, 52),
                            BackColor = Color.Black,
                            SizeMode = PictureBoxSizeMode.Zoom,
                            Image = DigimonEvoIcon(state.Service, d.Id)
                        };

                    var label =
                        new Label
                        {
                            Text =
                                $"{d.Id} — {d.Name}\r\n" +
                                $"Model {d.ModelId}  •  EvolutionType {d.EvolutionType}",
                            ForeColor = CText,
                            Font = new Font("Segoe UI", 8.6F),
                            Location = new Point(72, 10),
                            Size = new Size(440, 48)
                        };

                    var choose =
                        CreateEditorActionButton("SELECT");
                    choose.Location = new Point(548, 18);
                    choose.Size = new Size(92, 32);

                    choose.Click +=
                        (_, _) =>
                        {
                            close();

                            if (keepExistingOverlay != null &&
                                !keepExistingOverlay.IsDisposed)
                            {
                                keepExistingOverlay.BringToFront();
                            }

                            selected(d);
                        };

                    card.Controls.Add(icon);
                    card.Controls.Add(label);
                    card.Controls.Add(choose);
                    results.Controls.Add(card);
                }

                results.ResumeLayout(true);
                ResetDigimonEvoScrollToTop(results);
            }

            var timer =
                new System.Windows.Forms.Timer
                {
                    Interval = 150
                };

            timer.Tick +=
                (_, _) =>
                {
                    timer.Stop();
                    Render();
                };

            search.TextChanged +=
                (_, _) =>
                {
                    timer.Stop();
                    timer.Start();
                };

            content.Controls.Add(search);
            content.Controls.Add(info);
            content.Controls.Add(results);
            Render();

            overlay.BringToFront();
            search.Focus();
        }

        private Bitmap? DigimonEvoItemIcon(uint iconId)
        {
            if (iconId == 0)
                return null;

            if (digimonEvoItemIconCache.TryGetValue(iconId, out Bitmap? cached))
                return cached;

            Bitmap? icon =
                ImageDatabasePreview.TryLoadInterfaceIcon(
                    iconId,
                    "Item");

            digimonEvoItemIconCache[iconId] = icon;
            return icon;
        }

        private static string BuildEvolutionUnlockSummary(
            DigimonEvoEditorService service,
            XElement evo)
        {
            var parts = new List<string>();

            int level =
                DigimonEvoEditorService.I(
                    evo.Element("m_nOpenLevel")?.Value);

            if (level > 1)
                parts.Add($"Lv {level}");

            int questId =
                DigimonEvoEditorService.I(
                    evo.Element("m_nOpenQuest")?.Value);

            if (questId != 0)
            {
                DigimonEvoQuestRef? q = service.ResolveQuest(questId);
                parts.Add(
                    q == null
                        ? $"Quest {questId}"
                        : $"Quest: {q.Title}");
            }

            int section =
                DigimonEvoEditorService.I(
                    evo.Element("m_nOpenItemTypeS")?.Value);

            int count =
                DigimonEvoEditorService.I(
                    evo.Element("m_nOpenItemNum")?.Value);

            if (section != 0)
            {
                DigimonEvoItemRef? item = service.ResolveOpenItem(section);
                parts.Add(
                    item == null
                        ? $"nItemS {section} ×{count}"
                        : $"{item.Name} ×{count}");
            }

            uint useItem =
                DigimonEvoEditorService.U(
                    evo.Element("m_nUseItem")?.Value);

            if (useItem != 0)
            {
                DigimonEvoItemRef? item = service.ResolveItem(useItem);
                parts.Add(
                    item == null
                        ? $"Use Item {useItem}"
                        : $"Use: {item.Name}");
            }

            int jogress =
                DigimonEvoEditorService.I(
                    evo.Element("m_nJoGressQuestCheck")?.Value);

            if (jogress != 0)
                parts.Add($"Jogress Quest {jogress}");

            return parts.Count == 0
                ? "No special unlock requirement"
                : string.Join("  •  ", parts);
        }

        private static string BuildEvolutionLinksSummary(
            DigimonEvoEditorService service,
            XElement chain,
            XElement evo)
        {
            List<string> links =
                evo.Elements("EvolutionType")
                    .Select(
                        link =>
                            new
                            {
                                RawSlot =
                                    DigimonEvoEditorService.I(
                                        link.Element("nSlot")?.Value),
                                Id =
                                    DigimonEvoEditorService.U(
                                        link.Element("dwDigimonID")?.Value)
                            })
                    .Where(
                        link =>
                            link.Id != 0 &&
                            link.RawSlot != 65537)
                    .Select(
                        link =>
                        {
                            XElement? target =
                                chain.Elements("Evolution")
                                    .FirstOrDefault(
                                        candidate =>
                                            DigimonEvoEditorService.U(
                                                candidate.Element("digiId")?.Value) ==
                                            link.Id);

                            int level =
                                target == null
                                    ? link.RawSlot
                                    : DigimonEvoEditorService.I(
                                        target.Element("Level")?.Value);

                            return
                                $"Lv {level} → {service.ResolveDigimon(link.Id).Name}";
                        })
                    .ToList();

            return links.Count == 0
                ? "No forward evolution links"
                : "Links: " + string.Join("   |   ", links);
        }
    }
}
