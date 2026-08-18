using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class ItemListRecord
    {
        public required XElement Element { get; init; }
        public uint ItemId { get; init; }
        public string Name { get; init; } = string.Empty;
        public uint IconId { get; init; }
    }

    public sealed class ItemListEditorService
    {
        private XDocument _document = new();
        private XElement _root = null!;
        private XElement _index = null!;
        private readonly Dictionary<uint, XElement> _byId = new();
        private readonly List<XElement> _ordered = new();

        // Índice leve usado pelo Search/CountSearch.
        // Evita procurar repetidamente dentro de 19k XElement a cada tecla.
        private readonly List<ItemListSearchEntry> _searchIndex = new();

        private readonly Dictionary<string, HashSet<string>> _observedValues =
            new(StringComparer.Ordinal);

        public string FilePath { get; private set; } = string.Empty;
        public int TotalItems => _ordered.Count;

        private sealed class ItemListSearchEntry
        {
            public required XElement Element { get; init; }
            public uint ItemId { get; init; }
            public string ItemIdText { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string NameSearch { get; init; } = string.Empty;
            public uint IconId { get; init; }
        }

        public void Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("ItemList.xml não existe.", filePath);

            _document = XDocument.Load(
                filePath,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

            _root = _document.Root
                ?? throw new InvalidDataException("ItemList.xml não possui root.");

            if (!_root.Name.LocalName.Equals("ITEM", StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Root inválido: <{_root.Name.LocalName}>. Esperado <ITEM>.");

            _index = _root.Element("index")
                ?? throw new InvalidDataException("ItemList.xml não possui <index>.");

            _byId.Clear();
            _ordered.Clear();
            _searchIndex.Clear();
            _observedValues.Clear();

            int pos = 0;
            foreach (XElement item in _index.Elements("sINFO"))
            {
                pos++;
                uint id = ParseUInt(item.Element("s_dwItemID")?.Value, $"sINFO #{pos}.s_dwItemID");

                if (_byId.ContainsKey(id))
                    throw new InvalidDataException($"ItemList.xml contém ItemID duplicado: {id}.");

                _byId[id] = item;
                _ordered.Add(item);

                string name =
                    item.Element("s_szName")?.Value ?? string.Empty;

                uint iconId =
                    ParseUInt(
                        item.Element("s_nIcon")?.Value,
                        $"sINFO #{pos}.s_nIcon");

                _searchIndex.Add(
                    new ItemListSearchEntry
                    {
                        Element = item,
                        ItemId = id,
                        ItemIdText = id.ToString(),
                        Name = name,
                        NameSearch = name.ToUpperInvariant(),
                        IconId = iconId
                    });

                foreach (XElement field in item.Elements())
                {
                    string tag = field.Name.LocalName;

                    if (!_observedValues.TryGetValue(
                        tag,
                        out HashSet<string>? values))
                    {
                        values =
                            new HashSet<string>(
                                StringComparer.Ordinal);

                        _observedValues[tag] = values;
                    }

                    // We only need small-domain fields for dropdowns.
                    if (values.Count <= 64)
                        values.Add(field.Value);
                }
            }

            int declared = ParseInt(_root.Element("icount")?.Value, "icount");
            if (declared != _ordered.Count)
            {
                throw new InvalidDataException(
                    $"ItemList.xml inconsistente: <icount>={declared}, mas existem {_ordered.Count} <sINFO>.");
            }

            FilePath = filePath;
        }

        public IReadOnlyList<ItemListRecord> Search(
            string? query,
            int maxResults = 250)
        {
            string q = (query ?? string.Empty).Trim();
            int limit = Math.Max(1, maxResults);

            if (q.Length == 0)
            {
                return _searchIndex
                    .Take(limit)
                    .Select(ToRecord)
                    .ToList();
            }

            bool queryIsId =
                uint.TryParse(q, out uint exactId);

            string qUpper =
                q.ToUpperInvariant();

            var result =
                new List<ItemListRecord>(
                    Math.Min(limit, 250));

            foreach (ItemListSearchEntry entry in _searchIndex)
            {
                bool match =
                    (queryIsId && entry.ItemId == exactId) ||
                    entry.ItemIdText.Contains(
                        q,
                        StringComparison.OrdinalIgnoreCase) ||
                    entry.NameSearch.Contains(
                        qUpper,
                        StringComparison.Ordinal);

                if (!match)
                    continue;

                result.Add(ToRecord(entry));

                if (result.Count >= limit)
                    break;
            }

            return result;
        }

        public int CountSearch(string? query)
        {
            string q = (query ?? string.Empty).Trim();

            if (q.Length == 0)
                return _searchIndex.Count;

            bool queryIsId =
                uint.TryParse(q, out uint exactId);

            string qUpper =
                q.ToUpperInvariant();

            int count = 0;

            foreach (ItemListSearchEntry entry in _searchIndex)
            {
                if ((queryIsId && entry.ItemId == exactId) ||
                    entry.ItemIdText.Contains(
                        q,
                        StringComparison.OrdinalIgnoreCase) ||
                    entry.NameSearch.Contains(
                        qUpper,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        public IReadOnlyList<string> GetObservedValues(
            string tag,
            int maxValues = 32)
        {
            if (!_observedValues.TryGetValue(
                tag,
                out HashSet<string>? values))
            {
                return Array.Empty<string>();
            }

            if (values.Count > maxValues)
                return Array.Empty<string>();

            return values
                .OrderBy(
                    value =>
                    {
                        return long.TryParse(
                            value,
                            out long number)
                                ? number
                                : long.MaxValue;
                    })
                .ThenBy(
                    value => value,
                    StringComparer.Ordinal)
                .ToArray();
        }

        public bool Exists(uint itemId) => _byId.ContainsKey(itemId);

        public XElement GetClone(uint itemId)
        {
            if (!_byId.TryGetValue(itemId, out XElement? element))
                throw new KeyNotFoundException($"ItemID {itemId} não existe.");

            return new XElement(element);
        }

        public XElement CreateTemplate()
        {
            if (_ordered.Count == 0)
                throw new InvalidDataException("ItemList.xml está vazio e não existe um sINFO para usar como template.");

            XElement template = new XElement(_ordered[0]);

            SetFirst(template, "s_dwItemID", "0");
            SetFirst(template, "s_szName", "New Item");
            SetFirst(template, "s_nIcon", "0");
            SetFirst(template, "s_szComment", string.Empty);
            SetFirst(template, "s_cNif", string.Empty);
            SetFirst(template, "s_szTypeComment", string.Empty);
            SetFirst(template, "s_cModel_Nif", string.Empty);
            SetFirst(template, "s_cModel_Effect", string.Empty);

            return template;
        }

        public void SaveExisting(uint originalId, XElement edited)
        {
            uint newId = GetId(edited);

            if (!_byId.TryGetValue(originalId, out XElement? current))
                throw new InvalidDataException($"O ItemID original {originalId} já não existe no XML.");

            if (newId != originalId && _byId.ContainsKey(newId))
                throw new InvalidDataException($"O ItemID {newId} já existe.");

            XElement replacement = new XElement(edited);
            current.ReplaceWith(replacement);

            _byId.Remove(originalId);
            _byId[newId] = replacement;

            int index = _ordered.IndexOf(current);
            if (index >= 0)
            {
                _ordered[index] = replacement;
                RebuildSearchEntry(index, replacement);
            }

            UpdateCount();
            SaveAtomic();
        }

        public void AppendNew(XElement edited)
        {
            uint id = GetId(edited);

            if (_byId.ContainsKey(id))
                throw new InvalidDataException($"O ItemID {id} já existe.");

            XElement appended = new XElement(edited);
            _index.Add(appended);
            _ordered.Add(appended);
            _byId[id] = appended;
            _searchIndex.Add(BuildSearchEntry(appended));

            UpdateCount();
            SaveAtomic();
        }

        public void Delete(uint itemId)
        {
            if (!_byId.TryGetValue(itemId, out XElement? current))
                throw new InvalidDataException($"ItemID {itemId} não existe.");

            current.Remove();
            _byId.Remove(itemId);

            int index = _ordered.IndexOf(current);
            if (index >= 0)
            {
                _ordered.RemoveAt(index);

                if (index < _searchIndex.Count)
                    _searchIndex.RemoveAt(index);
            }

            UpdateCount();
            SaveAtomic();
        }

        public static string FormatBlock(XElement item)
        {
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = true,
                NewLineHandling = NewLineHandling.None
            };

            var sb = new StringBuilder();
            using XmlWriter writer = XmlWriter.Create(sb, settings);
            new XElement(item).Save(writer);
            writer.Flush();
            return sb.ToString();
        }

        private void UpdateCount()
        {
            XElement count = _root.Element("icount")
                ?? throw new InvalidDataException("ItemList.xml não possui <icount>.");

            count.Value = _ordered.Count.ToString();
        }

        private void SaveAtomic()
        {
            string folder = Path.GetDirectoryName(FilePath)
                ?? throw new InvalidDataException("Pasta do ItemList.xml inválida.");

            Directory.CreateDirectory(folder);

            string temp = FilePath + ".tmp";
            string backup = FilePath + ".editor.bak";

            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false,
                NewLineHandling = NewLineHandling.None
            };

            using (XmlWriter writer = XmlWriter.Create(temp, settings))
                _document.Save(writer);

            if (File.Exists(backup))
                File.Delete(backup);

            File.Copy(FilePath, backup, overwrite: true);
            File.Copy(temp, FilePath, overwrite: true);
            File.Delete(temp);
        }

        private static ItemListRecord ToRecord(
            ItemListSearchEntry entry) =>
            new ItemListRecord
            {
                Element = entry.Element,
                ItemId = entry.ItemId,
                Name = entry.Name,
                IconId = entry.IconId
            };

        private static ItemListSearchEntry BuildSearchEntry(
            XElement item)
        {
            uint id = GetId(item);

            string name =
                item.Element("s_szName")?.Value ?? string.Empty;

            uint iconId =
                ParseUInt(
                    item.Element("s_nIcon")?.Value,
                    "s_nIcon");

            return new ItemListSearchEntry
            {
                Element = item,
                ItemId = id,
                ItemIdText = id.ToString(),
                Name = name,
                NameSearch = name.ToUpperInvariant(),
                IconId = iconId
            };
        }

        private void RebuildSearchEntry(
            int index,
            XElement item)
        {
            ItemListSearchEntry entry =
                BuildSearchEntry(item);

            if (index < _searchIndex.Count)
                _searchIndex[index] = entry;
            else
                _searchIndex.Add(entry);
        }

        public static uint GetId(XElement item) =>
            ParseUInt(item.Element("s_dwItemID")?.Value, "s_dwItemID");

        private static void SetFirst(XElement item, string name, string value)
        {
            XElement? node = item.Element(name);
            if (node != null)
                node.Value = value;
        }

        private static uint ParseUInt(string? raw, string field)
        {
            if (!uint.TryParse((raw ?? string.Empty).Trim(), out uint value))
                throw new InvalidDataException($"{field}='{raw}' não é UInt32 válido.");

            return value;
        }

        private static int ParseInt(string? raw, string field)
        {
            if (!int.TryParse((raw ?? string.Empty).Trim(), out int value))
                throw new InvalidDataException($"{field}='{raw}' não é Int32 válido.");

            return value;
        }
    }
}
