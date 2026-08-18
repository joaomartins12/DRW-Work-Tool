using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DRW_Work_Tool.Core
{
    public sealed class ItemIconSlotInfo
    {
        public uint Id { get; init; }
        public string AtlasName { get; init; } = string.Empty;
        public Rectangle Bounds { get; init; }
    }

    public sealed class ItemIconAtlasInfo
    {
        public string Name { get; init; } = string.Empty;
        public string SourcePath { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }

        public List<ItemIconSlotInfo> Slots { get; init; } = new();
    }

    public sealed class ItemIconBrowserService : IDisposable
    {
        private readonly List<ItemIconAtlasInfo> _atlases = new();
        private readonly Dictionary<string, Bitmap> _bitmapCache =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ItemIconAtlasInfo> Atlases =>
            _atlases;

        public void Load()
        {
            _atlases.Clear();

            var database =
                new ImageDatabaseIndexService();

            database.Load(
                rebuildIndexIfMissing: true);

            IEnumerable<InterfaceIconMapEntry> itemMappings =
                database.InterfaceMap.Icons
                    .Where(
                        mapping =>
                            mapping.Category.Equals(
                                "Item",
                                StringComparison.OrdinalIgnoreCase));

            // Compatibility with older generated maps that did not yet write
            // Category=Item for item atlas entries.
            if (!itemMappings.Any())
            {
                itemMappings =
                    database.InterfaceMap.Icons
                        .Where(
                            mapping =>
                                IsItemAtlasName(
                                    mapping.Atlas));
            }

            foreach (IGrouping<string, InterfaceIconMapEntry> group
                     in itemMappings
                         .GroupBy(
                             x => x.Atlas,
                             StringComparer.OrdinalIgnoreCase)
                         .OrderBy(
                             x => NaturalAtlasKey(
                                 x.Key),
                             StringComparer.OrdinalIgnoreCase))
            {
                var slots =
                    new List<ItemIconSlotInfo>();

                string sourcePath =
                    string.Empty;

                int atlasWidth = 0;
                int atlasHeight = 0;

                foreach (InterfaceIconMapEntry mapping in group)
                {
                    if (!uint.TryParse(
                        mapping.Id,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out uint id))
                    {
                        continue;
                    }

                    if (!database.TryGetInterfaceIcon(
                        mapping.Id,
                        out ResolvedImageReference resolved,
                        "Item"))
                    {
                        continue;
                    }

                    if (sourcePath.Length == 0)
                    {
                        sourcePath =
                            resolved.SourcePath;

                        InterfaceAtlasEntry? atlas =
                            database.GetAtlas(
                                group.Key);

                        atlasWidth =
                            atlas?.Width ?? 0;

                        atlasHeight =
                            atlas?.Height ?? 0;
                    }

                    slots.Add(
                        new ItemIconSlotInfo
                        {
                            Id = id,
                            AtlasName =
                                group.Key,
                            Bounds =
                                new Rectangle(
                                    mapping.X,
                                    mapping.Y,
                                    mapping.Width,
                                    mapping.Height)
                        });
                }

                if (slots.Count == 0 ||
                    string.IsNullOrWhiteSpace(
                        sourcePath))
                {
                    continue;
                }

                _atlases.Add(
                    new ItemIconAtlasInfo
                    {
                        Name = group.Key,
                        SourcePath = sourcePath,
                        Width = atlasWidth,
                        Height = atlasHeight,
                        Slots =
                            slots
                                .OrderBy(x => x.Id)
                                .ToList()
                    });
            }
        }

        public Bitmap GetAtlasBitmap(
            ItemIconAtlasInfo atlas)
        {
            if (_bitmapCache.TryGetValue(
                atlas.SourcePath,
                out Bitmap? cached))
            {
                return cached;
            }

            Bitmap loaded;

            string extension =
                Path.GetExtension(
                    atlas.SourcePath);

            if (extension.Equals(
                ".dds",
                StringComparison.OrdinalIgnoreCase))
            {
                loaded =
                    DdsImageLoader.LoadBitmap(
                        atlas.SourcePath);
            }
            else
            {
                loaded =
                    new Bitmap(
                        atlas.SourcePath);
            }

            _bitmapCache[
                atlas.SourcePath] =
                loaded;

            return loaded;
        }

        public int FindAtlasIndexForIcon(
            uint iconId)
        {
            for (int i = 0;
                 i < _atlases.Count;
                 i++)
            {
                if (_atlases[i]
                    .Slots
                    .Any(x => x.Id == iconId))
                {
                    return i;
                }
            }

            return _atlases.Count > 0
                ? 0
                : -1;
        }

        public ItemIconSlotInfo? FindSlotAt(
            ItemIconAtlasInfo atlas,
            Point imagePoint)
        {
            return atlas.Slots
                .FirstOrDefault(
                    slot =>
                        slot.Bounds.Contains(
                            imagePoint));
        }

        public void Dispose()
        {
            foreach (Bitmap bitmap
                     in _bitmapCache.Values)
            {
                bitmap.Dispose();
            }

            _bitmapCache.Clear();
        }

        private static bool IsItemAtlasName(
            string name)
        {
            if (name.StartsWith(
                "cashshop",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (name.StartsWith(
                "achieve_icon",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Regex.IsMatch(
                name,
                @"^icon\d+$",
                RegexOptions.IgnoreCase);
        }

        private static string NaturalAtlasKey(
            string name)
        {
            Match match =
                Regex.Match(
                    name,
                    @"^(.*?)(\d+)$",
                    RegexOptions.IgnoreCase);

            if (!match.Success)
                return name;

            return
                match.Groups[1].Value +
                int.Parse(
                    match.Groups[2].Value,
                    CultureInfo.InvariantCulture)
                    .ToString(
                        "D8",
                        CultureInfo.InvariantCulture);
        }
    }
}
