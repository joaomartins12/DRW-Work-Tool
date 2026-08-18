using DRW_Work_Tool.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace DRW_Work_Tool.Core
{
    public static class ConverterManager
    {
        private static readonly List<IGameDataConverter> Converters = new()
        {
            new DigimonListConverter(),
            new TacticsConverter(),
            new AchieveConverter(),
            new BuffConverter(),
            new CashShopConverter(),
            new CharCreateTableConverter(),
            new DigimonEvoConverter(),
            new DMBaseConverter(),
            new EventConverter(),
            new GotchaConverter(),
            new ItemListConverter(),
            new NpcConverter(),
            new RideConverter(),
            new SkillConverter(),
            new WorldMapConverter(),
            new MasterCardConverter(),
            new MapNpcConverter(),
            new UITextConverter(),
            new ExtraExchangeConverter(),
            new MapListConverter(),
            new QuestConverter(),
            new DigimonBookConverter(),
            new TalkConverter(),
            new MapMonsterListConverter(),
            new MapPortalConverter(),
            new MapObjectConverter(),
            new MonsterConverter(),
            new ModelConverter()
        };

        public static bool ConvertEntityBinToXml(string entity)
        {
            AppPaths.EnsureWorkspace();

            string? exactName =
                BinCatalog.ResolveExactName(entity);

            if (exactName == null)
            {
                AppLogger.Warning(
                    $"{entity}: entidade não registada no catálogo.");
                return false;
            }

            IGameDataConverter? converter =
                FindByEntity(exactName);

            if (converter == null)
            {
                AppLogger.Warning(
                    exactName == "Model"
                        ? "Model.dat: conversor DAT -> XML ainda não implementado."
                        : $"{exactName}.bin: conversor BIN -> XML ainda não implementado.");
                return false;
            }

            string source =
                exactName == "Model"
                    ? Path.Combine(AppPaths.Bin, "Model.dat")
                    : Path.Combine(AppPaths.Bin, exactName + ".bin");

            if (!File.Exists(source))
            {
                AppLogger.Warning(
                    exactName == "Model"
                        ? $"Model.dat: ficheiro não encontrado em '{AppPaths.Bin}'."
                        : $"{exactName}.bin: ficheiro não encontrado em '{AppPaths.Bin}'.");
                return false;
            }

            string output =
                AppPaths.GetXmlOutputPath(exactName);

            string outputDisplay =
                exactName == "CashShop" ||
                exactName == "Talk" ||
                exactName == "Monster"
                    ? Path.Combine(AppPaths.Xml, exactName)
                    : output;

            string sourceLabel =
                exactName == "Model"
                    ? "Model.dat: DAT -> XML"
                    : $"{exactName}.bin: BIN -> XML";

            return Execute(
                sourceLabel,
                () => converter.BinToXml(source, output),
                source,
                outputDisplay);
        }

        public static bool ConvertEntityXmlToBin(string entity)
        {
            AppPaths.EnsureWorkspace();

            string? exactName =
                BinCatalog.ResolveExactName(entity);

            if (exactName == null)
            {
                AppLogger.Warning(
                    $"{entity}: entidade não registada no catálogo.");
                return false;
            }

            IGameDataConverter? converter =
                FindByEntity(exactName);

            if (converter == null)
            {
                AppLogger.Warning(
                    exactName == "Model"
                        ? "Model.dat: conversor XML -> DAT ainda não implementado."
                        : $"{exactName}.bin: conversor XML -> BIN ainda não implementado.");
                return false;
            }

            string source =
                GetXmlSourcePath(exactName);

            bool exists =
                exactName == "CashShop" ||
                exactName == "DMBase" ||
                exactName == "Talk" ||
                exactName == "Monster"
                    ? Directory.Exists(source)
                    : File.Exists(source);

            if (!exists)
            {
                AppLogger.ErrorDetailed(
                    exactName == "Model"
                        ? "Model.dat: XML -> DAT"
                        : $"{exactName}.bin: XML -> BIN",
                    $"A entrada XML não foi encontrada: {source}",
                    exactName == "CashShop"
                        ? "Confirma que existe a pasta XML\\CashShop e que contém Main, Main1, TamerInfo, DigimonInfo, AvatarInfo, PackageInfo e WebData."
                        : exactName == "Talk"
                            ? "Confirma que existe XML\\Talk com TalkDigimon.xml, TalkEvent.xml, TalkMessage.xml, TalkTip.xml e TalkLoadingTip.xml."
                            : exactName == "Monster"
                                ? "Confirma que existe XML\\Monster com Monster.xml, MonsterHit.xml, MonstersSkill.xml e MonstersSkillTerms.xml."
                                : $"Confirma que existe '{source}' e que o nome do ficheiro/pasta está correto.");

                return false;
            }

            string output =
                exactName == "Model"
                    ? Path.Combine(AppPaths.Output, "Model", "Model.dat")
                    : AppPaths.GetBinOutputPath(exactName);

            if (exactName == "Model")
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);

            string operation =
                exactName == "Model"
                    ? "Model.dat: XML -> DAT"
                    : $"{exactName}.bin: XML -> BIN";

            return Execute(
                operation,
                () => converter.XmlToBin(source, output),
                source,
                output);
        }

        public static void ConvertAllBinToXml()
        {
            AppPaths.EnsureWorkspace();

            AppLogger.Separator();
            AppLogger.Log("CONVERT ALL TO XML iniciado (BIN + DAT).");

            int ok = 0;
            int failed = 0;
            int ignored = 0;
            int missing = 0;

            foreach (string exactName in BinCatalog.Names)
            {
                string source =
                    exactName == "Model"
                        ? Path.Combine(AppPaths.Bin, "Model.dat")
                        : Path.Combine(AppPaths.Bin, exactName + ".bin");

                if (!File.Exists(source))
                {
                    missing++;
                    continue;
                }

                IGameDataConverter? converter =
                    FindByEntity(exactName);

                if (converter == null)
                {
                    AppLogger.Warning(
                        exactName == "Model"
                            ? "Ignorado (sem conversor): Model.dat"
                            : $"Ignorado (sem conversor): {exactName}.bin");

                    ignored++;
                    continue;
                }

                string output =
                    AppPaths.GetXmlOutputPath(exactName);

                string outputDisplay =
                    exactName == "CashShop" ||
                    exactName == "Talk" ||
                    exactName == "Monster"
                        ? Path.Combine(AppPaths.Xml, exactName)
                        : output;

                string operation =
                    exactName == "Model"
                        ? "Model.dat: DAT -> XML"
                        : $"{exactName}.bin: BIN -> XML";

                if (Execute(
                    operation,
                    () => converter.BinToXml(source, output),
                    source,
                    outputDisplay))
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            foreach (string path in
                Directory.EnumerateFiles(
                    AppPaths.Bin,
                    "*.bin"))
            {
                string baseName =
                    Path.GetFileNameWithoutExtension(path);

                if (BinCatalog.ResolveExactName(baseName) == null)
                {
                    AppLogger.Warning(
                        $"BIN desconhecido fora do catálogo: {Path.GetFileName(path)}");
                }
            }

            foreach (string path in
                Directory.EnumerateFiles(
                    AppPaths.Bin,
                    "*.dat"))
            {
                string baseName =
                    Path.GetFileNameWithoutExtension(path);

                if (!baseName.Equals("Model", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warning(
                        $"DAT desconhecido fora do catálogo: {Path.GetFileName(path)}");
                }
            }

            AppLogger.Log(
                $"CONVERT ALL TO XML terminado. " +
                $"OK={ok}, Erros={failed}, " +
                $"SemConversor={ignored}, Ausentes={missing}.");

            AppLogger.Separator();
        }

        public static void ConvertAllXmlToBin()
        {
            AppPaths.EnsureWorkspace();

            AppLogger.Separator();
            AppLogger.Log("CONVERT ALL TO BIN / DAT iniciado.");

            int ok = 0;
            int failed = 0;
            int ignored = 0;
            int missing = 0;

            foreach (string exactName in BinCatalog.Names)
            {
                string source =
                    GetXmlSourcePath(exactName);

                bool exists =
                    exactName == "CashShop" ||
                    exactName == "DMBase" ||
                    exactName == "Talk" ||
                    exactName == "Monster"
                        ? Directory.Exists(source)
                        : File.Exists(source);

                if (!exists)
                {
                    missing++;
                    continue;
                }

                IGameDataConverter? converter =
                    FindByEntity(exactName);

                if (converter == null)
                {
                    AppLogger.Warning(
                        $"Ignorado (sem conversor): {source}");

                    ignored++;
                    continue;
                }

                string output =
                    exactName == "Model"
                        ? Path.Combine(AppPaths.Output, "Model", "Model.dat")
                        : AppPaths.GetBinOutputPath(exactName);

                if (exactName == "Model")
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);

                string operation =
                    exactName == "Model"
                        ? "Model.dat: XML -> DAT"
                        : $"{exactName}.bin: XML -> BIN";

                if (Execute(
                    operation,
                    () => converter.XmlToBin(source, output),
                    source,
                    output))
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }

            AppLogger.Log(
                $"CONVERT ALL TO BIN / DAT terminado. " +
                $"OK={ok}, Erros={failed}, " +
                $"SemConversor={ignored}, Ausentes={missing}.");

            AppLogger.Separator();
        }

        private static string GetXmlSourcePath(
            string exactName)
        {
            if (exactName == "CashShop" ||
                exactName == "DMBase" ||
                exactName == "Talk" ||
                exactName == "Monster")
            {
                return Path.Combine(
                    AppPaths.Xml,
                    exactName);
            }

            return exactName switch
            {
                "WorldMap" =>
                    Path.Combine(AppPaths.Xml, "WorldMap", "WorldMapInfo.xml"),

                "MasterCard" =>
                    Path.Combine(AppPaths.Xml, "MasterCard", "MasterCards.xml"),

                "Event" =>
                    Path.Combine(AppPaths.Xml, "Event", "Event.xml"),

                "Gotcha" =>
                    Path.Combine(AppPaths.Xml, "Gotcha", "Gotcha.xml"),

                "ItemList" =>
                    Path.Combine(AppPaths.Xml, "ItemList", "ItemList.xml"),

                "Npc" =>
                    Path.Combine(AppPaths.Xml, "Npc", "Npc.xml"),

                "Skill" =>
                    Path.Combine(AppPaths.Xml, "Skill", "Skill.xml"),

                _ =>
                    AppPaths.GetExpectedXmlInputPath(exactName)
            };
        }

        private static IGameDataConverter? FindByEntity(
            string exactName)
        {
            return exactName switch
            {
                "Digimon_List" =>
                    Converters.OfType<DigimonListConverter>()
                        .FirstOrDefault(),

                "Tactics" =>
                    Converters.OfType<TacticsConverter>()
                        .FirstOrDefault(),

                "Achieve" =>
                    Converters.OfType<AchieveConverter>()
                        .FirstOrDefault(),

                "Buff" =>
                    Converters.OfType<BuffConverter>()
                        .FirstOrDefault(),

                "CashShop" =>
                    Converters.OfType<CashShopConverter>()
                        .FirstOrDefault(),

                "CharCreateTable" =>
                    Converters.OfType<CharCreateTableConverter>()
                        .FirstOrDefault(),

                "DigimonEvo" =>
                    Converters.OfType<DigimonEvoConverter>()
                        .FirstOrDefault(),

                "DMBase" =>
                    Converters.OfType<DMBaseConverter>()
                        .FirstOrDefault(),

                "Event" =>
                    Converters.OfType<EventConverter>()
                        .FirstOrDefault(),

                "Gotcha" =>
                    Converters.OfType<GotchaConverter>()
                        .FirstOrDefault(),

                "ItemList" =>
                    Converters.OfType<ItemListConverter>()
                        .FirstOrDefault(),

                "Npc" =>
                    Converters.OfType<NpcConverter>()
                        .FirstOrDefault(),

                "Ride" =>
                    Converters.OfType<RideConverter>()
                        .FirstOrDefault(),

                "Skill" =>
                    Converters.OfType<SkillConverter>()
                        .FirstOrDefault(),

                "WorldMap" =>
                    Converters.OfType<WorldMapConverter>()
                        .FirstOrDefault(),

                "MasterCard" =>
                    Converters.OfType<MasterCardConverter>()
                        .FirstOrDefault(),

                "MapNpc" =>
                    Converters.OfType<MapNpcConverter>()
                        .FirstOrDefault(),

                "UIText" =>
                    Converters.OfType<UITextConverter>()
                        .FirstOrDefault(),

                "ExtraExchange" =>
                    Converters.OfType<ExtraExchangeConverter>()
                        .FirstOrDefault(),

                "MapList" =>
                    Converters.OfType<MapListConverter>()
                        .FirstOrDefault(),

                "Quest" =>
                    Converters.OfType<QuestConverter>()
                        .FirstOrDefault(),

                "Digimon_Book" =>
                    Converters.OfType<DigimonBookConverter>()
                        .FirstOrDefault(),

                "Talk" =>
                    Converters.OfType<TalkConverter>()
                        .FirstOrDefault(),

                "MapMonsterList" =>
                    Converters.OfType<MapMonsterListConverter>()
                        .FirstOrDefault(),

                "MapPortal" =>
                    Converters.OfType<MapPortalConverter>()
                        .FirstOrDefault(),

                "MapObject" =>
                    Converters.OfType<MapObjectConverter>()
                        .FirstOrDefault(),

                "Monster" =>
                    Converters.OfType<MonsterConverter>()
                        .FirstOrDefault(),

                "Model" =>
                    Converters.OfType<ModelConverter>()
                        .FirstOrDefault(),

                _ => null
            };
        }

        private static bool Execute(
            string operation,
            Action action,
            string source,
            string output)
        {
            try
            {
                AppLogger.Log($"{operation} iniciado.");
                AppLogger.Log($"Entrada: {source}");
                AppLogger.Log($"Saída:   {output}");

                action();

                AppLogger.Success(
                    $"{operation}: SUCESSO.");

                return true;
            }
            catch (Exception ex)
            {
                (string reason, string solution) =
                    Diagnose(ex, source);

                AppLogger.ErrorDetailed(
                    operation,
                    reason,
                    solution);

                return false;
            }
        }

        private static (string Reason, string Solution) Diagnose(
            Exception ex,
            string source)
        {
            Exception actual =
                ex is AggregateException aggregate &&
                aggregate.InnerException != null
                    ? aggregate.InnerException
                    : ex;

            if (actual is XmlException xml)
            {
                string reason =
                    $"XML mal formado em '{source}'. " +
                    $"Linha {xml.LineNumber}, posição {xml.LinePosition}: {xml.Message}";

                string solution =
                    "Abre o XML indicado e verifica principalmente:\n" +
                    "- tags <...> que não foram fechadas com </...>;\n" +
                    "- um '<' ou '>' escrito dentro de texto normal;\n" +
                    "- '&' não escapado (usa &amp; quando fizer parte do texto);\n" +
                    "- aspas/atributos incompletos;\n" +
                    "- elementos fechados na ordem errada.\n" +
                    "A linha e a posição indicadas acima são o melhor ponto para começar.";

                return (reason, solution);
            }

            if (actual is FileNotFoundException file)
            {
                return (
                    file.Message,
                    "Confirma se o ficheiro/folder esperado existe, se mantém o nome original " +
                    "e se não foi movido para outra subpasta.");
            }

            if (actual is DirectoryNotFoundException)
            {
                return (
                    actual.Message,
                    "Confirma a estrutura de pastas dentro de XML. " +
                    "Na CashShop não deves mover Main/Main1, TamerInfo/TamerInfo1, " +
                    "DigimonInfo/DigimonInfo1, AvatarInfo/AvatarInfo1, PackageInfo/PackageInfo1 e WebData.");
            }

            if (actual is OverflowException)
            {
                return (
                    actual.Message,
                    "Existe provavelmente um número fora do limite do tipo binário. " +
                    "Revê o último campo editado e reduz o valor para o intervalo esperado.");
            }

            if (actual is FormatException)
            {
                return (
                    actual.Message,
                    "Um campo que deveria conter apenas números tem texto ou caracteres inválidos. " +
                    "Remove espaços/caracteres extra e volta a usar um valor numérico.");
            }

            if (actual is EndOfStreamException)
            {
                return (
                    actual.Message,
                    "O BIN terminou antes da estrutura esperada. " +
                    "Confirma se o ficheiro está completo e se corresponde à versão suportada pelo converter.");
            }

            if (actual is InvalidDataException invalid)
            {
                string msg = invalid.Message;

                if (msg.Contains(
                    "validação de limites do CLIENT",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return (
                        msg,
                        "Quest.xml não foi empacotado porque ultrapassa limites conhecidos do client.\n" +
                        "- UniqID máximo permitido: 6144 (6144 é válido; 6145+ é bloqueado).\n" +
                        "- Rewards de ITEM (RewardType=2): máximo 6 por QuestInfo.\n" +
                        "O motivo acima lista todas as quests problemáticas com UniqID, título e items. " +
                        "Corrige todas e volta a fazer PACK.");
                }

                if (msg.Contains(
                    "ResetQuest",
                    StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains(
                    "GoalCount",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return (
                        msg,
                        "Este campo existe no XML para compatibilidade/representação, mas não possui bytes físicos " +
                        "nesta versão do Quest.bin. Mantém o valor em 0.");
                }

                if (msg.Contains(
                    "bytes extra",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return (
                        msg,
                        "O BIN contém dados depois da estrutura conhecida. " +
                        "Confirma se estás a usar o CashShop.bin correto ou se o formato foi atualizado no client.");
                }

                if (msg.Contains(
                    "tamanho",
                    StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains(
                    "bytes",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return (
                        msg,
                        "Compara o número de registos/counts do XML e confirma se não adicionaste " +
                        "ou removeste bytes/campos manualmente. Não alteres o formato das strings fixas ou datas.");
                }

                if (msg.Contains(
                    "falta o elemento",
                    StringComparison.OrdinalIgnoreCase) ||
                    msg.Contains(
                    "root",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return (
                        msg,
                        "Restaura o elemento/tag indicado com o nome exato. " +
                        "Os nomes das tags são case-sensitive para este converter.");
                }

                return (
                    msg,
                    "Revê o ficheiro e o campo mencionado no motivo. " +
                    "Se o erro começou após uma edição, compara essa zona com uma cópia XML exportada diretamente do BIN.");
            }

            return (
                $"{actual.GetType().Name}: {actual.Message}",
                "Consulta as linhas imediatamente anteriores no log para identificar o ficheiro em processamento. " +
                "Se o problema persistir, restaura uma cópia original e repete a conversão para isolar a edição que causou a falha.");
        }
    }
}
