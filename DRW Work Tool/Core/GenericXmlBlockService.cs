using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class GenericXmlBlockService
    {
        private readonly List<XElement> _blocks = new();

        public string FilePath { get; private set; } = string.Empty;
        public string RootName { get; private set; } = string.Empty;
        public int TotalBlocks => _blocks.Count;

        public void Load(string filePath)
        {
            XDocument doc = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
            XElement root = doc.Root
                ?? throw new InvalidDataException($"{Path.GetFileName(filePath)} não possui root.");

            FilePath = filePath;
            RootName = root.Name.LocalName;

            _blocks.Clear();

            // Preferir um contentor com vários filhos repetidos.
            XElement? container = root.Elements()
                .FirstOrDefault(x =>
                {
                    List<XElement> children = x.Elements().ToList();
                    return children.Count > 1 &&
                           children.GroupBy(c => c.Name.LocalName).Any(g => g.Count() > 1);
                });

            IEnumerable<XElement> source = container != null
                ? container.Elements()
                : root.Elements();

            _blocks.AddRange(source.Select(x => new XElement(x)));
        }

        public IReadOnlyList<XElement> Search(string? query, int max = 150)
        {
            string q = (query ?? string.Empty).Trim();

            IEnumerable<XElement> source = _blocks;
            if (q.Length > 0)
            {
                source = source.Where(x =>
                    x.ToString(SaveOptions.DisableFormatting)
                        .Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            return source.Take(max).ToList();
        }

        public int CountSearch(string? query)
        {
            string q = (query ?? string.Empty).Trim();
            if (q.Length == 0)
                return _blocks.Count;

            return _blocks.Count(x =>
                x.ToString(SaveOptions.DisableFormatting)
                    .Contains(q, StringComparison.OrdinalIgnoreCase));
        }
    }
}
