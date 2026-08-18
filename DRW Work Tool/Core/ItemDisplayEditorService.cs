using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed record ItemDisplayRecord(
        int RowIndex,
        uint Section,
        uint ItemId);

    public sealed class ItemDisplayEditorService
    {
        private static readonly object SharedLock = new();
        private static readonly Dictionary<string, ItemDisplayEditorService> Shared =
            new(StringComparer.OrdinalIgnoreCase);

        private XDocument _document = null!;
        private XElement _root = null!;

        public string FilePath { get; private set; } = string.Empty;

        public int TotalEntries =>
            _root?.Elements("Item").Count() ?? 0;

        public IReadOnlyList<ItemDisplayRecord> GetAll()
        {
            return _root
                .Elements("Item")
                .Select(
                    (item, index) =>
                        new ItemDisplayRecord(
                            index,
                            ReadUInt(
                                item.Element("nItemS"),
                                "nItemS"),
                            ReadUInt(
                                item.Element("dwDispID"),
                                "dwDispID")))
                .ToArray();
        }

        public IReadOnlyList<ItemDisplayRecord> Search(
            string? query,
            IReadOnlyDictionary<uint, ItemDisplayItemReference> itemReferences)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            IEnumerable<ItemDisplayRecord> source =
                GetAll();

            if (q.Length == 0)
                return source.ToArray();

            return source
                .Where(
                    record =>
                    {
                        if (record.ItemId
                            .ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (record.Section
                            .ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        return
                            itemReferences.TryGetValue(
                                record.ItemId,
                                out ItemDisplayItemReference? item) &&
                            item.Name.Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase);
                    })
                .ToArray();
        }

        public static ItemDisplayEditorService OpenShared(
            string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            lock (SharedLock)
            {
                if (Shared.TryGetValue(
                    full,
                    out ItemDisplayEditorService? existing))
                {
                    return existing;
                }

                var service =
                    new ItemDisplayEditorService();

                service.Load(full);

                Shared[full] = service;

                return service;
            }
        }

        public void Load(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    "ItemDisplay.xml não foi encontrado.",
                    filePath);
            }

            FilePath =
                Path.GetFullPath(filePath);

            _document =
                XDocument.Load(
                    FilePath,
                    LoadOptions.PreserveWhitespace |
                    LoadOptions.SetLineInfo);

            _root =
                _document.Root
                ?? throw new InvalidDataException(
                    "ItemDisplay.xml não possui root.");

            if (!_root.Name.LocalName.Equals(
                "ItemDisplay",
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"ItemDisplay.xml possui root <{_root.Name.LocalName}>. " +
                    "Esperado <ItemDisplay>.");
            }
        }

        public bool ContainsExact(
            uint section,
            uint itemId)
        {
            return FindExact(
                section,
                itemId)
                .Any();
        }

        public bool ContainsItem(
            uint itemId)
        {
            return _root
                .Elements("Item")
                .Any(
                    item =>
                        ReadUInt(
                            item.Element("dwDispID"),
                            "dwDispID") == itemId);
        }

        public IReadOnlyList<uint> GetSectionsForItem(
            uint itemId)
        {
            return _root
                .Elements("Item")
                .Where(
                    item =>
                        ReadUInt(
                            item.Element("dwDispID"),
                            "dwDispID") == itemId)
                .Select(
                    item =>
                        ReadUInt(
                            item.Element("nItemS"),
                            "nItemS"))
                .ToArray();
        }

        /// <summary>
        /// Synchronizes one ItemList item into ItemDisplay.xml.
        ///
        /// Existing exact pair => no change.
        /// Editing:
        /// - if the old exact pair exists, that row is updated;
        /// - otherwise a new row is appended.
        ///
        /// This avoids deleting other legitimate ItemDisplay rows for the
        /// same dwDispID because the supplied XML does contain repeated IDs.
        /// </summary>
        public ItemDisplaySyncResult Sync(
            uint newSection,
            uint newItemId,
            uint? originalSection,
            uint? originalItemId)
        {
            if (ContainsExact(
                newSection,
                newItemId))
            {
                return new ItemDisplaySyncResult
                {
                    Changed = false,
                    Action = "AlreadyExists",
                    Section = newSection,
                    ItemId = newItemId,
                    TotalEntries = TotalEntries
                };
            }

            XElement? rowToUpdate = null;

            if (originalSection.HasValue &&
                originalItemId.HasValue)
            {
                rowToUpdate =
                    FindExact(
                        originalSection.Value,
                        originalItemId.Value)
                    .FirstOrDefault();
            }

            string action;

            if (rowToUpdate != null)
            {
                rowToUpdate.SetElementValue(
                    "nItemS",
                    newSection.ToString(
                        CultureInfo.InvariantCulture));

                rowToUpdate.SetElementValue(
                    "dwDispID",
                    newItemId.ToString(
                        CultureInfo.InvariantCulture));

                action = "Updated";
            }
            else
            {
                _root.Add(
                    new XElement(
                        "Item",
                        new XElement(
                            "nItemS",
                            newSection),
                        new XElement(
                            "dwDispID",
                            newItemId)));

                action = "Added";
            }

            SaveAtomic();

            return new ItemDisplaySyncResult
            {
                Changed = true,
                Action = action,
                Section = newSection,
                ItemId = newItemId,
                TotalEntries = TotalEntries
            };
        }

        public void Add(
            uint section,
            uint itemId)
        {
            if (ContainsExact(
                section,
                itemId))
            {
                throw new InvalidOperationException(
                    $"ItemDisplay já contém Section={section}, ItemID={itemId}.");
            }

            _root.Add(
                new XElement(
                    "Item",
                    new XElement(
                        "nItemS",
                        section),
                    new XElement(
                        "dwDispID",
                        itemId)));

            SaveAtomic();
        }

        public void UpdateAt(
            int rowIndex,
            uint section,
            uint itemId)
        {
            XElement[] rows =
                _root
                    .Elements("Item")
                    .ToArray();

            if (rowIndex < 0 ||
                rowIndex >= rows.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rowIndex));
            }

            bool duplicate =
                rows
                    .Where(
                        (_, index) =>
                            index != rowIndex)
                    .Any(
                        item =>
                            ReadUInt(
                                item.Element("nItemS"),
                                "nItemS") == section &&
                            ReadUInt(
                                item.Element("dwDispID"),
                                "dwDispID") == itemId);

            if (duplicate)
            {
                throw new InvalidOperationException(
                    $"ItemDisplay já contém Section={section}, ItemID={itemId}.");
            }

            rows[rowIndex]
                .SetElementValue(
                    "nItemS",
                    section.ToString(
                        CultureInfo.InvariantCulture));

            rows[rowIndex]
                .SetElementValue(
                    "dwDispID",
                    itemId.ToString(
                        CultureInfo.InvariantCulture));

            SaveAtomic();
        }

        public void DeleteAt(
            int rowIndex)
        {
            XElement[] rows =
                _root
                    .Elements("Item")
                    .ToArray();

            if (rowIndex < 0 ||
                rowIndex >= rows.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rowIndex));
            }

            rows[rowIndex].Remove();

            SaveAtomic();
        }

        private IEnumerable<XElement> FindExact(
            uint section,
            uint itemId)
        {
            return _root
                .Elements("Item")
                .Where(
                    item =>
                        ReadUInt(
                            item.Element("nItemS"),
                            "nItemS") == section &&
                        ReadUInt(
                            item.Element("dwDispID"),
                            "dwDispID") == itemId);
        }

        private void SaveAtomic()
        {
            string backup =
                FilePath + ".editor.bak";

            string temp =
                FilePath + ".editor.tmp";

            if (File.Exists(FilePath))
                File.Copy(
                    FilePath,
                    backup,
                    overwrite: true);

            using (XmlWriter writer =
                   XmlWriter.Create(
                       temp,
                       new XmlWriterSettings
                       {
                           Indent = true,
                           Encoding = new UTF8Encoding(false),
                           OmitXmlDeclaration = false,
                           NewLineHandling = NewLineHandling.None
                       }))
            {
                _document.Save(writer);
            }

            File.Copy(
                temp,
                FilePath,
                overwrite: true);

            File.Delete(temp);
        }

        private static uint ReadUInt(
            XElement? element,
            string field)
        {
            string raw =
                element?.Value?.Trim()
                ?? string.Empty;

            if (!uint.TryParse(
                raw,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value))
            {
                throw new InvalidDataException(
                    $"ItemDisplay.xml: <{field}>='{raw}' não é UInt32 válido.");
            }

            return value;
        }
    }

    public sealed record ItemDisplayItemReference(
        uint ItemId,
        string Name,
        uint IconId,
        uint Section);

    public sealed class ItemDisplaySyncResult
    {
        public bool Changed { get; init; }
        public string Action { get; init; } = string.Empty;
        public uint Section { get; init; }
        public uint ItemId { get; init; }
        public int TotalEntries { get; init; }
    }
}
