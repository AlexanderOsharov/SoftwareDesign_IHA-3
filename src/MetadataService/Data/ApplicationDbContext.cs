using KpoHw3.MetadataService.Models;
using Microsoft.EntityFrameworkCore;

namespace KpoHw3.MetadataService.Data;

/// <summary>
/// Контекст базы данных для управления сущностями сдачи работ.
/// Использует PostgreSQL через Npgsql.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Набор записей о сданных работах.
    /// </summary>
    public DbSet<WorkSubmission> WorkSubmissions { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkSubmission>(entity =>
        {
            entity.HasKey(e => e.WorkId);
            entity.Property(e => e.WorkId).ValueGeneratedOnAdd();
            entity.Property(e => e.StudentId).IsRequired();
            entity.Property(e => e.AssignmentId).IsRequired();
            entity.Property(e => e.SubmittedAt).IsRequired();
            entity.Property(e => e.FileId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ReportId).HasMaxLength(100);
            entity.Property(e => e.TextHash).HasMaxLength(64); // SHA256 = 32 байта -> 64 hex
        });

        base.OnModelCreating(modelBuilder);
    }
}