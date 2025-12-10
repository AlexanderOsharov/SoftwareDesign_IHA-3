using Microsoft.AspNetCore.Http.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.WriteTo.Console());

var config = builder.Configuration;

// Настройка клиентов для микросервисов
builder.Services.AddHttpClient("MetadataService", client =>
{
    client.BaseAddress = new Uri(config["ServiceUrls:Metadata"] ?? "http://metadata-service:8080/");
});

builder.Services.AddHttpClient("FileStoringService", client =>
{
    client.BaseAddress = new Uri(config["ServiceUrls:FileStoring"] ?? "http://file-storing:8080/");
});

builder.Services.AddHttpClient("FileAnalysisService", client =>
{
    client.BaseAddress = new Uri(config["ServiceUrls:FileAnalysis"] ?? "http://file-analysis:8080/");
});

var app = builder.Build();

// Маршрутизация запросов к микросервисам
app.Map("/metadata/{**path}", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("MetadataService");
    return await ProxyRequest(context, client);
});

app.Map("/files/{**path}", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("FileStoringService");
    return await ProxyRequest(context, client);
});

app.Map("/analysis/{**path}", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    var client = clientFactory.CreateClient("FileAnalysisService");
    return await ProxyRequest(context, client);
});

// Основной эндпоинт для отправки работы (интегрирует несколько сервисов)
app.MapPost("/api/submit-work", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    var form = await context.Request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file provided");

    // 1. Сохраняем файл в FileStoringService
    var fileStoringClient = clientFactory.CreateClient("FileStoringService");
    var fileContent = new MultipartFormDataContent
    {
        { new StreamContent(file.OpenReadStream()), "file", file.FileName }
    };
    
    var fileResponse = await fileStoringClient.PostAsync("/files", fileContent);
    if (!fileResponse.IsSuccessStatusCode)
        return Results.Problem("Failed to store file");
    
    var fileResult = await fileResponse.Content.ReadFromJsonAsync<FileStoringResponse>();
    
    // 2. Сохраняем метаданные в MetadataService
    var metadataClient = clientFactory.CreateClient("MetadataService");
    var studentId = form["studentId"].ToString();
    var assignmentId = form["assignmentId"].ToString();
    
    var metadataRequest = new
    {
        StudentId = Guid.Parse(studentId),
        AssignmentId = Guid.Parse(assignmentId),
        FileId = fileResult?.FileId
    };
    
    var metadataResponse = await metadataClient.PostAsJsonAsync("/submissions", metadataRequest);
    if (!metadataResponse.IsSuccessStatusCode)
        return Results.Problem("Failed to save metadata");
    
    var metadataResult = await metadataResponse.Content.ReadFromJsonAsync<MetadataResponse>();
    
    // 3. Запускаем анализ в FileAnalysisService
    var analysisClient = clientFactory.CreateClient("FileAnalysisService");
    var analysisRequest = new
    {
        FileId = fileResult?.FileId,
        WorkId = metadataResult?.WorkId,
        FileName = file.FileName
    };
    
    var analysisResponse = await analysisClient.PostAsJsonAsync("/analyze", analysisRequest);
    Console.WriteLine($"Analysis response status: {analysisResponse.StatusCode}");
    Console.WriteLine($"Analysis response content: {await analysisResponse.Content.ReadAsStringAsync()}");
    if (analysisResponse.IsSuccessStatusCode)
    {
        var analysisResult = await analysisResponse.Content.ReadFromJsonAsync<AnalysisResult>();
        
        // Обновляем метаданные с ReportId
        var updateResponse = await metadataClient.PostAsJsonAsync(
            $"/works/{metadataResult?.WorkId}/reports/{analysisResult?.ReportId}", 
            new { }
        );
    }
    
    // Возвращаем результат
    return Results.Ok(new
    {
        WorkId = metadataResult?.WorkId,
        FileId = fileResult?.FileId,
        AnalysisStarted = analysisResponse.IsSuccessStatusCode
    });
});

// Получение аналитики по работе
app.MapGet("/api/works/{workId}/reports", async (Guid workId, IHttpClientFactory clientFactory) =>
{
    // 1. Получаем метаданные работы
    var metadataClient = clientFactory.CreateClient("MetadataService");
    var metadataResponse = await metadataClient.GetAsync($"/works/{workId}");
    
    if (!metadataResponse.IsSuccessStatusCode)
        return Results.NotFound();
    
    var work = await metadataResponse.Content.ReadFromJsonAsync<WorkMetadata>();
    
    // 2. Получаем отчет анализа
    var analysisClient = clientFactory.CreateClient("FileAnalysisService");
    var analysisResponse = await analysisClient.GetAsync($"/reports/{work.ReportId}");
    
    if (!analysisResponse.IsSuccessStatusCode)
        return Results.NotFound();
    
    var report = await analysisResponse.Content.ReadFromJsonAsync<AnalysisReport>();
    
    return Results.Ok(new
    {
        Work = work,
        Report = report
    });
});

app.MapGet("/", () => "API Gateway is running. Use /api/submit-work to upload work or /api/works/{id}/reports to get analysis.");

app.Run();

// Вспомогательные методы и классы
static async Task<IResult> ProxyRequest(HttpContext context, HttpClient client)
{
    var request = context.Request;
    var targetUri = new Uri(client.BaseAddress!, request.Path + request.QueryString);
    
    var targetRequestMessage = new HttpRequestMessage();
    targetRequestMessage.RequestUri = targetUri;
    targetRequestMessage.Method = new HttpMethod(request.Method);
    
    // Копируем заголовки
    foreach (var header in request.Headers)
    {
        targetRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
    
    // Копируем тело запроса
    if (request.Body.CanRead)
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        ms.Seek(0, SeekOrigin.Begin);
        targetRequestMessage.Content = new StreamContent(ms);
        
        if (request.ContentType != null)
        {
            targetRequestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
        }
    }
    
    var response = await client.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    
    var result = new HttpResponseMessage(response.StatusCode)
    {
        Content = response.Content
    };
    
    // Копируем заголовки ответа
    foreach (var header in response.Headers)
    {
        result.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
    
    foreach (var header in response.Content.Headers)
    {
        result.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
    
    return Results.Stream(await result.Content.ReadAsStreamAsync(), result.Content.Headers.ContentType?.MediaType);
}

// Вспомогательные классы
public record FileStoringResponse(string FileId);
public record MetadataResponse(Guid WorkId);
public record WorkMetadata(Guid WorkId, Guid StudentId, Guid AssignmentId, DateTime SubmittedAt, string FileId, string? ReportId);
public record AnalysisResult(string ReportId);
public record AnalysisReport(string ReportId, string FileId, bool Plagiarism, List<PlagiarismEvidence> PlagiarismEvidence, string? WordCloudUrl, DateTime CreatedAt, string Status = "completed");
public record PlagiarismEvidence(Guid WorkId, Guid StudentId, DateTime SubmittedAt);