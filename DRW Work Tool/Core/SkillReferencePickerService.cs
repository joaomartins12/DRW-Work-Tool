using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class SkillReferenceRecord
    {
        public uint Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Comment { get; init; } = string.Empty;
        public uint IconId { get; init; }
        public int MaxLevel { get; init; }
        public int AttributeType { get; init; }
        public int NatureType { get; init; }
        public int SkillType { get; init; }
        public int LimitLevel { get; init; }
        public int UseDs { get; init; }
        public float Cooldown { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(Name)
                ? $"Unnamed Skill {Id}"
                : Name;
    }

    /// <summary>
    /// Read-only Skill.xml reference catalog for editors.
    ///
    /// The XML is parsed once and indexed by ID. Skill icons are resolved
    /// through ImageDatabasePreview using category "Skill", which maps
    /// s_nIcon to the siconNN atlas family.
    /// </summary>
    public sealed class SkillReferencePickerService
    {
        private readonly List<SkillReferenceRecord> _skills;
        private readonly Dictionary<uint, SkillReferenceRecord> _byId;

        private SkillReferencePickerService(
            string sourcePath,
            List<SkillReferenceRecord> skills)
        {
            SourcePath = sourcePath;
            _skills = skills;
            _byId =
                skills
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First());
        }

        public string SourcePath { get; }

        public IReadOnlyList<SkillReferenceRecord> Skills => _skills;

        public static SkillReferencePickerService Load(
            string? skillXmlPath = null)
        {
            string path =
                string.IsNullOrWhiteSpace(skillXmlPath)
                    ? Path.Combine(
                        AppContext.BaseDirectory,
                        "XML",
                        "Skill",
                        "Skill.xml")
                    : Path.GetFullPath(skillXmlPath);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Skill.xml was not found.",
                    path);
            }

            XDocument document =
                XDocument.Load(
                    path,
                    LoadOptions.PreserveWhitespace);

            XElement root =
                document.Root ??
                throw new InvalidDataException(
                    "Skill.xml has no root element.");

            var records =
                new List<SkillReferenceRecord>();

            foreach (XElement node
                     in root.Elements("SkillData"))
            {
                uint id =
                    ReadUInt(
                        node.Element("s_dwID")?.Value);

                if (id == 0)
                    continue;

                records.Add(
                    new SkillReferenceRecord
                    {
                        Id = id,
                        Name =
                            node.Element("s_szName")?.Value?.Trim()
                            ?? string.Empty,
                        Comment =
                            NormalizeComment(
                                node.Element("s_szComment")?.Value),
                        IconId =
                            ReadUInt(
                                node.Element("s_nIcon")?.Value),
                        MaxLevel =
                            ReadInt(
                                node.Element("s_nMaxLevel")?.Value),
                        AttributeType =
                            ReadInt(
                                node.Element("s_nAttributeType")?.Value),
                        NatureType =
                            ReadInt(
                                node.Element("s_nNatureType")?.Value),
                        SkillType =
                            ReadInt(
                                node.Element("s_nSkillType")?.Value),
                        LimitLevel =
                            ReadInt(
                                node.Element("s_nLimitLevel")?.Value),
                        UseDs =
                            ReadInt(
                                node.Element("s_nUseDS")?.Value),
                        Cooldown =
                            ReadFloat(
                                node.Element("s_fCooldownTime")?.Value)
                    });
            }

            records =
                records
                    .OrderBy(x => x.Id)
                    .ToList();

            return new SkillReferencePickerService(
                path,
                records);
        }

        public bool TryGet(
            uint id,
            out SkillReferenceRecord record) =>
            _byId.TryGetValue(
                id,
                out record!);

        public IReadOnlyList<SkillReferenceRecord> Search(
            string? query)
        {
            string value =
                (query ?? string.Empty).Trim();

            if (value.Length == 0)
                return _skills;

            string lowered =
                value.ToLowerInvariant();

            bool numeric =
                uint.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint numericId);

            return
                _skills
                    .Where(
                        x =>
                            (numeric &&
                             x.Id.ToString(
                                     CultureInfo.InvariantCulture)
                                 .Contains(
                                     value,
                                     StringComparison.OrdinalIgnoreCase))
                            ||
                            x.DisplayName.Contains(
                                value,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            x.Comment.Contains(
                                value,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();
        }

        public Bitmap? TryLoadIcon(
            SkillReferenceRecord record)
        {
            if (record.IconId == 0)
                return null;

            return
                ImageDatabasePreview
                    .TryLoadInterfaceIcon(
                        record.IconId,
                        "Skill");
        }

        private static string NormalizeComment(
            string? value) =>
            (value ?? string.Empty)
                .Replace(
                    "\r\n",
                    " ")
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ')
                .Trim();

        private static uint ReadUInt(
            string? value) =>
            uint.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result)
                ? result
                : 0;

        private static int ReadInt(
            string? value) =>
            int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result)
                ? result
                : 0;

        private static float ReadFloat(
            string? value) =>
            float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result)
                ? result
                : 0F;
    }
}
