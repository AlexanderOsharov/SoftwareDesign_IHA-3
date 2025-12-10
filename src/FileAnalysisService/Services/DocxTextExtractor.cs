using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace KpoHw3.FileAnalysisService.Services;

public class DocxTextExtractor : ITextExtractor
{
    public bool CanExtract(string fileName) =>
        Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] fileBytes)
    {
        using var memory = new MemoryStream(fileBytes);
        using var doc = WordprocessingDocument.Open(memory, false);
        var body = doc.MainDocumentPart?.Document.Body;
        //return body?.InnerText ?? string.Empty;
        return Task.FromResult(body?.InnerText ?? string.Empty);
    }
}