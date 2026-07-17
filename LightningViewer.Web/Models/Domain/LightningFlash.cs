namespace LightningViewer.Web.Models.Domain;

/// <summary>
/// Represents an accumulated flash observation from the MTG-LI AF (Accumulated Flashes) product.
/// Each record corresponds to a non-zero grid cell from the 30-second AF NetCDF file.
/// </summary>
public class LightningFlash
{
    public long Id { get; set; }

    /// <summary>UTC timestamp of the AF product sensing period start.</summary>
    public DateTime FlashTime { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Number of accumulated flashes in this grid cell during the 30-second window.</summary>
    public float FlashCount { get; set; }

    /// <summary>Source product file name (for traceability).</summary>
    public string? ProductFile { get; set; }

    public DateTime IngestedAt { get; set; } = DateTime.UtcNow;
}
