namespace DRW_Work_Tool.Converters
{
    public interface IGameDataConverter
    {
        string Name { get; }

        bool MatchesBin(string filePath);
        bool MatchesXml(string filePath);

        void BinToXml(string inputBin, string outputXml);
        void XmlToBin(string inputXml, string outputBin);
    }
}
