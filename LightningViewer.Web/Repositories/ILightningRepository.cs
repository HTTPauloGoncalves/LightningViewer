using LightningViewer.Web.Models.Domain;

namespace LightningViewer.Web.Repositories;

public interface ILightningRepository
{
    /// <summary>
    /// Returns all flashes within a bounding box and time window (last 3 hours).
    /// </summary>
    Task<List<LightningFlash>> GetFlashesInBoundsAsync(
        double minLat, double maxLat, double minLon, double maxLon,
        DateTime since, DateTime until);

    Task<DateTime?> GetLatestFlashTimeAsync();

    /// <summary>
    /// Bulk inserts a batch of flash records.
    /// </summary>
    Task BulkInsertAsync(IEnumerable<LightningFlash> flashes);

    /// <summary>
    /// Deletes all flash records older than the given cutoff time.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoff);

    /// <summary>
    /// Checks whether a product file has already been processed.
    /// </summary>
    Task<bool> IsFileProcessedAsync(string fileName);

    /// <summary>
    /// Marks a product file as processed.
    /// </summary>
    Task MarkFileProcessedAsync(string fileName);
}
