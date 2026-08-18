using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

namespace DRW_Work_Tool.Core
{
    public sealed class TalkMessageRecord
    {
        public uint Id { get; init; }
        public int MessageType { get; init; }
        public int Type { get; init; }
        public string TitleName { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public uint LinkId { get; init; }

        public override string ToString()
        {
            string message =
                TalkMessageRichTextRenderer.StripMarkup(
                    Message)
                    .Replace(
                        "\r",
                        " ")
                    .Replace(
                        "\n",
                        " ")
                    .Trim();

            if (message.Length > 115)
                message = message[..112] + "...";

            return
                $"{Id} — {TitleName} — {message}";
        }
    }

    public sealed class TalkMessageCatalog
    {
        private readonly Dictionary<uint, TalkMessageRecord> _byId;

        private TalkMessageCatalog(
            string? sourcePath,
            IReadOnlyList<TalkMessageRecord> records)
        {
            SourcePath = sourcePath;
            Records = records;

            _byId =
                records
                    .GroupBy(
                        x =>
                            x.Id)
                    .ToDictionary(
                        x =>
                            x.Key,
                        x =>
                            x.First());
        }

        public string? SourcePath { get; }

        public IReadOnlyList<TalkMessageRecord> Records { get; }

        public TalkMessageRecord? Find(
            uint id)
        {
            return
                _byId.TryGetValue(
                    id,
                    out TalkMessageRecord? record)
                    ? record
                    : null;
        }

        public IReadOnlyList<TalkMessageRecord> Search(
            string? query)
        {
            string q =
                (query ?? string.Empty)
                    .Trim();

            if (q.Length == 0)
                return Records;

            return
                Records
                    .Where(
                        x =>
                            x.Id.ToString()
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase) ||
                            x.TitleName.Contains(
                                q,
                                StringComparison.OrdinalIgnoreCase) ||
                            TalkMessageRichTextRenderer
                                .StripMarkup(
                                    x.Message)
                                .Contains(
                                    q,
                                    StringComparison.OrdinalIgnoreCase))
                    .ToList();
        }

        public static TalkMessageCatalog LoadNear(
            string sourceXml)
        {
            string? found =
                FindTalkMessagePath(
                    sourceXml);

            if (found == null)
            {
                return
                    new TalkMessageCatalog(
                        null,
                        Array.Empty<TalkMessageRecord>());
            }

            XDocument doc =
                XDocument.Load(
                    found,
                    LoadOptions.PreserveWhitespace);

            XElement root =
                doc.Root ??
                throw new InvalidDataException(
                    "TalkMessage.xml has no root element.");

            IReadOnlyList<TalkMessageRecord> records =
                root.Elements("TalkMessage")
                    .Select(
                        node =>
                            new TalkMessageRecord
                            {
                                Id =
                                    UInt(
                                        node,
                                        "s_dwID"),
                                MessageType =
                                    Int(
                                        node,
                                        "s_MsgType"),
                                Type =
                                    Int(
                                        node,
                                        "s_Type"),
                                TitleName =
                                    node.Element("s_TitleName")?.Value ??
                                    string.Empty,
                                Message =
                                    node.Element("s_Message")?.Value ??
                                    string.Empty,
                                LinkId =
                                    UInt(
                                        node,
                                        "s_dwLinkID")
                            })
                    .OrderBy(
                        x =>
                            x.Id)
                    .ToList();

            return
                new TalkMessageCatalog(
                    found,
                    records);
        }

        private static string? FindTalkMessagePath(
            string sourceXml)
        {
            string source =
                Path.GetFullPath(
                    sourceXml);

            string sourceDir =
                Path.GetDirectoryName(
                    source) ??
                AppContext.BaseDirectory;

            var candidates =
                new List<string>
                {
                    Path.Combine(
                        sourceDir,
                        "TalkMessage.xml"),
                    Path.Combine(
                        sourceDir,
                        "..",
                        "Talk",
                        "TalkMessage.xml"),
                    Path.Combine(
                        sourceDir,
                        "..",
                        "TalkMessage",
                        "TalkMessage.xml"),
                    Path.Combine(
                        AppPaths.Xml,
                        "Talk",
                        "TalkMessage.xml"),
                    Path.Combine(
                        AppPaths.Xml,
                        "TalkMessage",
                        "TalkMessage.xml"),
                    Path.Combine(
                        AppPaths.Xml,
                        "TalkMessage.xml")
                };

            foreach (string candidate in
                     candidates)
            {
                try
                {
                    string full =
                        Path.GetFullPath(
                            candidate);

                    if (File.Exists(
                            full))
                    {
                        return full;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static int Int(
            XElement node,
            string name)
        {
            return
                int.TryParse(
                    node.Element(name)?.Value,
                    out int value)
                    ? value
                    : 0;
        }

        private static uint UInt(
            XElement node,
            string name)
        {
            return
                uint.TryParse(
                    node.Element(name)?.Value,
                    out uint value)
                    ? value
                    : 0;
        }
    }

    public static class TalkMessageRichTextRenderer
    {
        private static readonly Regex TokenRegex =
            new(
                @"@<(?<close>/)?(?<tag>tc|tb)(?::(?<code>\d+))?>",
                RegexOptions.Compiled |
                RegexOptions.IgnoreCase);

        private static readonly Dictionary<int, Color> TextColors =
            new()
            {
                // DMO TalkMessage markup palette used by the supplied XML.
                // Unknown tc codes remain visible with the default editor text color.
                [900] = Color.FromArgb(255, 105, 105),
                [990] = Color.FromArgb(255, 225, 105),
                [998] = Color.FromArgb(105, 215, 255),
                [666] = Color.FromArgb(205, 145, 255),
                [444] = Color.FromArgb(255, 145, 80),
                [99]  = Color.FromArgb(120, 220, 140),
                [90]  = Color.FromArgb(120, 220, 140)
            };

        public static string StripMarkup(
            string? value)
        {
            if (string.IsNullOrEmpty(
                    value))
            {
                return string.Empty;
            }

            return
                TokenRegex.Replace(
                    value,
                    string.Empty);
        }

        public static void Render(
            RichTextBox box,
            string? markup)
        {
            if (box.IsDisposed)
                return;

            string input =
                markup ??
                string.Empty;

            box.SuspendLayout();

            try
            {
                box.Clear();

                Color defaultColor =
                    Color.FromArgb(
                        225,
                        225,
                        225);

                var colorStack =
                    new Stack<Color>();

                var boldStack =
                    new Stack<bool>();

                Color currentColor =
                    defaultColor;

                bool bold =
                    false;

                int position =
                    0;

                foreach (Match match in
                         TokenRegex.Matches(
                             input))
                {
                    if (match.Index >
                        position)
                    {
                        Append(
                            box,
                            input.Substring(
                                position,
                                match.Index -
                                position),
                            currentColor,
                            bold);
                    }

                    bool closing =
                        match.Groups["close"]
                            .Success;

                    string tag =
                        match.Groups["tag"]
                            .Value
                            .ToLowerInvariant();

                    if (tag == "tc")
                    {
                        if (closing)
                        {
                            currentColor =
                                colorStack.Count > 0
                                    ? colorStack.Pop()
                                    : defaultColor;
                        }
                        else
                        {
                            colorStack.Push(
                                currentColor);

                            if (int.TryParse(
                                    match.Groups["code"]
                                        .Value,
                                    out int code) &&
                                TextColors.TryGetValue(
                                    code,
                                    out Color color))
                            {
                                currentColor =
                                    color;
                            }
                        }
                    }
                    else if (tag == "tb")
                    {
                        if (closing)
                        {
                            bold =
                                boldStack.Count > 0 &&
                                boldStack.Pop();
                        }
                        else
                        {
                            boldStack.Push(
                                bold);

                            bold = true;
                        }
                    }

                    position =
                        match.Index +
                        match.Length;
                }

                if (position <
                    input.Length)
                {
                    Append(
                        box,
                        input[position..],
                        currentColor,
                        bold);
                }

                box.SelectionStart =
                    0;

                box.SelectionLength =
                    0;
            }
            finally
            {
                box.ResumeLayout();
            }
        }

        private static void Append(
            RichTextBox box,
            string text,
            Color color,
            bool bold)
        {
            if (text.Length == 0)
                return;

            int start =
                box.TextLength;

            box.AppendText(
                text);

            box.Select(
                start,
                text.Length);

            box.SelectionColor =
                color;

            box.SelectionFont =
                new Font(
                    box.Font,
                    bold
                        ? FontStyle.Bold
                        : FontStyle.Regular);

            box.Select(
                box.TextLength,
                0);
        }
    }
}
