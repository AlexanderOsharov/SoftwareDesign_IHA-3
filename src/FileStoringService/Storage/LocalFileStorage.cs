using System.IO;

namespace KpoHw3.FileStoringService.Storage;

/// <summary>
/// Реализует локальное файловое хранилище с генерацией уникальных идентификаторов файлов.
/// Файлы сохраняются на volume, доступном через Docker.
/// </summary>
public class LocalFileStorage
{
    private readonly string _storagePath;

    public LocalFileStorage(IConfiguration config)
    {
        _storagePath = config["Storage:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "files");
        Directory.CreateDirectory(_storagePath); // Обеспечивает существование каталога
    }

    /// <summary>
    /// Сохраняет поток как новый файл и возвращает уникальный fileId.
    /// Имя файла — GUID.
    /// </summary>
    public async Task<string> SaveFileAsync(Stream fileStream, string? originalFileName = null)
    {
        var fileId = Guid.NewGuid().ToString();
        var filePath = Path.Combine(_storagePath, fileId);

        await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fileStream.CopyToAsync(fs);

        return fileId;
    }

    /// <summary>
    /// Открывает поток для чтения файла по fileId.
    /// Возвращает null, если файл не найден.
    /// </summary>
    public Stream? OpenFileReadStream(string fileId)
    {
        var filePath = Path.Combine(_storagePath, fileId);
        return File.Exists(filePath)
            ? new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
    }

    /// <summary>
    /// Асинхронно читает содержимое файла в байтовый массив (используется для анализа).
    /// </summary>
    public async Task<byte[]?> ReadFileBytesAsync(string fileId)
    {
        var filePath = Path.Combine(_storagePath, fileId);
        return File.Exists(filePath)
            ? await File.ReadAllBytesAsync(filePath)
            : null;
    }
}