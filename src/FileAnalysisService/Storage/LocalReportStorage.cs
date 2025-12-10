using System.Text.Json;
using KpoHw3.FileAnalysisService.Models;

namespace KpoHw3.FileAnalysisService.Storage;

public class LocalReportStorage
{
    private readonly string _storagePath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public LocalReportStorage(IConfiguration config)
    {
        _storagePath = config["Storage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "reports");
        Directory.CreateDirectory(_storagePath);
    }

    public async Task SaveReportAsync(AnalysisReport report)
    {
        var filePath = Path.Combine(_storagePath, $"{report.ReportId}.json");
        var json = JsonSerializer.Serialize(report, _options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<AnalysisReport?> LoadReportAsync(string reportId)
    {
        var filePath = Path.Combine(_storagePath, $"{reportId}.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<AnalysisReport>(json);
    }
}