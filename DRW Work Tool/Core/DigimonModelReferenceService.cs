using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class DigimonModelReference
    {
        public uint Id { get; init; }
        public string KfmPath { get; init; } = string.Empty;
        public string FolderName { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public float Scale { get; init; }
        public float Height { get; init; }
        public float Width { get; init; }

        public string DisplayName =>
            string.IsNullOrWhiteSpace(FolderName)
                ? $"Model {Id}"
                : FolderName;
    }

    public sealed class DigimonModelReferenceService
    {
        private readonly List<DigimonModelReference> _models;
        private readonly Dictionary<uint, DigimonModelReference> _byId;

        private DigimonModelReferenceService(
            string sourcePath,
            List<DigimonModelReference> models)
        {
            SourcePath = sourcePath;
            _models = models;
            _byId =
                models
                    .GroupBy(x => x.Id)
                    .ToDictionary(x => x.Key, x => x.First());
        }

        public string SourcePath { get; }
        public IReadOnlyList<DigimonModelReference> Models => _models;

        public static DigimonModelReferenceService Load(string? modelXmlPath = null)
        {
            string path =
                string.IsNullOrWhiteSpace(modelXmlPath)
                    ? Path.Combine(AppContext.BaseDirectory, "XML", "Model", "Model.xml")
                    : Path.GetFullPath(modelXmlPath);

            if (!File.Exists(path))
            {
                string alt = Path.Combine(AppContext.BaseDirectory, "XML", "Model.xml");
                if (File.Exists(alt)) path = alt;
            }

            if (!File.Exists(path))
            {
                string alt = Path.Combine(AppContext.BaseDirectory, "Model.xml");
                if (File.Exists(alt)) path = alt;
            }

            if (!File.Exists(path))
                throw new FileNotFoundException("Model.xml was not found.", path);

            XDocument document = XDocument.Load(path, LoadOptions.None);
            XElement root = document.Root ?? throw new InvalidDataException("Model.xml has no root element.");

            var rows = new List<DigimonModelReference>();

            foreach (XElement model in root.Elements("Model"))
            {
                uint id = ReadUInt(model.Element("s_dwID")?.Value);
                if (id == 0) continue;

                string rawPath = model.Element("s_cKfmPath")?.Value ?? string.Empty;
                string normalized = rawPath.Replace('/', '\\').Trim();

                if (!normalized.StartsWith(
                        @"Data\Digimon\",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = normalized.Substring(@"Data\Digimon\".Length);
                string[] parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);

                string folder =
                    parts.Length > 1
                        ? parts[0]
                        : Path.GetFileNameWithoutExtension(relative);

                rows.Add(
                    new DigimonModelReference
                    {
                        Id = id,
                        KfmPath = normalized,
                        FolderName = CleanFolderName(folder),
                        FileName = Path.GetFileName(normalized),
                        Scale = ReadFloat(model.Element("s_fScale")?.Value),
                        Height = ReadFloat(model.Element("s_fHeight")?.Value),
                        Width = ReadFloat(model.Element("s_fWidth")?.Value)
                    });
            }

            return new DigimonModelReferenceService(
                path,
                rows.OrderBy(x => x.Id).ToList());
        }

        public bool TryGet(uint id, out DigimonModelReference model) =>
            _byId.TryGetValue(id, out model!);

        public IReadOnlyList<DigimonModelReference> Search(string? query)
        {
            string value = (query ?? string.Empty).Trim();
            if (value.Length == 0) return _models;

            return _models
                .Where(x =>
                    x.Id.ToString(CultureInfo.InvariantCulture)
                        .Contains(value, StringComparison.OrdinalIgnoreCase)
                    || x.DisplayName.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || x.KfmPath.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || x.FileName.Contains(value, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static string CleanFolderName(string value)
        {
            string result =
                value
                    .Replace("_Boss", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("_Move", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("_FanA", "", StringComparison.OrdinalIgnoreCase)
                    .Replace('_', ' ')
                    .Trim();

            return result.Length == 0 ? value : result;
        }

        private static uint ReadUInt(string? value) =>
            uint.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result)
                ? result
                : 0;

        private static float ReadFloat(string? value) =>
            float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result)
                ? result
                : 0F;
    }
}
