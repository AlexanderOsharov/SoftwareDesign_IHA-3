using Microsoft.AspNetCore.Http.Extensions;
using Serilog;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.WriteTo.Console());

var config = builder.Configuration;

// Глобальные настройки JSON
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

// Регистрация HTTP-клиентов
builder.Services.AddHttpClient("MetadataService", client =>
    client.BaseAddress = new Uri(config["ServiceUrls:Metadata"] ?? "http://metadata-service:8080/"));

builder.Services.AddHttpClient("FileStoringService", client =>
    client.BaseAddress = new Uri(config["ServiceUrls:FileStoring"] ?? "http://file-storing:8080/"));

builder.Services.AddHttpClient("FileAnalysisService", client =>
    client.BaseAddress = new Uri(config["ServiceUrls:FileAnalysis"] ?? "http://file-analysis:8080/"));

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger — всегда включён
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Anti-Plagiarism API v1");
    c.RoutePrefix = string.Empty;
});

// Глобальный обработчик ошибок
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Internal service error" }, jsonOptions);
    });
});

// Основной эндпоинт: отправка работы
app.MapPost("/api/submit-work", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    var form = await context.Request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file provided");

    // 1. Сохраняем файл
    var fileStoringClient = clientFactory.CreateClient("FileStoringService");
    var fileContent = new MultipartFormDataContent
    {
        { new StreamContent(file.OpenReadStream()), "file", file.FileName }
    };
    var fileResponse = await fileStoringClient.PostAsync("/files", fileContent);
    if (!fileResponse.IsSuccessStatusCode)
        return Results.Problem("Failed to store file");
    var fileResult = await fileResponse.Content.ReadFromJsonAsync<FileStoringResponse>(jsonOptions);

    // 2. Сохраняем метаданные
    var metadataClient = clientFactory.CreateClient("MetadataService");
    var studentId = form["studentId"].ToString();
    var assignmentId = form["assignmentId"].ToString();
    var metadataRequest = new
    {
        StudentId = Guid.Parse(studentId),
        AssignmentId = Guid.Parse(assignmentId),
        FileId = fileResult?.FileId
    };
    var metadataResponse = await metadataClient.PostAsJsonAsync("/submissions", metadataRequest, jsonOptions);
    if (!metadataResponse.IsSuccessStatusCode)
        return Results.Problem("Failed to save metadata");
    var metadataResult = await metadataResponse.Content.ReadFromJsonAsync<MetadataResponse>(jsonOptions);

    // 3. Запускаем анализ
    var analysisClient = clientFactory.CreateClient("FileAnalysisService");
    var analysisRequest = new
    {
        FileId = fileResult?.FileId,
        WorkId = metadataResult?.WorkId,
        FileName = file.FileName
    };
    var analysisResponse = await analysisClient.PostAsJsonAsync("/analyze", analysisRequest, jsonOptions);
    var analysisStarted = analysisResponse.IsSuccessStatusCode;

    return Results.Ok(new
    {
        WorkId = metadataResult?.WorkId,
        FileId = fileResult?.FileId,
        AnalysisStarted = analysisStarted
    });
});

// Получение отчёта
app.MapGet("/api/works/{workId}/reports", async (Guid workId, IHttpClientFactory clientFactory) =>
{
    var metadataClient = clientFactory.CreateClient("MetadataService");
    var metadataResponse = await metadataClient.GetAsync($"/works/{workId}");
    if (!metadataResponse.IsSuccessStatusCode)
        return Results.NotFound();

    var work = await metadataResponse.Content.ReadFromJsonAsync<WorkMetadata>(jsonOptions);
    if (string.IsNullOrEmpty(work?.ReportId))
        return Results.Accepted("Report not ready yet", null);

    var analysisClient = clientFactory.CreateClient("FileAnalysisService");
    var analysisResponse = await analysisClient.GetAsync($"/reports/{work.ReportId}");
    if (!analysisResponse.IsSuccessStatusCode)
        return Results.NotFound();

    var report = await analysisResponse.Content.ReadFromJsonAsync<AnalysisReport>(jsonOptions);
    return Results.Ok(new { Work = work, Report = report });
});

app.MapGet("/", () => "API Gateway is running. Use / (Swagger) to explore API.");

app.Run();

// Вспомогательный метод прокси (используется только если вы оставите прокси)
static async Task<IResult> ProxyRequest(HttpContext context, HttpClient client)
{
    var request = context.Request;
    var targetUri = new Uri(client.BaseAddress!, request.Path + request.QueryString);
    var msg = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

    foreach (var header in request.Headers)
        msg.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

    if (request.Body.CanRead)
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms);
        ms.Position = 0;
        msg.Content = new StreamContent(ms);
        if (request.ContentType != null)
            msg.Content.Headers.ContentType = new(request.ContentType);
    }

    var response = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
    return Results.Stream(await response.Content.ReadAsStreamAsync(), response.Content.Headers.ContentType?.MediaType);
}

// Модели
public record FileStoringResponse(string FileId);
public record MetadataResponse(Guid WorkId);
public record WorkMetadata(Guid WorkId, Guid StudentId, Guid AssignmentId, DateTime SubmittedAt, string FileId, string? ReportId);
public record AnalysisReport(string ReportId, string FileId, bool Plagiarism, List<PlagiarismEvidence> PlagiarismEvidence, string? WordCloudUrl, DateTime CreatedAt, string Status = "completed");
public record PlagiarismEvidence(Guid WorkId, Guid StudentId, DateTime SubmittedAt);