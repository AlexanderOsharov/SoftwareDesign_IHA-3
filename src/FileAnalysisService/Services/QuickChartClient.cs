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
            .Where(w => w.Length > 2)
            .GroupBy(w => w.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(50)
            .Select(g => g.Key)
            .ToList();

        if (!words.Any())
            return null;

        var wordCloudText = string.Join(" ", words);
        var encodedText = Uri.EscapeDataString(wordCloudText);
        
        // Прямое формирование URL (более простой способ)
        return $"https://quickchart.io/wordcloud?text={encodedText}&width=800&height=600&backgroundColor=white";
    }
    catch (Exception ex)
    {
        Console.WriteLine($"QuickChart exception: {ex.Message}");
        return null;
    }
}
}