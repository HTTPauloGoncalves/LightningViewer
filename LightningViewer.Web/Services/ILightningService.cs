using LightningViewer.Web.Models.ViewModels;

namespace LightningViewer.Web.Services;

public interface ILightningService
{
    /// <summary>
    /// Returns metadata (time + flash count) for all 5-minute frames in the last 3 hours,
    /// filtered to within <paramref name="radiusKm"/> of the given unit's position.
    /// </summary>
    Task<List<FrameMetaDto>> GetFrameMetasAsync(int unidadeId, double radiusKm);

    /// <summary>
    /// Returns the flash points for a specific 5-minute frame bucket.
    /// </summary>
    Task<FlashFrameDto> GetFrameAsync(int unidadeId, double radiusKm, DateTime frameTime);

    /// <summary>
    /// Returns the most recent frame (latest 5-minute bucket).
    /// </summary>
    Task<FlashFrameDto> GetLatestFrameAsync(int unidadeId, double radiusKm);

    /// <summary>
    /// Returns all flashes in the retention window (3 hours) as a single composite frame.
    /// </summary>
    Task<FlashFrameDto> GetCompositeFrameAsync(int unidadeId, double radiusKm);
}
