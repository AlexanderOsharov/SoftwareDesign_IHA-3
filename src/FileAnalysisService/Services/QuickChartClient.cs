using System.Text;
using System.Text.Json;

namespace KpoHw3.FileAnalysisService.Services;

public class QuickChartClient
{
    private readonly HttpClient _httpClient;

    public QuickChartClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> GenerateWordCloudUrlAsync(string text)
    {
        try
        {
            var words = text.Split(new char[] { ' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?' }, 
                StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2) // Игнорируем короткие слова
                .GroupBy(w => w.ToLowerInvariant())
                .OrderByDescending(g => g.Count())
                .Take(50) // Максимум 50 слов
                .ToDictionary(g => g.Key, g => g.Count());

            if (!words.Any())
                return null;

            var payload = new
            {
                format = "png",
                width = 800,
                height = 600,
                backgroundColor = "white",
                text = string.Join(" ", words.Keys)
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://quickchart.io/wordcloud", content);
        
            Console.WriteLine($"QuickChart response: {response.StatusCode}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"QuickChart error: {error}");
                return null;
            }

            var url = await response.Content.ReadAsStringAsync();
            return url.Trim('\"');
        }
        catch (Exception ex)
        {
            Console.WriteLine($"QuickChart exception: {ex.Message}");
            return null;
        }
    }
}