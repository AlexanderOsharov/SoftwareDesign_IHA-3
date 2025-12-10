# PowerShell скрипт для тестирования API
Write-Host "=== Тестирование Anti-Plagiarism System ===" -ForegroundColor Cyan

$baseUrl = "http://localhost:5263"
$testFile = "test_document_$(Get-Date -Format 'HHmmss').txt"

# Создаем тестовый файл
@'
Анализ алгоритмов сортировки в компьютерных науках.
Быстрая сортировка (QuickSort) является одним из наиболее эффективных алгоритмов.
Сортировка слиянием (MergeSort) обеспечивает стабильную производительность.
'@ | Out-File -FilePath $testFile -Encoding UTF8

Write-Host "Создан тестовый файл: $testFile" -ForegroundColor Green

# Функция для вывода цветного статуса
function Write-Status {
    param(
        [string]$Message,
        [bool]$Success = $true
    )
    
    if ($Success) {
        Write-Host "✓ $Message" -ForegroundColor Green
    } else {
        Write-Host "✗ $Message" -ForegroundColor Red
    }
}

# Тест 1: Проверка доступности сервисов
Write-Host "`n[1/5] Проверка доступности сервисов..." -ForegroundColor Yellow

$services = @(
    @{Name="ApiGateway"; Url="$baseUrl/"},
    @{Name="MetadataService"; Url="http://localhost:5068/health"},
    @{Name="FileStoringService"; Url="http://localhost:5015/health"},
    @{Name="FileAnalysisService"; Url="http://localhost:5198/health"}
)

foreach ($service in $services) {
    try {
        $response = Invoke-WebRequest -Uri $service.Url -Method Get -TimeoutSec 3
        Write-Status -Message "$($service.Name): Доступен ($($response.StatusCode))"
    } catch {
        Write-Status -Message "$($service.Name): Недоступен" -Success $false
    }
}

# Тест 2: Отправка работы
Write-Host "`n[2/5] Отправка работы на проверку..." -ForegroundColor Yellow

try {
    $boundary = [System.Guid]::NewGuid().ToString()
    $LF = "`r`n"
    
    $body = @"
--$boundary
Content-Disposition: form-data; name="file"; filename="$testFile"
Content-Type: text/plain

$((Get-Content $testFile -Raw))
--$boundary
Content-Disposition: form-data; name="studentId"

11111111-1111-1111-1111-111111111111
--$boundary
Content-Disposition: form-data; name="assignmentId"

22222222-2222-2222-2222-222222222222
--$boundary--
"@

    $response = Invoke-RestMethod -Uri "$baseUrl/api/submit-work" `
        -Method Post `
        -ContentType "multipart/form-data; boundary=$boundary" `
        -Body $body
    
    $workId = $response.WorkId
    Write-Status -Message "Работа отправлена. WorkId: $workId"
    
} catch {
    Write-Status -Message "Ошибка при отправке работы: $($_.Exception.Message)" -Success $false
    exit
}

# Тест 3: Проверка метаданных
Write-Host "`n[3/5] Проверка метаданных работы..." -ForegroundColor Yellow

try {
    Start-Sleep -Seconds 2
    $metadata = Invoke-RestMethod -Uri "http://localhost:5068/works/$workId" -Method Get
    Write-Status -Message "Метаданные получены"
    Write-Host "   StudentId: $($metadata.StudentId)" -ForegroundColor Gray
    Write-Host "   FileId: $($metadata.FileId)" -ForegroundColor Gray
} catch {
    Write-Status -Message "Метаданные не найдены" -Success $false
}

# Тест 4: Получение отчета
Write-Host "`n[4/5] Получение отчета анализа..." -ForegroundColor Yellow

try {
    Write-Host "Ждем завершения анализа (5 секунд)..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    
    $report = Invoke-RestMethod -Uri "$baseUrl/api/works/$workId/reports" -Method Get
    
    if ($report.Report) {
        Write-Status -Message "Отчет получен"
        
        if ($report.Report.Plagiarism) {
            Write-Host "   🚨 Обнаружен ПЛАГИАТ!" -ForegroundColor Red
        } else {
            Write-Host "   ✅ Плагиат не обнаружен" -ForegroundColor Green
        }
        
        if ($report.Report.WordCloudUrl) {
            Write-Host "   🌥 Облако слов: $($report.Report.WordCloudUrl)" -ForegroundColor Cyan
        }
    } else {
        Write-Status -Message "Отчет еще не готов" -Success $false
    }
    
} catch {
    Write-Status -Message "Ошибка при получении отчета: $($_.Exception.Message)" -Success $false
}

# Тест 5: Прямое тестирование микросервисов
Write-Host "`n[5/5] Прямое тестирование микросервисов..." -ForegroundColor Yellow

# Тестируем FileStoringService напрямую
try {
    $directUpload = Invoke-RestMethod -Uri "http://localhost:5015/files" `
        -Method Post `
        -Form @{file = Get-Item $testFile} `
        -ContentType "multipart/form-data"
    
    Write-Status -Message "Прямая загрузка файла: $($directUpload.FileId)"
} catch {
    Write-Status -Message "Ошибка прямой загрузки" -Success $false
}

# Убираем тестовый файл
Remove-Item $testFile -ErrorAction SilentlyContinue

Write-Host "`n=== Тестирование завершено ===" -ForegroundColor Cyan