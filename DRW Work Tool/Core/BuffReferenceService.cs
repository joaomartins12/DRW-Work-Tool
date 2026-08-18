using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class BuffReferenceRecord
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public int BuffType { get; init; }
        public int BuffClass { get; init; }
        public uint SkillCode { get; init; }
        public uint DigimonSkillCode { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name)
                ? $"Unnamed Buff {Id}"
                : Name;
    }

    /// <summary>
    /// Read-only reference catalog for Buff.xml used by the SkillApply editor.
    ///
    /// Important: SkillApply.s_nBuffCode is mixed in the supplied Skill.xml:
    /// some values are direct BuffData.s_dwID references, while other common
    /// values (for example 51, 13 and 32) are internal/raw bonus codes and do
    /// not exist as BuffData IDs. The UI therefore supports both workflows.
    /// </summary>
    public sealed class BuffReferenceService
    {
        private readonly List<BuffReferenceRecord> _records;
        private readonly Dictionary<uint, BuffReferenceRecord> _byId;

        private BuffReferenceService(
            string filePath,
            List<BuffReferenceRecord> records)
        {
            FilePath = filePath;
            _records = records;
            _byId =
                records
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First());
        }

        public string FilePath { get; }

        public IReadOnlyList<BuffReferenceRecord> Records =>
            _records;

        public int Count => _records.Count;

        public static BuffReferenceService Load(
            string filePath)
        {
            string full =
                Path.GetFullPath(filePath);

            if (!File.Exists(full))
            {
                throw new FileNotFoundException(
                    "Buff.xml was not found.",
                    full);
            }

            XDocument document =
                XDocument.Load(
                    full,
                    LoadOptions.None);

            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Buff.xml has no root element.");

            if (!root.Name.LocalName.Equals(
                    "BuffDataArray",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Unexpected Buff.xml root <{root.Name.LocalName}>. Expected <BuffDataArray>.");
            }

            List<BuffReferenceRecord> records =
                root
                    .Elements("BuffData")
                    .Select(
                        x =>
                            new BuffReferenceRecord
                            {
                                Id = U(x, "s_dwID"),
                                Name = S(x, "s_szName"),
                                Comment =
                                    Normalize(
                                        S(
                                            x,
                                            "s_szComment")),
                                IconId =
                                    U(
                                        x,
                                        "s_nBuffIcon"),
                                BuffType =
                                    I(
                                        x,
                                        "s_nBuffType"),
                                BuffClass =
                                    I(
                                        x,
                                        "s_nBuffClass"),
                                SkillCode =
                                    U(
                                        x,
                                        "s_dwSkillCode"),
                                DigimonSkillCode =
                                    U(
                                        x,
                                        "s_dwDigimonSkillCode")
                            })
                    .OrderBy(
                        x => x.DisplayName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(x => x.Id)
                    .ToList();

            return new BuffReferenceService(
                full,
                records);
        }

        public BuffReferenceRecord? FindById(
            uint id) =>
            _byId.TryGetValue(
                id,
                out BuffReferenceRecord? record)
                ? record
                : null;

        public IReadOnlyList<BuffReferenceRecord> Search(
            string? query)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            if (q.Length == 0)
                return _records;

            return _records
                .Where(
                    x =>
                        x.Id.ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase) ||
                        x.IconId.ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase) ||
                        x.BuffClass.ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase) ||
                        x.SkillCode.ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase) ||
                        x.DigimonSkillCode.ToString(
                                CultureInfo.InvariantCulture)
                            .Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase) ||
                        x.DisplayName.Contains(
                            q,
                            StringComparison.CurrentCultureIgnoreCase) ||
                        x.Comment.Contains(
                            q,
                            StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        }

        public Bitmap? TryLoadIcon(
            BuffReferenceRecord record)
        {
            if (record.IconId == 0)
                return null;

            // Buff.xml s_nBuffIcon uses the same siconNN atlas family as
            // Skill.xml. In the supplied Buff.xml all non-zero icons fall in
            // the sicon01..sicon07 numeric ranges.
            return ImageDatabasePreview
                .TryLoadInterfaceIcon(
                    record.IconId,
                    "Skill");
        }

        private static uint U(
            XElement node,
            string name) =>
            uint.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint value)
                    ? value
                    : 0;

        private static int I(
            XElement node,
            string name) =>
            int.TryParse(
                node.Element(name)?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value)
                    ? value
                    : 0;

        private static string S(
            XElement node,
            string name) =>
            node.Element(name)?.Value
            ?? string.Empty;

        private static string Normalize(
            string value) =>
            (value ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
    }
}
