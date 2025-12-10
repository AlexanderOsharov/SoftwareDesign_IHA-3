using System.Text.Json;

namespace KpoHw3.FileAnalysisService.Clients;

public class MetadataClient
{
    private readonly HttpClient _httpClient;

    public MetadataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<WorkSubmissionDto>?> GetSubmissionsWithHashAsync(string textHash)
    {
        var response = await _httpClient.GetAsync($"/submissions/by-hash/{textHash}");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<WorkSubmissionDto>>(json);
    }

    public async Task<HttpResponseMessage?> UpdateReportIdAsync(Guid workId, string reportId)
    {
        try
        {
            var request = new { ReportId = reportId };
            return await _httpClient.PostAsJsonAsync($"/works/{workId}/reports/{reportId}", request);
        }
        catch
        {
            return null;
        }
    }
}

public class WorkSubmissionDto
{
    public Guid WorkId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string TextHash { get; set; } = string.Empty;
}