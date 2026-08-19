using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DRW_Work_Tool.Core
{
    public sealed class InterfaceIconMapBuildResult
    {
        public string DatabaseRoot { get; internal set; } = string.Empty;
        public string MapPath { get; internal set; } = string.Empty;
        public string AnalysisPath { get; internal set; } = string.Empty;
        public int MappedAtlases { get; internal set; }
        public int UnmappedAtlases { get; internal set; }
        public int TotalMappedIcons { get; internal set; }
        public int WarningsCount { get; internal set; }
    }

    public static class InterfaceIconMapBuilder
    {
        private static readonly Regex RxItemAtlas = new(
            @"^icon(?<n>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxSkillAtlas = new(
            @"^sicon(?<n>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxCashShopAtlas = new(
            @"^cashshop(?<g>\d+)_(?<p>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static InterfaceIconMapBuildResult BuildAndAnalyze(
            string? databaseRoot = null,
            IProgress<string>? progress = null)
        {
            progress?.Report("Icon Map: a reconstruir dimensões reais da ImageDatabase...");

            // Always rebuild first. This is important for CashShop because older indexes
            // incorrectly treated every interface atlas as 32x32 tiles.
            ImageDatabaseIndexBuilder.Rebuild(databaseRoot);

            var service = new ImageDatabaseIndexService(databaseRoot);
            service.Load(rebuildIndexIfMissing: false);

            string root = service.DatabaseRoot;
            progress?.Report("Icon Map: a gerar mapeamento...");

            var map = new InterfaceIconMapDocument { Version = 1 };
            var warnings = new List<string>();
            var unmapped = new List<string>();
            var analysis = new StringBuilder();

            analysis.AppendLine("Interface Icon Map - Reajuste / Analyse");
            analysis.AppendLine("========================================");
            analysis.AppendLine($"Generated UTC : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            analysis.AppendLine($"Database root  : {root}");
            analysis.AppendLine();
            analysis.AppendLine("Regras aplicadas:");
            analysis.AppendLine("- siconNN  -> base = NN * 1000; até 256 slots");
            analysis.AppendLine("- iconNN   -> base = NN * 1000; até 1000 slots");
            analysis.AppendLine("- achieve_icon    -> 0..255");
            analysis.AppendLine("- achieve_icon_02 -> 300..555");
            analysis.AppendLine("- achieve_icon_03 -> 556..811");
            analysis.AppendLine("- cashshopG_PPP -> base GPPP00; 36 slots sequenciais 00..35; grelha 6x6; tile 80x80");
            analysis.AppendLine();
            analysis.AppendLine("CashShop verification:");
            analysis.AppendLine("- original TGA guides are 480x480");
            analysis.AppendLine("- suffix 00 = slot 0");
            analysis.AppendLine("- suffix 10 = slot 10 = col 4,row 1");
            analysis.AppendLine("- suffix 20 = slot 20 = col 2,row 3");
            analysis.AppendLine("- suffix 30 = slot 30 = col 0,row 5");
            analysis.AppendLine();
            analysis.AppendLine("Atlases analisados");
            analysis.AppendLine("------------------");

            foreach (InterfaceAtlasEntry atlas in service.Index.InterfaceAtlases
                         .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                progress?.Report($"Icon Map: a analisar {atlas.Name}...");

                AtlasRule? rule = TryResolveRule(atlas, out string? warning);
                if (!string.IsNullOrWhiteSpace(warning))
                    warnings.Add($"{atlas.Name}: {warning}");

                if (rule == null)
                {
                    unmapped.Add(atlas.Name);
                    analysis.AppendLine($"{atlas.Name} | sem regra automática");
                    continue;
                }

                int mappedCount = AddEntries(map, atlas, rule);

                analysis.AppendLine(
                    $"{atlas.Name} | Category={rule.Category} | Base={rule.BaseId} | " +
                    $"Mapped={mappedCount} | Capacity={atlas.Capacity} | " +
                    $"Grid={atlas.Columns}x{atlas.Rows} | Tile={atlas.TileWidth}x{atlas.TileHeight} | " +
                    $"Size={atlas.Width}x{atlas.Height}");

                if (!string.IsNullOrWhiteSpace(rule.Note))
                    analysis.AppendLine($"  Note: {rule.Note}");
            }

            map.Icons = map.Icons
                .OrderBy(x => ParseSortableId(x.Id))
                .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Atlas, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Y)
                .ThenBy(x => x.X)
                .ToList();

            string mapPath = Path.Combine(root, "InterfaceIconMap.json");
            ImageDatabaseIndexService.Serialize(mapPath, map);

            string analysisPath = Path.Combine(root, "InterfaceIconMap_Analysis.txt");
            analysis.AppendLine();
            analysis.AppendLine("Resumo");
            analysis.AppendLine("------");
            analysis.AppendLine($"Mapped atlases  : {service.Index.InterfaceAtlases.Count - unmapped.Count}");
            analysis.AppendLine($"Unmapped atlases: {unmapped.Count}");
            analysis.AppendLine($"Mapped icons    : {map.Icons.Count}");
            analysis.AppendLine($"Warnings        : {warnings.Count}");

            if (warnings.Count > 0)
            {
                analysis.AppendLine();
                analysis.AppendLine("Warnings");
                analysis.AppendLine("--------");
                foreach (string item in warnings)
                    analysis.AppendLine($"- {item}");
            }

            File.WriteAllText(analysisPath, analysis.ToString(), Encoding.UTF8);

            return new InterfaceIconMapBuildResult
            {
                DatabaseRoot = root,
                MapPath = mapPath,
                AnalysisPath = analysisPath,
                MappedAtlases = service.Index.InterfaceAtlases.Count - unmapped.Count,
                UnmappedAtlases = unmapped.Count,
                TotalMappedIcons = map.Icons.Count,
                WarningsCount = warnings.Count
            };
        }

        private static AtlasRule? TryResolveRule(InterfaceAtlasEntry atlas, out string? warning)
        {
            warning = null;

            if (atlas.Width <= 0 || atlas.Height <= 0)
            {
                warning = "dimensões inválidas; atlas ignorado.";
                return null;
            }

            Match mSkill = RxSkillAtlas.Match(atlas.Name);
            if (mSkill.Success)
            {
                int n = int.Parse(mSkill.Groups["n"].Value);
                int count = Math.Min(atlas.Capacity, 256);
                return new AtlasRule
                {
                    Category = "Skill",
                    BaseId = n * 1000,
                    Count = count,
                    Columns = atlas.Columns,
                    TileWidth = atlas.TileWidth,
                    TileHeight = atlas.TileHeight,
                    Note = $"sicon{n:00} => {n * 1000}..{n * 1000 + count - 1}"
                };
            }

            if (atlas.Name.Equals("achieve_icon", StringComparison.OrdinalIgnoreCase))
                return CreateFixedRule("Achieve", 0, Math.Min(atlas.Capacity, 256), atlas, "achieve_icon => 0..255");

            if (atlas.Name.Equals("achieve_icon_02", StringComparison.OrdinalIgnoreCase))
                return CreateFixedRule("Achieve", 300, Math.Min(atlas.Capacity, 256), atlas, "achieve_icon_02 => 300..555");

            if (atlas.Name.Equals("achieve_icon_03", StringComparison.OrdinalIgnoreCase))
                return CreateFixedRule("Achieve", 556, Math.Min(atlas.Capacity, 256), atlas, "achieve_icon_03 => 556..811");

            Match mCash = RxCashShopAtlas.Match(atlas.Name);
            if (mCash.Success)
            {
                string g = mCash.Groups["g"].Value;
                string p = mCash.Groups["p"].Value;
                int baseId = int.Parse($"{g}{p}00");

                if (atlas.Width != 480 || atlas.Height != 480 ||
                    atlas.Columns != 6 || atlas.Rows != 6 ||
                    atlas.TileWidth != 80 || atlas.TileHeight != 80 || atlas.Capacity != 36)
                {
                    warning =
                        $"geometria CashShop inesperada após rebuild: " +
                        $"Size={atlas.Width}x{atlas.Height}, Grid={atlas.Columns}x{atlas.Rows}, " +
                        $"Tile={atlas.TileWidth}x{atlas.TileHeight}, Capacity={atlas.Capacity}.";
                }

                return new AtlasRule
                {
                    Category = "CashShop",
                    BaseId = baseId,
                    Count = 36,
                    Columns = 6,
                    TileWidth = 80,
                    TileHeight = 80,
                    Note = $"{atlas.Name} => {baseId}..{baseId + 35} | verified 6x6 / 80x80"
                };
            }

            Match mItem = RxItemAtlas.Match(atlas.Name);
            if (mItem.Success)
            {
                int n = int.Parse(mItem.Groups["n"].Value);
                int count = Math.Min(atlas.Capacity, 1000);
                return new AtlasRule
                {
                    Category = "Item",
                    BaseId = n * 1000,
                    Count = count,
                    Columns = atlas.Columns,
                    TileWidth = atlas.TileWidth,
                    TileHeight = atlas.TileHeight,
                    Note = $"icon{n:00} => {n * 1000}..{n * 1000 + count - 1}"
                };
            }

            return null;
        }

        private static AtlasRule CreateFixedRule(
            string category,
            int baseId,
            int count,
            InterfaceAtlasEntry atlas,
            string note) => new()
        {
            Category = category,
            BaseId = baseId,
            Count = count,
            Columns = atlas.Columns,
            TileWidth = atlas.TileWidth,
            TileHeight = atlas.TileHeight,
            Note = note
        };

        private static int AddEntries(
            InterfaceIconMapDocument document,
            InterfaceAtlasEntry atlas,
            AtlasRule rule)
        {
            int added = 0;

            for (int i = 0; i < rule.Count; i++)
            {
                int column = i % rule.Columns;
                int row = i / rule.Columns;
                int x = column * rule.TileWidth;
                int y = row * rule.TileHeight;

                if (x + rule.TileWidth > atlas.Width || y + rule.TileHeight > atlas.Height)
                    break;

                document.Icons.Add(new InterfaceIconMapEntry
                {
                    Id = (rule.BaseId + i).ToString(),
                    Atlas = atlas.Name,
                    X = x,
                    Y = y,
                    Width = rule.TileWidth,
                    Height = rule.TileHeight,
                    Category = rule.Category,
                    Note = $"AutoMap | SlotIndex={i} | Base={rule.BaseId} | Columns={rule.Columns}"
                });
                added++;
            }

            return added;
        }

        private static ulong ParseSortableId(string id) =>
            ulong.TryParse(id, out ulong value) ? value : ulong.MaxValue;

        private sealed class AtlasRule
        {
            public string Category { get; init; } = string.Empty;
            public int BaseId { get; init; }
            public int Count { get; init; }
            public int Columns { get; init; }
            public int TileWidth { get; init; }
            public int TileHeight { get; init; }
            public string Note { get; init; } = string.Empty;
        }
    }
}
