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
        private static readonly Regex RxItemAtlas =
            new Regex(
                @"^icon(?<n>\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxSkillAtlas =
            new Regex(
                @"^sicon(?<n>\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex RxCashShopAtlas =
            new Regex(
                @"^cashshop(?<g>\d+)_(?<p>\d+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static InterfaceIconMapBuildResult BuildAndAnalyze(
            string? databaseRoot = null,
            IProgress<string>? progress = null)
        {
            var service = new ImageDatabaseIndexService(databaseRoot);
            service.Load(rebuildIndexIfMissing: true);

            string root = service.DatabaseRoot;
            progress?.Report("Icon Map: a carregar ImageDatabase.json...");

            var map = new InterfaceIconMapDocument
            {
                Version = 1
            };

            var warnings = new List<string>();
            var unmapped = new List<string>();
            var analysis = new StringBuilder();

            analysis.AppendLine("Interface Icon Map - Reajuste / Analyse");
            analysis.AppendLine("========================================");
            analysis.AppendLine($"Generated UTC : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            analysis.AppendLine($"Database root  : {root}");
            analysis.AppendLine();
            analysis.AppendLine("Regras aplicadas:");
            analysis.AppendLine("- siconNN  -> base = NN * 1000; mapeia 256 slots (1000..1255, 2000..2255, ...)");
            analysis.AppendLine("- iconNN   -> base = NN * 1000; mapeia até 1000 slots lógicos (capacidade física pode ser 1024)");
            analysis.AppendLine("- achieve_icon    -> 0..255");
            analysis.AppendLine("- achieve_icon_02 -> 300..555");
            analysis.AppendLine("- achieve_icon_03 -> 556..811");
            analysis.AppendLine("- cashshopG_PPP   -> base = GPPP00; mapeia no máximo 100 slots lógicos por atlas");
            analysis.AppendLine();
            analysis.AppendLine("Atlases analisados");
            analysis.AppendLine("------------------");

            foreach (InterfaceAtlasEntry atlas in service.Index.InterfaceAtlases
                         .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                progress?.Report($"Icon Map: a analisar {atlas.Name}...");

                AtlasRule? rule = TryResolveRule(atlas, warnings, out string? warning);

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
                    $"Grid={atlas.Columns}x{atlas.Rows} | Size={atlas.Width}x{atlas.Height}");

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

            if (unmapped.Count > 0)
            {
                analysis.AppendLine();
                analysis.AppendLine("Atlases sem regra automática:");
                foreach (string atlasName in unmapped)
                    analysis.AppendLine($"- {atlasName}");
            }

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

        private static AtlasRule? TryResolveRule(
            InterfaceAtlasEntry atlas,
            List<string> warnings,
            out string? immediateWarning)
        {
            immediateWarning = null;

            if (atlas.Columns <= 0 || atlas.Rows <= 0 || atlas.TileWidth <= 0 || atlas.TileHeight <= 0)
            {
                immediateWarning = "dimensões/grelha inválidas; atlas ignorado.";
                return null;
            }

            Match mSkill = RxSkillAtlas.Match(atlas.Name);
            if (mSkill.Success)
            {
                int n = int.Parse(mSkill.Groups["n"].Value);
                if (atlas.Capacity != 256)
                {
                    immediateWarning =
                        $"esperado 256 slots físicos para skill atlas, mas o atlas tem {atlas.Capacity}. O mapeamento será truncado para {Math.Min(atlas.Capacity, 256)}.";
                }

                return new AtlasRule
                {
                    Category = "Skill",
                    BaseId = n * 1000,
                    Count = Math.Min(atlas.Capacity, 256),
                    Note = $"sicon{n:00} => {n * 1000}..{n * 1000 + Math.Min(atlas.Capacity, 256) - 1}"
                };
            }

            if (atlas.Name.Equals("achieve_icon", StringComparison.OrdinalIgnoreCase))
            {
                if (atlas.Capacity != 256)
                {
                    immediateWarning =
                        $"esperado 256 slots físicos para achieve_icon, mas o atlas tem {atlas.Capacity}.";
                }

                return new AtlasRule
                {
                    Category = "Achieve",
                    BaseId = 0,
                    Count = Math.Min(atlas.Capacity, 256),
                    Note = "achieve_icon => 0..255"
                };
            }

            if (atlas.Name.Equals("achieve_icon_02", StringComparison.OrdinalIgnoreCase))
            {
                if (atlas.Capacity != 256)
                {
                    immediateWarning =
                        $"esperado 256 slots físicos para achieve_icon_02, mas o atlas tem {atlas.Capacity}.";
                }

                return new AtlasRule
                {
                    Category = "Achieve",
                    BaseId = 300,
                    Count = Math.Min(atlas.Capacity, 256),
                    Note = "achieve_icon_02 => 300..555"
                };
            }

            if (atlas.Name.Equals("achieve_icon_03", StringComparison.OrdinalIgnoreCase))
            {
                if (atlas.Capacity != 256)
                {
                    immediateWarning =
                        $"esperado 256 slots físicos para achieve_icon_03, mas o atlas tem {atlas.Capacity}.";
                }

                return new AtlasRule
                {
                    Category = "Achieve",
                    BaseId = 556,
                    Count = Math.Min(atlas.Capacity, 256),
                    Note = "achieve_icon_03 => 556..811"
                };
            }

            Match mCash = RxCashShopAtlas.Match(atlas.Name);
            if (mCash.Success)
            {
                string g = mCash.Groups["g"].Value;
                string p = mCash.Groups["p"].Value;
                int baseId = int.Parse($"{g}{p}00");
                int count = Math.Min(atlas.Capacity, 100);

                if (atlas.Capacity > 100)
                {
                    immediateWarning =
                        $"o atlas físico tem {atlas.Capacity} slots, mas a janela lógica cashshop foi limitada a 100 IDs ({baseId}..{baseId + count - 1}).";
                }

                return new AtlasRule
                {
                    Category = "CashShop",
                    BaseId = baseId,
                    Count = count,
                    Note = $"cashshop{g}_{p} => {baseId}..{baseId + count - 1}"
                };
            }

            Match mItem = RxItemAtlas.Match(atlas.Name);
            if (mItem.Success)
            {
                int n = int.Parse(mItem.Groups["n"].Value);
                int count = Math.Min(atlas.Capacity, 1000);

                if (atlas.Capacity > 1000)
                {
                    immediateWarning =
                        $"o atlas físico tem {atlas.Capacity} slots, mas o namespace lógico de icon{n:00} foi limitado a 1000 IDs ({n * 1000}..{n * 1000 + count - 1}).";
                }

                return new AtlasRule
                {
                    Category = "Item",
                    BaseId = n * 1000,
                    Count = count,
                    Note = $"icon{n:00} => {n * 1000}..{n * 1000 + count - 1}"
                };
            }

            return null;
        }

        private static int AddEntries(
            InterfaceIconMapDocument document,
            InterfaceAtlasEntry atlas,
            AtlasRule rule)
        {
            int count = Math.Min(rule.Count, atlas.Capacity);

            for (int i = 0; i < count; i++)
            {
                int column = i % atlas.Columns;
                int row = i / atlas.Columns;

                document.Icons.Add(
                    new InterfaceIconMapEntry
                    {
                        Id = (rule.BaseId + i).ToString(),
                        Atlas = atlas.Name,
                        X = column * atlas.TileWidth,
                        Y = row * atlas.TileHeight,
                        Width = atlas.TileWidth,
                        Height = atlas.TileHeight,
                        Category = rule.Category,
                        Note = $"AutoMap | SlotIndex={i} | Base={rule.BaseId}"
                    });
            }

            return count;
        }

        private static ulong ParseSortableId(string id) =>
            ulong.TryParse(id, out ulong value)
                ? value
                : ulong.MaxValue;

        private sealed class AtlasRule
        {
            public string Category { get; init; } = string.Empty;
            public int BaseId { get; init; }
            public int Count { get; init; }
            public string Note { get; init; } = string.Empty;
        }
    }
}
