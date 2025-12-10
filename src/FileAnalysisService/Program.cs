using Serilog;
using KpoHw3.FileAnalysisService.Clients;
using KpoHw3.FileAnalysisService.Models;
using KpoHw3.FileAnalysisService.Services;
using KpoHw3.FileAnalysisService.Storage;
using Microsoft.Extensions.Http.Resilience;
using Polly;

// Вспомогательные методы
static string NormalizeText(string text)
{
    if (string.IsNullOrEmpty(text))
        return string.Empty;

    // Только оставляем буквы, цифры и пробелы, приводим к нижнему регистру
    var result = string.Concat(text.Select(c => 
        char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' '));
    
    return result.ToLowerInvariant()
                 .Replace("  ", " ")
                 .Trim();
}

static string ComputeSha256(string input)
{
    using var sha = System.Security.Cryptography.SHA256.Create();
    var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(hash).ToLowerInvariant();
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.WriteTo.Console());

// Хранилище отчётов
builder.Services.AddSingleton<LocalReportStorage>();

// Извлечение текста
builder.Services.AddSingleton<ITextExtractor>(sp => new PlainTextExtractor());
builder.Services.AddSingleton<ITextExtractor>(sp => new DocxTextExtractor());

// HTTP-клиенты с resilience
builder.Services.AddHttpClient<FileStoringClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ServiceUrls:FileStoring"]!);
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 5;
});

builder.Services.AddHttpClient<MetadataClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ServiceUrls:Metadata"]!);
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 5;
});

builder.Services.AddHttpClient<QuickChartClient>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

// Анализ файла
app.MapPost("/analyze", async (
    AnalysisRequest request,
    FileStoringClient fileClient,
    MetadataClient metaClient,
    LocalReportStorage storage,
    IEnumerable<ITextExtractor> extractors) =>
{
    // 1. Получаем файл
    var fileBytes = await fileClient.GetFileBytesAsync(request.FileId);
    if (fileBytes == null) return Results.NotFound("File not found");

    // 2. Извлекаем текст
    var text = "";
    var extractor = extractors.FirstOrDefault(e => e.CanExtract(request.FileName ?? "unknown"));
    if (extractor != null)
        text = await extractor.ExtractTextAsync(fileBytes);
    else
        return Results.BadRequest("Unsupported file type");

    // 3. Нормализуем и хешируем
    var normalized = NormalizeText(text);
    var textHash = ComputeSha256(normalized);
    var updateHashResponse = await metaClient.UpdateTextHashAsync(request.WorkId, textHash);
    if (!updateHashResponse?.IsSuccessStatusCode ?? true)
    {
        Console.WriteLine($"⚠️ Failed to update TextHash for WorkId {request.WorkId}");
    }

    // 4. Проверяем дубликаты
    var earlierSubmissions = await metaClient.GetSubmissionsWithHashAsync(textHash);
    Console.WriteLine($"[DEBUG] TextHash: {textHash}");
    Console.WriteLine($"[DEBUG] Earlier submissions count: {earlierSubmissions?.Count ?? 0}");
    var plagiarism = false;
    var evidence = new List<PlagiarismEvidence>();
    if (earlierSubmissions?.Count > 0)
    {
        var otherSubmissions = earlierSubmissions
        .Where(s => s.WorkId != request.WorkId) // исключаем себя
        .ToList();
    
        if (otherSubmissions.Any())
        {
            plagiarism = true;
            evidence = otherSubmissions
                .Select(s => new PlagiarismEvidence
                {
                    WorkId = s.WorkId,
                    StudentId = s.StudentId,
                    SubmittedAt = s.SubmittedAt
                }).ToList();
 
            Console.WriteLine($"[DEBUG] Found {evidence.Count} plagiarized submissions"); 
        }

        foreach (var sub in earlierSubmissions)
        {
            Console.WriteLine($"[DEBUG] Found submission: WorkId={sub.WorkId}, StudentId={sub.StudentId}");
        }
        
    }

    // 5. Генерируем облако слов
    var httpClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();
    var quickChart = new QuickChartClient(httpClient);
    var wordCloudUrl = await quickChart.GenerateWordCloudUrlAsync(text);

    // 6. Сохраняем отчёт
    var report = new AnalysisReport
    {
        FileId = request.FileId,
        Plagiarism = plagiarism,
        PlagiarismEvidence = evidence,
        WordCloudUrl = wordCloudUrl
    };
    await storage.SaveReportAsync(report);
    Console.WriteLine($"[DEBUG] Generated ReportId: {report.ReportId} for WorkId: {request.WorkId}");
    
    // 7. Обновляем ReportId в MetadataService через API Gateway
    try
    {
        var metadataClient = app.Services.GetRequiredService<MetadataClient>();
        var updateResponse = await metadataClient.UpdateReportIdAsync(request.WorkId, report.ReportId);
        Console.WriteLine($"Updated ReportId in MetadataService: {updateResponse?.IsSuccessStatusCode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to update ReportId: {ex.Message}");
    }
    
    return Results.Ok(new { reportId = report.ReportId });
});

// Получение отчёта
app.MapGet("/reports/{reportId}", async (string reportId, LocalReportStorage storage) =>
{
    var report = await storage.LoadReportAsync(reportId);
    return report != null ? Results.Ok(report) : Results.NotFound();
});

app.MapGet("/", () => "File Analysis Service is running");

app.Run();

record AnalysisRequest(string FileId, Guid WorkId, string? FileName = null);