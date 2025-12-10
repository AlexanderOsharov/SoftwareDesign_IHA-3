using KpoHw3.FileStoringService.Storage;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Логирование
builder.Host.UseSerilog((ctx, cfg) => cfg.WriteTo.Console());

// Регистрация хранилища как singleton
builder.Services.AddSingleton<LocalFileStorage>();

// Health check
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

// === API ENDPOINTS ===

// Сохранение файла
app.MapPost("/files", async (HttpContext ctx, LocalFileStorage storage) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var file = form.Files.FirstOrDefault();

    if (file == null || file.Length == 0)
        return Results.BadRequest("No file provided");

    // Ограничиваем размер до 100 МБ
    if (file.Length > 100 * 1024 * 1024)
        return Results.BadRequest("File too large (max 100 MB)");

    using var stream = file.OpenReadStream();
    var fileId = await storage.SaveFileAsync(stream, file.FileName);

    return Results.Ok(new { fileId });
});

// Получение файла
app.MapGet("/files/{fileId}", async (string fileId, LocalFileStorage storage) =>
{
    var stream = storage.OpenFileReadStream(fileId);
    if (stream == null)
        return Results.NotFound();

    return Results.File(stream, "application/octet-stream", enableRangeProcessing: true);
});

app.Run();