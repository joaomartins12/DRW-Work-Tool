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
    public sealed class CashShopConverter : IGameDataConverter
    {
        public string Name => "CashShop";

        private static readonly Encoding Cp949 = CreateCp949();

        private sealed class CashItem
        {
            public int ItemId { get; set; }
            public int Amount { get; set; }
        }

        private sealed class CashInfo
        {
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public byte Enabled { get; set; }
            public uint UniqueId { get; set; }
            public string Date1 { get; set; } = "";
            public string Date2 { get; set; } = "";

            public int PurchaseCashType { get; set; }
            public int StandardSellingPrice { get; set; }
            public int RealSellingPrice { get; set; }
            public int SalePercent { get; set; }
            public int IconId { get; set; }
            public int MaskType { get; set; }
            public int DispType { get; set; }
            public int DispCount { get; set; }

            public List<CashItem> Items { get; } = new();
        }

        private sealed class ProductGroup
        {
            public uint CashShopId { get; set; }
            public List<CashInfo> Variants { get; } = new();
        }

        private sealed class Category
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<ulong> MainProducts { get; } = new();
            public List<ProductGroup> ProductGroups { get; } = new();
        }

        private sealed class MajorCategory
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public List<Category> Children { get; } = new();
        }

        private sealed class CashShopTable
        {
            public int TableType { get; set; }
            public List<MajorCategory> Majors { get; } = new();
        }

        private sealed class WebInfo
        {
            public string ImageFile { get; set; } = "";
            public string LinkUrl { get; set; } = "";
        }

        private sealed class WebTable
        {
            public int TableType { get; set; }
            public List<WebInfo> Entries { get; } = new();
        }

        private sealed class CashShopModel
        {
            public List<CashShopTable> Tables { get; } = new();
            public List<WebTable> WebTables { get; } = new();
        }

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("CashShop", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath)
        {
            if (Directory.Exists(filePath))
                return Path.GetFileName(filePath)
                    .Equals("CashShop", StringComparison.OrdinalIgnoreCase);

            return Path.GetFileNameWithoutExtension(filePath)
                .Equals("CashShop", StringComparison.OrdinalIgnoreCase);
        }

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            AppLogger.Log($"CashShop: a analisar {data.Length:N0} bytes...");

            CashShopModel model = ReadBinary(data);

            string cashShopFolder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML de CashShop.");

            Directory.CreateDirectory(cashShopFolder);

            WriteXmlTree(model, cashShopFolder);

            AppLogger.Log(
                $"CashShop: BIN -> XML concluído. " +
                $"Tables={model.Tables.Count}, WebTables={model.WebTables.Count}.");

            AppLogger.Log(
                $"CashShop: tamanho BIN verificado: " +
                $"{data.Length:N0} bytes, leitura terminou exatamente no EOF (OK).");

            AppLogger.Log($"CashShop: XML criado em: {cashShopFolder}");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string cashShopFolder = ResolveCashShopXmlFolder(inputXml);

            AppLogger.Log($"CashShop: a validar XML em: {cashShopFolder}");

            CashShopModel model = ReadXmlTree(cashShopFolder);

            long expectedSize = CalculateBinarySize(model);

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta Output."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs, Encoding.UTF8, leaveOpen: true);

            WriteBinary(bw, model);
            bw.Flush();

            long actualSize = fs.Length;

            if (actualSize != expectedSize)
            {
                long diff = actualSize - expectedSize;

                throw new InvalidDataException(
                    $"CashShop.bin foi gerado com tamanho inesperado. " +
                    $"Atual={actualSize:N0}, Esperado={expectedSize:N0}, Diferença={diff:+#;-#;0} bytes.");
            }

            AppLogger.Log(
                $"CashShop: XML -> BIN concluído. " +
                $"Tables={model.Tables.Count}, WebTables={model.WebTables.Count}.");

            AppLogger.Log(
                $"CashShop: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. Esperado={expectedSize:N0} bytes (OK).");
        }

        // ============================================================
        // BIN -> MODEL
        // ============================================================

        private static CashShopModel ReadBinary(byte[] data)
        {
            CashShopModel model = new();

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            try
            {
                int tableCount = ReadCount(br, "CashShop.TableCount", 16);

                for (int t = 0; t < tableCount; t++)
                {
                    CashShopTable table = new()
                    {
                        TableType = br.ReadInt32()
                    };

                    int majorCount = ReadCount(
                        br,
                        $"CashShop.Table[{t}].MajorCount",
                        32);

                    for (int m = 0; m < majorCount; m++)
                    {
                        MajorCategory major = new()
                        {
                            Id = br.ReadInt32(),
                            Name = ReadWideString(br, $"Table[{t}].Major[{m}].Name")
                        };

                        int childCount = ReadCount(
                            br,
                            $"Table[{t}].Major[{m}].ChildCount",
                            64);

                        for (int c = 0; c < childCount; c++)
                        {
                            Category child = new()
                            {
                                Id = br.ReadInt32(),
                                Name = ReadWideString(
                                    br,
                                    $"Table[{t}].Major[{m}].Child[{c}].Name")
                            };

                            int entryCount = ReadCount(
                                br,
                                $"Table[{t}].Major[{m}].Child[{c}].EntryCount",
                                100_000);

                            if (major.Id == 1)
                            {
                                for (int e = 0; e < entryCount; e++)
                                    child.MainProducts.Add(br.ReadUInt64());
                            }
                            else
                            {
                                for (int e = 0; e < entryCount; e++)
                                {
                                    ProductGroup group = new()
                                    {
                                        CashShopId = br.ReadUInt32()
                                    };

                                    int variantCount = ReadCount(
                                        br,
                                        $"CashShopId={group.CashShopId}.VariantCount",
                                        1_000);

                                    for (int v = 0; v < variantCount; v++)
                                    {
                                        group.Variants.Add(
                                            ReadCashInfo(
                                                br,
                                                group.CashShopId,
                                                v));
                                    }

                                    child.ProductGroups.Add(group);
                                }
                            }

                            major.Children.Add(child);
                        }

                        table.Majors.Add(major);
                    }

                    model.Tables.Add(table);
                }

                int webTableCount = ReadCount(
                    br,
                    "CashShop.WebTableCount",
                    32);

                for (int i = 0; i < webTableCount; i++)
                {
                    WebTable web = new()
                    {
                        TableType = br.ReadInt32()
                    };

                    int webCount = ReadCount(
                        br,
                        $"WebTable[{i}].EntryCount",
                        10_000);

                    for (int e = 0; e < webCount; e++)
                    {
                        int imageSize = ReadCount(
                            br,
                            $"WebTable[{i}].Entry[{e}].ImageSize",
                            1_000_000);

                        string image = ReadByteString(
                            br,
                            imageSize,
                            $"WebTable[{i}].Entry[{e}].Image");

                        int urlSize = ReadCount(
                            br,
                            $"WebTable[{i}].Entry[{e}].UrlSize",
                            1_000_000);

                        string url = ReadByteString(
                            br,
                            urlSize,
                            $"WebTable[{i}].Entry[{e}].Url");

                        web.Entries.Add(
                            new WebInfo
                            {
                                ImageFile = image,
                                LinkUrl = url
                            });
                    }

                    model.WebTables.Add(web);
                }
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException(
                    $"CashShop.bin terminou antes do esperado no offset " +
                    $"0x{ms.Position:X} ({ms.Position:N0}). " +
                    $"O BIN pode estar truncado ou ter uma estrutura diferente.",
                    ex);
            }

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"CashShop.bin contém {extra:N0} bytes extra após a estrutura conhecida. " +
                    $"Offset final esperado: 0x{ms.Position:X}; tamanho do ficheiro: {ms.Length:N0}.");
            }

            return model;
        }

        private static CashInfo ReadCashInfo(
            BinaryReader br,
            uint cashShopId,
            int variantIndex)
        {
            string context =
                $"CashShopId={cashShopId}, Variant={variantIndex}";

            CashInfo info = new()
            {
                Name = ReadWideString(br, $"{context}.Name"),
                Description = ReadWideString(br, $"{context}.Description"),
                Enabled = br.ReadByte(),
                UniqueId = br.ReadUInt32(),
                Date1 = ReadFixedCp949(br, 64, $"{context}.Date1"),
                Date2 = ReadFixedCp949(br, 64, $"{context}.Date2"),

                PurchaseCashType = br.ReadInt32(),
                StandardSellingPrice = br.ReadInt32(),
                RealSellingPrice = br.ReadInt32(),
                SalePercent = br.ReadInt32(),
                IconId = br.ReadInt32(),
                MaskType = br.ReadInt32(),
                DispType = br.ReadInt32(),
                DispCount = br.ReadInt32()
            };

            int itemCount = ReadCount(
                br,
                $"{context}.CashItems",
                10_000);

            for (int i = 0; i < itemCount; i++)
            {
                info.Items.Add(
                    new CashItem
                    {
                        ItemId = br.ReadInt32(),
                        Amount = br.ReadInt32()
                    });
            }

            return info;
        }

        // ============================================================
        // MODEL -> BIN
        // ============================================================

        private static void WriteBinary(
            BinaryWriter bw,
            CashShopModel model)
        {
            bw.Write(model.Tables.Count);

            foreach (CashShopTable table in model.Tables)
            {
                bw.Write(table.TableType);
                bw.Write(table.Majors.Count);

                foreach (MajorCategory major in table.Majors)
                {
                    bw.Write(major.Id);
                    WriteWideString(bw, major.Name);
                    bw.Write(major.Children.Count);

                    foreach (Category child in major.Children)
                    {
                        bw.Write(child.Id);
                        WriteWideString(bw, child.Name);

                        if (major.Id == 1)
                        {
                            bw.Write(child.MainProducts.Count);

                            foreach (ulong productId in child.MainProducts)
                                bw.Write(productId);
                        }
                        else
                        {
                            bw.Write(child.ProductGroups.Count);

                            foreach (ProductGroup group in child.ProductGroups)
                            {
                                bw.Write(group.CashShopId);
                                bw.Write(group.Variants.Count);

                                foreach (CashInfo info in group.Variants)
                                    WriteCashInfo(bw, info);
                            }
                        }
                    }
                }
            }

            bw.Write(model.WebTables.Count);

            foreach (WebTable web in model.WebTables)
            {
                bw.Write(web.TableType);
                bw.Write(web.Entries.Count);

                foreach (WebInfo entry in web.Entries)
                {
                    byte[] image = Cp949.GetBytes(entry.ImageFile);
                    byte[] url = Cp949.GetBytes(entry.LinkUrl);

                    bw.Write(image.Length);
                    bw.Write(image);

                    bw.Write(url.Length);
                    bw.Write(url);
                }
            }
        }

        private static void WriteCashInfo(
            BinaryWriter bw,
            CashInfo info)
        {
            WriteWideString(bw, info.Name);
            WriteWideString(bw, info.Description);

            bw.Write(info.Enabled);
            bw.Write(info.UniqueId);

            WriteFixedCp949(bw, info.Date1, 64, "Date1");
            WriteFixedCp949(bw, info.Date2, 64, "Date2");

            bw.Write(info.PurchaseCashType);
            bw.Write(info.StandardSellingPrice);
            bw.Write(info.RealSellingPrice);
            bw.Write(info.SalePercent);
            bw.Write(info.IconId);
            bw.Write(info.MaskType);
            bw.Write(info.DispType);
            bw.Write(info.DispCount);

            bw.Write(info.Items.Count);

            foreach (CashItem item in info.Items)
            {
                bw.Write(item.ItemId);
                bw.Write(item.Amount);
            }
        }

        private static long CalculateBinarySize(
            CashShopModel model)
        {
            long size = 4;

            foreach (CashShopTable table in model.Tables)
            {
                size += 4 + 4;

                foreach (MajorCategory major in table.Majors)
                {
                    size += 4;
                    size += WideStringSize(major.Name);
                    size += 4;

                    foreach (Category child in major.Children)
                    {
                        size += 4;
                        size += WideStringSize(child.Name);
                        size += 4;

                        if (major.Id == 1)
                        {
                            size += child.MainProducts.Count * 8L;
                        }
                        else
                        {
                            foreach (ProductGroup group in child.ProductGroups)
                            {
                                size += 4 + 4;

                                foreach (CashInfo info in group.Variants)
                                {
                                    size += WideStringSize(info.Name);
                                    size += WideStringSize(info.Description);

                                    size += 1;
                                    size += 4;
                                    size += 64;
                                    size += 64;

                                    size += 8 * 4L;

                                    size += 4;
                                    size += info.Items.Count * 8L;
                                }
                            }
                        }
                    }
                }
            }

            size += 4;

            foreach (WebTable web in model.WebTables)
            {
                size += 4 + 4;

                foreach (WebInfo info in web.Entries)
                {
                    size += 4 + Cp949.GetByteCount(info.ImageFile);
                    size += 4 + Cp949.GetByteCount(info.LinkUrl);
                }
            }

            return size;
        }

        private static long WideStringSize(string value) =>
            4L + Encoding.Unicode.GetByteCount(value ?? "");

        // ============================================================
        // BIN -> XML TREE
        // ============================================================

        private static void WriteXmlTree(
            CashShopModel model,
            string rootFolder)
        {
            foreach (CashShopTable table in model.Tables)
            {
                string suffix = table.TableType == 0
                    ? ""
                    : table.TableType.ToString(CultureInfo.InvariantCulture);

                MajorCategory main = RequireMajor(table, 1, "Main");
                MajorCategory tamer = RequireMajor(table, 2, "Tamer");
                MajorCategory digimon = RequireMajor(table, 3, "Digimon");
                MajorCategory avatar = RequireMajor(table, 4, "Avatar");
                MajorCategory package = RequireMajor(table, 5, "Packages");

                string mainFolder =
                    Path.Combine(rootFolder, $"Main{suffix}");

                Directory.CreateDirectory(mainFolder);

                SaveXml(
                    BuildMainXml(table, main),
                    Path.Combine(
                        mainFolder,
                        "CashShopMainInformation.xml"));

                string tamerFolder =
                    Path.Combine(rootFolder, $"TamerInfo{suffix}");

                Directory.CreateDirectory(tamerFolder);

                SaveXml(
                    BuildTamerMetadata(tamer),
                    Path.Combine(
                        tamerFolder,
                        "CashShopTamerInfo.xml"));

                WriteCategoryXml(
                    tamer,
                    tamerFolder,
                    new Dictionary<int, string>
                    {
                        [2] = @"Expansion\Expansion.xml",
                        [3] = @"Exp\Exp.xml",
                        [4] = @"Moviment\Moviment.xml",
                        [5] = @"Chat\Chat.xml",
                        [6] = @"Etc\Etc.xml"
                    });

                string digimonFolder =
                    Path.Combine(rootFolder, $"DigimonInfo{suffix}");

                Directory.CreateDirectory(digimonFolder);

                XDocument digimonMeta =
                    BuildDigimonMetadata(digimon);

                SaveXml(
                    digimonMeta,
                    Path.Combine(
                        digimonFolder,
                        "CashShopDigimonInfo.xml"));

                // O extractor antigo também produzia esta cópia no TableType 1.
                if (table.TableType == 1)
                {
                    SaveXml(
                        new XDocument(digimonMeta),
                        Path.Combine(
                            digimonFolder,
                            "CashShopDigimonInfo1.xml"));
                }

                WriteCategoryXml(
                    digimon,
                    digimonFolder,
                    new Dictionary<int, string>
                    {
                        [2] = @"DigiEgg\DigiEgg.xml",
                        [3] = @"Evolution\Evolution.xml",
                        [4] = @"Hatch\Hatch.xml",
                        [5] = @"Reinforced\Reinforced.xml",
                        [6] = @"Riding\Riding.xml",
                        [7] = @"Etc\Etc.xml"
                    });

                string avatarFolder =
                    Path.Combine(rootFolder, $"AvatarInfo{suffix}");

                Directory.CreateDirectory(avatarFolder);

                XDocument avatarMeta =
                    BuildAvatarMetadata(avatar);

                SaveXml(
                    avatarMeta,
                    Path.Combine(
                        avatarFolder,
                        "CashShopAvatarInfo.xml"));

                // O extractor antigo também produzia esta cópia no TableType 1.
                if (table.TableType == 1)
                {
                    SaveXml(
                        new XDocument(avatarMeta),
                        Path.Combine(
                            avatarFolder,
                            "CashShopAvatarInfo1.xml"));
                }

                WriteCategoryXml(
                    avatar,
                    avatarFolder,
                    new Dictionary<int, string>
                    {
                        [2] = @"Reinforced\Reinforced.xml",
                        [3] = @"Head\Head.xml",
                        [4] = @"Top\Top.xml",
                        [5] = @"Bottom\Bottom.xml",
                        [6] = @"Gloves\Gloves.xml",
                        [7] = @"Shoes\Shoes.xml",
                        [8] = @"Fashion\Fashion.xml",
                        [9] = @"Costume\Costume.xml"
                    });

                string packageFolder =
                    Path.Combine(rootFolder, $"PackageInfo{suffix}");

                Directory.CreateDirectory(packageFolder);

                SaveXml(
                    BuildPackageMetadata(package),
                    Path.Combine(
                        packageFolder,
                        "CashShopPackageInfo.xml"));

                WriteCategoryXml(
                    package,
                    packageFolder,
                    new Dictionary<int, string>
                    {
                        [1] = @"PackageItems\PackageItems.xml"
                    });
            }

            string webFolder =
                Path.Combine(rootFolder, "WebData");

            Directory.CreateDirectory(webFolder);

            SaveXml(
                BuildWebDataXml(model.WebTables),
                Path.Combine(webFolder, "WebData.xml"));
        }

        private static XDocument BuildMainXml(
            CashShopTable table,
            MajorCategory main)
        {
            if (main.Children.Count != 4)
            {
                throw new InvalidDataException(
                    $"CashShop Main do TableType {table.TableType} " +
                    $"tem {main.Children.Count} categorias; esperado=4.");
            }

            Category newItems = main.Children[0];
            Category hotItems = main.Children[1];
            Category eventItems = main.Children[2];
            Category unknownItems = main.Children[3];

            XElement root = new("CashShopMainInformation",
                new XElement("unknow", table.TableType),
                new XElement("unknow1", table.Majors.Count),
                new XElement("unknow2", main.Id),
                new XElement("MainTitle", main.Name),
                new XElement("unknow3", main.Children.Count),
                new XElement("unknow4", newItems.Id),
                new XElement("MainNewTitle", newItems.Name),
                new XElement("unknow5", hotItems.Id),
                new XElement("MainHotTitle", hotItems.Name),
                new XElement("unknow6", eventItems.Id),
                new XElement("MainEventTitle", eventItems.Name),
                new XElement("unknow7", unknownItems.Id),
                new XElement("UnknowItemsTitle", unknownItems.Name),

                // Compatibilidade com o XML antigo:
                // este campo correspondia ao ID da próxima major category (Tamer).
                new XElement("unknow8", 2));

            AddMainProducts(root, "MainNewItems", newItems.MainProducts);
            AddMainProducts(root, "MainHotItems", hotItems.MainProducts);
            AddMainProducts(root, "MainEventItems", eventItems.MainProducts);
            AddMainProducts(root, "MainUnknowItems", unknownItems.MainProducts);

            return Xml(root);
        }

        private static void AddMainProducts(
            XElement root,
            string elementName,
            IEnumerable<ulong> ids)
        {
            foreach (ulong id in ids)
            {
                root.Add(
                    new XElement(
                        elementName,
                        new XElement("ProductID", id)));
            }
        }

        private static XDocument BuildTamerMetadata(
            MajorCategory major)
        {
            RequireChildCount(major, 6);

            XElement root = new("CashShopTamerInfo",
                new XElement("CategoryName", major.Name),
                new XElement("unknow", major.Children.Count),
                new XElement("unknow1", major.Children[0].Id),
                new XElement("AllName", major.Children[0].Name),
                new XElement("unknow2", major.Children[0].ProductGroups.Count),
                new XElement("unknow3", major.Children[1].Id),
                new XElement("ExpasionName", major.Children[1].Name),
                new XElement("unknow4", major.Children[2].Id),
                new XElement("ExpName", major.Children[2].Name),
                new XElement("unknow5", major.Children[3].Id),
                new XElement("MovimentName", major.Children[3].Name),
                new XElement("unknow6", major.Children[4].Id),
                new XElement("ChatName", major.Children[4].Name),
                new XElement("unknow7", major.Children[5].Id),
                new XElement("EtcName", major.Children[5].Name));

            return Xml(root);
        }

        private static XDocument BuildDigimonMetadata(
            MajorCategory major)
        {
            RequireChildCount(major, 7);

            XElement root = new("CashShopDigimonInfo",
                new XElement("CategoryNameSize", 0),
                new XElement("CategoryName", major.Name),
                new XElement("unknow", major.Id),
                new XElement("unknow1", major.Children.Count),
                new XElement("AllName", major.Children[0].Name),
                new XElement("unknow2", major.Children[0].Id),
                new XElement("unknow3", major.Children[0].ProductGroups.Count),
                new XElement("unknow4", major.Children[1].Id),
                new XElement("DigiEggName", major.Children[1].Name),
                new XElement("unknow5", major.Children[2].Id),
                new XElement("EvolutionName", major.Children[2].Name),
                new XElement("unknow6", major.Children[3].Id),
                new XElement("HatchName", major.Children[3].Name),
                new XElement("unknow7", major.Children[4].Id),
                new XElement("ReinforcedName", major.Children[4].Name),
                new XElement("unknow8", major.Children[5].Id),
                new XElement("RidingName", major.Children[5].Name),
                new XElement("unknow9", major.Children[6].Id),
                new XElement("EtcName", major.Children[6].Name));

            return Xml(root);
        }

        private static XDocument BuildAvatarMetadata(
            MajorCategory major)
        {
            RequireChildCount(major, 9);

            XElement root = new("CashShopAvatarInfo",
                new XElement("CategoryNameSize", 0),
                new XElement("CategoryName", major.Name),
                new XElement("unknow", major.Id),
                new XElement("unknow1", major.Children.Count),
                new XElement("AllName", major.Children[0].Name),
                new XElement("unknow2", major.Children[0].Id),
                new XElement("unknow3", major.Children[0].ProductGroups.Count),
                new XElement("ReinforcedName", major.Children[1].Name),
                new XElement("unknow4", major.Children[1].Id),
                new XElement("unknow5", major.Children[2].Id),
                new XElement("HeadName", major.Children[2].Name),
                new XElement("unknow6", major.Children[3].Id),
                new XElement("TopName", major.Children[3].Name),
                new XElement("unknow7", major.Children[4].Id),
                new XElement("BottomName", major.Children[4].Name),
                new XElement("unknow8", major.Children[5].Id),
                new XElement("GlovesName", major.Children[5].Name),
                new XElement("unknow9", major.Children[6].Id),
                new XElement("ShoesName", major.Children[6].Name),
                new XElement("unknow10", major.Children[7].Id),
                new XElement("FashionName", major.Children[7].Name),
                new XElement("unknow11", major.Children[8].Id),
                new XElement("CostumeName", major.Children[8].Name));

            return Xml(root);
        }

        private static XDocument BuildPackageMetadata(
            MajorCategory major)
        {
            RequireChildCount(major, 1);

            XElement root = new("CashShopPackageInfo",
                new XElement("CategoryNameSize", 0),
                new XElement("CategoryName", major.Name),
                new XElement("unknow", major.Id),
                new XElement("unknow1", major.Children.Count),
                new XElement("AllName", major.Children[0].Name),
                new XElement("unknow2", major.Children[0].Id),
                new XElement("PackageName", ""));

            return Xml(root);
        }

        private static void WriteCategoryXml(
            MajorCategory major,
            string majorFolder,
            IReadOnlyDictionary<int, string> outputFiles)
        {
            foreach (Category child in major.Children)
            {
                if (!outputFiles.TryGetValue(child.Id, out string? relative))
                    continue;

                string path = Path.Combine(
                    majorFolder,
                    relative.Replace('\\', Path.DirectorySeparatorChar));

                Directory.CreateDirectory(
                    Path.GetDirectoryName(path)!);

                SaveXml(
                    BuildProductGroupXml(child),
                    path);
            }
        }

        private static XDocument BuildProductGroupXml(
            Category category)
        {
            XElement root = new("CashShopInformationCounts");

            foreach (ProductGroup group in category.ProductGroups)
            {
                XElement cashInfoElement = new("CashInfo");

                foreach (CashInfo info in group.Variants)
                {
                    XElement cashItems = new("CashItems");

                    foreach (CashItem item in info.Items)
                    {
                        cashItems.Add(
                            new XElement("Item",
                                new XElement("ItemId", item.ItemId),
                                new XElement("Amount", item.Amount)));
                    }

                    cashInfoElement.Add(
                        new XElement("CASHINFO",
                            new XElement("CashName", ""),
                            new XElement("Description", info.Description),
                            new XElement("bActive", 0),
                            new XElement("dwProductID", 0),
                            new XElement("szStartTime", ""),
                            new XElement("szEndTime", ""),
                            new XElement("nPurchaseCashType", info.PurchaseCashType),
                            new XElement("nStandardSellingPrice", info.StandardSellingPrice),
                            new XElement("nRealSellingPrice", info.RealSellingPrice),
                            new XElement("nSalePersent", info.SalePercent),
                            new XElement("nIconID", info.IconId),
                            new XElement("nMaskType", info.MaskType),
                            new XElement("nDispType", info.DispType),
                            new XElement("nDispCount", info.DispCount),
                            new XElement("packageItems", 0),
                            new XElement("cashshop_id", group.CashShopId),
                            new XElement("Name", info.Name),
                            new XElement("Desc", ""),
                            new XElement("Enabled", info.Enabled),
                            new XElement("Date1", info.Date1),
                            new XElement("Date2", info.Date2),
                            new XElement("unique_id", info.UniqueId),
                            cashItems));
                }

                root.Add(
                    new XElement("CashShopInformationCount",
                        new XElement("CashShopId", group.CashShopId),
                        cashInfoElement));
            }

            return Xml(root);
        }

        private static XDocument BuildWebDataXml(
            IEnumerable<WebTable> tables)
        {
            XElement root = new("CashWebDataList");

            foreach (WebTable table in tables)
            {
                XElement web = new("CashWebData",
                    new XElement("nTableType", table.TableType),
                    new XElement("m_mapWebData", table.Entries.Count));

                foreach (WebInfo info in table.Entries)
                {
                    web.Add(
                        new XElement("CashWebDataInfo",
                            new XElement(
                                "Size",
                                Cp949.GetByteCount(info.ImageFile)),
                            new XElement("sWebImageFile", info.ImageFile),
                            new XElement(
                                "Size2",
                                Cp949.GetByteCount(info.LinkUrl)),
                            new XElement("sWebLinkUrl", info.LinkUrl)));
                }

                root.Add(web);
            }

            return Xml(root);
        }

        // ============================================================
        // XML TREE -> MODEL
        // ============================================================

        private static CashShopModel ReadXmlTree(
            string rootFolder)
        {
            if (!Directory.Exists(rootFolder))
            {
                throw new DirectoryNotFoundException(
                    $"A pasta XML da CashShop não existe: {rootFolder}");
            }

            CashShopModel model = new();

            bool foundAnyTable = false;

            foreach ((int tableType, string suffix) in
                new[] { (0, ""), (1, "1") })
            {
                string mainPath =
                    Path.Combine(
                        rootFolder,
                        $"Main{suffix}",
                        "CashShopMainInformation.xml");

                if (!File.Exists(mainPath))
                {
                    throw new FileNotFoundException(
                        $"Falta o XML principal do TableType {tableType}. " +
                        $"Esperado: {mainPath}",
                        mainPath);
                }

                foundAnyTable = true;

                CashShopTable table = new()
                {
                    TableType = tableType
                };

                table.Majors.Add(
                    ReadMainMajor(mainPath, tableType));

                table.Majors.Add(
                    ReadTamerMajor(
                        Path.Combine(
                            rootFolder,
                            $"TamerInfo{suffix}")));

                table.Majors.Add(
                    ReadDigimonMajor(
                        Path.Combine(
                            rootFolder,
                            $"DigimonInfo{suffix}")));

                table.Majors.Add(
                    ReadAvatarMajor(
                        Path.Combine(
                            rootFolder,
                            $"AvatarInfo{suffix}")));

                table.Majors.Add(
                    ReadPackageMajor(
                        Path.Combine(
                            rootFolder,
                            $"PackageInfo{suffix}")));

                model.Tables.Add(table);
            }

            if (!foundAnyTable)
            {
                throw new InvalidDataException(
                    "Não foi encontrado nenhum TableType da CashShop.");
            }

            string webPath =
                Path.Combine(
                    rootFolder,
                    "WebData",
                    "WebData.xml");

            XDocument webDoc = LoadXml(webPath);
            XElement webRoot = RequireRoot(
                webDoc,
                "CashWebDataList",
                webPath);

            foreach (XElement webElement in
                webRoot.Elements("CashWebData"))
            {
                WebTable web = new()
                {
                    TableType = RequiredInt(
                        webElement,
                        "nTableType",
                        webPath)
                };

                foreach (XElement info in
                    webElement.Elements("CashWebDataInfo"))
                {
                    string image =
                        RequiredText(
                            info,
                            "sWebImageFile",
                            webPath,
                            allowEmpty: true);

                    string url =
                        RequiredText(
                            info,
                            "sWebLinkUrl",
                            webPath,
                            allowEmpty: true);

                    int declaredImageSize =
                        RequiredInt(
                            info,
                            "Size",
                            webPath);

                    int declaredUrlSize =
                        RequiredInt(
                            info,
                            "Size2",
                            webPath);

                    int actualImageSize =
                        Cp949.GetByteCount(image);

                    int actualUrlSize =
                        Cp949.GetByteCount(url);

                    if (declaredImageSize != actualImageSize)
                    {
                        throw new InvalidDataException(
                            $"{webPath}: <Size>={declaredImageSize}, " +
                            $"mas sWebImageFile ocupa {actualImageSize} bytes CP949.");
                    }

                    if (declaredUrlSize != actualUrlSize)
                    {
                        throw new InvalidDataException(
                            $"{webPath}: <Size2>={declaredUrlSize}, " +
                            $"mas sWebLinkUrl ocupa {actualUrlSize} bytes CP949.");
                    }

                    web.Entries.Add(
                        new WebInfo
                        {
                            ImageFile = image,
                            LinkUrl = url
                        });
                }

                int declaredCount =
                    RequiredInt(
                        webElement,
                        "m_mapWebData",
                        webPath);

                if (declaredCount != web.Entries.Count)
                {
                    throw new InvalidDataException(
                        $"{webPath}: <m_mapWebData>={declaredCount}, " +
                        $"mas existem {web.Entries.Count} CashWebDataInfo.");
                }

                model.WebTables.Add(web);
            }

            if (model.WebTables.Count == 0)
            {
                throw new InvalidDataException(
                    $"{webPath}: não existem elementos <CashWebData>.");
            }

            return model;
        }

        private static MajorCategory ReadMainMajor(
            string path,
            int expectedTableType)
        {
            XDocument doc = LoadXml(path);
            XElement root = RequireRoot(
                doc,
                "CashShopMainInformation",
                path);

            int tableType = RequiredInt(root, "unknow", path);

            if (tableType != expectedTableType)
            {
                throw new InvalidDataException(
                    $"{path}: <unknow>={tableType}, " +
                    $"mas esta pasta corresponde ao TableType {expectedTableType}.");
            }

            MajorCategory major = new()
            {
                Id = RequiredInt(root, "unknow2", path),
                Name = RequiredText(root, "MainTitle", path)
            };

            AddMainChild(
                major,
                RequiredInt(root, "unknow4", path),
                RequiredText(root, "MainNewTitle", path, true),
                root.Elements("MainNewItems"),
                path);

            AddMainChild(
                major,
                RequiredInt(root, "unknow5", path),
                RequiredText(root, "MainHotTitle", path, true),
                root.Elements("MainHotItems"),
                path);

            AddMainChild(
                major,
                RequiredInt(root, "unknow6", path),
                RequiredText(root, "MainEventTitle", path, true),
                root.Elements("MainEventItems"),
                path);

            AddMainChild(
                major,
                RequiredInt(root, "unknow7", path),
                RequiredText(root, "UnknowItemsTitle", path, true),
                root.Elements("MainUnknowItems"),
                path);

            int declaredChildCount =
                RequiredInt(root, "unknow3", path);

            if (declaredChildCount != major.Children.Count)
            {
                throw new InvalidDataException(
                    $"{path}: <unknow3>={declaredChildCount}, " +
                    $"mas foram encontradas {major.Children.Count} categorias Main.");
            }

            return major;
        }

        private static void AddMainChild(
            MajorCategory major,
            int id,
            string name,
            IEnumerable<XElement> itemElements,
            string path)
        {
            Category child = new()
            {
                Id = id,
                Name = name
            };

            foreach (XElement item in itemElements)
            {
                XElement? idElement =
                    item.Element("ProductID");

                if (idElement == null ||
                    !ulong.TryParse(
                        idElement.Value.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out ulong productId))
                {
                    throw new InvalidDataException(
                        $"{path}: <ProductID> inválido dentro de <{item.Name}>.");
                }

                child.MainProducts.Add(productId);
            }

            major.Children.Add(child);
        }

        private static MajorCategory ReadTamerMajor(
            string folder)
        {
            string metaPath =
                Path.Combine(
                    folder,
                    "CashShopTamerInfo.xml");

            XDocument doc = LoadXml(metaPath);
            XElement root = RequireRoot(
                doc,
                "CashShopTamerInfo",
                metaPath);

            MajorCategory major = new()
            {
                Id = 2,
                Name = RequiredText(
                    root,
                    "CategoryName",
                    metaPath)
            };

            AddProductCategory(
                major,
                RequiredInt(root, "unknow1", metaPath),
                RequiredText(root, "AllName", metaPath),
                null);

            AddProductCategory(
                major,
                RequiredInt(root, "unknow3", metaPath),
                RequiredText(root, "ExpasionName", metaPath),
                Path.Combine(folder, "Expansion", "Expansion.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow4", metaPath),
                RequiredText(root, "ExpName", metaPath),
                Path.Combine(folder, "Exp", "Exp.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow5", metaPath),
                RequiredText(root, "MovimentName", metaPath),
                Path.Combine(folder, "Moviment", "Moviment.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow6", metaPath),
                RequiredText(root, "ChatName", metaPath),
                Path.Combine(folder, "Chat", "Chat.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow7", metaPath),
                RequiredText(root, "EtcName", metaPath),
                Path.Combine(folder, "Etc", "Etc.xml"));

            ValidateDeclaredChildCount(
                RequiredInt(root, "unknow", metaPath),
                major,
                metaPath);

            return major;
        }

        private static MajorCategory ReadDigimonMajor(
            string folder)
        {
            string metaPath =
                Path.Combine(
                    folder,
                    "CashShopDigimonInfo.xml");

            XDocument doc = LoadXml(metaPath);
            XElement root = RequireRoot(
                doc,
                "CashShopDigimonInfo",
                metaPath);

            MajorCategory major = new()
            {
                Id = RequiredInt(root, "unknow", metaPath),
                Name = RequiredText(
                    root,
                    "CategoryName",
                    metaPath)
            };

            AddProductCategory(
                major,
                RequiredInt(root, "unknow2", metaPath),
                RequiredText(root, "AllName", metaPath),
                null);

            AddProductCategory(
                major,
                RequiredInt(root, "unknow4", metaPath),
                RequiredText(root, "DigiEggName", metaPath),
                Path.Combine(folder, "DigiEgg", "DigiEgg.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow5", metaPath),
                RequiredText(root, "EvolutionName", metaPath),
                Path.Combine(folder, "Evolution", "Evolution.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow6", metaPath),
                RequiredText(root, "HatchName", metaPath),
                Path.Combine(folder, "Hatch", "Hatch.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow7", metaPath),
                RequiredText(root, "ReinforcedName", metaPath),
                Path.Combine(folder, "Reinforced", "Reinforced.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow8", metaPath),
                RequiredText(root, "RidingName", metaPath),
                Path.Combine(folder, "Riding", "Riding.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow9", metaPath),
                RequiredText(root, "EtcName", metaPath),
                Path.Combine(folder, "Etc", "Etc.xml"));

            ValidateDeclaredChildCount(
                RequiredInt(root, "unknow1", metaPath),
                major,
                metaPath);

            return major;
        }

        private static MajorCategory ReadAvatarMajor(
            string folder)
        {
            string metaPath =
                Path.Combine(
                    folder,
                    "CashShopAvatarInfo.xml");

            XDocument doc = LoadXml(metaPath);
            XElement root = RequireRoot(
                doc,
                "CashShopAvatarInfo",
                metaPath);

            MajorCategory major = new()
            {
                Id = RequiredInt(root, "unknow", metaPath),
                Name = RequiredText(
                    root,
                    "CategoryName",
                    metaPath)
            };

            AddProductCategory(
                major,
                RequiredInt(root, "unknow2", metaPath),
                RequiredText(root, "AllName", metaPath),
                null);

            AddProductCategory(
                major,
                RequiredInt(root, "unknow4", metaPath),
                RequiredText(root, "ReinforcedName", metaPath),
                Path.Combine(folder, "Reinforced", "Reinforced.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow5", metaPath),
                RequiredText(root, "HeadName", metaPath),
                Path.Combine(folder, "Head", "Head.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow6", metaPath),
                RequiredText(root, "TopName", metaPath),
                Path.Combine(folder, "Top", "Top.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow7", metaPath),
                RequiredText(root, "BottomName", metaPath),
                Path.Combine(folder, "Bottom", "Bottom.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow8", metaPath),
                RequiredText(root, "GlovesName", metaPath),
                Path.Combine(folder, "Gloves", "Gloves.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow9", metaPath),
                RequiredText(root, "ShoesName", metaPath),
                Path.Combine(folder, "Shoes", "Shoes.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow10", metaPath),
                RequiredText(root, "FashionName", metaPath),
                Path.Combine(folder, "Fashion", "Fashion.xml"));

            AddProductCategory(
                major,
                RequiredInt(root, "unknow11", metaPath),
                RequiredText(root, "CostumeName", metaPath),
                Path.Combine(folder, "Costume", "Costume.xml"));

            ValidateDeclaredChildCount(
                RequiredInt(root, "unknow1", metaPath),
                major,
                metaPath);

            return major;
        }

        private static MajorCategory ReadPackageMajor(
            string folder)
        {
            string metaPath =
                Path.Combine(
                    folder,
                    "CashShopPackageInfo.xml");

            XDocument doc = LoadXml(metaPath);
            XElement root = RequireRoot(
                doc,
                "CashShopPackageInfo",
                metaPath);

            MajorCategory major = new()
            {
                Id = RequiredInt(root, "unknow", metaPath),
                Name = RequiredText(
                    root,
                    "CategoryName",
                    metaPath)
            };

            AddProductCategory(
                major,
                RequiredInt(root, "unknow2", metaPath),
                RequiredText(root, "AllName", metaPath),
                Path.Combine(
                    folder,
                    "PackageItems",
                    "PackageItems.xml"));

            ValidateDeclaredChildCount(
                RequiredInt(root, "unknow1", metaPath),
                major,
                metaPath);

            return major;
        }

        private static void AddProductCategory(
            MajorCategory major,
            int id,
            string name,
            string? xmlPath)
        {
            Category category = new()
            {
                Id = id,
                Name = name
            };

            if (xmlPath != null)
            {
                foreach (ProductGroup group in
                    ReadProductGroups(xmlPath))
                {
                    category.ProductGroups.Add(group);
                }
            }

            major.Children.Add(category);
        }

        private static List<ProductGroup> ReadProductGroups(
            string path)
        {
            XDocument doc = LoadXml(path);
            XElement root = RequireRoot(
                doc,
                "CashShopInformationCounts",
                path);

            List<ProductGroup> result = new();

            int groupIndex = 0;

            foreach (XElement groupElement in
                root.Elements("CashShopInformationCount"))
            {
                uint cashShopId =
                    RequiredUInt(
                        groupElement,
                        "CashShopId",
                        path);

                ProductGroup group = new()
                {
                    CashShopId = cashShopId
                };

                XElement? cashInfoContainer =
                    groupElement.Element("CashInfo");

                if (cashInfoContainer != null)
                {
                    int variantIndex = 0;

                    foreach (XElement infoElement in
                        cashInfoContainer.Elements("CASHINFO"))
                    {
                        group.Variants.Add(
                            ReadCashInfoXml(
                                infoElement,
                                cashShopId,
                                path,
                                groupIndex,
                                variantIndex));

                        variantIndex++;
                    }
                }

                result.Add(group);
                groupIndex++;
            }

            return result;
        }

        private static CashInfo ReadCashInfoXml(
            XElement element,
            uint cashShopId,
            string path,
            int groupIndex,
            int variantIndex)
        {
            string ctx =
                $"{path} | CashShopId={cashShopId} | Variant={variantIndex}";

            CashInfo info = new()
            {
                Name = RequiredText(
                    element,
                    "Name",
                    ctx,
                    allowEmpty: true),

                Description = RequiredText(
                    element,
                    "Description",
                    ctx,
                    allowEmpty: true),

                Enabled = RequiredByte(
                    element,
                    "Enabled",
                    ctx),

                UniqueId = RequiredUInt(
                    element,
                    "unique_id",
                    ctx),

                Date1 = RequiredText(
                    element,
                    "Date1",
                    ctx,
                    allowEmpty: true),

                Date2 = RequiredText(
                    element,
                    "Date2",
                    ctx,
                    allowEmpty: true),

                PurchaseCashType = RequiredInt(
                    element,
                    "nPurchaseCashType",
                    ctx),

                StandardSellingPrice = RequiredInt(
                    element,
                    "nStandardSellingPrice",
                    ctx),

                RealSellingPrice = RequiredInt(
                    element,
                    "nRealSellingPrice",
                    ctx),

                SalePercent = RequiredInt(
                    element,
                    "nSalePersent",
                    ctx),

                IconId = RequiredInt(
                    element,
                    "nIconID",
                    ctx),

                MaskType = RequiredInt(
                    element,
                    "nMaskType",
                    ctx),

                DispType = RequiredInt(
                    element,
                    "nDispType",
                    ctx),

                DispCount = RequiredInt(
                    element,
                    "nDispCount",
                    ctx)
            };

            ValidateFixedString(
                info.Date1,
                64,
                $"{ctx} <Date1>");

            ValidateFixedString(
                info.Date2,
                64,
                $"{ctx} <Date2>");

            XElement? items =
                element.Element("CashItems");

            if (items != null)
            {
                int itemIndex = 0;

                foreach (XElement item in
                    items.Elements("Item"))
                {
                    info.Items.Add(
                        new CashItem
                        {
                            ItemId = RequiredInt(
                                item,
                                "ItemId",
                                $"{ctx} CashItems[{itemIndex}]"),

                            Amount = RequiredInt(
                                item,
                                "Amount",
                                $"{ctx} CashItems[{itemIndex}]")
                        });

                    itemIndex++;
                }
            }

            return info;
        }

        // ============================================================
        // VALIDATION / HELPERS
        // ============================================================

        private static XDocument LoadXml(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"XML obrigatório não encontrado: {path}",
                    path);
            }

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
            string path)
        {
            XElement? root = doc.Root;

            if (root == null)
                throw new InvalidDataException(
                    $"{path}: XML sem elemento root.");

            if (!root.Name.LocalName.Equals(
                expected,
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{path}: root <{root.Name.LocalName}> inválido. " +
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
            XElement? e = parent.Element(name);

            if (e == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            string value = e.Value;

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
            string context)
        {
            string value = RequiredText(
                parent,
                name,
                context,
                allowEmpty: false);

            if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é um Int32 válido.");
            }

            return result;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string value = RequiredText(
                parent,
                name,
                context,
                allowEmpty: false);

            if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é um UInt32 válido.");
            }

            return result;
        }

        private static byte RequiredByte(
            XElement parent,
            string name,
            string context)
        {
            string value = RequiredText(
                parent,
                name,
                context,
                allowEmpty: false);

            if (!byte.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out byte result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe num byte (0-255).");
            }

            return result;
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value = br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Intervalo esperado: 0..{max}.");
            }

            return value;
        }

        private static string ReadWideString(
            BinaryReader br,
            string field)
        {
            int charCount = ReadCount(
                br,
                $"{field}.Length",
                10_000_000);

            int byteCount = checked(charCount * 2);

            byte[] bytes = br.ReadBytes(byteCount);

            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {byteCount} bytes UTF-16LE, " +
                    $"recebidos {bytes.Length}.");
            }

            return Encoding.Unicode.GetString(bytes);
        }

        private static void WriteWideString(
            BinaryWriter bw,
            string value)
        {
            string text = value ?? "";

            bw.Write(text.Length);
            bw.Write(Encoding.Unicode.GetBytes(text));
        }

        private static string ReadFixedCp949(
            BinaryReader br,
            int byteCount,
            string field)
        {
            byte[] bytes = br.ReadBytes(byteCount);

            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {byteCount} bytes, " +
                    $"recebidos {bytes.Length}.");
            }

            int zero = Array.IndexOf(bytes, (byte)0);

            if (zero < 0)
                zero = bytes.Length;

            return Cp949.GetString(
                bytes,
                0,
                zero);
        }

        private static void WriteFixedCp949(
            BinaryWriter bw,
            string value,
            int byteCount,
            string field)
        {
            byte[] encoded =
                Cp949.GetBytes(value ?? "");

            if (encoded.Length >= byteCount)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {encoded.Length} bytes CP949, " +
                    $"mas o buffer suporta no máximo {byteCount - 1} bytes + terminador.");
            }

            byte[] buffer = new byte[byteCount];

            Buffer.BlockCopy(
                encoded,
                0,
                buffer,
                0,
                encoded.Length);

            bw.Write(buffer);
        }

        private static void ValidateFixedString(
            string value,
            int byteCount,
            string field)
        {
            int bytes = Cp949.GetByteCount(value ?? "");

            if (bytes >= byteCount)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {bytes} bytes CP949; " +
                    $"o limite é {byteCount - 1} bytes.");
            }
        }

        private static string ReadByteString(
            BinaryReader br,
            int byteCount,
            string field)
        {
            byte[] bytes = br.ReadBytes(byteCount);

            if (bytes.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {byteCount} bytes, " +
                    $"recebidos {bytes.Length}.");
            }

            return Cp949.GetString(bytes);
        }

        private static MajorCategory RequireMajor(
            CashShopTable table,
            int id,
            string expectedName)
        {
            MajorCategory? major =
                table.Majors.FirstOrDefault(x => x.Id == id);

            if (major == null)
            {
                throw new InvalidDataException(
                    $"CashShop TableType={table.TableType}: " +
                    $"major category ID={id} ({expectedName}) não encontrada.");
            }

            return major;
        }

        private static void RequireChildCount(
            MajorCategory major,
            int expected)
        {
            if (major.Children.Count != expected)
            {
                throw new InvalidDataException(
                    $"CashShop major '{major.Name}' tem " +
                    $"{major.Children.Count} children; esperado={expected}.");
            }
        }

        private static void ValidateDeclaredChildCount(
            int declared,
            MajorCategory major,
            string path)
        {
            if (declared != major.Children.Count)
            {
                throw new InvalidDataException(
                    $"{path}: count de categorias={declared}, " +
                    $"mas foram montadas {major.Children.Count}.");
            }
        }

        private static string ResolveCashShopXmlFolder(
            string inputXml)
        {
            if (Directory.Exists(inputXml))
                return inputXml;

            string? directory =
                Path.GetDirectoryName(inputXml);

            if (directory == null)
            {
                throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML da CashShop.");
            }

            return directory;
        }

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration("1.0", "utf-8", null),
                root);

        private static void SaveXml(
            XDocument doc,
            string path)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!);

            XmlWriterSettings settings = new()
            {
                Indent = true,
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false
            };

            using XmlWriter writer =
                XmlWriter.Create(path, settings);

            doc.Save(writer);
        }

        private static Encoding CreateCp949()
        {
            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(
                949,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
        }
    }
}
