namespace KpoHw3.FileAnalysisService.Models;

public class AnalysisReport
{
    public string ReportId { get; set; } = Guid.NewGuid().ToString();
    public string FileId { get; set; } = string.Empty;
    public bool Plagiarism { get; set; }
    public List<PlagiarismEvidence> PlagiarismEvidence { get; set; } = new();
    public string? WordCloudUrl { get; set; }
}

public class PlagiarismEvidence
{
    public Guid WorkId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime SubmittedAt { get; set; }
}