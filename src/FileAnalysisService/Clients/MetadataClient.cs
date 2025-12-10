using System.Text.Json;
using System.Text.Json.Serialization;

namespace KpoHw3.FileAnalysisService.Clients;

public class MetadataClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public MetadataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<List<WorkSubmissionDto>?> GetSubmissionsWithHashAsync(string textHash)
    {
        var response = await _httpClient.GetAsync($"/submissions/by-hash/{textHash}");
        if (!response.IsSuccessStatusCode) 
        {
            Console.WriteLine($"[ERROR] Metadata response failed: {response.StatusCode}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[DEBUG] Metadata response JSON: {json}");
        
        try
        {
            var result = JsonSerializer.Deserialize<List<WorkSubmissionDto>>(json, _jsonOptions);
            Console.WriteLine($"[DEBUG] Deserialized {result?.Count ?? 0} submissions");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to deserialize: {ex.Message}");
            return null;
        }
    }

    public async Task<HttpResponseMessage?> UpdateReportIdAsync(Guid workId, string reportId)
    {
        try
        {
            var request = new { ReportId = reportId };
            return await _httpClient.PostAsJsonAsync($"/works/{workId}/reports", request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MetadataClient.UpdateReportIdAsync error: {ex.Message}");
            return null;
        }
    }
    
    public async Task<HttpResponseMessage?> UpdateTextHashAsync(Guid workId, string textHash)
    {
        try
        {
            var request = new { TextHash = textHash };
            return await _httpClient.PostAsJsonAsync($"/works/{workId}/text-hash", request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"MetadataClient.UpdateTextHashAsync error: {ex.Message}");
            return null;
        }
    }
}

public class WorkSubmissionDto
{
    [JsonPropertyName("workId")]
    public Guid WorkId { get; set; }
    
    [JsonPropertyName("studentId")]
    public Guid StudentId { get; set; }
    
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
    
    [JsonPropertyName("textHash")]
    public string TextHash { get; set; } = string.Empty;
}