namespace KpoHw3.MetadataService.Models;

/// <summary>
/// Представляет запись о сдаче контрольной работы студентом.
/// Является основной бизнес-сущностью в системе учёта работ.
/// </summary>
public class WorkSubmission
{
    /// <summary>
    /// Уникальный идентификатор сданной работы (PK).
    /// Генерируется при создании записи.
    /// </summary>
    public Guid WorkId { get; set; }

    /// <summary>
    /// Идентификатор студента (в MVP — произвольный GUID).
    /// </summary>
    public Guid StudentId { get; set; }

    /// <summary>
    /// Идентификатор задания/контрольной работы.
    /// </summary>
    public Guid AssignmentId { get; set; }

    /// <summary>
    /// Дата и время сдачи работы в UTC.
    /// Устанавливается автоматически при создании записи.
    /// </summary>
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Внешний идентификатор файла, хранящегося в File Storing Service.
    /// Не может быть null — файл всегда загружается вместе с метаданными.
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// Внешний идентификатор отчёта анализа (может быть null до завершения анализа).
    /// Заполняется позже, после завершения работы File Analysis Service.
    /// </summary>
    public string? ReportId { get; set; }

    /// <summary>
    /// Хеш нормализованного текста работы (SHA256 в lowercase hex).
    /// Используется для быстрого поиска дубликатов.
    /// Может быть null, если файл не текстовый или анализ ещё не запущен.
    /// </summary>
    public string? TextHash { get; set; }
}