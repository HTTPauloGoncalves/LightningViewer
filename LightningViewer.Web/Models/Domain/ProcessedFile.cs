namespace LightningViewer.Web.Models.Domain;

/// <summary>
/// Tracks EUMETSAT product files that have already been ingested.
/// Prevents duplicate processing on worker restart or overlapping polls.
/// </summary>
public class ProcessedFile
{
    public int Id { get; set; }

    /// <summary>EUMETSAT product identifier (file name without extension).</summary>
    public string FileName { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
