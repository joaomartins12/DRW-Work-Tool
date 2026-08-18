using System;
using System.IO;

namespace DRW_Work_Tool.Core
{
    public static class AppPaths
    {
        public static string Root => AppContext.BaseDirectory;

        public static string Bin => Path.Combine(Root, "BIN");
        public static string Xml => Path.Combine(Root, "XML");
        public static string Output => Path.Combine(Root, "Output");
        public static string Logs => Path.Combine(Root, "Logs");

        public static string LogFile => Path.Combine(Logs, "converter_log.txt");

        public static void EnsureWorkspace()
        {
            Directory.CreateDirectory(Bin);
            Directory.CreateDirectory(Xml);
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Logs);

            if (!File.Exists(LogFile))
                File.WriteAllText(LogFile, string.Empty);
        }

        public static string GetXmlEntityFolder(string binBaseName)
        {
            string folder = Path.Combine(Xml, binBaseName);
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetXmlOutputPath(string binBaseName)
        {
            return Path.Combine(
                GetXmlEntityFolder(binBaseName),
                binBaseName + ".xml");
        }

        public static string GetBinOutputPath(string binBaseName)
        {
            Directory.CreateDirectory(Output);
            return Path.Combine(Output, binBaseName + ".bin");
        }

        public static string GetExpectedXmlInputPath(string binBaseName)
        {
            return Path.Combine(Xml, binBaseName, binBaseName + ".xml");
        }
    }
}
