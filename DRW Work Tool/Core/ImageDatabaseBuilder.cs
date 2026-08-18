using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DRW_Work_Tool.Core
{
    public sealed class ImageDatabaseBuildResult
    {
        public int FoldersScanned { get; internal set; }
        public int InterfaceIconsCopied { get; internal set; }
        public int DigimonIconsCopied { get; internal set; }
        public int TamerIconsCopied { get; internal set; }
        public int NpcIconsCopied { get; internal set; }

        public int TotalFilesCopied =>
            InterfaceIconsCopied +
            DigimonIconsCopied +
            TamerIconsCopied +
            NpcIconsCopied;

        public string DatabaseRoot { get; internal set; } = string.Empty;
    }

    public static class ImageDatabaseBuilder
    {
        private static readonly HashSet<string> AllowedInterfaceExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".dds",
                ".bmp",
                ".tga"
            };

        private static readonly Regex CashShopRegex =
            new(
                @"^cashshop(?<group>[2-5])_(?<number>10[1-9])$",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled);

        private static readonly Regex IconRegex =
            new(
                @"^icon(?<number>\d{2})$",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled);

        private static readonly Regex SmallIconRegex =
            new(
                @"^sicon(?<number>\d{2})$",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled);

        private static readonly Regex CharacterIconRegex =
            new(
                @"^(?<id>\d+)_.*s\.tga$",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled);

        private static readonly Regex NpcIconRegex =
            new(
                @"^(?<id>\d+).*l\.tga$",
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled);

        public static ImageDatabaseBuildResult Build(
            string selectedFolder,
            IProgress<string>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(selectedFolder))
                throw new ArgumentException(
                    "A pasta selecionada está vazia.",
                    nameof(selectedFolder));

            string selected =
                Path.GetFullPath(selectedFolder);

            if (!Directory.Exists(selected))
            {
                throw new DirectoryNotFoundException(
                    $"A pasta selecionada não existe: {selected}");
            }

            string dataRoot =
                ResolveDataRoot(selected);

            string interfaceIconRoot =
                Path.Combine(
                    dataRoot,
                    "interface",
                    "icon");

            string digimonRoot =
                Path.Combine(
                    dataRoot,
                    "digimon");

            string tamerRoot =
                Path.Combine(
                    dataRoot,
                    "tamer");

            string npcRoot =
                Path.Combine(
                    dataRoot,
                    "npc");

            if (!Directory.Exists(interfaceIconRoot) &&
                !Directory.Exists(digimonRoot) &&
                !Directory.Exists(tamerRoot) &&
                !Directory.Exists(npcRoot))
            {
                throw new DirectoryNotFoundException(
                    "Não encontrei nenhuma das folders esperadas dentro de Data:\n" +
                    @"- data\interface\icon" + "\n" +
                    @"- data\digimon" + "\n" +
                    @"- data\tamer" + "\n" +
                    @"- data\npc" + "\n\n" +
                    $"Data resolvida: {dataRoot}");
            }

            string databaseRoot =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "ImgDatabase");

            Directory.CreateDirectory(databaseRoot);

            var result = new ImageDatabaseBuildResult
            {
                DatabaseRoot = databaseRoot
            };

            if (Directory.Exists(interfaceIconRoot))
            {
                progress?.Report(
                    @"ImageDatabase: a procurar em data\interface\icon...");

                string destination =
                    Path.Combine(
                        databaseRoot,
                        "interface",
                        "icon");

                Directory.CreateDirectory(destination);

                Traverse(
                    interfaceIconRoot,
                    result,
                    file =>
                    {
                        if (!IsWantedInterfaceIcon(file))
                            return;

                        string target =
                            Path.Combine(
                                destination,
                                Path.GetFileName(file));

                        File.Copy(
                            file,
                            target,
                            overwrite: true);

                        result.InterfaceIconsCopied++;
                    });
            }

            if (Directory.Exists(digimonRoot))
            {
                progress?.Report(
                    @"ImageDatabase: a procurar icons em data\digimon...");

                string destinationRoot =
                    Path.Combine(
                        databaseRoot,
                        "Digimon");

                Directory.CreateDirectory(destinationRoot);

                Traverse(
                    digimonRoot,
                    result,
                    file =>
                    {
                        if (!TryGetCharacterIconId(
                            file,
                            out string id))
                        {
                            return;
                        }

                        string sourceFolderName =
                            new DirectoryInfo(
                                Path.GetDirectoryName(file)
                                ?? digimonRoot).Name;

                        string destinationFolder =
                            Path.Combine(
                                destinationRoot,
                                sourceFolderName);

                        Directory.CreateDirectory(
                            destinationFolder);

                        string target =
                            Path.Combine(
                                destinationFolder,
                                id + ".tga");

                        File.Copy(
                            file,
                            target,
                            overwrite: true);

                        result.DigimonIconsCopied++;
                    });
            }

            if (Directory.Exists(tamerRoot))
            {
                progress?.Report(
                    @"ImageDatabase: a procurar icons em data\tamer...");

                string destinationRoot =
                    Path.Combine(
                        databaseRoot,
                        "Tamer");

                Directory.CreateDirectory(destinationRoot);

                Traverse(
                    tamerRoot,
                    result,
                    file =>
                    {
                        if (!TryGetCharacterIconId(
                            file,
                            out string id))
                        {
                            return;
                        }

                        string sourceFolderName =
                            new DirectoryInfo(
                                Path.GetDirectoryName(file)
                                ?? tamerRoot).Name;

                        string destinationFolder =
                            Path.Combine(
                                destinationRoot,
                                sourceFolderName);

                        Directory.CreateDirectory(
                            destinationFolder);

                        string target =
                            Path.Combine(
                                destinationFolder,
                                id + ".tga");

                        File.Copy(
                            file,
                            target,
                            overwrite: true);

                        result.TamerIconsCopied++;
                    });
            }

            if (Directory.Exists(npcRoot))
            {
                progress?.Report(
                    @"ImageDatabase: a procurar NPC icons em data\npc (*l.tga)...");

                string destinationRoot =
                    Path.Combine(
                        databaseRoot,
                        "Npc");

                Directory.CreateDirectory(
                    destinationRoot);

                Traverse(
                    npcRoot,
                    result,
                    file =>
                    {
                        if (!TryGetNpcIconId(
                            file,
                            out string id))
                        {
                            return;
                        }

                        string sourceFolderName =
                            new DirectoryInfo(
                                Path.GetDirectoryName(file)
                                ?? npcRoot).Name;

                        string destinationFolder =
                            Path.Combine(
                                destinationRoot,
                                sourceFolderName);

                        Directory.CreateDirectory(
                            destinationFolder);

                        string target =
                            Path.Combine(
                                destinationFolder,
                                id + ".tga");

                        File.Copy(
                            file,
                            target,
                            overwrite: true);

                        result.NpcIconsCopied++;
                    });
            }

            progress?.Report(
                "ImageDatabase: a criar índice da database...");

            ImageDatabaseIndexDocument index =
                ImageDatabaseIndexBuilder.Rebuild(databaseRoot);

            progress?.Report(
                $"ImageDatabase concluída: {result.TotalFilesCopied:N0} imagens. " +
                $"Atlases={index.InterfaceAtlases.Count:N0}, " +
                $"Digimon indexados={index.DigimonIcons.Count:N0}, " +
                $"Tamers indexados={index.TamerIcons.Count:N0}, " +
                $"NPCs indexados={index.NpcIcons.Count:N0}.");

            return result;
        }

        private static string ResolveDataRoot(
            string selected)
        {
            string selectedName =
                new DirectoryInfo(selected).Name;

            if (selectedName.Equals(
                "data",
                StringComparison.OrdinalIgnoreCase))
            {
                return selected;
            }

            string directData =
                FindChildDirectory(
                    selected,
                    "data");

            if (!string.IsNullOrEmpty(directData))
                return directData;

            // Também aceitamos que o utilizador escolha diretamente
            // uma pasta que já seja equivalente à Data mesmo que tenha
            // outro casing/nome, desde que possua as subfolders esperadas.
            bool looksLikeData =
                Directory.Exists(
                    FindChildDirectory(selected, "digimon")) ||
                Directory.Exists(
                    FindChildDirectory(selected, "tamer")) ||
                Directory.Exists(
                    FindChildDirectory(selected, "npc")) ||
                Directory.Exists(
                    FindChildDirectory(selected, "interface"));

            if (looksLikeData)
                return selected;

            throw new DirectoryNotFoundException(
                "Não encontrei a pasta Data dentro da folder selecionada.\n\n" +
                "Seleciona a root do client (onde existe Data) " +
                "ou seleciona diretamente a pasta Data.");
        }

        private static string FindChildDirectory(
            string parent,
            string expectedName)
        {
            try
            {
                return Directory
                    .EnumerateDirectories(
                        parent,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(
                        x => Path
                            .GetFileName(x)
                            .Equals(
                                expectedName,
                                StringComparison.OrdinalIgnoreCase))
                    ?? string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static void Traverse(
            string root,
            ImageDatabaseBuildResult result,
            Action<string> fileAction)
        {
            var pending =
                new Stack<string>();

            pending.Push(root);

            while (pending.Count > 0)
            {
                string current =
                    pending.Pop();

                result.FoldersScanned++;

                IEnumerable<string> files;

                try
                {
                    files =
                        Directory.EnumerateFiles(
                            current,
                            "*",
                            SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    try
                    {
                        fileAction(file);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Continua a database mesmo que um ficheiro isolado
                        // esteja protegido.
                    }
                    catch (IOException)
                    {
                        // Continua a database se um ficheiro estiver em uso.
                    }
                }

                IEnumerable<string> directories;

                try
                {
                    directories =
                        Directory.EnumerateDirectories(
                            current,
                            "*",
                            SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string directory in directories)
                    pending.Push(directory);
            }
        }

        private static bool IsWantedInterfaceIcon(
            string file)
        {
            string extension =
                Path.GetExtension(file);

            if (!AllowedInterfaceExtensions.Contains(extension))
                return false;

            string name =
                Path.GetFileNameWithoutExtension(file);

            if (name.Equals(
                    "achieve_icon",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "achieve_icon_02",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Equals(
                    "achieve_icon_03",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Match cashShop =
                CashShopRegex.Match(name);

            if (cashShop.Success)
                return true;

            Match icon =
                IconRegex.Match(name);

            if (icon.Success &&
                int.TryParse(
                    icon.Groups["number"].Value,
                    out int iconNumber) &&
                iconNumber >= 1 &&
                iconNumber <= 49)
            {
                return true;
            }

            Match smallIcon =
                SmallIconRegex.Match(name);

            return smallIcon.Success &&
                   int.TryParse(
                       smallIcon.Groups["number"].Value,
                       out int smallIconNumber) &&
                   smallIconNumber >= 1 &&
                   smallIconNumber <= 7;
        }

        private static bool TryGetCharacterIconId(
            string file,
            out string id)
        {
            id = string.Empty;

            if (!Path
                .GetExtension(file)
                .Equals(
                    ".tga",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fileName =
                Path.GetFileName(file);

            Match match =
                CharacterIconRegex.Match(fileName);

            if (!match.Success)
                return false;

            id =
                match.Groups["id"].Value;

            return !string.IsNullOrWhiteSpace(id);
        }

        private static bool TryGetNpcIconId(
            string file,
            out string id)
        {
            id = string.Empty;

            if (!Path
                .GetExtension(file)
                .Equals(
                    ".tga",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Match match =
                NpcIconRegex.Match(
                    Path.GetFileName(file));

            if (!match.Success)
                return false;

            id =
                match.Groups["id"].Value;

            return !string.IsNullOrWhiteSpace(
                id);
        }
    }
}
