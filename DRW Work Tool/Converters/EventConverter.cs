using DRW_Work_Tool.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DRW_Work_Tool.Converters
{
    public sealed class EventConverter : IGameDataConverter
    {
        public string Name => "Event";

        private const int AttendanceSpanCount = 2;
        private const int TimeSpanIntCount = 9;

        private const int EventItemCount = 6;
        private const int EventNameChars = 512;
        private const int EventRecordSize = 1068;

        private const int MensalItemCount = 6;
        private const int MensalStringChars = 32;
        private const int MensalRecordSize = 192;

        private const int MonthlyItemCount = 28;
        private const int MonthlyMessageChars = 512;
        private const int MonthlyRecordSize = 1196;

        private static readonly string[] TimeSpanFields =
        {
            "tm_sec",
            "tm_min",
            "tm_hour",
            "tm_mday",
            "tm_mon",
            "tm_year",
            "tm_wday",
            "tm_yday",
            "tm_isdst"
        };

        private static readonly string[] MensalIntFields =
        {
            "Id",
            "s_nUse",
            "s_nIndex",
            "s_nType",
            "s_nSuccessType",
            "s_nSuccessValue",
            "s_nItemKind"
        };

        public bool MatchesBin(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Event", StringComparison.OrdinalIgnoreCase);

        public bool MatchesXml(string filePath) =>
            Path.GetFileNameWithoutExtension(filePath)
                .Equals("Event", StringComparison.OrdinalIgnoreCase);

        public void BinToXml(string inputBin, string outputXml)
        {
            byte[] data = File.ReadAllBytes(inputBin);

            string folder =
                Path.GetDirectoryName(outputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Event.");

            Directory.CreateDirectory(folder);

            using MemoryStream ms = new(data, writable: false);
            using BinaryReader br = new(ms, Encoding.UTF8, leaveOpen: true);

            long startAttendance = ms.Position;
            XDocument attendance = ReadAttendance(br);
            long endAttendance = ms.Position;

            long startEvent = ms.Position;
            XDocument eventXml = ReadEvents(br);
            long endEvent = ms.Position;

            long startMensal = ms.Position;
            XDocument mensal = ReadMensalEvents(br);
            long endMensal = ms.Position;

            long startMonthly = ms.Position;
            XDocument monthly = ReadMonthlyEvents(br);
            long endMonthly = ms.Position;

            long startTime = ms.Position;
            XDocument time = ReadTimeEvents(br);
            long endTime = ms.Position;

            long start100 = ms.Position;
            XDocument event100 = Read100Days(br);
            long end100 = ms.Position;

            if (ms.Position != ms.Length)
            {
                long extra = ms.Length - ms.Position;

                throw new InvalidDataException(
                    $"Event.bin contém {extra:N0} bytes extra. " +
                    $"Leitura terminou no offset {ms.Position:N0}, " +
                    $"ficheiro possui {ms.Length:N0} bytes.");
            }

            SaveXml(
                attendance,
                Path.Combine(folder, "AttendenceTime.xml"));

            SaveXml(
                eventXml,
                Path.Combine(folder, "Event.xml"));

            SaveXml(
                event100,
                Path.Combine(folder, "Event100Days.xml"));

            SaveXml(
                mensal,
                Path.Combine(folder, "MensalEvent.xml"));

            SaveXml(
                monthly,
                Path.Combine(folder, "MonthlyEvent.xml"));

            SaveXml(
                time,
                Path.Combine(folder, "TimeEvent.xml"));

            AppLogger.Log(
                "Event: BIN -> XML concluído. 6 XMLs gerados.");

            AppLogger.Log(
                $"Event: secções em bytes -> " +
                $"Attendence={endAttendance - startAttendance:N0}, " +
                $"Event={endEvent - startEvent:N0}, " +
                $"Mensal={endMensal - startMensal:N0}, " +
                $"Monthly={endMonthly - startMonthly:N0}, " +
                $"TimeEvent={endTime - startTime:N0}, " +
                $"Event100Days={end100 - start100:N0}.");

            AppLogger.Log(
                $"Event: tamanho BIN verificado: " +
                $"{data.Length:N0} / {data.Length:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputBin)
        {
            string folder =
                Path.GetDirectoryName(inputXml)
                ?? throw new InvalidDataException(
                    "Não foi possível determinar a pasta XML do Event.");

            string attendancePath =
                Path.Combine(folder, "AttendenceTime.xml");

            string eventPath =
                Path.Combine(folder, "Event.xml");

            string event100Path =
                Path.Combine(folder, "Event100Days.xml");

            string mensalPath =
                Path.Combine(folder, "MensalEvent.xml");

            string monthlyPath =
                Path.Combine(folder, "MonthlyEvent.xml");

            string timePath =
                Path.Combine(folder, "TimeEvent.xml");

            string[] required =
            {
                attendancePath,
                eventPath,
                event100Path,
                mensalPath,
                monthlyPath,
                timePath
            };

            foreach (string path in required)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Event: XML obrigatório não encontrado: {path}",
                        path);
                }
            }

            XDocument attendance = LoadXml(attendancePath);
            XDocument events = LoadXml(eventPath);
            XDocument event100 = LoadXml(event100Path);
            XDocument mensal = LoadXml(mensalPath);
            XDocument monthly = LoadXml(monthlyPath);
            XDocument time = LoadXml(timePath);

            ValidateAll(
                attendance,
                events,
                event100,
                mensal,
                monthly,
                time);

            Directory.CreateDirectory(
                Path.GetDirectoryName(outputBin)
                ?? throw new InvalidDataException(
                    "Pasta Output inválida para Event."));

            using FileStream fs = File.Create(outputBin);
            using BinaryWriter bw = new(fs, Encoding.UTF8, leaveOpen: true);

            WriteAttendance(bw, attendance);
            WriteEvents(bw, events);
            WriteMensalEvents(bw, mensal);
            WriteMonthlyEvents(bw, monthly);
            WriteTimeEvents(bw, time);
            Write100Days(bw, event100);

            bw.Flush();

            long actualSize = fs.Length;
            long expectedSize =
                CalculateExpectedSize(
                    attendance,
                    events,
                    event100,
                    mensal,
                    monthly,
                    time);

            if (actualSize != expectedSize)
            {
                throw new InvalidDataException(
                    $"Event.bin gerado com tamanho incorreto. " +
                    $"Atual={actualSize:N0}, " +
                    $"Esperado={expectedSize:N0}, " +
                    $"Diferença={(actualSize - expectedSize):+#;-#;0} bytes.");
            }

            AppLogger.Log(
                "Event: XML -> BIN concluído. 6 XMLs validados.");

            AppLogger.Log(
                $"Event: tamanho BIN gerado: " +
                $"{actualSize:N0} bytes. " +
                $"Esperado={expectedSize:N0} bytes (OK).");
        }

        // ============================================================
        // ATTENDENCE TIME
        // ============================================================

        private static XDocument ReadAttendance(BinaryReader br)
        {
            XElement spans = new("TimeSpans");

            for (int i = 0; i < AttendanceSpanCount; i++)
            {
                XElement span = new("TimeSpan");

                foreach (string field in TimeSpanFields)
                {
                    span.Add(
                        new XElement(
                            field,
                            br.ReadInt32()));
                }

                spans.Add(span);
            }

            return Xml(
                new XElement(
                    "Atendences",
                    new XElement(
                        "Atendence",
                        spans)));
        }

        private static void WriteAttendance(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "Atendences",
                    "AttendenceTime.xml");

            List<XElement> attendences =
                root.Elements("Atendence").ToList();

            if (attendences.Count != 1)
            {
                throw new InvalidDataException(
                    $"AttendenceTime.xml: esperado exatamente 1 <Atendence>, " +
                    $"encontrados {attendences.Count}.");
            }

            XElement? spansContainer =
                attendences[0].Element("TimeSpans");

            List<XElement> spans =
                spansContainer?
                    .Elements("TimeSpan")
                    .ToList()
                ?? new List<XElement>();

            if (spans.Count != AttendanceSpanCount)
            {
                throw new InvalidDataException(
                    $"AttendenceTime.xml: esperado exatamente " +
                    $"{AttendanceSpanCount} <TimeSpan>, encontrados {spans.Count}.");
            }

            foreach (XElement span in spans)
            {
                foreach (string field in TimeSpanFields)
                {
                    bw.Write(
                        RequiredInt(
                            span,
                            field,
                            "AttendenceTime.xml"));
                }
            }
        }

        // ============================================================
        // EVENT
        // ============================================================

        private static XDocument ReadEvents(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "Event.Count",
                    100_000);

            XElement root = new("Events");

            for (int i = 0; i < count; i++)
            {
                long start = br.BaseStream.Position;

                int tableNo = br.ReadInt32();
                int minutes = br.ReadInt32();

                uint[] itemIds =
                    new uint[EventItemCount];

                ushort[] itemCounts =
                    new ushort[EventItemCount];

                for (int j = 0; j < EventItemCount; j++)
                    itemIds[j] = br.ReadUInt32();

                for (int j = 0; j < EventItemCount; j++)
                    itemCounts[j] = br.ReadUInt16();

                string name =
                    ReadFixedUnicode(
                        br,
                        EventNameChars);

                XElement items =
                    new("EventItems");

                for (int j = 0; j < EventItemCount; j++)
                {
                    items.Add(
                        new XElement(
                            "EventItem",
                            new XElement("ItemId", itemIds[j]),
                            new XElement("ItemCount", itemCounts[j])));
                }

                root.Add(
                    new XElement(
                        "Event",
                        new XElement("s_TableNo", tableNo),
                        new XElement("TimeInMinutes", minutes),
                        items,
                        new XElement("Name", name)));

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != EventRecordSize)
                {
                    throw new InvalidDataException(
                        $"Event record #{i} ocupa {consumed} bytes; " +
                        $"esperado={EventRecordSize}.");
                }
            }

            return Xml(root);
        }

        private static void WriteEvents(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "Events",
                    "Event.xml");

            List<XElement> rows =
                root.Elements("Event").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int tableNo =
                    RequiredInt(
                        row,
                        "s_TableNo",
                        "Event.xml");

                bw.Write(tableNo);

                bw.Write(
                    RequiredInt(
                        row,
                        "TimeInMinutes",
                        $"Event TableNo={tableNo}"));

                List<XElement> items =
                    row.Element("EventItems")?
                        .Elements("EventItem")
                        .ToList()
                    ?? new List<XElement>();

                if (items.Count != EventItemCount)
                {
                    throw new InvalidDataException(
                        $"Event TableNo={tableNo}: esperado exatamente " +
                        $"{EventItemCount} <EventItem>, encontrados {items.Count}.");
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredUInt(
                            item,
                            "ItemId",
                            $"Event TableNo={tableNo}"));
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredUInt16(
                            item,
                            "ItemCount",
                            $"Event TableNo={tableNo}"));
                }

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "Name",
                        $"Event TableNo={tableNo}",
                        allowEmpty: true),
                    EventNameChars,
                    $"Event TableNo={tableNo} <Name>");
            }
        }

        // ============================================================
        // MENSAL EVENT
        // ============================================================

        private static XDocument ReadMensalEvents(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "MensalEvent.Count",
                    100_000);

            XElement root =
                new("MensalEvents");

            for (int i = 0; i < count; i++)
            {
                long start =
                    br.BaseStream.Position;

                XElement row =
                    new("MensalEvent");

                foreach (string field in MensalIntFields)
                {
                    row.Add(
                        new XElement(
                            field,
                            br.ReadInt32()));
                }

                uint[] ids =
                    new uint[MensalItemCount];

                ushort[] counts =
                    new ushort[MensalItemCount];

                for (int j = 0; j < MensalItemCount; j++)
                    ids[j] = br.ReadUInt32();

                for (int j = 0; j < MensalItemCount; j++)
                    counts[j] = br.ReadUInt16();

                XElement items =
                    new("MensalItems");

                for (int j = 0; j < MensalItemCount; j++)
                {
                    items.Add(
                        new XElement(
                            "EventItems",
                            new XElement("ItemId", ids[j]),
                            new XElement("ItemCount", counts[j])));
                }

                row.Add(items);

                row.Add(
                    new XElement(
                        "Name",
                        ReadFixedUnicode(
                            br,
                            MensalStringChars)));

                row.Add(
                    new XElement(
                        "EndTime",
                        ReadFixedUnicode(
                            br,
                            MensalStringChars)));

                root.Add(row);

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != MensalRecordSize)
                {
                    throw new InvalidDataException(
                        $"MensalEvent record #{i} ocupa {consumed} bytes; " +
                        $"esperado={MensalRecordSize}.");
                }
            }

            return Xml(root);
        }

        private static void WriteMensalEvents(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "MensalEvents",
                    "MensalEvent.xml");

            List<XElement> rows =
                root.Elements("MensalEvent").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int id =
                    RequiredInt(
                        row,
                        "Id",
                        "MensalEvent.xml");

                foreach (string field in MensalIntFields)
                {
                    bw.Write(
                        RequiredInt(
                            row,
                            field,
                            $"MensalEvent Id={id}"));
                }

                List<XElement> items =
                    row.Element("MensalItems")?
                        .Elements("EventItems")
                        .ToList()
                    ?? new List<XElement>();

                if (items.Count != MensalItemCount)
                {
                    throw new InvalidDataException(
                        $"MensalEvent Id={id}: esperado exatamente " +
                        $"{MensalItemCount} <EventItems>, encontrados {items.Count}.");
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredUInt(
                            item,
                            "ItemId",
                            $"MensalEvent Id={id}"));
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredUInt16(
                            item,
                            "ItemCount",
                            $"MensalEvent Id={id}"));
                }

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "Name",
                        $"MensalEvent Id={id}",
                        allowEmpty: true),
                    MensalStringChars,
                    $"MensalEvent Id={id} <Name>");

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "EndTime",
                        $"MensalEvent Id={id}",
                        allowEmpty: true),
                    MensalStringChars,
                    $"MensalEvent Id={id} <EndTime>");
            }
        }

        // ============================================================
        // MONTHLY EVENT
        // ============================================================

        private static XDocument ReadMonthlyEvents(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "MonthlyEvent.Count",
                    100_000);

            XElement root =
                new("MonthlyEvents");

            for (int i = 0; i < count; i++)
            {
                long start =
                    br.BaseStream.Position;

                int tableNo =
                    br.ReadInt32();

                string message =
                    ReadFixedUnicode(
                        br,
                        MonthlyMessageChars);

                uint[] ids =
                    new uint[MonthlyItemCount];

                ushort[] counts =
                    new ushort[MonthlyItemCount];

                for (int j = 0; j < MonthlyItemCount; j++)
                    ids[j] = br.ReadUInt32();

                for (int j = 0; j < MonthlyItemCount; j++)
                    counts[j] = br.ReadUInt16();

                XElement items =
                    new("MonthlyItems");

                for (int j = 0; j < MonthlyItemCount; j++)
                {
                    items.Add(
                        new XElement(
                            "MonthlyItem",
                            new XElement("ItemId", ids[j]),
                            new XElement("ItemCount", counts[j])));
                }

                root.Add(
                    new XElement(
                        "MonthlyEvent",
                        new XElement("s_nTableNo", tableNo),
                        new XElement("s_szMessage", message),
                        items));

                long consumed =
                    br.BaseStream.Position - start;

                if (consumed != MonthlyRecordSize)
                {
                    throw new InvalidDataException(
                        $"MonthlyEvent record #{i} ocupa {consumed} bytes; " +
                        $"esperado={MonthlyRecordSize}.");
                }
            }

            return Xml(root);
        }

        private static void WriteMonthlyEvents(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "MonthlyEvents",
                    "MonthlyEvent.xml");

            List<XElement> rows =
                root.Elements("MonthlyEvent").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int tableNo =
                    RequiredInt(
                        row,
                        "s_nTableNo",
                        "MonthlyEvent.xml");

                bw.Write(tableNo);

                WriteFixedUnicode(
                    bw,
                    RequiredText(
                        row,
                        "s_szMessage",
                        $"MonthlyEvent TableNo={tableNo}",
                        allowEmpty: true),
                    MonthlyMessageChars,
                    $"MonthlyEvent TableNo={tableNo} <s_szMessage>");

                List<XElement> items =
                    row.Element("MonthlyItems")?
                        .Elements("MonthlyItem")
                        .ToList()
                    ?? new List<XElement>();

                if (items.Count != MonthlyItemCount)
                {
                    throw new InvalidDataException(
                        $"MonthlyEvent TableNo={tableNo}: esperado exatamente " +
                        $"{MonthlyItemCount} <MonthlyItem>, encontrados {items.Count}.");
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredUInt(
                            item,
                            "ItemId",
                            $"MonthlyEvent TableNo={tableNo}"));
                }

                foreach (XElement item in items)
                {
                    bw.Write(
                        RequiredUInt16(
                            item,
                            "ItemCount",
                            $"MonthlyEvent TableNo={tableNo}"));
                }
            }
        }

        // ============================================================
        // TIME EVENT
        // ============================================================

        private static XDocument ReadTimeEvents(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "TimeEvent.Count",
                    100_000);

            XElement root =
                new("TimeEvents");

            for (int i = 0; i < count; i++)
            {
                int id =
                    br.ReadInt32();

                root.Add(
                    new XElement(
                        "TimeEvent",
                        new XElement("Id", id),
                        new XElement(
                            "StartDate",
                            ReadDynamicUnicode(
                                br,
                                $"TimeEvent[{i}].StartDate")),
                        new XElement(
                            "EndDate",
                            ReadDynamicUnicode(
                                br,
                                $"TimeEvent[{i}].EndDate")),
                        new XElement("Day", br.ReadInt32()),
                        new XElement(
                            "StartTime",
                            ReadDynamicUnicode(
                                br,
                                $"TimeEvent[{i}].StartTime")),
                        new XElement(
                            "EndTime",
                            ReadDynamicUnicode(
                                br,
                                $"TimeEvent[{i}].EndTime")),
                        new XElement("ItemId", br.ReadInt32()),
                        new XElement("ItemCount", br.ReadInt32())));
            }

            return Xml(root);
        }

        private static void WriteTimeEvents(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "TimeEvents",
                    "TimeEvent.xml");

            List<XElement> rows =
                root.Elements("TimeEvent").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int id =
                    RequiredInt(
                        row,
                        "Id",
                        "TimeEvent.xml");

                bw.Write(id);

                WriteDynamicUnicode(
                    bw,
                    RequiredText(
                        row,
                        "StartDate",
                        $"TimeEvent Id={id}",
                        allowEmpty: true));

                WriteDynamicUnicode(
                    bw,
                    RequiredText(
                        row,
                        "EndDate",
                        $"TimeEvent Id={id}",
                        allowEmpty: true));

                bw.Write(
                    RequiredInt(
                        row,
                        "Day",
                        $"TimeEvent Id={id}"));

                WriteDynamicUnicode(
                    bw,
                    RequiredText(
                        row,
                        "StartTime",
                        $"TimeEvent Id={id}",
                        allowEmpty: true));

                WriteDynamicUnicode(
                    bw,
                    RequiredText(
                        row,
                        "EndTime",
                        $"TimeEvent Id={id}",
                        allowEmpty: true));

                bw.Write(
                    RequiredInt(
                        row,
                        "ItemId",
                        $"TimeEvent Id={id}"));

                bw.Write(
                    RequiredInt(
                        row,
                        "ItemCount",
                        $"TimeEvent Id={id}"));
            }
        }

        // ============================================================
        // EVENT 100 DAYS
        // ============================================================

        private static XDocument Read100Days(BinaryReader br)
        {
            int count =
                ReadCount(
                    br,
                    "Event100Days.Count",
                    100_000);

            XElement root =
                new("Event100Days");

            for (int i = 0; i < count; i++)
            {
                int id =
                    br.ReadInt32();

                XElement row =
                    new(
                        "Event100Days",
                        new XElement("Id", id),
                        new XElement(
                            "Event",
                            ReadDynamicUnicode(
                                br,
                                $"Event100Days[{i}].Event")),
                        new XElement(
                            "EventTitle",
                            ReadDynamicUnicode(
                                br,
                                $"Event100Days[{i}].EventTitle")),
                        new XElement(
                            "EventDescript",
                            ReadDynamicUnicode(
                                br,
                                $"Event100Days[{i}].EventDescript")),
                        new XElement(
                            "StartTime",
                            ReadDynamicUnicode(
                                br,
                                $"Event100Days[{i}].StartTime")),
                        new XElement(
                            "EndTime",
                            ReadDynamicUnicode(
                                br,
                                $"Event100Days[{i}].EndTime")),
                        new XElement(
                            "Reset",
                            ReadDynamicUnicode(
                                br,
                                $"Event100Days[{i}].Reset")));

                int itemCount =
                    ReadCount(
                        br,
                        $"Event100Days[{i}].ItemCount",
                        100_000);

                XElement items =
                    new("EventItems");

                for (int item = 0; item < itemCount; item++)
                {
                    items.Add(
                        new XElement(
                            "EventItem",
                            new XElement(
                                "Name",
                                ReadDynamicUnicode(
                                    br,
                                    $"Event100Days[{i}].Item[{item}].Name")),
                            new XElement("ItemId", br.ReadInt32()),
                            new XElement("ItemCount", br.ReadInt32()),

                            // O XML antigo possui este campo,
                            // mas não existem bytes próprios para ele no BIN.
                            new XElement("Unknow", 0)));
                }

                row.Add(items);
                root.Add(row);
            }

            return Xml(root);
        }

        private static void Write100Days(
            BinaryWriter bw,
            XDocument doc)
        {
            XElement root =
                RequireRoot(
                    doc,
                    "Event100Days",
                    "Event100Days.xml");

            List<XElement> rows =
                root.Elements("Event100Days").ToList();

            bw.Write(rows.Count);

            foreach (XElement row in rows)
            {
                int id =
                    RequiredInt(
                        row,
                        "Id",
                        "Event100Days.xml");

                bw.Write(id);

                foreach (string field in new[]
                {
                    "Event",
                    "EventTitle",
                    "EventDescript",
                    "StartTime",
                    "EndTime",
                    "Reset"
                })
                {
                    WriteDynamicUnicode(
                        bw,
                        RequiredText(
                            row,
                            field,
                            $"Event100Days Id={id}",
                            allowEmpty: true));
                }

                List<XElement> items =
                    row.Element("EventItems")?
                        .Elements("EventItem")
                        .ToList()
                    ?? new List<XElement>();

                bw.Write(items.Count);

                foreach (XElement item in items)
                {
                    string name =
                        RequiredText(
                            item,
                            "Name",
                            $"Event100Days Id={id}",
                            allowEmpty: true);

                    WriteDynamicUnicode(
                        bw,
                        name);

                    bw.Write(
                        RequiredInt(
                            item,
                            "ItemId",
                            $"Event100Days Id={id}, Item={name}"));

                    bw.Write(
                        RequiredInt(
                            item,
                            "ItemCount",
                            $"Event100Days Id={id}, Item={name}"));

                    XElement? unknown =
                        item.Element("Unknow");

                    if (unknown != null)
                    {
                        int value =
                            ParseInt(
                                unknown.Value,
                                $"Event100Days Id={id}, Item={name} <Unknow>");

                        if (value != 0)
                        {
                            throw new InvalidDataException(
                                $"Event100Days Id={id}, Item={name}: " +
                                $"<Unknow> deve permanecer 0. " +
                                $"Este elemento existe no XML antigo, " +
                                $"mas não possui bytes próprios no BIN.");
                        }
                    }
                }
            }
        }

        // ============================================================
        // VALIDATION / EXPECTED SIZE
        // ============================================================

        private static void ValidateAll(
            XDocument attendance,
            XDocument events,
            XDocument event100,
            XDocument mensal,
            XDocument monthly,
            XDocument time)
        {
            // Usa MemoryStreams para reutilizar exatamente as mesmas
            // validações usadas durante a serialização.
            using MemoryStream ms = new();
            using BinaryWriter bw =
                new(ms, Encoding.UTF8, leaveOpen: true);

            WriteAttendance(bw, attendance);
            WriteEvents(bw, events);
            WriteMensalEvents(bw, mensal);
            WriteMonthlyEvents(bw, monthly);
            WriteTimeEvents(bw, time);
            Write100Days(bw, event100);
        }

        private static long CalculateExpectedSize(
            XDocument attendance,
            XDocument events,
            XDocument event100,
            XDocument mensal,
            XDocument monthly,
            XDocument time)
        {
            using MemoryStream ms = new();
            using BinaryWriter bw =
                new(ms, Encoding.UTF8, leaveOpen: true);

            WriteAttendance(bw, attendance);
            WriteEvents(bw, events);
            WriteMensalEvents(bw, mensal);
            WriteMonthlyEvents(bw, monthly);
            WriteTimeEvents(bw, time);
            Write100Days(bw, event100);

            bw.Flush();

            return ms.Length;
        }

        // ============================================================
        // STRING / XML HELPERS
        // ============================================================

        private static XDocument LoadXml(string path)
        {
            try
            {
                return XDocument.Load(
                    path,
                    LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                throw;
            }
        }

        private static XElement RequireRoot(
            XDocument doc,
            string expected,
            string context)
        {
            XElement? root =
                doc.Root;

            if (root == null)
            {
                throw new InvalidDataException(
                    $"{context}: XML sem root.");
            }

            if (root.Name.LocalName != expected)
            {
                throw new InvalidDataException(
                    $"{context}: root <{root.Name.LocalName}> inválido. " +
                    $"Esperado <{expected}>.");
            }

            return root;
        }

        private static int ReadCount(
            BinaryReader br,
            string field,
            int max)
        {
            int value =
                br.ReadInt32();

            if (value < 0 || value > max)
            {
                throw new InvalidDataException(
                    $"{field}: count inválido ({value}). " +
                    $"Esperado entre 0 e {max}.");
            }

            return value;
        }

        private static string ReadFixedUnicode(
            BinaryReader br,
            int wcharCount)
        {
            int byteCount =
                wcharCount * 2;

            byte[] raw =
                br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"Esperados {byteCount} bytes UTF-16LE, " +
                    $"recebidos {raw.Length}.");
            }

            string value =
                Encoding.Unicode.GetString(raw);

            int zero =
                value.IndexOf('\0');

            return zero >= 0
                ? value[..zero]
                : value;
        }

        private static void WriteFixedUnicode(
            BinaryWriter bw,
            string value,
            int wcharCount,
            string field)
        {
            byte[] raw =
                Encoding.Unicode.GetBytes(
                    value ?? string.Empty);

            int maxBytes =
                (wcharCount - 1) * 2;

            if (raw.Length > maxBytes)
            {
                throw new InvalidDataException(
                    $"{field} ocupa {raw.Length} bytes UTF-16LE; " +
                    $"o limite útil é {maxBytes} bytes " +
                    $"({wcharCount - 1} caracteres + terminador).");
            }

            byte[] buffer =
                new byte[wcharCount * 2];

            Buffer.BlockCopy(
                raw,
                0,
                buffer,
                0,
                raw.Length);

            bw.Write(buffer);
        }

        private static string ReadDynamicUnicode(
            BinaryReader br,
            string field)
        {
            int charCount =
                ReadCount(
                    br,
                    $"{field}.Length",
                    10_000_000);

            int byteCount =
                checked(charCount * 2);

            byte[] raw =
                br.ReadBytes(byteCount);

            if (raw.Length != byteCount)
            {
                throw new EndOfStreamException(
                    $"{field}: esperados {byteCount} bytes UTF-16LE, " +
                    $"recebidos {raw.Length}.");
            }

            return Encoding.Unicode.GetString(raw);
        }

        private static void WriteDynamicUnicode(
            BinaryWriter bw,
            string value)
        {
            string text =
                value ?? string.Empty;

            bw.Write(text.Length);
            bw.Write(
                Encoding.Unicode.GetBytes(text));
        }

        private static string RequiredText(
            XElement parent,
            string name,
            string context,
            bool allowEmpty = false)
        {
            XElement? element =
                parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"{context}: falta o elemento <{name}>.");
            }

            string value =
                element.Value;

            if (!allowEmpty &&
                string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}> está vazio.");
            }

            return value;
        }

        private static int RequiredInt(
            XElement parent,
            string name,
            string context) =>
            ParseInt(
                RequiredText(
                    parent,
                    name,
                    context),
                $"{context} <{name}>");

        private static int ParseInt(
            string value,
            string context)
        {
            if (!int.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                throw new InvalidDataException(
                    $"{context}='{value}' não é Int32 válido.");
            }

            return result;
        }

        private static uint RequiredUInt(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!uint.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out uint result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não é UInt32 válido.");
            }

            return result;
        }

        private static ushort RequiredUInt16(
            XElement parent,
            string name,
            string context)
        {
            string value =
                RequiredText(
                    parent,
                    name,
                    context);

            if (!ushort.TryParse(
                value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort result))
            {
                throw new InvalidDataException(
                    $"{context}: <{name}>='{value}' não cabe em UInt16 (0..65535).");
            }

            return result;
        }

        private static XDocument Xml(XElement root) =>
            new(
                new XDeclaration(
                    "1.0",
                    "utf-8",
                    null),
                root);

        private static void SaveXml(
            XDocument document,
            string path)
        {
            using XmlWriter writer =
                XmlWriter.Create(
                    path,
                    new XmlWriterSettings
                    {
                        Indent = true,
                        Encoding = new UTF8Encoding(false),
                        OmitXmlDeclaration = false
                    });

            document.Save(writer);
        }
    }
}
