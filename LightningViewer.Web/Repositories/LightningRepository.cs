using LightningViewer.Web.Data;
using LightningViewer.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace LightningViewer.Web.Repositories;

public class LightningRepository : ILightningRepository
{
    private readonly AppDbContext _context;

    public LightningRepository(AppDbContext context) => _context = context;

    public Task<List<LightningFlash>> GetFlashesInBoundsAsync(
        double minLat, double maxLat, double minLon, double maxLon,
        DateTime since, DateTime until)
    {
        return _context.LightningFlashes
            .Where(f =>
                f.FlashTime >= since &&
                f.FlashTime <= until &&
                f.Latitude  >= minLat && f.Latitude  <= maxLat &&
                f.Longitude >= minLon && f.Longitude <= maxLon)
            .OrderBy(f => f.FlashTime)
            .ToListAsync();
    }

    public Task<DateTime?> GetLatestFlashTimeAsync()
    {
        return _context.LightningFlashes
            .MaxAsync(f => (DateTime?)f.FlashTime);
    }

    public async Task BulkInsertAsync(IEnumerable<LightningFlash> flashes)
    {
        // EF Core batch insert — batches up to 1000 rows per call for efficiency
        var batch = new List<LightningFlash>();
        foreach (var flash in flashes)
        {
            batch.Add(flash);
            if (batch.Count >= 1000)
            {
                _context.LightningFlashes.AddRange(batch);
                await _context.SaveChangesAsync();
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            _context.LightningFlashes.AddRange(batch);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff)
    {
        return await _context.LightningFlashes
            .Where(f => f.FlashTime < cutoff)
            .ExecuteDeleteAsync();
    }

    public Task<bool> IsFileProcessedAsync(string fileName)
        => _context.ProcessedFiles.AnyAsync(p => p.FileName == fileName);

    public async Task MarkFileProcessedAsync(string fileName)
    {
        if (!await IsFileProcessedAsync(fileName))
        {
            _context.ProcessedFiles.Add(new ProcessedFile { FileName = fileName });
            await _context.SaveChangesAsync();
        }
    }
}
