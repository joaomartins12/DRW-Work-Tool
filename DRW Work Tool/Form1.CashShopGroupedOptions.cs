using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool
{
    public partial class Form1
    {
        private sealed class CashShopEditContext
        {
            public required CashShopService Service { get; init; }
            public required CashShopRecord Record { get; init; }
        }

        private sealed class CashShopXElementReferenceComparer : IEqualityComparer<XElement>
        {
            public static readonly CashShopXElementReferenceComparer Instance = new();
            public bool Equals(XElement? x, XElement? y) => ReferenceEquals(x, y);
            public int GetHashCode(XElement obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly HashSet<CashShopService> _cashShopNormalizedServices = new();
        private readonly HashSet<TabPage> _cashShopEnhancedEditTabs = new();
        private Timer? _cashShopGroupedOptionsTimer;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _cashShopGroupedOptionsTimer = new Timer { Interval = 180 };
            _cashShopGroupedOptionsTimer.Tick += (_, _) => EnhanceCashShopGroupedOptions();
            _cashShopGroupedOptionsTimer.Start();
        }

        private void EnhanceCashShopGroupedOptions()
        {
            if (IsDisposed || editorTabs == null)
                return;

            foreach (TabPage page in editorTabs.TabPages.Cast<TabPage>().ToList())
            {
                if (page.Tag is CashShopBrowseState browser)
                {
                    if (_cashShopNormalizedServices.Add(browser.Service))
                    {
                        NormalizeCashShopRecords(browser.Service);
                        browser.PageIndex = 0;
                        RefreshCashShopBrowser(browser);
                    }

                    EnhanceCashShopBrowser(browser);
                }
                else if (page.Tag is CashShopEditContext context &&
                         _cashShopEnhancedEditTabs.Add(page))
                {
                    EnhanceCashShopEditPage(page, context.Service, context.Record);
                }
            }
        }

        private static int CashShopVariantCount(CashShopRecord record) =>
            record.Container.Element("CashInfo")?.Elements("CASHINFO").Count() ?? 0;

        private static IReadOnlyList<XElement> CashShopVariants(CashShopRecord record) =>
            record.Container.Element("CashInfo")?.Elements("CASHINFO").ToList()
            ?? new List<XElement>();

        private void NormalizeCashShopRecords(CashShopService service)
        {
            var seen = new HashSet<XElement>(CashShopXElementReferenceComparer.Instance);
            var normalized = new List<CashShopRecord>();

            foreach (CashShopRecord record in service.Records)
            {
                if (!seen.Add(record.Container))
                    continue;

                XElement? primary = record.Container
                    .Element("CashInfo")?
                    .Elements("CASHINFO")
                    .FirstOrDefault();

                if (primary == null)
                    continue;

                record.Node = primary;
                normalized.Add(record);
            }

            service.Records.Clear();
            service.Records.AddRange(normalized);

            seen.Clear();
            var main = new List<CashShopRecord>();
            foreach (CashShopRecord record in service.MainRecords)
            {
                if (!seen.Add(record.Container))
                    continue;

                XElement? primary = record.Container
                    .Element("CashInfo")?
                    .Elements("CASHINFO")
                    .FirstOrDefault();

                if (primary == null)
                    continue;

                record.Node = primary;
                main.Add(record);
            }

            service.MainRecords.Clear();
            service.MainRecords.AddRange(main);
        }

        private void EnhanceCashShopBrowser(CashShopBrowseState state)
        {
            foreach (Label label in FindControlsRecursive<Label>(state.Cards.Parent))
            {
                if (label.Text.Contains("product templates", StringComparison.OrdinalIgnoreCase))
                {
                    label.Text =
                        $"{state.Service.Records.Count:N0} product groups • canonical XML set • Cash Shop DDS icons • ItemList.xml linked";
                    break;
                }
            }

            ReplaceCashShopNewTemplateButton(state);

            foreach (Panel card in state.Cards.Controls.OfType<Panel>().ToList())
            {
                if (card.Tag is not CashShopRecord record)
                    continue;

                if (card.Controls.Find("CashShopGroupedDelete", false).Length > 0)
                    continue;

                EnhanceCashShopCard(state, card, record);
            }
        }

        private void ReplaceCashShopNewTemplateButton(CashShopBrowseState state)
        {
            Control? root = state.Cards.Parent;
            if (root == null)
                return;

            Button? original = FindControlsRecursive<Button>(root)
                .FirstOrDefault(x => x.Text.Equals("NEW TEMPLATE", StringComparison.OrdinalIgnoreCase));

            if (original == null || original.Name == "CashShopGroupedNewTemplate")
                return;

            var replacement = CreateEditorActionButton("NEW TEMPLATE");
            replacement.Name = "CashShopGroupedNewTemplate";
            replacement.Bounds = original.Bounds;
            replacement.Anchor = original.Anchor;
            replacement.Parent = original.Parent;

            replacement.Click += (_, _) =>
            {
                try
                {
                    CashShopRecord? template = state.Service.Records.FirstOrDefault(x =>
                            x.Group.Equals(state.Group, StringComparison.OrdinalIgnoreCase) &&
                            (state.Category.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                             x.Category.Equals(state.Category, StringComparison.OrdinalIgnoreCase)))
                        ?? state.Service.Records.FirstOrDefault();

                    if (template == null)
                        throw new InvalidOperationException("No Cash Shop template is available for this section.");

                    CashShopRecord created = CloneCashShopGroup(
                        state.Service,
                        template,
                        "New Cash Shop Item");

                    OpenGroupedCashShopEditTab(state.Service, created);
                    RefreshCashShopBrowser(state);
                }
                catch (Exception ex)
                {
                    ShowEditorError("Create Cash Shop Template", ex);
                }
            };

            original.Parent?.Controls.Remove(original);
            original.Dispose();
        }

        private void EnhanceCashShopCard(
            CashShopBrowseState state,
            Panel card,
            CashShopRecord record)
        {
            foreach (Button button in card.Controls.OfType<Button>().ToList())
            {
                if (button.Text.Equals("EDIT", StringComparison.OrdinalIgnoreCase) ||
                    button.Text.Equals("CLONE", StringComparison.OrdinalIgnoreCase))
                {
                    button.Visible = false;
                }
            }

            int variants = Math.Max(1, CashShopVariantCount(record));

            var optionLabel = new Label
            {
                Name = "CashShopGroupedOptionsLabel",
                Text = variants == 1 ? "1 PURCHASE OPTION" : $"{variants} PURCHASE OPTIONS",
                ForeColor = variants > 1
                    ? Color.FromArgb(105, 185, 255)
                    : CMuted,
                Font = new Font("Segoe UI Semibold", 6.2F),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(5, 89),
                Size = new Size(Math.Max(100, card.ClientSize.Width - 10), 13),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            var edit = CreateEditorActionButton("EDIT");
            edit.Name = "CashShopGroupedEdit";
            edit.Size = new Size(62, 23);

            var clone = CreateEditorActionButton("CLONE");
            clone.Name = "CashShopGroupedClone";
            clone.Size = new Size(62, 23);

            var delete = CreateEditorActionButton("DELETE");
            delete.Name = "CashShopGroupedDelete";
            delete.Size = new Size(68, 23);
            delete.ForeColor = Color.FromArgb(245, 115, 115);

            void LayoutActions()
            {
                int gap = 5;
                int total = edit.Width + clone.Width + delete.Width + gap * 2;
                int x = Math.Max(4, (card.ClientSize.Width - total) / 2);
                int y = Math.Max(103, card.ClientSize.Height - edit.Height - 4);

                optionLabel.Location = new Point(5, Math.Max(88, y - 14));
                optionLabel.Size = new Size(Math.Max(100, card.ClientSize.Width - 10), 13);

                edit.Location = new Point(x, y);
                clone.Location = new Point(edit.Right + gap, y);
                delete.Location = new Point(clone.Right + gap, y);
            }

            edit.Click += (_, _) => OpenGroupedCashShopEditTab(state.Service, record);

            clone.Click += (_, _) =>
            {
                try
                {
                    CashShopRecord cloned = CloneCashShopGroup(
                        state.Service,
                        record,
                        record.Name + " [Clone]");

                    OpenGroupedCashShopEditTab(state.Service, cloned);
                    RefreshCashShopBrowser(state);
                }
                catch (Exception ex)
                {
                    ShowEditorError("Clone Cash Shop Product", ex);
                }
            };

            delete.Click += (_, _) =>
            {
                int count = Math.Max(1, CashShopVariantCount(record));
                DialogResult result = MessageBox.Show(
                    $"Delete '{record.Name}'?\r\n\r\n" +
                    $"CashShop ID: {record.CashShopId}\r\n" +
                    $"Purchase options: {count}\r\n\r\n" +
                    "This removes the complete CashShopInformationCount group.",
                    "Delete Cash Shop Product",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                    return;

                try
                {
                    DeleteCashShopGroup(state.Service, record);
                    RefreshCashShopBrowser(state);
                }
                catch (Exception ex)
                {
                    ShowEditorError("Delete Cash Shop Product", ex);
                }
            };

            card.Controls.AddRange(new Control[]
            {
                optionLabel,
                edit,
                clone,
                delete
            });

            card.Resize += (_, _) => LayoutActions();
            LayoutActions();
        }

        private void OpenGroupedCashShopEditTab(
            CashShopService service,
            CashShopRecord record)
        {
            OpenCashShopEditTab(service, record, new XElement(record.Node));

            TabPage? page = editorTabs.SelectedTab;
            if (page == null)
                return;

            page.Tag = new CashShopEditContext
            {
                Service = service,
                Record = record
            };

            if (_cashShopEnhancedEditTabs.Add(page))
                EnhanceCashShopEditPage(page, service, record);
        }

        private void EnhanceCashShopEditPage(
            TabPage page,
            CashShopService service,
            CashShopRecord record)
        {
            Panel? scroll = page.Controls.OfType<Panel>()
                .FirstOrDefault(x => x.Dock == DockStyle.Fill);

            Panel? form = scroll?.Controls.OfType<Panel>().FirstOrDefault();
            if (form == null)
                return;

            int top = form.Controls.Cast<Control>()
                .Where(x => x.Visible)
                .Select(x => x.Bottom)
                .DefaultIfEmpty(0)
                .Max() + 18;

            var section = new Panel
            {
                Name = "CashShopPurchaseOptionsSection",
                Location = new Point(16, top),
                Size = new Size(Math.Max(700, form.ClientSize.Width - 32), 104),
                BackColor = Color.FromArgb(24, 24, 29),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            section.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(70, 70, 82));
                e.Graphics.DrawRectangle(pen, 0, 0, section.Width - 1, section.Height - 1);
            };

            var title = new Label
            {
                Text = "PURCHASE OPTIONS / PRICE TIERS",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Location = new Point(12, 10),
                AutoSize = true
            };

            var summary = new Label
            {
                Name = "CashShopPurchaseOptionsSummary",
                ForeColor = Color.FromArgb(115, 190, 255),
                Location = new Point(12, 35),
                Size = new Size(Math.Max(420, section.Width - 190), 54),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                AutoEllipsis = true
            };

            var manage = CreateEditorActionButton("MANAGE OPTIONS");
            manage.Location = new Point(section.Width - 158, 34);
            manage.Size = new Size(144, 34);
            manage.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            void RefreshSummary()
            {
                List<XElement> variants = CashShopVariants(record).ToList();
                if (variants.Count <= 1)
                {
                    summary.Text = "Single purchase option. Add tiers when the same product should sell more units for a different price.";
                    return;
                }

                summary.Text =
                    $"{variants.Count} options: " +
                    string.Join("   |   ", variants.Take(6).Select(x =>
                    {
                        int count = Math.Max(1, ParseInt(x.Element("nDispCount")?.Value));
                        int price = ParseInt(x.Element("nRealSellingPrice")?.Value);
                        return $"x{count} / C {price:N0}";
                    }));
            }

            manage.Click += (_, _) =>
            {
                if (!OpenCashShopGroupedOptionsManager(service, record))
                    return;

                RefreshSummary();

                foreach (TabPage browserPage in editorTabs.TabPages.Cast<TabPage>())
                {
                    if (browserPage.Tag is CashShopBrowseState browser &&
                        ReferenceEquals(browser.Service, service))
                    {
                        RefreshCashShopBrowser(browser);
                    }
                }
            };

            section.Controls.AddRange(new Control[] { title, summary, manage });
            form.Controls.Add(section);
            form.Height = Math.Max(form.Height, section.Bottom + 24);
            RefreshSummary();
        }

        private bool OpenCashShopGroupedOptionsManager(
            CashShopService service,
            CashShopRecord record)
        {
            List<XElement> options = CashShopVariants(record)
                .Select(x => new XElement(x))
                .ToList();

            if (options.Count == 0)
                options.Add(new XElement(record.Node));

            using var dialog = new Form
            {
                Text = "Cash Shop Purchase Options / Price Tiers",
                Size = new Size(930, 660),
                StartPosition = FormStartPosition.CenterParent,
                BackColor = CEditor,
                ForeColor = CText,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false
            };

            var heading = new Label
            {
                Text = "PURCHASE OPTIONS / PRICE TIERS",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                Location = new Point(16, 14),
                AutoSize = true
            };

            var hint = new Label
            {
                Text = "These options belong to one Cash Shop card. Each option can give a different quantity and charge a different price.",
                ForeColor = CMuted,
                Location = new Point(18, 42),
                Size = new Size(860, 22)
            };

            var list = new ListBox
            {
                Location = new Point(16, 76),
                Size = new Size(350, 466),
                BackColor = Color.FromArgb(16, 16, 18),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle
            };

            int fieldX = 392;
            TextBox uniqueId = CreateOptionField(dialog, "Unique Product ID", fieldX, 80, 190);
            TextBox enabled = CreateOptionField(dialog, "Enabled", fieldX + 210, 80, 110);
            TextBox displayCount = CreateOptionField(dialog, "Display Count", fieldX, 142, 150);
            TextBox standardPrice = CreateOptionField(dialog, "Standard Price", fieldX + 170, 142, 150);
            TextBox sellingPrice = CreateOptionField(dialog, "Selling Price", fieldX + 340, 142, 150);
            TextBox sale = CreateOptionField(dialog, "Sale %", fieldX, 204, 150);
            TextBox iconId = CreateOptionField(dialog, "Cash Shop Icon ID", fieldX + 170, 204, 150);
            TextBox optionName = CreateOptionField(dialog, "Name", fieldX, 266, 490);

            var itemsTitle = new Label
            {
                Text = "ITEMS / QUANTITY",
                ForeColor = CText,
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                Location = new Point(fieldX, 330),
                AutoSize = true
            };

            var items = new ListBox
            {
                Location = new Point(fieldX, 352),
                Size = new Size(492, 130),
                BackColor = Color.FromArgb(16, 16, 18),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle
            };

            var add = CreateEditorActionButton("ADD OPTION");
            add.Location = new Point(16, 555);
            add.Size = new Size(108, 32);

            var clone = CreateEditorActionButton("CLONE OPTION");
            clone.Location = new Point(132, 555);
            clone.Size = new Size(116, 32);

            var remove = CreateEditorActionButton("DELETE OPTION");
            remove.Location = new Point(256, 555);
            remove.Size = new Size(110, 32);
            remove.ForeColor = Color.FromArgb(245, 115, 115);

            var selectIcon = CreateEditorActionButton("SELECT ICON");
            selectIcon.Location = new Point(fieldX + 340, 224);
            selectIcon.Size = new Size(120, 26);

            var save = CreateEditorActionButton("SAVE OPTIONS");
            save.Location = new Point(748, 555);
            save.Size = new Size(136, 32);

            dialog.Controls.AddRange(new Control[]
            {
                heading, hint, list, itemsTitle, items,
                add, clone, remove, selectIcon, save
            });

            int selectedIndex = 0;
            bool loading = false;

            void SyncAmounts(XElement option)
            {
                int amount = Math.Max(1, ParseInt(option.Element("nDispCount")?.Value));
                IEnumerable<XElement> entries =
                    option.Element("CashItems")?.Elements("Item")
                    ?? Enumerable.Empty<XElement>();

                foreach (XElement entry in entries)
                {
                    SetCashShopElement(
                        entry,
                        "Amount",
                        amount.ToString(CultureInfo.InvariantCulture));
                }
            }

            void PullCurrent()
            {
                if (loading || selectedIndex < 0 || selectedIndex >= options.Count)
                    return;

                XElement option = options[selectedIndex];
                SetCashShopElement(option, "unique_id", uniqueId.Text);
                SetCashShopElement(option, "Enabled", enabled.Text);
                SetCashShopElement(option, "nDispCount", displayCount.Text);
                SetCashShopElement(option, "nStandardSellingPrice", standardPrice.Text);
                SetCashShopElement(option, "nRealSellingPrice", sellingPrice.Text);
                SetCashShopElement(option, "nSalePersent", sale.Text);
                SetCashShopElement(option, "nIconID", iconId.Text);
                SetCashShopElement(option, "Name", optionName.Text);
                SyncAmounts(option);
            }

            void LoadCurrent()
            {
                if (selectedIndex < 0 || selectedIndex >= options.Count)
                    return;

                loading = true;
                XElement option = options[selectedIndex];

                uniqueId.Text = option.Element("unique_id")?.Value ?? "0";
                enabled.Text = option.Element("Enabled")?.Value ?? "0";
                displayCount.Text = option.Element("nDispCount")?.Value ?? "1";
                standardPrice.Text = option.Element("nStandardSellingPrice")?.Value ?? "0";
                sellingPrice.Text = option.Element("nRealSellingPrice")?.Value ?? "0";
                sale.Text = option.Element("nSalePersent")?.Value ?? "0";
                iconId.Text = option.Element("nIconID")?.Value ?? "0";
                optionName.Text = option.Element("Name")?.Value ?? string.Empty;

                items.Items.Clear();
                IEnumerable<XElement> entries =
                    option.Element("CashItems")?.Elements("Item")
                    ?? Enumerable.Empty<XElement>();

                foreach (XElement entry in entries)
                {
                    uint itemId = ParseUInt(entry.Element("ItemId")?.Value);
                    int amount = Math.Max(1, ParseInt(entry.Element("Amount")?.Value));
                    items.Items.Add(
                        $"{itemId} x{amount} — {service.FindItem(itemId)?.Name ?? "Unknown Item"}");
                }

                loading = false;
            }

            void RefreshList()
            {
                list.BeginUpdate();
                list.Items.Clear();

                for (int i = 0; i < options.Count; i++)
                {
                    XElement option = options[i];
                    int count = Math.Max(1, ParseInt(option.Element("nDispCount")?.Value));
                    int price = ParseInt(option.Element("nRealSellingPrice")?.Value);
                    uint productId = ParseUInt(option.Element("unique_id")?.Value);

                    list.Items.Add(
                        $"Option {i + 1}   •   x{count}   •   C {price:N0}   •   Product {productId}");
                }

                list.EndUpdate();

                if (options.Count > 0)
                {
                    selectedIndex = Math.Clamp(selectedIndex, 0, options.Count - 1);
                    list.SelectedIndex = selectedIndex;
                }
            }

            uint NextUniqueId()
            {
                HashSet<uint> used = GetAllCashShopUniqueIds(service);
                foreach (XElement option in options)
                {
                    uint id = ParseUInt(option.Element("unique_id")?.Value);
                    if (id > 0)
                        used.Add(id);
                }

                uint candidate = used.Count == 0 ? 1u : used.Max() + 1u;
                while (candidate == 0 || used.Contains(candidate))
                    candidate++;

                return candidate;
            }

            list.SelectedIndexChanged += (_, _) =>
            {
                if (list.SelectedIndex < 0 || list.SelectedIndex == selectedIndex)
                    return;

                PullCurrent();
                selectedIndex = list.SelectedIndex;
                LoadCurrent();
            };

            displayCount.TextChanged += (_, _) =>
            {
                if (loading)
                    return;

                PullCurrent();
                LoadCurrent();
                RefreshList();
            };

            selectIcon.Click += (_, _) =>
            {
                uint? selected = OpenCashShopIconPicker(ParseUInt(iconId.Text));
                if (selected.HasValue)
                    iconId.Text = selected.Value.ToString(CultureInfo.InvariantCulture);
            };

            add.Click += (_, _) =>
            {
                PullCurrent();
                XElement option = new XElement(options[Math.Clamp(selectedIndex, 0, options.Count - 1)]);
                SetCashShopElement(option, "unique_id", NextUniqueId().ToString(CultureInfo.InvariantCulture));
                SetCashShopElement(option, "Enabled", "0");

                int nextCount = Math.Max(1, ParseInt(option.Element("nDispCount")?.Value)) + 1;
                SetCashShopElement(option, "nDispCount", nextCount.ToString(CultureInfo.InvariantCulture));
                SyncAmounts(option);

                options.Add(option);
                selectedIndex = options.Count - 1;
                RefreshList();
                LoadCurrent();
            };

            clone.Click += (_, _) =>
            {
                PullCurrent();
                XElement option = new XElement(options[Math.Clamp(selectedIndex, 0, options.Count - 1)]);
                SetCashShopElement(option, "unique_id", NextUniqueId().ToString(CultureInfo.InvariantCulture));
                SetCashShopElement(option, "Enabled", "0");

                options.Add(option);
                selectedIndex = options.Count - 1;
                RefreshList();
                LoadCurrent();
            };

            remove.Click += (_, _) =>
            {
                if (options.Count <= 1)
                {
                    MessageBox.Show(
                        "A Cash Shop product must keep at least one purchase option.",
                        "Cash Shop Options",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                options.RemoveAt(Math.Clamp(selectedIndex, 0, options.Count - 1));
                selectedIndex = Math.Clamp(selectedIndex, 0, options.Count - 1);
                RefreshList();
                LoadCurrent();
            };

            save.Click += (_, _) =>
            {
                try
                {
                    PullCurrent();
                    SaveCashShopOptions(service, record, options);
                    dialog.DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Save Cash Shop Options",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            RefreshList();
            LoadCurrent();
            return dialog.ShowDialog(this) == DialogResult.OK;
        }

        private TextBox CreateOptionField(
            Form dialog,
            string label,
            int x,
            int y,
            int width)
        {
            dialog.Controls.Add(new Label
            {
                Text = label,
                ForeColor = CText,
                Location = new Point(x, y),
                Size = new Size(width, 18),
                Font = new Font("Segoe UI Semibold", 8F)
            });

            var box = new TextBox
            {
                Location = new Point(x, y + 20),
                Size = new Size(width, 24),
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = CText,
                BorderStyle = BorderStyle.FixedSingle
            };

            dialog.Controls.Add(box);
            return box;
        }

        private CashShopRecord CloneCashShopGroup(
            CashShopService service,
            CashShopRecord source,
            string newName)
        {
            XElement newContainer = new XElement(source.Container);
            uint cashShopId = GetNextGroupedCashShopId(service);
            SetCashShopElement(
                newContainer,
                "CashShopId",
                cashShopId.ToString(CultureInfo.InvariantCulture));

            List<XElement> options = newContainer
                .Element("CashInfo")?
                .Elements("CASHINFO")
                .ToList()
                ?? new List<XElement>();

            if (options.Count == 0)
                throw new InvalidDataException("The selected Cash Shop group contains no CASHINFO options.");

            HashSet<uint> used = GetAllCashShopUniqueIds(service);
            uint candidate = used.Count == 0 ? 1u : used.Max() + 1u;

            for (int i = 0; i < options.Count; i++)
            {
                while (candidate == 0 || used.Contains(candidate))
                    candidate++;

                SetCashShopElement(
                    options[i],
                    "unique_id",
                    candidate.ToString(CultureInfo.InvariantCulture));

                SetCashShopElement(options[i], "Enabled", "0");

                if (i == 0)
                    SetCashShopElement(options[i], "Name", newName);

                used.Add(candidate);
                candidate++;
            }

            source.Container.AddAfterSelf(newContainer);
            SaveCashShopDocument(source.Document, source.FilePath);

            var record = new CashShopRecord
            {
                FilePath = source.FilePath,
                Document = source.Document,
                Container = newContainer,
                Node = options[0],
                Group = source.Group,
                Category = source.Category
            };

            service.Records.Add(record);
            return record;
        }

        private void DeleteCashShopGroup(
            CashShopService service,
            CashShopRecord record)
        {
            record.Container.Remove();
            SaveCashShopDocument(record.Document, record.FilePath);

            service.Records.RemoveAll(x =>
                ReferenceEquals(x.Container, record.Container));

            service.MainRecords.RemoveAll(x =>
                ReferenceEquals(x.Container, record.Container));
        }

        private void SaveCashShopOptions(
            CashShopService service,
            CashShopRecord record,
            IReadOnlyList<XElement> options)
        {
            if (options.Count == 0)
                throw new InvalidDataException("A Cash Shop product needs at least one purchase option.");

            HashSet<uint> external = new();
            foreach (CashShopRecord other in service.Records)
            {
                if (ReferenceEquals(other.Container, record.Container))
                    continue;

                foreach (XElement option in CashShopVariants(other))
                {
                    uint id = ParseUInt(option.Element("unique_id")?.Value);
                    if (id > 0)
                        external.Add(id);
                }
            }

            var local = new HashSet<uint>();
            foreach (XElement option in options)
            {
                uint id = ParseUInt(option.Element("unique_id")?.Value);
                if (id == 0)
                    throw new InvalidDataException("Every purchase option needs a Unique Product ID greater than zero.");

                if (!local.Add(id))
                    throw new InvalidDataException($"Unique Product ID {id} is duplicated inside this product group.");

                if (external.Contains(id))
                    throw new InvalidDataException($"Unique Product ID {id} is already used by another Cash Shop product.");

                int amount = Math.Max(1, ParseInt(option.Element("nDispCount")?.Value));
                foreach (XElement entry in option.Element("CashItems")?.Elements("Item") ?? Enumerable.Empty<XElement>())
                {
                    SetCashShopElement(
                        entry,
                        "Amount",
                        amount.ToString(CultureInfo.InvariantCulture));
                }
            }

            XElement cashInfo = record.Container.Element("CashInfo") ?? new XElement("CashInfo");
            if (cashInfo.Parent == null)
                record.Container.Add(cashInfo);

            cashInfo.Elements("CASHINFO").Remove();
            foreach (XElement option in options)
                cashInfo.Add(new XElement(option));

            record.Node = cashInfo.Elements("CASHINFO").First();
            SaveCashShopDocument(record.Document, record.FilePath);
        }

        private static HashSet<uint> GetAllCashShopUniqueIds(CashShopService service)
        {
            var used = new HashSet<uint>();

            foreach (CashShopRecord record in service.Records)
            {
                foreach (XElement option in CashShopVariants(record))
                {
                    uint id = ParseUInt(option.Element("unique_id")?.Value);
                    if (id > 0)
                        used.Add(id);
                }
            }

            return used;
        }

        private static uint GetNextGroupedCashShopId(CashShopService service)
        {
            HashSet<uint> used = service.Records
                .Select(x => x.CashShopId)
                .Where(x => x > 0)
                .ToHashSet();

            uint candidate = used.Count == 0 ? 1u : used.Max() + 1u;
            while (candidate == 0 || used.Contains(candidate))
                candidate++;

            return candidate;
        }

        private static void SaveCashShopDocument(XDocument document, string path)
        {
            if (File.Exists(path))
                File.Copy(path, path + ".editor.bak", true);

            document.Save(path, SaveOptions.None);
        }

        private static IEnumerable<T> FindControlsRecursive<T>(Control? root)
            where T : Control
        {
            if (root == null)
                yield break;

            foreach (Control child in root.Controls)
            {
                if (child is T typed)
                    yield return typed;

                foreach (T nested in FindControlsRecursive<T>(child))
                    yield return nested;
            }
        }
    }
}
