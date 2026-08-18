using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class NpcEditorService
    {
        private readonly XDocument _document;
        private readonly Dictionary<uint, XElement> _byId = new();

        public string FilePath { get; }
        public int TotalNpcs => _byId.Count;

        public NpcEditorService(string filePath)
        {
            FilePath = Path.GetFullPath(filePath);

            if (!File.Exists(FilePath))
                throw new FileNotFoundException("Npc.xml não encontrado.", FilePath);

            _document = XDocument.Load(FilePath, LoadOptions.PreserveWhitespace);

            if (_document.Root?.Name.LocalName != "NPCs")
                throw new InvalidDataException("Npc.xml inválido: root <NPCs> esperada.");

            RebuildIndex();
        }

        public bool Exists(uint id) => _byId.ContainsKey(id);

        /// <summary>
        /// Returns the next free NPC ID after the highest ID currently present
        /// in Npc.xml.
        ///
        /// Example:
        /// highest existing ID = 99600
        /// result              = 99601
        ///
        /// If the next numeric value is already occupied for any reason, the
        /// method continues until it finds a free ID.
        /// </summary>
        public uint GetNextAvailableId()
        {
            if (_byId.Count == 0)
                return 1;

            uint highest =
                _byId.Keys.Max();

            if (highest == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "Não existe um NpcID disponível acima do maior ID atual.");
            }

            uint candidate =
                highest + 1;

            while (_byId.ContainsKey(candidate))
            {
                if (candidate == uint.MaxValue)
                {
                    throw new InvalidOperationException(
                        "Não existe um NpcID disponível acima do maior ID atual.");
                }

                candidate++;
            }

            return candidate;
        }

        public XElement? GetClone(uint id) =>
            _byId.TryGetValue(id, out XElement? node)
                ? new XElement(node)
                : null;

        public IReadOnlyList<XElement> Search(string query, int take = 100)
        {
            query = (query ?? string.Empty).Trim();

            IEnumerable<XElement> source = _document.Root!.Elements("NPC");

            if (query.Length > 0)
            {
                source = source.Where(node =>
                {
                    string id = node.Element("NpcID")?.Value ?? string.Empty;
                    string name = node.Element("NPCName")?.Value ?? string.Empty;
                    string tag = node.Element("NPCTag")?.Value ?? string.Empty;

                    return
                        id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        tag.Contains(query, StringComparison.OrdinalIgnoreCase);
                });
            }

            return source
                .Take(Math.Max(1, take))
                .Select(x => new XElement(x))
                .ToArray();
        }

        public XElement CreateTemplate(uint suggestedId = 0, int npcType = 0)
        {
            return new XElement(
                "NPC",
                new XElement("NpcID", suggestedId),
                new XElement("MapID", 3),
                new XElement("NPCType", npcType),
                new XElement("NPCMOVE", 0),
                new XElement("s_nDisplayPlag", 3),
                new XElement("NPCTag", string.Empty),
                new XElement("NPCName", string.Empty),
                new XElement("Model", 0),
                new XElement("NPCDesc", string.Empty),
                new XElement("nExtraData", 0));
        }

        public void Save(XElement working, uint? originalId)
        {
            ValidateBasic(working, originalId);

            uint id = uint.Parse(working.Element("NpcID")!.Value);

            CreateBackup();

            if (originalId.HasValue)
            {
                if (!_byId.TryGetValue(originalId.Value, out XElement? existing))
                    throw new InvalidDataException($"NPC {originalId.Value} já não existe em Npc.xml.");

                existing.ReplaceWith(new XElement(working));
            }
            else
            {
                _document.Root!.Add(new XElement(working));
            }

            _document.Save(FilePath, SaveOptions.None);
            RebuildIndex();
        }

        public void Delete(uint id)
        {
            if (!_byId.TryGetValue(id, out XElement? existing))
                throw new InvalidDataException($"NPC {id} não existe.");

            CreateBackup();
            existing.Remove();
            _document.Save(FilePath, SaveOptions.None);
            RebuildIndex();
        }

        private void ValidateBasic(XElement working, uint? originalId)
        {
            if (!uint.TryParse(working.Element("NpcID")?.Value, out uint id) || id == 0)
                throw new InvalidDataException("NpcID inválido. Usa um ID numérico maior que 0.");

            bool same = originalId.HasValue && originalId.Value == id;

            if (!same && _byId.ContainsKey(id))
                throw new InvalidDataException($"NpcID {id} já existe em Npc.xml.");

            if (!uint.TryParse(working.Element("MapID")?.Value, out _))
                throw new InvalidDataException("MapID inválido.");

            if (!int.TryParse(working.Element("NPCType")?.Value, out _))
                throw new InvalidDataException("NPCType inválido.");

            if (!uint.TryParse(working.Element("Model")?.Value, out _))
                throw new InvalidDataException("Model inválido.");
        }

        private void RebuildIndex()
        {
            _byId.Clear();

            foreach (XElement node in _document.Root!.Elements("NPC"))
            {
                if (!uint.TryParse(node.Element("NpcID")?.Value, out uint id))
                    continue;

                if (!_byId.ContainsKey(id))
                    _byId[id] = node;
            }
        }

        private void CreateBackup()
        {
            File.Copy(FilePath, FilePath + ".editor.bak", overwrite: true);
        }
    }
}
