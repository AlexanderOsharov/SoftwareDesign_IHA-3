using Serilog;
using KpoHw3.MetadataService.Data;
using KpoHw3.MetadataService.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, cfg) => cfg.WriteTo.Console());

// Регистрация DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Автоматическое применение миграций при старте
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        context.Database.Migrate(); // <-- Эта строка применит все миграции
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
        throw;
    }
}

// Health endpoint
app.MapHealthChecks("/health");

// === API ENDPOINTS ===

// Создание новой записи о сдаче
app.MapPost("/submissions", async (ApplicationDbContext db, WorkSubmissionDto dto) =>
{
    if (dto.StudentId == Guid.Empty || dto.AssignmentId == Guid.Empty || string.IsNullOrWhiteSpace(dto.FileId))
        return Results.BadRequest("Invalid input");

    var submission = new WorkSubmission
    {
        StudentId = dto.StudentId,
        AssignmentId = dto.AssignmentId,
        FileId = dto.FileId,
        SubmittedAt = DateTime.UtcNow
    };

    db.WorkSubmissions.Add(submission);
    await db.SaveChangesAsync();

    return Results.Created($"/works/{submission.WorkId}", new { workId = submission.WorkId });
});

// Получение записи по WorkId
app.MapGet("/works/{workId:Guid}", async (ApplicationDbContext db, Guid workId) =>
{
    var work = await db.WorkSubmissions.FindAsync(workId);
    if (work == null)
        return Results.NotFound();

    return Results.Ok(work);
});

// Обновление ReportId (вызывается из API Gateway)
app.MapPost("/works/{workId:Guid}/reports", async (ApplicationDbContext db, Guid workId, SetReportIdDto dto) =>
{
    var work = await db.WorkSubmissions.FindAsync(workId);
    if (work == null) return Results.NotFound();
    work.ReportId = dto.ReportId;
    await db.SaveChangesAsync();
    return Results.Ok();
});

// Поиск работ по хешу текста (для антиплагиата)
app.MapGet("/submissions/by-hash/{textHash}", async (ApplicationDbContext db, string textHash) =>
{
    if (string.IsNullOrWhiteSpace(textHash) || textHash.Length != 64)
        return Results.BadRequest("Invalid hash format");

    var submissions = await db.WorkSubmissions
        .Where(w => w.TextHash == textHash)
        .Select(w => new
        {
            w.WorkId,
            w.StudentId,
            w.SubmittedAt,
            w.TextHash
        })
        .ToListAsync();
    Console.WriteLine($"[DEBUG] Found {submissions.Count} submissions with hash {textHash}");
    return Results.Ok(submissions);
});

// Обновление хеша текста (вызывается из File Analysis Service)
app.MapPost("/works/{workId:Guid}/text-hash", async (ApplicationDbContext db, Guid workId, SetTextHashDto dto) =>
{
    var work = await db.WorkSubmissions.FindAsync(workId);
    if (work == null)
        return Results.NotFound();

    work.TextHash = dto.TextHash;
    await db.SaveChangesAsync();

    return Results.Ok();
});

app.Run();

record WorkSubmissionDto(Guid StudentId, Guid AssignmentId, string FileId);
record SetTextHashDto(string TextHash);
record SetReportIdDto(string ReportId);