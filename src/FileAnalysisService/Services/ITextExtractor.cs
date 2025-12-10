namespace KpoHw3.FileAnalysisService.Services;

public interface ITextExtractor
{
    bool CanExtract(string fileName);
    Task<string> ExtractTextAsync(byte[] fileBytes);
}