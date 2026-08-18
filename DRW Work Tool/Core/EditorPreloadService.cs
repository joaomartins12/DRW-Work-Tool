using System;
using System.IO;
using System.Drawing;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DRW_Work_Tool.Core
{
    public sealed record StartupPreloadProgress(
        int Percent,
        string Message);

    public static class EditorPreloadService
    {
        private static readonly object Sync = new();

        private static ItemListEditorService? _itemList;
        private static string _itemListPath = string.Empty;

        private static EditorReferenceCatalogService? _references;
        private static string _referenceAnchorPath = string.Empty;

        private static NpcEditorService? _npcService;
        private static string _npcPath = string.Empty;

        private static DigimonListEditorService? _digimonList;
        private static string _digimonListPath = string.Empty;

        private static DigimonModelReferenceService? _digimonModels;
        private static string _digimonModelPath = string.Empty;

        private static SkillReferencePickerService? _skillReferences;
        private static string _skillReferencePath = string.Empty;

        private static SkillEditorService? _skillEditor;
        private static string _skillEditorPath = string.Empty;

        private static BuffReferenceService? _buffReferences;
        private static string _buffReferencePath = string.Empty;

        private static DigimonEvoEditorService? _digimonEvo;
        private static string _digimonEvoPath = string.Empty;

        private static MonsterEditorService? _monsterEditor;
        private static string _monsterEditorPath = string.Empty;

        private static MonsterSkillEditorService? _monsterSkillEditor;
        private static string _monsterSkillEditorPath = string.Empty;

        private static MonsterSkillTermsEditorService? _monsterSkillTermsEditor;
        private static string _monsterSkillTermsEditorPath = string.Empty;

        private static readonly Dictionary<string, ItemMakingEditorService>
            ItemMakingCache =
                new(StringComparer.OrdinalIgnoreCase);

        private static Task? _preloadTask;
        private static Exception? _preloadError;

        public static bool IsReady
        {
            get
            {
                lock (Sync)
                    return
                        _itemList != null &&
                        _references != null &&
                        _digimonModels != null &&
                        _digimonList != null &&
                        _preloadError == null;
            }
        }

        public static bool IsCompleted
        {
            get
            {
                lock (Sync)
                    return
                        _preloadTask != null &&
                        _preloadTask.IsCompleted;
            }
        }

        public static Exception? LastError
        {
            get
            {
                lock (Sync)
                    return _preloadError;
            }
        }

        public static Task StartAsync(
            CancellationToken cancellationToken = default) =>
            StartAsync(
                progress: null,
                cancellationToken);

        public static Task StartAsync(
            IProgress<StartupPreloadProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            lock (Sync)
            {
                if (_preloadTask != null)
                    return _preloadTask;

                _preloadError = null;

                _preloadTask =
                    Task.Run(
                        () =>
                        {
                            try
                            {
                                PreloadCore(
                                    cancellationToken,
                                    progress);
                            }
                            catch (Exception ex)
                            {
                                lock (Sync)
                                    _preloadError = ex;

                                throw;
                            }
                        },
                        cancellationToken);

                return _preloadTask;
            }
        }

        public static async Task<BuffReferenceService?>
            GetBuffReferencesAsync()
        {
            string path =
                Path.Combine(
                    AppPaths.Xml,
                    "Buff",
                    "Buff.xml");

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            lock (Sync)
            {
                if (_buffReferences != null &&
                    _buffReferencePath.Equals(
                        Path.GetFullPath(path),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _buffReferences;
                }
            }

            if (!File.Exists(path))
                return null;

            return await Task.Run(
                () =>
                {
                    BuffReferenceService loaded =
                        BuffReferenceService.Load(
                            path);

                    lock (Sync)
                    {
                        _buffReferences = loaded;
                        _buffReferencePath =
                            Path.GetFullPath(path);
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static BuffReferenceService?
            TryGetBuffReferences()
        {
            lock (Sync)
                return _buffReferences;
        }

        public static async Task<SkillEditorService>
            GetSkillEditorAsync(
                string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                    // Normal open-on-demand fallback below.
                }
            }

            lock (Sync)
            {
                if (_skillEditor != null &&
                    _skillEditorPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _skillEditor;
                }
            }

            return await Task.Run(
                () =>
                {
                    SkillEditorService loaded =
                        SkillEditorService.Load(full);

                    lock (Sync)
                    {
                        _skillEditor = loaded;
                        _skillEditorPath = full;
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static SkillEditorService?
            TryGetSkillEditor()
        {
            lock (Sync)
                return _skillEditor;
        }

        public static void ReplaceSkillEditor(
            string filePath,
            SkillEditorService service)
        {
            string full =
                Path.GetFullPath(filePath);

            lock (Sync)
            {
                _skillEditor = service;
                _skillEditorPath = full;
            }
        }

        public static void InvalidateSkillEditor(
            string? filePath = null)
        {
            lock (Sync)
            {
                if (filePath == null ||
                    _skillEditorPath.Equals(
                        Path.GetFullPath(filePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _skillEditor = null;
                    _skillEditorPath = string.Empty;
                }
            }
        }

        public static void InvalidateSkillReferences()
        {
            lock (Sync)
            {
                _skillReferences = null;
                _skillReferencePath = string.Empty;
            }
        }

        public static async Task<SkillReferencePickerService>
            GetSkillReferencesAsync(
                string? skillXmlPath = null)
        {
            string full =
                Path.GetFullPath(
                    string.IsNullOrWhiteSpace(skillXmlPath)
                        ? Path.Combine(
                            AppPaths.Xml,
                            "Skill",
                            "Skill.xml")
                        : skillXmlPath);

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                    // Normal on-demand fallback below.
                }
            }

            lock (Sync)
            {
                if (_skillReferences != null &&
                    _skillReferencePath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _skillReferences;
                }
            }

            return await Task.Run(
                () =>
                {
                    SkillReferencePickerService service =
                        SkillReferencePickerService.Load(full);

                    lock (Sync)
                    {
                        _skillReferences = service;
                        _skillReferencePath = full;
                    }

                    return service;
                })
                .ConfigureAwait(false);
        }

        public static SkillReferencePickerService?
            TryGetSkillReferences()
        {
            lock (Sync)
                return _skillReferences;
        }

        public static async Task<ItemListEditorService> GetItemListAsync(
            string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                    // Se o preload falhou por não existir ItemList na altura,
                    // tentamos carregar normalmente abaixo.
                }
            }

            lock (Sync)
            {
                if (_itemList != null &&
                    _itemListPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _itemList;
                }
            }

            return await Task.Run(
                () => LoadAndCacheItemList(full))
                .ConfigureAwait(false);
        }

        public static ItemListEditorService GetItemList(
            string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            lock (Sync)
            {
                if (_itemList != null &&
                    _itemListPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _itemList;
                }
            }

            return LoadAndCacheItemList(full);
        }

        public static async Task<EditorReferenceCatalogService>
            GetReferencesAsync(
                string anchorXmlPath)
        {
            string full =
                Path.GetFullPath(
                    anchorXmlPath);

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                    // Fall through to direct background load.
                }
            }

            lock (Sync)
            {
                if (_references != null)
                    return _references;
            }

            return await Task.Run(
                () =>
                {
                    var loaded =
                        EditorReferenceCatalogService.Load(
                            full);

                    lock (Sync)
                    {
                        _references = loaded;
                        _referenceAnchorPath = full;
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static async Task<NpcEditorService>
            GetNpcServiceAsync(
                string filePath)
        {
            string full =
                Path.GetFullPath(
                    filePath);

            lock (Sync)
            {
                if (_npcService != null &&
                    _npcPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _npcService;
                }
            }

            return await Task.Run(
                () =>
                {
                    var loaded =
                        new NpcEditorService(
                            full);

                    lock (Sync)
                    {
                        _npcService = loaded;
                        _npcPath = full;
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static async Task<ItemMakingEditorService>
            GetItemMakingServiceAsync(
                string filePath)
        {
            string full =
                Path.GetFullPath(
                    filePath);

            lock (Sync)
            {
                if (ItemMakingCache.TryGetValue(
                    full,
                    out ItemMakingEditorService? cached))
                {
                    return cached;
                }
            }

            return await Task.Run(
                () =>
                {
                    var loaded =
                        new ItemMakingEditorService(
                            full);

                    lock (Sync)
                        ItemMakingCache[full] = loaded;

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static async Task<DigimonModelReferenceService>
            GetDigimonModelsAsync(
                string? modelXmlPath = null)
        {
            Task? preloadTask;

            lock (Sync)
                preloadTask = _preloadTask;

            if (preloadTask != null)
            {
                try
                {
                    await preloadTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            lock (Sync)
            {
                if (_digimonModels != null)
                    return _digimonModels;
            }

            return await Task.Run(
                () =>
                {
                    DigimonModelReferenceService loaded =
                        DigimonModelReferenceService.Load(modelXmlPath);

                    lock (Sync)
                    {
                        _digimonModels = loaded;
                        _digimonModelPath = loaded.SourcePath;
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static DigimonModelReferenceService? TryGetDigimonModels()
        {
            lock (Sync)
                return _digimonModels;
        }

        public static async Task<DigimonListEditorService>
            GetDigimonListAsync(
                string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort startup preload; direct fallback below.
                }
            }

            lock (Sync)
            {
                if (_digimonList != null &&
                    _digimonListPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _digimonList;
                }
            }

            return await Task.Run(
                () =>
                {
                    var loaded =
                        DigimonListEditorService.Load(full);

                    lock (Sync)
                    {
                        _digimonList = loaded;
                        _digimonListPath = full;
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static Bitmap? TryGetDigimonIcon(
            uint id)
        {
            lock (Sync)
                return _digimonList?.GetIcon(id);
        }

        public static void ReplaceDigimonListDocument(
            string filePath,
            XDocument document)
        {
            string full =
                Path.GetFullPath(filePath);

            lock (Sync)
            {
                if (_digimonList != null &&
                    _digimonListPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _digimonList.ReplaceDocument(document);
                }
            }
        }

        public static void InvalidateDigimonList()
        {
            lock (Sync)
            {
                _digimonList = null;
                _digimonListPath = string.Empty;
                _digimonModels = null;
                _digimonModelPath = string.Empty;
                _skillReferences = null;
                _skillReferencePath = string.Empty;
            }
        }

        public static async Task<DigimonEvoEditorService>
            GetDigimonEvoAsync(
                string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            Task? preload;

            lock (Sync)
                preload = _preloadTask;

            if (preload != null)
            {
                try
                {
                    await preload.ConfigureAwait(false);
                }
                catch
                {
                    // Normal on-demand fallback below.
                }
            }

            lock (Sync)
            {
                if (_digimonEvo != null &&
                    _digimonEvoPath.Equals(
                        full,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _digimonEvo;
                }
            }

            return await Task.Run(
                () =>
                {
                    DigimonEvoEditorService loaded =
                        LoadDigimonEvoService(full);

                    lock (Sync)
                    {
                        _digimonEvo = loaded;
                        _digimonEvoPath = full;
                    }

                    return loaded;
                })
                .ConfigureAwait(false);
        }

        public static DigimonEvoEditorService?
            TryGetDigimonEvo()
        {
            lock (Sync)
                return _digimonEvo;
        }

        public static void ReplaceDigimonEvo(
            string filePath,
            DigimonEvoEditorService service)
        {
            string full =
                Path.GetFullPath(filePath);

            lock (Sync)
            {
                _digimonEvo = service;
                _digimonEvoPath = full;
            }
        }

        public static void InvalidateDigimonEvo(
            string? filePath = null)
        {
            lock (Sync)
            {
                if (filePath == null ||
                    _digimonEvoPath.Equals(
                        Path.GetFullPath(filePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _digimonEvo = null;
                    _digimonEvoPath = string.Empty;
                }
            }
        }

        public static void InvalidateReferenceCatalog()
        {
            lock (Sync)
            {
                _references = null;
                _referenceAnchorPath = string.Empty;
            }
        }

        public static void InvalidateNpc(
            string? filePath = null)
        {
            lock (Sync)
            {
                if (filePath == null ||
                    _npcPath.Equals(
                        Path.GetFullPath(filePath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _npcService = null;
                    _npcPath = string.Empty;
                }

                _references = null;
                _referenceAnchorPath = string.Empty;
            }
        }

        public static void InvalidateItemMaking(
            string filePath)
        {
            string full =
                Path.GetFullPath(
                    filePath);

            lock (Sync)
                ItemMakingCache.Remove(
                    full);
        }

        public static void InvalidateItemList()
        {
            lock (Sync)
            {
                _itemList = null;
                _itemListPath = string.Empty;
                _references = null;
                _referenceAnchorPath = string.Empty;
                _npcService = null;
                _npcPath = string.Empty;
                _digimonList = null;
                _digimonListPath = string.Empty;
                _digimonEvo = null;
                _digimonEvoPath = string.Empty;
                _monsterEditor = null;
                _monsterEditorPath = string.Empty;
                _monsterSkillEditor = null;
                _monsterSkillEditorPath = string.Empty;
                _monsterSkillTermsEditor = null;
                _monsterSkillTermsEditorPath = string.Empty;
                ItemMakingCache.Clear();
                _preloadError = null;
                _preloadTask = null;
            }
        }

        public static async Task<MonsterEditorService> GetMonsterEditorAsync(
            string filePath)
        {
            string full = Path.GetFullPath(filePath);
            Task? preload;
            lock (Sync) preload = _preloadTask;

            if (preload != null)
            {
                try { await preload.ConfigureAwait(false); }
                catch { }
            }

            lock (Sync)
            {
                if (_monsterEditor != null &&
                    _monsterEditorPath.Equals(full, StringComparison.OrdinalIgnoreCase))
                    return _monsterEditor;
            }

            return await Task.Run(() =>
            {
                MonsterEditorService loaded = MonsterEditorService.Load(full);
                lock (Sync)
                {
                    _monsterEditor = loaded;
                    _monsterEditorPath = full;
                }
                return loaded;
            }).ConfigureAwait(false);
        }

        public static MonsterEditorService? TryGetMonsterEditor()
        {
            lock (Sync) return _monsterEditor;
        }

        public static async Task<MonsterSkillEditorService> GetMonsterSkillEditorAsync(
            string filePath)
        {
            string full = Path.GetFullPath(filePath);
            Task? preload;
            lock (Sync) preload = _preloadTask;

            if (preload != null)
            {
                try { await preload.ConfigureAwait(false); }
                catch { }
            }

            lock (Sync)
            {
                if (_monsterSkillEditor != null &&
                    _monsterSkillEditorPath.Equals(full, StringComparison.OrdinalIgnoreCase))
                    return _monsterSkillEditor;
            }

            return await Task.Run(() =>
            {
                MonsterSkillEditorService loaded = MonsterSkillEditorService.Load(full);
                lock (Sync)
                {
                    _monsterSkillEditor = loaded;
                    _monsterSkillEditorPath = full;
                }
                return loaded;
            }).ConfigureAwait(false);
        }

        public static MonsterSkillEditorService? TryGetMonsterSkillEditor()
        {
            lock (Sync) return _monsterSkillEditor;
        }

        public static async Task<MonsterSkillTermsEditorService?> GetMonsterSkillTermsAsync(
            string filePath)
        {
            string full = Path.GetFullPath(filePath);
            if (!File.Exists(full))
                return null;

            Task? preload;
            lock (Sync) preload = _preloadTask;

            if (preload != null)
            {
                try { await preload.ConfigureAwait(false); }
                catch { }
            }

            lock (Sync)
            {
                if (_monsterSkillTermsEditor != null &&
                    _monsterSkillTermsEditorPath.Equals(full, StringComparison.OrdinalIgnoreCase))
                    return _monsterSkillTermsEditor;
            }

            return await Task.Run(() =>
            {
                MonsterSkillTermsEditorService loaded = MonsterSkillTermsEditorService.Load(full);
                lock (Sync)
                {
                    _monsterSkillTermsEditor = loaded;
                    _monsterSkillTermsEditorPath = full;
                }
                return loaded;
            }).ConfigureAwait(false);
        }

        public static MonsterSkillTermsEditorService? TryGetMonsterSkillTerms()
        {
            lock (Sync) return _monsterSkillTermsEditor;
        }

        public static void InvalidateMonsterEditors()
        {
            lock (Sync)
            {
                _monsterEditor = null;
                _monsterEditorPath = string.Empty;
                _monsterSkillEditor = null;
                _monsterSkillEditorPath = string.Empty;
                _monsterSkillTermsEditor = null;
                _monsterSkillTermsEditorPath = string.Empty;
            }
        }

        private static void PreloadCore(
            CancellationToken cancellationToken,
            IProgress<StartupPreloadProgress>? progress)
        {
            static void Report(
                IProgress<StartupPreloadProgress>? target,
                int percent,
                string message)
            {
                target?.Report(
                    new StartupPreloadProgress(
                        Math.Clamp(
                            percent,
                            0,
                            100),
                        message));
            }

            Report(
                progress,
                3,
                "Preparing workspace...");

            AppPaths.EnsureWorkspace();

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                8,
                "Loading ImageDatabase index...");

            // 1) Image indexes.
            ImageDatabasePreview.Preload();

            NpcPreviewCache
                .PreloadAsync()
                .GetAwaiter()
                .GetResult();

            cancellationToken.ThrowIfCancellationRequested();

            // 2) Decode the interface DDS/BMP atlases NOW, while only the
            // LoadingForm is visible. Pre-render every mapped Item/Skill/etc.
            // slot into a small master Bitmap stored in RAM.
            Report(
                progress,
                12,
                "Caching interface icon images into memory...");

            InterfaceIconPreloadResult iconResult =
                ImageDatabasePreview
                    .PreloadAllInterfaceIcons(
                        (current, total, message) =>
                        {
                            if (total <= 0)
                                return;

                            double ratio =
                                current /
                                (double)total;

                            int percent =
                                12 +
                                (int)Math.Round(
                                    ratio *
                                    32.0);

                            Report(
                                progress,
                                percent,
                                message);
                        },
                        cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                46,
                $"Icons in memory: {iconResult.LoadedIcons:N0}. Loading item / skill / accessory references...");

            // 2) Linked Skill/Accessory indexes.
            LinkedItemReferenceService.Preload();

            cancellationToken.ThrowIfCancellationRequested();

            string itemListPath =
                Path.Combine(
                    AppPaths.Xml,
                    "ItemList",
                    "ItemList.xml");

            string npcPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Npc",
                    "Npc.xml");

            string itemMakingPath =
                Path.Combine(
                    AppPaths.Xml,
                    "ItemList",
                    "ItemMaking.xml");

            string itemDisplayPath =
                Path.Combine(
                    AppPaths.Xml,
                    "ItemList",
                    "ItemDisplay.xml");

            string digimonListPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Digimon_List",
                    "Digimon_List.xml");

            string digimonEvoPath =
                Path.Combine(
                    AppPaths.Xml,
                    "DigimonEvo",
                    "DigimonEvo.xml");

            string questPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Quest",
                    "Quest.xml");

            string modelPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Model",
                    "Model.xml");

            if (!File.Exists(modelPath))
                modelPath = Path.Combine(AppPaths.Xml, "Model.xml");

            string skillPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Skill",
                    "Skill.xml");


            string monsterPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Monster",
                    "Monster.xml");

            string monsterSkillPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Monster",
                    "MonstersSkill.xml");

            if (!File.Exists(monsterSkillPath))
                monsterSkillPath = Path.Combine(AppPaths.Xml, "MonstersSkill", "MonstersSkill.xml");

            string monsterSkillTermsPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Monster",
                    "MonstersSkillTerms.xml");

            if (!File.Exists(monsterSkillTermsPath))
                monsterSkillTermsPath = Path.Combine(AppPaths.Xml, "MonstersSkill", "MonstersSkillTerms.xml");

            Report(
                progress,
                52,
                "Loading Skill.xml reference catalog...");

            if (File.Exists(skillPath))
            {
                try
                {
                    SkillReferencePickerService skillReferences =
                        SkillReferencePickerService.Load(
                            skillPath);

                    lock (Sync)
                    {
                        _skillReferences = skillReferences;
                        _skillReferencePath =
                            Path.GetFullPath(
                                skillPath);
                    }

                    SkillEditorService skillEditor =
                        SkillEditorService.Load(
                            skillPath);

                    lock (Sync)
                    {
                        _skillEditor = skillEditor;
                        _skillEditorPath =
                            Path.GetFullPath(
                                skillPath);
                    }

                    string buffPath =
                        Path.Combine(
                            AppPaths.Xml,
                            "Buff",
                            "Buff.xml");

                    if (File.Exists(buffPath))
                    {
                        BuffReferenceService buffReferences =
                            BuffReferenceService.Load(
                                buffPath);

                        lock (Sync)
                        {
                            _buffReferences =
                                buffReferences;

                            _buffReferencePath =
                                Path.GetFullPath(
                                    buffPath);
                        }
                    }
                }
                catch
                {
                    // Selector keeps an open-on-demand fallback.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                56,
                "Loading ItemList.xml...");

            // 3) ItemList parsed index.
            if (File.Exists(itemListPath))
            {
                LoadAndCacheItemList(
                    Path.GetFullPath(
                        itemListPath));
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                67,
                "Loading NPC, Map, Model and Digimon references...");

            // 4) Cross-editor immutable reference catalog:
            // ItemList + Npc + MapList + Model + Digimon_List.
            string anchor =
                File.Exists(npcPath)
                    ? npcPath
                    : File.Exists(itemListPath)
                        ? itemListPath
                        : Path.Combine(
                            AppPaths.Xml,
                            "Npc",
                            "Npc.xml");

            try
            {
                var refs =
                    EditorReferenceCatalogService.Load(
                        anchor);

                lock (Sync)
                {
                    _references = refs;
                    _referenceAnchorPath =
                        Path.GetFullPath(anchor);
                }
            }
            catch
            {
                // Missing optional XMLs must not kill the entire preload.
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                80,
                "Loading Npc.xml...");

            // 5) NPC document itself.
            if (File.Exists(npcPath))
            {
                try
                {
                    var npc =
                        new NpcEditorService(
                            npcPath);

                    lock (Sync)
                    {
                        _npcService = npc;
                        _npcPath =
                            Path.GetFullPath(
                                npcPath);
                    }
                }
                catch
                {
                    // Open-on-demand fallback remains available.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                88,
                "Loading ItemMaking.xml...");

            // 6) ItemMaking document itself.
            if (File.Exists(itemMakingPath))
            {
                try
                {
                    var making =
                        new ItemMakingEditorService(
                            itemMakingPath);

                    lock (Sync)
                    {
                        ItemMakingCache[
                            Path.GetFullPath(
                                itemMakingPath)] =
                            making;
                    }
                }
                catch
                {
                    // Open-on-demand fallback remains available.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                90,
                "Loading Monster.xml database...");

            if (File.Exists(monsterPath))
            {
                try
                {
                    MonsterEditorService monster = MonsterEditorService.Load(monsterPath);
                    lock (Sync)
                    {
                        _monsterEditor = monster;
                        _monsterEditorPath = Path.GetFullPath(monsterPath);
                    }
                }
                catch
                {
                    // Open-on-demand fallback remains available.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                91,
                "Loading MonstersSkill.xml mechanics...");

            if (File.Exists(monsterSkillPath))
            {
                try
                {
                    MonsterSkillEditorService monsterSkills = MonsterSkillEditorService.Load(monsterSkillPath);
                    lock (Sync)
                    {
                        _monsterSkillEditor = monsterSkills;
                        _monsterSkillEditorPath = Path.GetFullPath(monsterSkillPath);
                    }
                }
                catch
                {
                    // Open-on-demand fallback remains available.
                }
            }

            if (File.Exists(monsterSkillTermsPath))
            {
                try
                {
                    MonsterSkillTermsEditorService monsterTerms = MonsterSkillTermsEditorService.Load(monsterSkillTermsPath);
                    lock (Sync)
                    {
                        _monsterSkillTermsEditor = monsterTerms;
                        _monsterSkillTermsEditorPath = Path.GetFullPath(monsterSkillTermsPath);
                    }
                }
                catch
                {
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                92,
                "Loading ItemDisplay.xml...");

            // ItemDisplayEditorService owns a static shared cache.
            // Preloading through OpenShared means Form1 later receives the
            // already parsed instance without requiring extra methods on
            // EditorPreloadService.
            if (File.Exists(itemDisplayPath))
            {
                try
                {
                    ItemDisplayEditorService
                        .OpenShared(
                            itemDisplayPath);
                }
                catch
                {
                    // Open-on-demand fallback remains available.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                93,
                "Loading complete Digimon model catalog from Model.xml...");

            cancellationToken.ThrowIfCancellationRequested();

            // Model.xml is a core editor dependency. Do not silently continue
            // with a half-initialized application: the Digimon model picker,
            // Monster editor and Digimon_List editor all depend on this index.
            DigimonModelReferenceService models;

            try
            {
                models =
                    DigimonModelReferenceService.Load(
                        File.Exists(modelPath)
                            ? modelPath
                            : null);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    "Required Model.xml Digimon model catalog could not be preloaded. " +
                    "The main editor will not open with an incomplete model cache.",
                    ex);
            }

            lock (Sync)
            {
                _digimonModels = models;
                _digimonModelPath =
                    models.SourcePath;
            }

            Report(
                progress,
                94,
                $"Digimon model catalog ready: {models.Models.Count:N0} Data\\Digimon models.");

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                95,
                "Loading Digimon_List.xml and caching Digimon images...");

            if (File.Exists(digimonListPath))
            {
                try
                {
                    var digimon =
                        DigimonListEditorService.Load(
                            digimonListPath,
                            progress,
                            cancellationToken,
                            95,
                            99);

                    lock (Sync)
                    {
                        _digimonList = digimon;
                        _digimonListPath =
                            Path.GetFullPath(
                                digimonListPath);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException(
                        "Required Digimon_List.xml image/reference cache could not be preloaded.",
                        ex);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                99,
                "Loading DigimonEvo.xml evolution trees and unlock references...");

            if (File.Exists(digimonEvoPath))
            {
                try
                {
                    DigimonEvoEditorService evo =
                        DigimonEvoEditorService.Load(
                            digimonEvoPath,
                            digimonListPath,
                            itemListPath,
                            itemDisplayPath,
                            questPath);

                    lock (Sync)
                    {
                        _digimonEvo = evo;
                        _digimonEvoPath =
                            Path.GetFullPath(
                                digimonEvoPath);
                    }
                }
                catch
                {
                    // The editor keeps an async open-on-demand fallback.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Report(
                progress,
                100,
                "Ready.");
        }

        private static DigimonEvoEditorService
            LoadDigimonEvoService(
                string fullPath)
        {
            string digimonListPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Digimon_List",
                    "Digimon_List.xml");

            string itemListPath =
                Path.Combine(
                    AppPaths.Xml,
                    "ItemList",
                    "ItemList.xml");

            string itemDisplayPath =
                Path.Combine(
                    AppPaths.Xml,
                    "ItemList",
                    "ItemDisplay.xml");

            string questPath =
                Path.Combine(
                    AppPaths.Xml,
                    "Quest",
                    "Quest.xml");

            return DigimonEvoEditorService.Load(
                fullPath,
                digimonListPath,
                itemListPath,
                itemDisplayPath,
                questPath);
        }

        private static ItemListEditorService LoadAndCacheItemList(
            string fullPath)
        {
            var service =
                new ItemListEditorService();

            service.Load(fullPath);

            lock (Sync)
            {
                _itemList = service;
                _itemListPath = fullPath;
                _preloadError = null;
            }

            return service;
        }
    }
}
