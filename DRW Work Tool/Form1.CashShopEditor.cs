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
        private const int CashShopCardsPerPage = 9;

        private sealed class CashShopItemReference
        {
            public uint Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public uint IconId { get; init; }
            public string Description { get; init; } = string.Empty;
        }

        private sealed class CashShopRecord
        {
            public required string FilePath { get; init; }
            public required XDocument Document { get; init; }
            public required XElement Container { get; init; }
            public required XElement Node { get; set; }
            public required string Group { get; init; }
            public required string Category { get; init; }
            public string Badge { get; set; } = string.Empty;

            public uint CashShopId => ParseUInt(Container.Element("CashShopId")?.Value);
            public uint UniqueId => ParseUInt(Node.Element("unique_id")?.Value);
            public uint IconId => ParseUInt(Node.Element("nIconID")?.Value);
            public bool Active => ParseInt(Node.Element("Enabled")?.Value) != 0;
            public int Price => ParseInt(Node.Element("nRealSellingPrice")?.Value);

            public string Name
            {
                get
                {
                    string name = (Node.Element("Name")?.Value ?? string.Empty).Replace("\\n", " ").Trim();
                    if (name.Length > 0) return name;
                    return (Node.Element("CashName")?.Value ?? string.Empty).Replace("\\n", " ").Trim();
                }
            }

            public IReadOnlyList<(uint ItemId, int Amount)> Items =>
                Node.Element("CashItems")?.Elements("Item")
                    .Select(x => (ItemId: ParseUInt(x.Element("ItemId")?.Value), Amount: Math.Max(1, ParseInt(x.Element("Amount")?.Value))))
                    .Where(x => x.ItemId > 0).ToList()
                ?? new List<(uint ItemId, int Amount)>();
        }

        private sealed class CashShopService
        {
            private readonly Dictionary<uint, CashShopItemReference> _itemsById = new();
            public string RootPath { get; }
            public string ItemListPath { get; }
            public List<CashShopRecord> Records { get; } = new();
            public List<CashShopRecord> MainRecords { get; } = new();

            public CashShopService(string rootPath)
            {
                RootPath = Path.GetFullPath(rootPath);
                if (!Directory.Exists(RootPath))
                    throw new DirectoryNotFoundException($"CashShop folder was not found: {RootPath}");

                // Numbered duplicate trees (*1) are intentionally ignored.
                // The unnumbered folders are the canonical complete data set.
                LoadCanonicalGroup("TamerInfo", "Tamer");
                LoadCanonicalGroup("DigimonInfo", "Digimon");
                LoadCanonicalGroup("AvatarInfo", "Avatar");
                LoadCanonicalGroup("PackageInfo", "Packages");

                ItemListPath = FindItemListPath();
                LoadItemList();
                BuildMainView();

                if (Records.Count == 0)
                    throw new InvalidDataException("No CashShopInformationCount records were found in the canonical CashShop folders.");
            }

            public IEnumerable<string> Groups => new[] { "Main", "Tamer", "Digimon", "Avatar", "Packages" };

            public IReadOnlyList<string> Categories(string group)
            {
                if (group.Equals("Main", StringComparison.OrdinalIgnoreCase))
                    return new[] { "All", "NEW", "HOT", "EVENT", "OTHER" };

                return Records.Where(x => x.Group.Equals(group, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Prepend("All").ToList();
            }

            public IReadOnlyList<CashShopRecord> Query(string group, string category, string search)
            {
                IEnumerable<CashShopRecord> source = group.Equals("Main", StringComparison.OrdinalIgnoreCase)
                    ? MainRecords
                    : Records.Where(x => x.Group.Equals(group, StringComparison.OrdinalIgnoreCase));

                if (!category.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    if (group.Equals("Main", StringComparison.OrdinalIgnoreCase))
                    {
                        source = source.Where(x => x.Badge.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                            (category.Equals("OTHER", StringComparison.OrdinalIgnoreCase) &&
                             !new[] { "NEW", "HOT", "EVENT" }.Contains(x.Badge, StringComparer.OrdinalIgnoreCase)));
                    }
                    else source = source.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
                }

                string q = (search ?? string.Empty).Trim();
                if (q.Length > 0)
                {
                    source = source.Where(record =>
                    {
                        string items = string.Join(" ", record.Items.Select(x => x.ItemId));
                        return record.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               record.CashShopId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               record.UniqueId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               record.IconId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               items.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               (record.Node.Element("Description")?.Value ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase);
                    });
                }

                return source.OrderByDescending(x => x.Active).ThenBy(x => x.CashShopId).ThenBy(x => x.UniqueId).ToList();
            }

            public CashShopItemReference? FindItem(uint id) => _itemsById.TryGetValue(id, out CashShopItemReference? item) ? item : null;

            public IReadOnlyList<CashShopItemReference> SearchItems(string query)
            {
                string q = (query ?? string.Empty).Trim();
                return _itemsById.Values.Where(x => q.Length == 0 || x.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Id).Take(1000).ToList();
            }

            public void Save(CashShopRecord source, XElement working, uint cashShopId)
            {
                XElement? id = source.Container.Element("CashShopId");
                if (id == null) source.Container.AddFirst(new XElement("CashShopId", cashShopId));
                else id.Value = cashShopId.ToString(CultureInfo.InvariantCulture);

                XElement replacement = new XElement(working);
                source.Node.ReplaceWith(replacement);
                source.Node = replacement;
                SaveDocument(source.Document, source.FilePath);
            }

            public CashShopRecord CreateTemplate(string group, string category)
            {
                CashShopRecord? template = Records.FirstOrDefault(x => x.Group.Equals(group, StringComparison.OrdinalIgnoreCase) &&
                    (category.Equals("All", StringComparison.OrdinalIgnoreCase) || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
                    ?? Records.FirstOrDefault();
                if (template == null) throw new InvalidOperationException("No Cash Shop template is available.");
                return CloneCore(template, "New Cash Shop Item");
            }

            public CashShopRecord CloneRecord(CashShopRecord source) => CloneCore(source, source.Name + " [Clone]");

            private CashShopRecord CloneCore(CashShopRecord source, string name)
            {
                XElement clone = new XElement(source.Node);
                uint nextUnique = Records.Select(x => x.UniqueId).DefaultIfEmpty(source.UniqueId).Max();
                if (nextUnique < uint.MaxValue) nextUnique++;
                Set(clone, "unique_id", nextUnique.ToString(CultureInfo.InvariantCulture));
                Set(clone, "Enabled", "0");
                Set(clone, "Name", name);

                XElement cashInfo = source.Container.Element("CashInfo") ?? throw new InvalidDataException("CashInfo container is missing.");
                XElement newNode = new XElement(clone);
                cashInfo.Add(newNode);
                SaveDocument(source.Document, source.FilePath);

                var record = new CashShopRecord
                {
                    FilePath = source.FilePath,
                    Document = source.Document,
                    Container = source.Container,
                    Node = newNode,
                    Group = source.Group,
                    Category = source.Category
                };
                Records.Add(record);
                return record;
            }

            private void LoadCanonicalGroup(string folderName, string groupName)
            {
                string folder = Path.Combine(RootPath, folderName);
                if (!Directory.Exists(folder)) return;

                foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.AllDirectories))
                {
                    XDocument doc;
                    try { doc = XDocument.Load(file, LoadOptions.PreserveWhitespace); }
                    catch { continue; }
                    if (doc.Root?.Name.LocalName != "CashShopInformationCounts") continue;

                    string relative = Path.GetRelativePath(folder, file);
                    string category = Path.GetDirectoryName(relative) ?? string.Empty;
                    if (category.Length == 0) category = Path.GetFileNameWithoutExtension(file);
                    category = category.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "All";

                    foreach (XElement container in doc.Root.Elements("CashShopInformationCount"))
                    {
                        XElement? cashInfo = container.Element("CashInfo");
                        if (cashInfo == null) continue;
                        foreach (XElement node in cashInfo.Elements("CASHINFO"))
                        {
                            Records.Add(new CashShopRecord
                            {
                                FilePath = file, Document = doc, Container = container, Node = node,
                                Group = groupName, Category = category
                            });
                        }
                    }
                }
            }

            private void BuildMainView()
            {
                string path = Path.Combine(RootPath, "Main", "CashShopMainInformation.xml");
                if (!File.Exists(path)) return;
                XDocument doc;
                try { doc = XDocument.Load(path, LoadOptions.PreserveWhitespace); }
                catch { return; }

                var byUnique = Records.Where(x => x.UniqueId > 0).GroupBy(x => x.UniqueId).ToDictionary(x => x.Key, x => x.First());
                foreach (XElement element in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    XElement? product = element.Element("ProductID");
                    if (product == null || !byUnique.TryGetValue(ParseUInt(product.Value), out CashShopRecord? source)) continue;
                    string badge = element.Name.LocalName.Contains("New", StringComparison.OrdinalIgnoreCase) ? "NEW" :
                                   element.Name.LocalName.Contains("Hot", StringComparison.OrdinalIgnoreCase) ? "HOT" :
                                   element.Name.LocalName.Contains("Event", StringComparison.OrdinalIgnoreCase) ? "EVENT" : "OTHER";
                    MainRecords.Add(new CashShopRecord
                    {
                        FilePath = source.FilePath, Document = source.Document, Container = source.Container,
                        Node = source.Node, Group = source.Group, Category = source.Category, Badge = badge
                    });
                }
            }

            private string FindItemListPath() => Directory.Exists(AppPaths.Xml)
                ? Directory.EnumerateFiles(AppPaths.Xml, "ItemList.xml", SearchOption.AllDirectories).OrderBy(x => x.Length).FirstOrDefault() ?? string.Empty
                : string.Empty;

            private void LoadItemList()
            {
                if (string.IsNullOrWhiteSpace(ItemListPath) || !File.Exists(ItemListPath)) return;
                XDocument doc;
                try { doc = XDocument.Load(ItemListPath, LoadOptions.PreserveWhitespace); }
                catch { return; }

                string[] idNames = { "s_dwItemID", "s_nItemID", "ItemId", "ItemID", "ID" };
                foreach (XElement node in doc.Descendants())
                {
                    XElement? idElement = idNames.Select(name => node.Element(name)).FirstOrDefault(x => x != null);
                    if (idElement == null) continue;
                    uint id = ParseUInt(idElement.Value);
                    if (id == 0 || _itemsById.ContainsKey(id)) continue;

                    _itemsById[id] = new CashShopItemReference
                    {
                        Id = id,
                        Name = FirstText(node, "s_szName", "s_szItemName", "ItemName", "Name"),
                        IconId = FirstUInt(node, "s_nIcon", "s_nIconID", "s_dwIcon", "IconID", "Icon"),
                        Description = FirstText(node, "s_szComment", "s_szDescription", "Description", "Desc")
                    };
                }
            }

            private static string FirstText(XElement node, params string[] names)
            {
                foreach (string name in names)
                {
                    string value = node.Element(name)?.Value?.Trim() ?? string.Empty;
                    if (value.Length > 0) return value;
                }
                return string.Empty;
            }

            private static uint FirstUInt(XElement node, params string[] names)
            {
                foreach (string name in names)
                {
                    uint value = ParseUInt(node.Element(name)?.Value);
                    if (value > 0) return value;
                }
                return 0;
            }

            private static void Set(XElement node, string name, string value)
            {
                XElement? element = node.Element(name);
                if (element == null) node.Add(new XElement(name, value)); else element.Value = value;
            }

            private static void SaveDocument(XDocument document, string path)
            {
                if (File.Exists(path)) File.Copy(path, path + ".editor.bak", true);
                document.Save(path, SaveOptions.None);
            }
        }

        private sealed class CashShopBrowseState
        {
            public required CashShopService Service { get; init; }
            public required FlowLayoutPanel Cards { get; init; }
            public required FlowLayoutPanel GroupTabs { get; init; }
            public required FlowLayoutPanel CategoryTabs { get; init; }
            public required TextBox Search { get; init; }
            public required Label Count { get; init; }
            public required Label PageInfo { get; init; }
            public required Button Previous { get; init; }
            public required Button Next { get; init; }
            public string Group { get; set; } = "Main";
            public string Category { get; set; } = "All";
            public int PageIndex { get; set; }
            public IReadOnlyList<CashShopRecord> Filtered { get; set; } = Array.Empty<CashShopRecord>();
        }

        private async Task OpenCashShopVisualEditorAsync()
        {
            string root = Path.Combine(AppPaths.Xml, "CashShop");
            if (!Directory.Exists(root))
            {
                MessageBox.Show("CashShop folder was not found:\r\n\r\n" + root, "Cash Shop Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            TabPage? existing = editorTabs.TabPages.Cast<TabPage>().FirstOrDefault(x => x.Tag is CashShopBrowseState);
            if (existing != null) { editorTabs.SelectedTab = existing; return; }

            var page = CreateDarkTab("Cash Shop");
            var loading = new EditorLoadingView("Loading Cash Shop Editor", "Reading canonical CashShop XML, ItemList.xml and DDS icon mappings...");
            page.Controls.Add(loading);
            editorTabs.TabPages.Add(page);
            editorTabs.SelectedTab = page;
            await Task.Delay(60);

            try
            {
                CashShopService service = await Task.Run(() => new CashShopService(root));
                if (!page.IsDisposed) BuildCashShopBrowser(page, service);
            }
            catch (Exception ex)
            {
                if (page.IsDisposed) return;
                page.Controls.Clear();
                page.Controls.Add(CreateInfoLabel("Cash Shop editor could not be loaded.\r\n\r\n" + ex.Message));
                AppLogger.ErrorDetailed("Cash Shop Editor", ex.Message, "Verify XML/CashShop, ItemList.xml and ImgDatabase DDS mappings.");
            }
        }

        private void BuildCashShopBrowser(TabPage page, CashShopService service)
        {
            page.Controls.Clear();
            var root = new Panel { Dock = DockStyle.Fill, BackColor = CEditor, Padding = new Padding(14) };
            var header = new Panel { Dock = DockStyle.Top, Height = 176, BackColor = CEditor };
            var title = new Label { Text = "Cash Shop Visual Editor", ForeColor = CText, Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold), Location = new Point(8, 3), AutoSize = true };
            var subtitle = new Label { Text = $"{service.Records.Count:N0} product templates • canonical XML set • DDS icons • ItemList.xml linked", ForeColor = CMuted, Location = new Point(10, 33), AutoSize = true };
            var groupTabs = new FlowLayoutPanel { Location = new Point(8, 56), Height = 34, Width = 720, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = CEditor, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var categoryTabs = new FlowLayoutPanel { Location = new Point(8, 92), Height = 34, Width = 720, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = CEditor, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var search = new TextBox { Location = new Point(8, 132), Height = 27, Width = 520, BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Search product, CashShop ID, unique ID, ItemList ID...", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var create = CreateEditorActionButton("NEW TEMPLATE"); create.Size = new Size(132, 32); create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var count = new Label { ForeColor = CMuted, Location = new Point(10, 160), AutoSize = true };
            var previous = CreateEditorActionButton("◀ PREVIOUS"); previous.Size = new Size(112, 30); previous.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var pageInfo = new Label { ForeColor = CText, Size = new Size(80, 30), TextAlign = ContentAlignment.MiddleCenter, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var next = CreateEditorActionButton("NEXT ▶"); next.Size = new Size(96, 30); next.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = CEditor, Padding = new Padding(4, 8, 16, 8) };
            DarkUi.ApplyDarkScrollBar(cards);
            header.Controls.AddRange(new Control[] { title, subtitle, groupTabs, categoryTabs, search, create, count, previous, pageInfo, next });
            root.Controls.Add(cards); root.Controls.Add(header); page.Controls.Add(root);

            var state = new CashShopBrowseState { Service = service, Cards = cards, GroupTabs = groupTabs, CategoryTabs = categoryTabs, Search = search, Count = count, PageInfo = pageInfo, Previous = previous, Next = next };
            page.Tag = state;

            void LayoutHeader()
            {
                create.Location = new Point(Math.Max(600, header.ClientSize.Width - create.Width - 8), 4);
                search.Width = Math.Max(260, header.ClientSize.Width - 420);
                next.Location = new Point(Math.Max(350, header.ClientSize.Width - next.Width - 8), 134);
                pageInfo.Location = new Point(next.Left - pageInfo.Width - 6, 134);
                previous.Location = new Point(pageInfo.Left - previous.Width - 6, 134);
                groupTabs.Width = categoryTabs.Width = Math.Max(300, header.ClientSize.Width - 16);
            }

            header.Resize += (_, _) => LayoutHeader();
            cards.Resize += (_, _) => ResizeCashShopCards(state);
            search.TextChanged += (_, _) => { state.PageIndex = 0; RefreshCashShopBrowser(state); };
            previous.Click += (_, _) => { if (state.PageIndex > 0) { state.PageIndex--; RefreshCashShopBrowser(state); cards.AutoScrollPosition = Point.Empty; } };
            next.Click += (_, _) => { int pages = Math.Max(1, (int)Math.Ceiling(state.Filtered.Count / (double)CashShopCardsPerPage)); if (state.PageIndex < pages - 1) { state.PageIndex++; RefreshCashShopBrowser(state); cards.AutoScrollPosition = Point.Empty; } };
            create.Click += (_, _) => { CashShopRecord record = service.CreateTemplate(state.Group, state.Category); OpenCashShopEditTab(service, record, new XElement(record.Node)); RefreshCashShopBrowser(state); };

            BuildCashShopGroupTabs(state); LayoutHeader(); RefreshCashShopBrowser(state);
        }

        private void BuildCashShopGroupTabs(CashShopBrowseState state)
        {
            state.GroupTabs.Controls.Clear();
            foreach (string group in state.Service.Groups)
            {
                var button = CreateEditorActionButton(group.ToUpperInvariant()); button.Size = new Size(group == "Packages" ? 106 : 90, 30);
                if (group.Equals(state.Group, StringComparison.OrdinalIgnoreCase)) button.FlatAppearance.BorderColor = Color.FromArgb(255, 180, 40);
                button.Click += (_, _) => { state.Group = group; state.Category = "All"; state.PageIndex = 0; BuildCashShopGroupTabs(state); RefreshCashShopBrowser(state); };
                state.GroupTabs.Controls.Add(button);
            }
            BuildCashShopCategoryTabs(state);
        }

        private void BuildCashShopCategoryTabs(CashShopBrowseState state)
        {
            state.CategoryTabs.Controls.Clear();
            foreach (string category in state.Service.Categories(state.Group))
            {
                var button = CreateEditorActionButton(category.ToUpperInvariant()); button.AutoSize = true; button.MinimumSize = new Size(76, 28);
                if (category.Equals(state.Category, StringComparison.OrdinalIgnoreCase)) button.FlatAppearance.BorderColor = Color.FromArgb(255, 180, 40);
                button.Click += (_, _) => { state.Category = category; state.PageIndex = 0; BuildCashShopCategoryTabs(state); RefreshCashShopBrowser(state); };
                state.CategoryTabs.Controls.Add(button);
            }
        }

        private void RefreshCashShopBrowser(CashShopBrowseState state)
        {
            state.Filtered = state.Service.Query(state.Group, state.Category, state.Search.Text);
            int pages = Math.Max(1, (int)Math.Ceiling(state.Filtered.Count / (double)CashShopCardsPerPage));
            state.PageIndex = Math.Clamp(state.PageIndex, 0, pages - 1);
            DisposeCashShopCardImages(state.Cards);
            state.Cards.SuspendLayout(); state.Cards.Controls.Clear();
            foreach (CashShopRecord record in state.Filtered.Skip(state.PageIndex * CashShopCardsPerPage).Take(CashShopCardsPerPage)) state.Cards.Controls.Add(CreateCashShopCard(state, record));
            state.Cards.ResumeLayout();
            state.Count.Text = $"Results: {state.Filtered.Count:N0} • 9 templates per page";
            state.PageInfo.Text = $"{state.PageIndex + 1} / {pages}";
            state.Previous.Enabled = state.PageIndex > 0; state.Next.Enabled = state.PageIndex < pages - 1;
            ResizeCashShopCards(state);
        }

        private Control CreateCashShopCard(CashShopBrowseState state, CashShopRecord record)
        {
            var card = new Panel { Width = 226, Height = 208, BackColor = Color.FromArgb(32, 32, 40), Margin = new Padding(5), Tag = record };
            card.Paint += (_, e) => { using var p = new Pen(Color.FromArgb(72, 72, 84)); e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
            Image? preview = CashShopDdsIconCache.TryLoad(record.IconId);
            if (preview == null)
            {
                uint itemId = record.Items.FirstOrDefault().ItemId;
                CashShopItemReference? item = state.Service.FindItem(itemId);
                if (item?.IconId > 0) preview = CashShopDdsIconCache.TryLoad(item.IconId);
            }
            var icon = new PictureBox { Location = new Point(12, 10), Size = new Size(72, 72), BackColor = Color.FromArgb(8, 8, 12), SizeMode = PictureBoxSizeMode.Zoom, Image = preview };
            var badge = new Label { Text = record.Badge, ForeColor = Color.FromArgb(255, 205, 70), Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold), Location = new Point(92, 10), Size = new Size(70, 18), Visible = !string.IsNullOrWhiteSpace(record.Badge) };
            var status = new Label { Text = record.Active ? "ACTIVE" : "DISABLED", ForeColor = record.Active ? Color.FromArgb(100, 230, 130) : Color.FromArgb(240, 95, 95), Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold), Location = new Point(92, 30), Size = new Size(120, 20) };
            var name = new Label { Text = string.IsNullOrWhiteSpace(record.Name) ? $"Cash Shop {record.CashShopId}" : record.Name, ForeColor = CText, Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), Location = new Point(12, 88), Size = new Size(202, 36), AutoEllipsis = true };
            string itemText = record.Items.Count == 0 ? "No ItemList item" : string.Join(", ", record.Items.Take(3).Select(x => $"{x.ItemId} x{x.Amount}"));
            var item = new Label { Text = itemText, ForeColor = CMuted, Location = new Point(12, 126), Size = new Size(202, 20), AutoEllipsis = true };
            var price = new Label { Text = $"C {record.Price:N0} • ID {record.CashShopId} • Product {record.UniqueId}", ForeColor = Color.FromArgb(245, 205, 80), Location = new Point(12, 148), Size = new Size(202, 20), AutoEllipsis = true };
            var edit = CreateEditorActionButton("EDIT"); edit.Location = new Point(12, 174); edit.Size = new Size(94, 28);
            var clone = CreateEditorActionButton("CLONE"); clone.Location = new Point(120, 174); clone.Size = new Size(94, 28);
            edit.Click += (_, _) => OpenCashShopEditTab(state.Service, record, new XElement(record.Node));
            clone.Click += (_, _) => { CashShopRecord cloned = state.Service.CloneRecord(record); OpenCashShopEditTab(state.Service, cloned, new XElement(cloned.Node)); RefreshCashShopBrowser(state); };
            card.Controls.AddRange(new Control[] { icon, badge, status, name, item, price, edit, clone });
            return card;
        }

        private void ResizeCashShopCards(CashShopBrowseState state)
        {
            int available = Math.Max(690, state.Cards.ClientSize.Width - 28);
            int width = Math.Max(210, (available - 30) / 3);
            foreach (Control card in state.Cards.Controls) card.Width = width;
        }

        private void OpenCashShopEditTab(CashShopService service, CashShopRecord record, XElement working)
        {
            var page = CreateDarkTab((record.Name.Length > 0 ? record.Name : "Cash Shop Item") + " [Edit]");
            var top = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = CEditor, Padding = new Padding(14, 12, 14, 10) };
            var save = CreateEditorActionButton("SAVE"); save.Size = new Size(100, 34);
            var source = new Label { ForeColor = CMuted, Location = new Point(120, 8), Size = new Size(600, 42), AutoEllipsis = true, Text = $"{record.Group} / {record.Category} • {Path.GetFileName(record.FilePath)}" };
            top.Controls.Add(save); top.Controls.Add(source);
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CEditor, Padding = new Padding(18) }; DarkUi.ApplyDarkScrollBar(scroll);
            var form = new Panel { Width = 820, Height = 860, BackColor = CEditor }; scroll.Controls.Add(form); page.Controls.Add(scroll); page.Controls.Add(top);
            var preview = new PictureBox { Location = new Point(16, 12), Size = new Size(108, 108), BackColor = Color.FromArgb(8, 8, 12), SizeMode = PictureBoxSizeMode.Zoom }; form.Controls.Add(preview);
            var activeLabel = new Label { Location = new Point(142, 12), Size = new Size(180, 24), Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold) }; form.Controls.Add(activeLabel);
            var fields = new Dictionary<string, TextBox>(); int y = 48;
            AddField("CashShop ID", "__CashShopId", 142, y, 160); AddField("Unique Product ID", "unique_id", 320, y, 220); y += 58;
            AddField("Name", "Name", 142, y, 398); y += 58;
            AddField("Description", "Description", 16, y, 744, true, 96); y += 132;
            AddField("Enabled (0/1)", "Enabled", 16, y, 130); AddField("Icon ID", "nIconID", 164, y, 170); AddField("Currency Type", "nPurchaseCashType", 352, y, 150); y += 58;
            AddField("Standard Price", "nStandardSellingPrice", 16, y, 160); AddField("Selling Price", "nRealSellingPrice", 194, y, 160); AddField("Sale %", "nSalePersent", 372, y, 130); y += 58;
            AddField("Display Type", "nDispType", 16, y, 150); AddField("Display Count", "nDispCount", 184, y, 150); AddField("Mask Type", "nMaskType", 352, y, 150); y += 58;
            AddField("Start Date", "Date1", 16, y, 235); AddField("End Date", "Date2", 270, y, 235); y += 72;
            var itemTitle = new Label { Text = "ITEMLIST CONTENT", ForeColor = CText, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Location = new Point(16, y), AutoSize = true }; form.Controls.Add(itemTitle); y += 26;
            var itemList = new ListBox { Location = new Point(16, y), Size = new Size(490, 128), BackColor = Color.FromArgb(16, 16, 18), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle };
            var selectItem = CreateEditorActionButton("SELECT ITEM"); selectItem.Location = new Point(520, y); selectItem.Size = new Size(132, 32);
            var addItem = CreateEditorActionButton("ADD ITEM"); addItem.Location = new Point(520, y + 40); addItem.Size = new Size(132, 32);
            var removeItem = CreateEditorActionButton("REMOVE"); removeItem.Location = new Point(520, y + 80); removeItem.Size = new Size(132, 32);
            form.Controls.AddRange(new Control[] { itemList, selectItem, addItem, removeItem });

            var workingItems = working.Element("CashItems")?.Elements("Item").Select(x => new XElement(x)).ToList() ?? new List<XElement>();
            void RefreshItems()
            {
                itemList.Items.Clear();
                foreach (XElement entry in workingItems)
                {
                    uint id = ParseUInt(entry.Element("ItemId")?.Value); int amount = Math.Max(1, ParseInt(entry.Element("Amount")?.Value));
                    itemList.Items.Add($"{id} x{amount} — {service.FindItem(id)?.Name ?? "Unknown Item"}");
                }
                if (itemList.Items.Count > 0 && itemList.SelectedIndex < 0) itemList.SelectedIndex = 0;
            }

            selectItem.Click += (_, _) =>
            {
                CashShopItemReference? selected = OpenCashShopItemPicker(service); if (selected == null) return;
                int index = itemList.SelectedIndex;
                if (index < 0 || index >= workingItems.Count) workingItems.Add(new XElement("Item", new XElement("ItemId", selected.Id), new XElement("Amount", 1)));
                else SetCashShopElement(workingItems[index], "ItemId", selected.Id.ToString(CultureInfo.InvariantCulture));
                if (selected.IconId > 0) fields["nIconID"].Text = selected.IconId.ToString(CultureInfo.InvariantCulture);
                RefreshItems();
            };
            addItem.Click += (_, _) => { CashShopItemReference? selected = OpenCashShopItemPicker(service); if (selected == null) return; workingItems.Add(new XElement("Item", new XElement("ItemId", selected.Id), new XElement("Amount", 1))); if (selected.IconId > 0 && ParseUInt(fields["nIconID"].Text) == 0) fields["nIconID"].Text = selected.IconId.ToString(CultureInfo.InvariantCulture); RefreshItems(); };
            removeItem.Click += (_, _) => { int i = itemList.SelectedIndex; if (i >= 0 && i < workingItems.Count) { workingItems.RemoveAt(i); RefreshItems(); } };
            fields["Enabled"].TextChanged += (_, _) => RefreshStatus(); fields["nIconID"].TextChanged += (_, _) => RefreshPreview();

            save.Click += (_, _) =>
            {
                try
                {
                    PullFields();
                    XElement cashItems = working.Element("CashItems") ?? new XElement("CashItems"); if (cashItems.Parent == null) working.Add(cashItems); cashItems.RemoveNodes(); foreach (XElement entry in workingItems) cashItems.Add(new XElement(entry));
                    uint cashShopId = ParseUInt(fields["__CashShopId"].Text); if (cashShopId == 0) throw new InvalidDataException("CashShop ID must be greater than zero.");
                    service.Save(record, working, cashShopId);
                    page.Text = (working.Element("Name")?.Value ?? "Cash Shop Item").Replace("\\n", " ") + " [Edit]";
                    foreach (TabPage browserPage in editorTabs.TabPages) if (browserPage.Tag is CashShopBrowseState browser && ReferenceEquals(browser.Service, service)) RefreshCashShopBrowser(browser);
                    MessageBox.Show("Cash Shop XML saved successfully.", "Cash Shop Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { ShowEditorError("Save Cash Shop Product", ex); }
            };

            scroll.Resize += (_, _) => form.Width = Math.Max(790, scroll.ClientSize.Width - 40);
            RefreshItems(); RefreshStatus(); RefreshPreview(); editorTabs.TabPages.Add(page); editorTabs.SelectedTab = page;

            void AddField(string label, string element, int x, int topY, int width, bool multiline = false, int height = 24)
            {
                form.Controls.Add(new Label { Text = label, ForeColor = CText, Location = new Point(x, topY), Size = new Size(width, 18), Font = new Font("Segoe UI Semibold", 8F) });
                string value = element == "__CashShopId" ? record.CashShopId.ToString(CultureInfo.InvariantCulture) : working.Element(element)?.Value ?? string.Empty;
                var box = new TextBox { Text = value, Location = new Point(x, topY + 20), Size = new Size(width, height), Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle };
                fields[element] = box; form.Controls.Add(box);
            }
            void PullFields() { foreach ((string key, TextBox box) in fields) if (key != "__CashShopId") SetCashShopElement(working, key, box.Text); }
            void RefreshStatus() { bool active = ParseInt(fields["Enabled"].Text) != 0; activeLabel.Text = active ? "ACTIVE" : "DISABLED"; activeLabel.ForeColor = active ? Color.FromArgb(100, 230, 130) : Color.FromArgb(240, 95, 95); }
            void RefreshPreview() { uint iconId = ParseUInt(fields["nIconID"].Text); Image? old = preview.Image; preview.Image = CashShopDdsIconCache.TryLoad(iconId); if (!ReferenceEquals(old, preview.Image)) old?.Dispose(); }
        }

        private CashShopItemReference? OpenCashShopItemPicker(CashShopService service)
        {
            using var dialog = new Form { Text = "Select Item from ItemList.xml", Size = new Size(780, 590), StartPosition = FormStartPosition.CenterParent, BackColor = CEditor, ForeColor = CText, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
            var search = new TextBox { Location = new Point(14, 14), Size = new Size(738, 28), PlaceholderText = "Search Item ID, name or description...", BackColor = Color.FromArgb(10, 10, 10), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle };
            var list = new ListBox { Location = new Point(14, 52), Size = new Size(738, 450), BackColor = Color.FromArgb(18, 18, 18), ForeColor = CText, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9F) };
            var select = CreateEditorActionButton("SELECT"); select.Location = new Point(632, 514); select.Size = new Size(120, 32);
            dialog.Controls.AddRange(new Control[] { search, list, select }); List<CashShopItemReference> current = new();
            void Refresh() { current = service.SearchItems(search.Text).ToList(); list.BeginUpdate(); list.Items.Clear(); foreach (CashShopItemReference item in current) list.Items.Add($"{item.Id} — {item.Name} — Icon {item.IconId}"); list.EndUpdate(); if (list.Items.Count > 0) list.SelectedIndex = 0; }
            search.TextChanged += (_, _) => Refresh(); select.Click += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK; }; list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK; }; Refresh();
            return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0 && list.SelectedIndex < current.Count ? current[list.SelectedIndex] : null;
        }

        private static void DisposeCashShopCardImages(Control root)
        {
            foreach (Control card in root.Controls) foreach (PictureBox picture in card.Controls.OfType<PictureBox>()) { picture.Image?.Dispose(); picture.Image = null; }
        }

        private static uint ParseUInt(string? value) => uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0;
        private static int ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
        private static void SetCashShopElement(XElement node, string name, string value) { XElement? element = node.Element(name); if (element == null) node.Add(new XElement(name, value)); else element.Value = value; }
    }
}
