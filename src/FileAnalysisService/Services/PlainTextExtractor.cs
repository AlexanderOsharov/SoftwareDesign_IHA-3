using System.Text;

namespace KpoHw3.FileAnalysisService.Services;

public class PlainTextExtractor : ITextExtractor
{
    public bool CanExtract(string fileName) =>
        Path.GetExtension(fileName).Equals(".txt", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] fileBytes) =>
        Task.FromResult(Encoding.UTF8.GetString(fileBytes));
}