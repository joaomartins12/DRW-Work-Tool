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
    public sealed class ModelConverter : IGameDataConverter
    {
        private const int ModelPathSize = 160;
        private const int ModelDummySize = 16;
        private const int SequenceHeaderSize = 16;

        private const int EventTextSize = 128;
        private const int EventSize = 192;

        private const int ShaderNameSize = 32;
        private const int ShaderSize = 68;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static Encoding _textEncoding = Encoding.UTF8;
        private static bool _encodingReady;

        public string Name => "Model";

        public bool MatchesBin(string filePath)
        {
            return Path.GetExtension(filePath)
                    .Equals(".dat", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(filePath)
                    .Equals("Model", StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesXml(string filePath)
        {
            return Path.GetExtension(filePath)
                    .Equals(".xml", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(filePath)
                    .Equals("Model", StringComparison.OrdinalIgnoreCase);
        }

        public void BinToXml(string inputDat, string outputXml)
        {
            EnsureTextEncoding();

            string? folder = Path.GetDirectoryName(outputXml);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            long inputSize = new FileInfo(inputDat).Length;

            AppLogger.Log("Model.dat: leitura da tabela de Models iniciada.");
            ExtractDatToXml(inputDat, outputXml);

            long outputSize = new FileInfo(outputXml).Length;

            AppLogger.Log(
                $"Model.dat: DAT -> XML concluído. XML={outputSize:N0} bytes.");

            AppLogger.Log(
                $"Model.dat: tamanho DAT lido: {inputSize:N0} bytes (OK).");
        }

        public void XmlToBin(string inputXml, string outputDat)
        {
            EnsureTextEncoding();

            string? folder = Path.GetDirectoryName(outputDat);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            AppLogger.Log("Model.dat: serialização XML -> DAT iniciada.");
            ImportXmlToDat(inputXml, outputDat);

            long size = new FileInfo(outputDat).Length;

            AppLogger.Log(
                $"Model.dat: tamanho DAT gerado: {size:N0} bytes (OK).");
        }

        private static void EnsureTextEncoding()
        {
            if (_encodingReady)
                return;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _textEncoding = Encoding.GetEncoding(
                949,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);

            _encodingReady = true;
        }

        // ============================================================
        // DAT -> XML
        // ============================================================

        public static void ExtractDatToXml(string datPath, string xmlPath)
        {
            using FileStream fs = new(
                datPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            using BinaryReader br = new(fs);

            if (fs.Length < 4)
                throw new InvalidDataException("model.dat demasiado pequeno.");

            uint modelCount = ReadUInt32(br, "ModelCount");

            if (modelCount == 0 || modelCount > 1_000_000)
                throw new InvalidDataException(
                    $"Quantidade de Models inválida: {modelCount}");

            XmlWriterSettings settings = new()
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = true
            };

            int totalSequences = 0;
            int totalEvents = 0;
            int totalShaders = 0;

            using XmlWriter xw = XmlWriter.Create(xmlPath, settings);

            xw.WriteStartDocument();
            xw.WriteStartElement("DigimonData");

            for (uint modelIndex = 0; modelIndex < modelCount; modelIndex++)
            {
                long modelOffset = fs.Position;

                EnsureRemaining(
                    br,
                    196,
                    $"Model #{modelIndex} em 0x{modelOffset:X}");

                uint id = ReadUInt32(br, "s_dwID");
                string kfmPath = ReadFixedString(br, ModelPathSize);

                float scale = ReadSingle(br, "s_fScale");
                float height = ReadSingle(br, "s_fHeight");
                float width = ReadSingle(br, "s_fWidth");

                int sequenceCount = ReadInt32(br, "s_nSequenceCount");

                ValidateCount(
                    sequenceCount,
                    100_000,
                    $"s_nSequenceCount do Model {id}");

                byte[] modelDummy = ReadExact(
                    br,
                    ModelDummySize,
                    $"s_Dummy do Model {id}");

                xw.WriteStartElement("Model");

                WriteElement(xw, "s_dwID", id);
                WriteElement(xw, "s_cKfmPath", kfmPath);
                WriteFloat(xw, "s_fScale", scale);
                WriteFloat(xw, "s_fHeight", height);
                WriteFloat(xw, "s_fWidth", width);
                WriteElement(xw, "s_nSequenceCount", sequenceCount);
                WriteElement(
                    xw,
                    "s_Dummy",
                    Convert.ToBase64String(modelDummy));

                for (int seqIndex = 0; seqIndex < sequenceCount; seqIndex++)
                {
                    EnsureRemaining(
                        br,
                        SequenceHeaderSize,
                        $"Sequence #{seqIndex} do Model {id}");

                    uint sequenceId = ReadUInt32(br, "s_dwSequenceID");
                    int eventCount = ReadInt32(br, "s_nEventCount");
                    int loopCount = ReadInt32(br, "s_nLoopCnt");
                    int shaderCount = ReadInt32(br, "s_nShaderCnt");

                    ValidateCount(
                        eventCount,
                        100_000,
                        $"s_nEventCount em Model {id}, Sequence {sequenceId}");

                    ValidateCount(
                        loopCount,
                        100_000,
                        $"s_nLoopCnt em Model {id}, Sequence {sequenceId}");

                    ValidateCount(
                        shaderCount,
                        100_000,
                        $"s_nShaderCnt em Model {id}, Sequence {sequenceId}");

                    xw.WriteStartElement("Sequence");

                    WriteElement(xw, "s_dwSequenceID", sequenceId);
                    WriteElement(xw, "s_nEventCount", eventCount);
                    WriteElement(xw, "s_nLoopCnt", loopCount);
                    WriteElement(xw, "s_nShaderCnt", shaderCount);

                    for (int eventIndex = 0; eventIndex < eventCount; eventIndex++)
                    {
                        long eventOffset = fs.Position;

                        EnsureRemaining(
                            br,
                            EventSize,
                            $"Event #{eventIndex} em Model {id}, Sequence {sequenceId}");

                        ReadEvent(
                            br,
                            xw,
                            id,
                            sequenceId,
                            eventIndex,
                            eventOffset);

                        totalEvents++;
                    }

                    // O loopCount é apenas metadata neste formato.
                    // Não existem blocos de Loop adicionais entre Events e Shaders.

                    for (int shaderIndex = 0;
                         shaderIndex < shaderCount;
                         shaderIndex++)
                    {
                        long shaderOffset = fs.Position;

                        EnsureRemaining(
                            br,
                            ShaderSize,
                            $"Shader #{shaderIndex} em Model {id}, Sequence {sequenceId}");

                        ReadShader(
                            br,
                            xw,
                            id,
                            sequenceId,
                            shaderIndex,
                            shaderOffset);

                        totalShaders++;
                    }

                    xw.WriteEndElement(); // Sequence
                    totalSequences++;
                }

                xw.WriteEndElement(); // Model

                if ((modelIndex + 1) % 100 == 0 || modelIndex + 1 == modelCount)
                {
                    AppLogger.Log(
                        $"Models: {modelIndex + 1}/{modelCount} | " +
                        $"Offset: 0x{fs.Position:X}");
                }
            }

            xw.WriteEndElement(); // DigimonData
            xw.WriteEndDocument();

            if (fs.Position != fs.Length)
            {
                long remaining = fs.Length - fs.Position;

                throw new InvalidDataException(
                    $"O parser terminou em 0x{fs.Position:X}, " +
                    $"mas o DAT termina em 0x{fs.Length:X}. " +
                    $"Restam {remaining} bytes.");
            }

            AppLogger.Log(string.Empty);
            AppLogger.Log("Validação DAT -> XML:");
            AppLogger.Log($"  Models    : {modelCount}");
            AppLogger.Log($"  Sequences : {totalSequences}");
            AppLogger.Log($"  Events    : {totalEvents}");
            AppLogger.Log($"  Shaders   : {totalShaders}");
            AppLogger.Log($"  EOF       : OK");
        }

        private static void ReadEvent(
            BinaryReader br,
            XmlWriter xw,
            uint modelId,
            uint sequenceId,
            int eventIndex,
            long eventOffset)
        {
            float eventTime = ReadSingle(br, "s_fEventTime");
            int type = ReadInt32(br, "s_nType");
            int staticIndex = ReadInt32(br, "s_nStaticIndex");

            string text = ReadFixedString(br, EventTextSize);

            uint plag = ReadUInt32(br, "s_dwPlag");

            float offsetX = ReadSingle(br, "s_vOffsetx");
            float offsetY = ReadSingle(br, "s_vOffsety");
            float offsetZ = ReadSingle(br, "s_vOffsetz");

            float effectScale = ReadSingle(br, "s_fEffectScale");

            byte parentScale = ReadByte(br, "s_bParentScale");
            byte[] parentPadding = ReadExact(
                br,
                3,
                "padding após s_bParentScale");

            float fadeoutTime = ReadSingle(br, "s_fFadeoutTime");

            float valueX = ReadSingle(br, "s_vValuex");
            float valueY = ReadSingle(br, "s_vValuey");
            float valueZ = ReadSingle(br, "s_vValuez");

            float value2X = ReadSingle(br, "s_vValue2x");
            float value2Y = ReadSingle(br, "s_vValue2y");
            float value2Z = ReadSingle(br, "s_vValue2z");

            // IMPORTANTE:
            // s_fUnknown1 e s_fUnknown2 existem apenas no XML legado.
            // NÃO existem fisicamente no Model.dat.
            //
            // O Event físico termina depois de s_vValue2z:
            // 192 bytes no total.
            const float unknown1 = 700.0f;
            const float unknown2 = 1200.0f;

            long consumed = br.BaseStream.Position - eventOffset;
            if (consumed != EventSize)
            {
                throw new InvalidDataException(
                    $"Event size inválido em Model {modelId}, " +
                    $"Sequence {sequenceId}, Event {eventIndex}. " +
                    $"Lidos {consumed}, esperados {EventSize}.");
            }

            xw.WriteStartElement("Event");

            WriteFloat(xw, "s_fEventTime", eventTime);
            WriteElement(xw, "s_nType", type);
            WriteElement(xw, "s_nStaticIndex", staticIndex);
            WriteElement(xw, "s_cText", text);
            WriteElement(xw, "s_dwPlag", plag);

            WriteFloat(xw, "s_vOffsetx", offsetX);
            WriteFloat(xw, "s_vOffsety", offsetY);
            WriteFloat(xw, "s_vOffsetz", offsetZ);

            WriteFloat(xw, "s_fEffectScale", effectScale);
            WriteElement(xw, "s_bParentScale", parentScale);

            // Mantido por compatibilidade visual com o XML antigo.
            WriteElement(xw, "unk", "vb29");

            // Preserva os 3 bytes reais.
            WriteElement(
                xw,
                "s_ParentScalePadding",
                Convert.ToBase64String(parentPadding));

            WriteFloat(xw, "s_fFadeoutTime", fadeoutTime);

            WriteFloat(xw, "s_vValuex", valueX);
            WriteFloat(xw, "s_vValuey", valueY);
            WriteFloat(xw, "s_vValuez", valueZ);

            WriteFloat(xw, "s_vValue2x", value2X);
            WriteFloat(xw, "s_vValue2y", value2Y);
            WriteFloat(xw, "s_vValue2z", value2Z);

            WriteFloat(xw, "s_fUnknown1", unknown1);
            WriteFloat(xw, "s_fUnknown2", unknown2);

            xw.WriteEndElement(); // Event
        }

        private static void ReadShader(
            BinaryReader br,
            XmlWriter xw,
            uint modelId,
            uint sequenceId,
            int shaderIndex,
            long shaderOffset)
        {
            string applyObjectName = ReadFixedString(br, ShaderNameSize);

            int shaderType = ReadInt32(br, "s_eShaderType");
            int value1Int = ReadInt32(br, "s_nValue1");

            float value1 = ReadSingle(br, "s_fValue1");
            float value2 = ReadSingle(br, "s_fValue2");
            float value3 = ReadSingle(br, "s_fValue3");

            int dummy0 = ReadInt32(br, "s_nDummy[0]");
            int dummy1 = ReadInt32(br, "s_nDummy[1]");
            int dummy2 = ReadInt32(br, "s_nDummy[2]");
            int dummy3 = ReadInt32(br, "s_nDummy[3]");

            long consumed = br.BaseStream.Position - shaderOffset;
            if (consumed != ShaderSize)
            {
                throw new InvalidDataException(
                    $"Shader size inválido em Model {modelId}, " +
                    $"Sequence {sequenceId}, Shader {shaderIndex}. " +
                    $"Lidos {consumed}, esperados {ShaderSize}.");
            }

            xw.WriteStartElement("Shader");

            WriteElement(xw, "s_cApplyObjectName", applyObjectName);
            WriteElement(xw, "s_eShaderType", shaderType);
            WriteElement(xw, "s_nValue1", value1Int);

            WriteFloat(xw, "s_fValue1", value1);
            WriteFloat(xw, "s_fValue2", value2);
            WriteFloat(xw, "s_fValue3", value3);

            WriteElement(
                xw,
                "s_nDummy",
                string.Join(
                    ",",
                    dummy0.ToString(Inv),
                    dummy1.ToString(Inv),
                    dummy2.ToString(Inv),
                    dummy3.ToString(Inv)));

            xw.WriteEndElement(); // Shader
        }

        // ============================================================
        // XML -> DAT
        // ============================================================

        public static void ImportXmlToDat(string xmlPath, string datPath)
        {
            XDocument doc = XDocument.Load(
                xmlPath,
                LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);

            XElement root = doc.Root
                ?? throw new InvalidDataException("XML sem elemento raiz.");

            if (!string.Equals(
                    root.Name.LocalName,
                    "DigimonData",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Elemento raiz inválido: {root.Name}. " +
                    "Esperado: DigimonData.");
            }

            List<XElement> models = root
                .Elements("Model")
                .ToList();

            if (models.Count == 0)
                throw new InvalidDataException(
                    "O XML não contém nenhum elemento <Model>.");

            using FileStream fs = new(
                datPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            using BinaryWriter bw = new(fs);

            // Header
            bw.Write((uint)models.Count);

            int totalSequences = 0;
            int totalEvents = 0;
            int totalShaders = 0;

            for (int modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                XElement model = models[modelIndex];

                uint id = GetUInt32(model, "s_dwID");
                string kfmPath = GetString(model, "s_cKfmPath");

                float scale = GetFloat(model, "s_fScale");
                float height = GetFloat(model, "s_fHeight");
                float width = GetFloat(model, "s_fWidth");

                List<XElement> sequences = model
                    .Elements("Sequence")
                    .ToList();

                int declaredSequenceCount = GetInt32(
                    model,
                    "s_nSequenceCount",
                    sequences.Count);

                if (declaredSequenceCount != sequences.Count)
                {
                    AppLogger.Log(
                        $"Aviso Model {id}: s_nSequenceCount=" +
                        $"{declaredSequenceCount}, mas existem " +
                        $"{sequences.Count} <Sequence>. " +
                        $"Será usado {sequences.Count}.");
                }

                byte[] modelDummy = GetBase64OrDefault(
                    model,
                    "s_Dummy",
                    ModelDummySize);

                bw.Write(id);
                WriteFixedString(bw, kfmPath, ModelPathSize);

                bw.Write(scale);
                bw.Write(height);
                bw.Write(width);

                // O DAT deve refletir os elementos reais do XML.
                bw.Write(sequences.Count);

                bw.Write(modelDummy);

                foreach (XElement sequence in sequences)
                {
                    uint sequenceId = GetUInt32(
                        sequence,
                        "s_dwSequenceID");

                    List<XElement> events = sequence
                        .Elements("Event")
                        .ToList();

                    List<XElement> shaders = sequence
                        .Elements("Shader")
                        .ToList();

                    int loopCount = GetInt32(
                        sequence,
                        "s_nLoopCnt",
                        0);

                    int declaredEventCount = GetInt32(
                        sequence,
                        "s_nEventCount",
                        events.Count);

                    int declaredShaderCount = GetInt32(
                        sequence,
                        "s_nShaderCnt",
                        shaders.Count);

                    if (declaredEventCount != events.Count)
                    {
                        AppLogger.Log(
                            $"Aviso Model {id}, Sequence {sequenceId}: " +
                            $"s_nEventCount={declaredEventCount}, " +
                            $"mas existem {events.Count} <Event>. " +
                            $"Será usado {events.Count}.");
                    }

                    if (declaredShaderCount != shaders.Count)
                    {
                        AppLogger.Log(
                            $"Aviso Model {id}, Sequence {sequenceId}: " +
                            $"s_nShaderCnt={declaredShaderCount}, " +
                            $"mas existem {shaders.Count} <Shader>. " +
                            $"Será usado {shaders.Count}.");
                    }

                    ValidateCount(
                        loopCount,
                        100_000,
                        $"s_nLoopCnt em Model {id}, Sequence {sequenceId}");

                    bw.Write(sequenceId);
                    bw.Write(events.Count);
                    bw.Write(loopCount);
                    bw.Write(shaders.Count);

                    foreach (XElement evt in events)
                    {
                        WriteEventToDat(
                            bw,
                            evt,
                            id,
                            sequenceId);

                        totalEvents++;
                    }

                    foreach (XElement shader in shaders)
                    {
                        WriteShaderToDat(
                            bw,
                            shader,
                            id,
                            sequenceId);

                        totalShaders++;
                    }

                    totalSequences++;
                }

                if ((modelIndex + 1) % 100 == 0 ||
                    modelIndex + 1 == models.Count)
                {
                    AppLogger.Log(
                        $"Models: {modelIndex + 1}/{models.Count} | " +
                        $"Offset DAT: 0x{fs.Position:X}");
                }
            }

            bw.Flush();

            AppLogger.Log(string.Empty);
            AppLogger.Log("Validação XML -> DAT:");
            AppLogger.Log($"  Models    : {models.Count}");
            AppLogger.Log($"  Sequences : {totalSequences}");
            AppLogger.Log($"  Events    : {totalEvents}");
            AppLogger.Log($"  Shaders   : {totalShaders}");
            AppLogger.Log($"  Bytes DAT : {fs.Length}");
        }

        private static void WriteEventToDat(
            BinaryWriter bw,
            XElement evt,
            uint modelId,
            uint sequenceId)
        {
            long start = bw.BaseStream.Position;

            bw.Write(GetFloat(evt, "s_fEventTime"));
            bw.Write(GetInt32(evt, "s_nType"));
            bw.Write(GetInt32(evt, "s_nStaticIndex"));

            WriteFixedString(
                bw,
                GetString(evt, "s_cText"),
                EventTextSize);

            bw.Write(GetUInt32(evt, "s_dwPlag"));

            bw.Write(GetFloat(evt, "s_vOffsetx"));
            bw.Write(GetFloat(evt, "s_vOffsety"));
            bw.Write(GetFloat(evt, "s_vOffsetz"));

            bw.Write(GetFloat(evt, "s_fEffectScale"));

            int parentScaleInt = GetInt32(
                evt,
                "s_bParentScale",
                0);

            if (parentScaleInt < byte.MinValue ||
                parentScaleInt > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"s_bParentScale fora de Byte em Model {modelId}, " +
                    $"Sequence {sequenceId}: {parentScaleInt}");
            }

            bw.Write((byte)parentScaleInt);

            byte[] parentPadding = GetBase64OrDefault(
                evt,
                "s_ParentScalePadding",
                3);

            bw.Write(parentPadding);

            bw.Write(GetFloat(evt, "s_fFadeoutTime"));

            bw.Write(GetFloat(evt, "s_vValuex"));
            bw.Write(GetFloat(evt, "s_vValuey"));
            bw.Write(GetFloat(evt, "s_vValuez"));

            bw.Write(GetFloat(evt, "s_vValue2x"));
            bw.Write(GetFloat(evt, "s_vValue2y"));
            bw.Write(GetFloat(evt, "s_vValue2z"));

            /*
             * IMPORTANTE:
             * <s_fUnknown1> e <s_fUnknown2> são mantidos no XML apenas
             * por compatibilidade visual com os XMLs antigos.
             *
             * Eles NÃO possuem bytes físicos no Model.dat e por isso
             * NÃO devem ser escritos aqui.
             *
             * O Event físico termina em s_vValue2z e ocupa 192 bytes.
             */
            long size = bw.BaseStream.Position - start;

            if (size != EventSize)
            {
                throw new InvalidDataException(
                    $"Event gerado com {size} bytes em Model {modelId}, " +
                    $"Sequence {sequenceId}; esperado: {EventSize}.");
            }
        }

        private static void WriteShaderToDat(
            BinaryWriter bw,
            XElement shader,
            uint modelId,
            uint sequenceId)
        {
            long start = bw.BaseStream.Position;

            WriteFixedString(
                bw,
                GetString(shader, "s_cApplyObjectName"),
                ShaderNameSize);

            bw.Write(GetInt32(shader, "s_eShaderType"));
            bw.Write(GetInt32(shader, "s_nValue1"));

            bw.Write(GetFloat(shader, "s_fValue1"));
            bw.Write(GetFloat(shader, "s_fValue2"));
            bw.Write(GetFloat(shader, "s_fValue3"));

            int[] dummy = ParseShaderDummy(
                GetString(shader, "s_nDummy", "0,0,0,0"));

            bw.Write(dummy[0]);
            bw.Write(dummy[1]);
            bw.Write(dummy[2]);
            bw.Write(dummy[3]);

            long size = bw.BaseStream.Position - start;

            if (size != ShaderSize)
            {
                throw new InvalidDataException(
                    $"Shader gerado com {size} bytes em Model {modelId}, " +
                    $"Sequence {sequenceId}; esperado: {ShaderSize}.");
            }
        }

        // ============================================================
        // XML helpers
        // ============================================================

        private static string GetString(
            XElement parent,
            string name,
            string defaultValue = "")
        {
            XElement? element = parent.Element(name);

            return element == null
                ? defaultValue
                : element.Value;
        }

        private static int GetInt32(
            XElement parent,
            string name)
        {
            string text = RequiredElementValue(parent, name);

            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    Inv,
                    out int value))
            {
                throw XmlValueException(
                    parent,
                    name,
                    text,
                    "Int32");
            }

            return value;
        }

        private static int GetInt32(
            XElement parent,
            string name,
            int defaultValue)
        {
            XElement? element = parent.Element(name);

            if (element == null ||
                string.IsNullOrWhiteSpace(element.Value))
            {
                return defaultValue;
            }

            string text = element.Value.Trim();

            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    Inv,
                    out int value))
            {
                throw XmlValueException(
                    parent,
                    name,
                    text,
                    "Int32");
            }

            return value;
        }

        private static uint GetUInt32(
            XElement parent,
            string name)
        {
            string text = RequiredElementValue(parent, name);

            if (!uint.TryParse(
                    text,
                    NumberStyles.Integer,
                    Inv,
                    out uint value))
            {
                throw XmlValueException(
                    parent,
                    name,
                    text,
                    "UInt32");
            }

            return value;
        }

        private static float GetFloat(
            XElement parent,
            string name)
        {
            string text = RequiredElementValue(parent, name);

            if (!float.TryParse(
                    text,
                    NumberStyles.Float,
                    Inv,
                    out float value))
            {
                throw XmlValueException(
                    parent,
                    name,
                    text,
                    "Single");
            }

            return value;
        }

        private static float GetFloat(
            XElement parent,
            string name,
            float defaultValue)
        {
            XElement? element = parent.Element(name);

            if (element == null ||
                string.IsNullOrWhiteSpace(element.Value))
            {
                return defaultValue;
            }

            string text = element.Value.Trim();

            if (!float.TryParse(
                    text,
                    NumberStyles.Float,
                    Inv,
                    out float value))
            {
                throw XmlValueException(
                    parent,
                    name,
                    text,
                    "Single");
            }

            return value;
        }

        private static string RequiredElementValue(
            XElement parent,
            string name)
        {
            XElement? element = parent.Element(name);

            if (element == null)
            {
                throw new InvalidDataException(
                    $"Campo obrigatório <{name}> não encontrado " +
                    $"em <{parent.Name.LocalName}>.");
            }

            return element.Value.Trim();
        }

        private static byte[] GetBase64OrDefault(
            XElement parent,
            string name,
            int requiredSize)
        {
            XElement? element = parent.Element(name);

            if (element == null ||
                string.IsNullOrWhiteSpace(element.Value))
            {
                return new byte[requiredSize];
            }

            byte[] data;

            try
            {
                data = Convert.FromBase64String(
                    element.Value.Trim());
            }
            catch (FormatException)
            {
                throw new InvalidDataException(
                    $"<{name}> contém Base64 inválido.");
            }

            if (data.Length != requiredSize)
            {
                throw new InvalidDataException(
                    $"<{name}> possui {data.Length} bytes, " +
                    $"mas são necessários {requiredSize}.");
            }

            return data;
        }

        private static int[] ParseShaderDummy(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new[] { 0, 0, 0, 0 };

            string[] parts = text
                .Split(
                    new[] { ',', ';', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                if (!int.TryParse(parts[0], NumberStyles.Integer, Inv, out int one))
                    throw new InvalidDataException(
                        $"s_nDummy inválido: {text}");

                return new[] { one, 0, 0, 0 };
            }

            if (parts.Length != 4)
            {
                throw new InvalidDataException(
                    $"s_nDummy deve conter 4 Int32 separados por vírgula. " +
                    $"Valor recebido: {text}");
            }

            int[] values = new int[4];

            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(
                        parts[i],
                        NumberStyles.Integer,
                        Inv,
                        out values[i]))
                {
                    throw new InvalidDataException(
                        $"s_nDummy inválido: {text}");
                }
            }

            return values;
        }

        private static InvalidDataException XmlValueException(
            XElement parent,
            string field,
            string value,
            string expectedType)
        {
            IXmlLineInfo info = parent;

            string line = info.HasLineInfo()
                ? $" Linha {info.LineNumber}."
                : string.Empty;

            return new InvalidDataException(
                $"Valor inválido em <{field}>: \"{value}\". " +
                $"Esperado: {expectedType}.{line}");
        }

        // ============================================================
        // Binary helpers
        // ============================================================

        private static string ReadFixedString(BinaryReader br, int size)
        {
            byte[] raw = ReadExact(
                br,
                size,
                $"fixed string [{size}]");

            int zero = Array.IndexOf(raw, (byte)0);
            int length = zero >= 0 ? zero : raw.Length;

            if (length == 0)
                return string.Empty;

            return _textEncoding
                .GetString(raw, 0, length)
                .TrimEnd('\0');
        }

        private static void WriteFixedString(
            BinaryWriter bw,
            string value,
            int size)
        {
            value ??= string.Empty;

            byte[] encoded = _textEncoding.GetBytes(value);

            /*
             * Buffer char[size].
             * Deixamos sempre espaço para NULL quando a string é demasiado grande.
             */
            if (encoded.Length >= size)
            {
                throw new InvalidDataException(
                    $"String demasiado longa para char[{size}]: " +
                    $"\"{value}\" ({encoded.Length} bytes).");
            }

            byte[] buffer = new byte[size];
            Array.Copy(encoded, buffer, encoded.Length);

            bw.Write(buffer);
        }

        private static byte[] ReadExact(
            BinaryReader br,
            int count,
            string field)
        {
            byte[] data = br.ReadBytes(count);

            if (data.Length != count)
            {
                throw new EndOfStreamException(
                    $"EOF inesperado ao ler {field}. " +
                    $"Esperados {count} bytes, recebidos {data.Length}. " +
                    $"Offset atual: 0x{br.BaseStream.Position:X}");
            }

            return data;
        }

        private static byte ReadByte(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(br, 1, field);
            return br.ReadByte();
        }

        private static int ReadInt32(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(br, 4, field);
            return br.ReadInt32();
        }

        private static uint ReadUInt32(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(br, 4, field);
            return br.ReadUInt32();
        }

        private static float ReadSingle(
            BinaryReader br,
            string field)
        {
            EnsureRemaining(br, 4, field);
            return br.ReadSingle();
        }

        private static void EnsureRemaining(
            BinaryReader br,
            long required,
            string context)
        {
            long remaining =
                br.BaseStream.Length - br.BaseStream.Position;

            if (remaining < required)
            {
                throw new EndOfStreamException(
                    $"Dados insuficientes ao ler {context}. " +
                    $"Offset: 0x{br.BaseStream.Position:X}, " +
                    $"necessários: {required}, disponíveis: {remaining}.");
            }
        }

        private static void ValidateCount(
            int value,
            int maximum,
            string field)
        {
            if (value < 0 || value > maximum)
            {
                throw new InvalidDataException(
                    $"{field} inválido: {value}.");
            }
        }

        // ============================================================
        // XML writing helpers
        // ============================================================

        private static void WriteElement(
            XmlWriter xw,
            string name,
            object? value)
        {
            xw.WriteStartElement(name);

            if (value != null)
            {
                string text = value switch
                {
                    IFormattable f => f.ToString(
                        null,
                        CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty
                };

                xw.WriteString(text);
            }

            xw.WriteEndElement();
        }

        private static void WriteFloat(
            XmlWriter xw,
            string name,
            float value)
        {
            string text =
                ((double)value).ToString(
                    "R",
                    CultureInfo.InvariantCulture);

            WriteElement(xw, name, text);
        }
    }
}
