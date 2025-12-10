using Spectre.Console;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace KpoHw3.ConsoleClient;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly string _baseUrl = Environment.GetEnvironmentVariable("API_GATEWAY_URL") ?? "http://localhost:8080"; // ApiGateway

    // ⚠️ КРИТИЧЕСКИ ВАЖНЫЕ НАСТРОЙКИ ДЛЯ ДЕСЕРИАЛИЗАЦИИ
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static async Task Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("Anti-Plagiarism System")
                .Color(Color.Blue));

        AnsiConsole.MarkupLine("[yellow]Добро пожаловать в систему проверки работ на плагиат![/]");
        
        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Выберите действие:")
                    .PageSize(10)
                    .AddChoices(new[] {
                        "1. Отправить работу на проверку",
                        "2. Получить отчет по работе",
                        "3. Создать тестовые данные",
                        "4. Провести тестирование API",
                        "5. Запустить полный тест",
                        "6. Проверить состояние сервисов",
                        "7. Выход"
                    }));

            switch (choice[0])
            {
                case '1':
                    await SubmitWorkAsync();
                    break;
                case '2':
                    await GetReportAsync();
                    break;
                case '3':
                    await CreateTestDataAsync();
                    break;
                case '4':
                    await TestDirectApiCall();
                    break;
                case '5':
                    await RunFullTestAsync();
                    break;
                case '6':
                    await CheckServicesAsync();
                    break;
                case '7':
                    AnsiConsole.MarkupLine("[green]До свидания![/]");
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Нажмите любую клавишу для продолжения...[/]");
            Console.ReadKey();
            Console.Clear();
        }
    }

    static async Task SubmitWorkAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Отправка работы на проверку[/]");
        
        // Получение данных от пользователя
        var filePath = AnsiConsole.Ask<string>("Введите путь к файлу (txt или docx):");
        
        if (!File.Exists(filePath))
        {
            AnsiConsole.MarkupLine("[red]Файл не найден![/]");
            return;
        }
    
        var fileName = Path.GetFileName(filePath);
        var studentId = AnsiConsole.Ask<Guid>("Введите ID студента (или нажмите Enter для генерации):", Guid.NewGuid());
        var assignmentId = AnsiConsole.Ask<Guid>("Введите ID задания (или нажмите Enter для генерации):", Guid.NewGuid());
    
        SubmitWorkResponse? result = null;
        bool success = false;
        
        // Отправка работы (первый Status)
        await AnsiConsole.Status()
            .StartAsync("Отправка работы...", async ctx =>
            {
                try
                {
                    using var form = new MultipartFormDataContent();
                    using var fileStream = File.OpenRead(filePath);
                    form.Add(new StreamContent(fileStream), "file", fileName);
                    form.Add(new StringContent(studentId.ToString()), "studentId");
                    form.Add(new StringContent(assignmentId.ToString()), "assignmentId");
    
                    var response = await _httpClient.PostAsync($"{_baseUrl}/api/submit-work", form);
                
                    if (response.IsSuccessStatusCode)
                    {
                        // Используем ReadFromJsonAsync для согласованности
                        result = await response.Content.ReadFromJsonAsync<SubmitWorkResponse>(_jsonOptions);
                        success = true;
                    }
                    else
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        AnsiConsole.MarkupLine($"[red]✗ Ошибка при отправке: {response.StatusCode} - {error}[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗ Ошибка: {ex.Message}[/]");
                }
            });

        if (!success || result == null) return;
        
        // Вывод результатов отправки
        AnsiConsole.MarkupLine($"[green]✓ Работа успешно отправлена![/]");
        AnsiConsole.MarkupLine($"[grey]WorkId: {result?.WorkId}[/]");
        AnsiConsole.MarkupLine($"[grey]FileId: {result?.FileId}[/]");
    
        // Ожидание анализа
        AnsiConsole.MarkupLine("[yellow]Ожидание анализа... (3 секунды)[/]");
        await Task.Delay(3000);
    
        // Запрос отчета (отдельный вызов)
        if (result?.WorkId != null)
        {
            await ShowReportAsync(result.WorkId.Value);
        }
    }

    static async Task GetReportAsync()
    {
        var workId = AnsiConsole.Ask<Guid>("Введите WorkId работы:");
        await ShowReportAsync(workId);
    }

    static async Task ShowReportAsync(Guid workId)
    {
        try
        {
            AnsiConsole.MarkupLine("[yellow]Получение отчета...[/]");
            
            // Пробуем несколько раз с паузами
            for (int attempt = 0; attempt < 5; attempt++) // максимум 5 попыток
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_baseUrl}/api/works/{workId}/reports");
                
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        AnsiConsole.WriteLine($"Raw JSON response:\n{json}");
                        
                        // 🔥 ИСПРАВЛЕНИЕ: ИСПОЛЬЗУЕМ _jsonOptions!
                        var result = JsonSerializer.Deserialize<ReportResponse>(json, _jsonOptions);
                        
                        if (result?.Work != null || result?.Report != null)
                        {
                            DisplayReport(result);
                            return;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Работа не найдена (404)[/]");
                        return;
                    }
                    
                    if (attempt < 4)
                    {
                        AnsiConsole.MarkupLine($"[grey]Попытка {attempt + 1}/5, ожидание 3 секунды...[/]");
                        await Task.Delay(3000);
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Ошибка на попытке {attempt + 1}: {ex.Message}[/]");
                    if (attempt < 4) await Task.Delay(2000);
                }
            }
            AnsiConsole.MarkupLine($"[yellow]Отчет еще не готов или работа не найдена[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Ошибка: {ex.Message}[/]");
            Console.WriteLine($"StackTrace: {ex.StackTrace}");
        }
    }

    static void DisplayReport(ReportResponse? report)
    {
        if (report == null) return;

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("Параметр");
        table.AddColumn("Значение");

        table.AddRow("[bold]Работа[/]", "");
        table.AddRow("WorkId", report.Work?.WorkId.ToString() ?? "N/A");
        table.AddRow("StudentId", report.Work?.StudentId.ToString() ?? "N/A");
        table.AddRow("AssignmentId", report.Work?.AssignmentId.ToString() ?? "N/A");
        table.AddRow("SubmittedAt", report.Work?.SubmittedAt.ToString("O") ?? "N/A");

        if (report.Report != null)
        {
            table.AddRow("[bold]Анализ[/]", "");
            table.AddRow("ReportId", report.Report.ReportId ?? "N/A");
            table.AddRow("Обнаружен плагиат", report.Report.Plagiarism ? "[red]ДА[/]" : "[green]НЕТ[/]");
            table.AddRow("Облако слов", report.Report.WordCloudUrl ?? "Не сгенерировано");
            
            if (report.Report.Plagiarism && report.Report.PlagiarismEvidence?.Any() == true)
            {
                table.AddRow("[bold]Найденные совпадения[/]", "");
                foreach (var evidence in report.Report.PlagiarismEvidence)
                {
                    table.AddRow($"  WorkId", evidence.WorkId.ToString());
                    table.AddRow($"  StudentId", evidence.StudentId.ToString());
                    table.AddRow($"  SubmittedAt", evidence.SubmittedAt.ToString("O"));
                }
            }
        }

        AnsiConsole.Write(table);

        // Показываем облако слов если есть
        if (!string.IsNullOrEmpty(report.Report?.WordCloudUrl))
        {
            AnsiConsole.MarkupLine($"[yellow]Ссылка на облако слов:[/] {report.Report.WordCloudUrl}");
            
            var open = AnsiConsole.Confirm("Открыть в браузере?");
            if (open)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = report.Report.WordCloudUrl,
                    UseShellExecute = true
                });
            }
        }
    }

    static async Task CreateTestDataAsync()
    {
        await AnsiConsole.Status()
            .StartAsync("Создание тестовых данных...", async ctx =>
            {
                try
                {
                    // Создаем тестовые файлы
                    var testDir = Path.Combine(Directory.GetCurrentDirectory(), "test_files");
                    Directory.CreateDirectory(testDir);

                    // Файл 1 - оригинальный
                    var file1 = Path.Combine(testDir, "work1.txt");
                    await File.WriteAllTextAsync(file1, 
                        "Исследование алгоритмов сортировки. Быстрая сортировка является эффективным алгоритмом.");
                    
                    // Файл 2 - плагиат (частичное совпадение)
                    var file2 = Path.Combine(testDir, "work2.txt");
                    await File.WriteAllTextAsync(file2,
                        "Алгоритмы сортировки важны в программировании. Быстрая сортировка - эффективный алгоритм.");

                    // Файл 3 - уникальный
                    var file3txt = Path.Combine(testDir, "work3.txt");
                    await File.WriteAllTextAsync(file3txt,
                        "Исследование структур данных. Деревья и графы используются для хранения данных.");

                    AnsiConsole.MarkupLine($"[green]✓ Созданы тестовые файлы в папке: {testDir}[/]");
                    AnsiConsole.MarkupLine("[grey]Теперь вы можете отправить их на проверку[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗ Ошибка: {ex.Message}[/]");
                }
            });
    }

    static async Task TestDirectApiCall()
    {
        var workId = AnsiConsole.Ask<Guid>("Введите WorkId для теста API:");
        
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/works/{workId}/reports");
            var json = await response.Content.ReadAsStringAsync();
            
            AnsiConsole.WriteLine($"Status Code: {response.StatusCode}");
            AnsiConsole.WriteLine($"Response JSON:\n{json}");
            
            if (response.IsSuccessStatusCode)
            {
                // 🔥 ИСПРАВЛЕНИЕ: ИСПОЛЬЗУЕМ _jsonOptions!
                var result = JsonSerializer.Deserialize<ReportResponse>(json, _jsonOptions);
                if (result != null)
                {
                    DisplayReport(result);
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Ошибка: {ex.Message}[/]");
        }
    }

    static async Task RunFullTestAsync()
    {
        AnsiConsole.MarkupLine("[yellow]Запуск полного теста системы...[/]");
        
        var panel = new Panel("Тестирование микросервисной системы проверки плагиата");
        panel.Border = BoxBorder.Rounded;
        panel.Header = new PanelHeader("FULL TEST");
        AnsiConsole.Write(panel);

        var testResults = new List<(string, bool, string)>();

        // Тест 1: Проверка сервисов
        testResults.Add(await TestService("ApiGateway", $"{_baseUrl}/"));
        testResults.Add(await TestService("MetadataService", "http://localhost:8081/health"));
        testResults.Add(await TestService("FileStoringService", "http://localhost:8082/health"));
        testResults.Add(await TestService("FileAnalysisService", "http://localhost:8083/health"));

        // Тест 2: Отправка работы
        await Task.Delay(1000);
        var submitResult = await TestSubmitWork();
        testResults.Add(submitResult);

        // Вывод результатов
        var resultTable = new Table();
        resultTable.Border(TableBorder.Rounded);
        resultTable.AddColumn("Тест");
        resultTable.AddColumn("Результат");
        resultTable.AddColumn("Детали");

        foreach (var (testName, success, details) in testResults)
        {
            resultTable.AddRow(
                testName,
                success ? "[green]PASS[/]" : "[red]FAIL[/]",
                details
            );
        }

        AnsiConsole.Write(resultTable);

        var passed = testResults.Count(r => r.Item2);
        var total = testResults.Count;
        
        if (passed == total)
            AnsiConsole.MarkupLine($"[green]✓ Все тесты пройдены ({passed}/{total})[/]");
        else
            AnsiConsole.MarkupLine($"[yellow]⚠ Пройдено {passed} из {total} тестов[/]");
    }

    static async Task<(string, bool, string)> TestService(string name, string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            return (name, response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (name, false, ex.Message);
        }
    }

    static async Task<(string, bool, string)> TestSubmitWork()
    {
        try
        {
            // Создаем временный файл с уникальным именем
            var tempFile = Path.GetTempFileName();
            var tempTxtFile = tempFile + ".txt";
            File.Move(tempFile, tempTxtFile);
            
            await File.WriteAllTextAsync(tempTxtFile, "Тестовый документ для проверки системы антиплагиата.");

            using var form = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(tempTxtFile);
            form.Add(new StreamContent(fileStream), "file", "test.txt");
            form.Add(new StringContent(Guid.NewGuid().ToString()), "studentId");
            form.Add(new StringContent(Guid.NewGuid().ToString()), "assignmentId");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/submit-work", form);
            
            // Закрываем поток перед удалением
            fileStream.Close();
            File.Delete(tempTxtFile);
    
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<SubmitWorkResponse>(_jsonOptions);
                return ("Submit Work", true, $"WorkId: {result?.WorkId}");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                return ("Submit Work", false, $"Status: {response.StatusCode}, Error: {error}");
            }
        }
        catch (Exception ex)
        {
            return ("Submit Work", false, ex.Message);
        }
    }

    static async Task CheckServicesAsync()
    {
        var services = new[]
        {
            ("ApiGateway", $"{_baseUrl}/"),
            ("MetadataService", "http://localhost:8081/health"),
            ("FileStoringService", "http://localhost:8082/health"),
            ("FileAnalysisService", "http://localhost:8083/health")
        };

        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("Сервис");
        table.AddColumn("Статус");
        table.AddColumn("URL");

        foreach (var (name, url) in services)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                table.AddRow(
                    name,
                    response.IsSuccessStatusCode ? "[green]✓ Работает[/]" : "[red]✗ Ошибка[/]",
                    url
                );
            }
            catch
            {
                table.AddRow(name, "[red]✗ Недоступен[/]", url);
            }
        }

        AnsiConsole.Write(table);
    }
}

// Модели для десериализации
public record SubmitWorkResponse(Guid? WorkId, string? FileId, bool AnalysisStarted);
public record ReportResponse(WorkMetadata? Work, AnalysisReport? Report);
public record WorkMetadata(Guid WorkId, Guid StudentId, Guid AssignmentId, DateTime SubmittedAt, string FileId, string? ReportId);
public record AnalysisReport(string? ReportId, string FileId, bool Plagiarism, List<PlagiarismEvidence> PlagiarismEvidence, string? WordCloudUrl, DateTime CreatedAt);
public record PlagiarismEvidence(Guid WorkId, Guid StudentId, DateTime SubmittedAt);