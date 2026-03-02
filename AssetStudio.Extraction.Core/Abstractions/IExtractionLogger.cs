namespace AssetStudio.Extraction.Core.Abstractions
{
    public interface IExtractionLogger
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }
}
