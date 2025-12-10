namespace KpoHw3.FileAnalysisService.Clients;

public class FileStoringClient
{
    private readonly HttpClient _httpClient;

    public FileStoringClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]?> GetFileBytesAsync(string fileId)
    {
        var response = await _httpClient.GetAsync($"/files/{fileId}");
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
    }
}