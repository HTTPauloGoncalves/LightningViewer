using LightningViewer.Web.Models.ViewModels;
using LightningViewer.Web.Repositories;
using Microsoft.Extensions.Caching.Memory;

namespace LightningViewer.Web.Services;

public class LightningService : ILightningService
{
    private const int RetentionHours = 3;
    private const int FrameMinutes   = 5; // player frame bucket size

    private readonly ILightningRepository _repo;
    private readonly IUnidadeRepository   _unidadeRepo;
    private readonly IMemoryCache          _cache;

    public LightningService(
        ILightningRepository repo,
        IUnidadeRepository   unidadeRepo,
        IMemoryCache         cache)
    {
        _repo        = repo;
        _unidadeRepo = unidadeRepo;
        _cache       = cache;
    }

    public async Task<List<FrameMetaDto>> GetFrameMetasAsync(int unidadeId, double radiusKm)
    {
        var cacheKey = $"framemeta:{unidadeId}:{radiusKm}";
        if (_cache.TryGetValue(cacheKey, out List<FrameMetaDto>? cached) && cached != null)
            return cached;

        double minLat, maxLat, minLon, maxLon;
        double? centerLat = null, centerLon = null;

        if (unidadeId == 0)
        {
            // South America bounding box
            minLat = -60.0; maxLat = 15.0;
            minLon = -90.0; maxLon = -30.0;
        }
        else
        {
            var unidade = await _unidadeRepo.GetByIdAsync(unidadeId)
                ?? throw new ArgumentException($"Unidade {unidadeId} not found");

            (minLat, maxLat, minLon, maxLon) = GeoCalculator.BoundingBox(unidade.Latitude, unidade.Longitude, radiusKm);
            centerLat = unidade.Latitude;
            centerLon = unidade.Longitude;
        }

        var latestTime = await _repo.GetLatestFlashTimeAsync() ?? DateTime.UtcNow;
        var since = latestTime.AddHours(-RetentionHours);
        var until = latestTime;

        var flashes = await _repo.GetFlashesInBoundsAsync(
            minLat, maxLat, minLon, maxLon, since, until);

        // Apply exact Haversine filter if we are tracking a specific unit
        var filtered = centerLat.HasValue && centerLon.HasValue
            ? flashes.Where(f => GeoCalculator.HaversineDistanceKm(centerLat.Value, centerLon.Value, f.Latitude, f.Longitude) <= radiusKm)
            : flashes;

        // Bucket into FrameMinutes-wide slots and compute metadata
        var metas = filtered
            .GroupBy(f => BucketTime(f.FlashTime))
            .OrderBy(g => g.Key)
            .Select(g => new FrameMetaDto
            {
                FrameTime    = g.Key.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                TotalFlashes = (int)g.Sum(f => f.FlashCount)
            })
            .ToList();

        _cache.Set(cacheKey, metas, TimeSpan.FromMinutes(1));
        return metas;
    }

    public async Task<FlashFrameDto> GetFrameAsync(int unidadeId, double radiusKm, DateTime frameTime)
    {
        var cacheKey = $"frame:{unidadeId}:{radiusKm}:{frameTime:yyyyMMddHHmm}";
        if (_cache.TryGetValue(cacheKey, out FlashFrameDto? cached) && cached != null)
            return cached;

        double minLat, maxLat, minLon, maxLon;
        double? centerLat = null, centerLon = null;

        if (unidadeId == 0)
        {
            // Global bounding box for full EUMETSAT data visibility
            // South America bounding box
            minLat = -60.0; maxLat = 15.0;
            minLon = -90.0; maxLon = -30.0;
        }
        else
        {
            var unidade = await _unidadeRepo.GetByIdAsync(unidadeId)
                ?? throw new ArgumentException($"Unidade {unidadeId} not found");

            (minLat, maxLat, minLon, maxLon) = GeoCalculator.BoundingBox(unidade.Latitude, unidade.Longitude, radiusKm);
            centerLat = unidade.Latitude;
            centerLon = unidade.Longitude;
        }

        // Window for this frame bucket
        var bucketStart = BucketTime(frameTime);
        var bucketEnd   = bucketStart.AddMinutes(FrameMinutes);

        var flashes = await _repo.GetFlashesInBoundsAsync(
            minLat, maxLat, minLon, maxLon, bucketStart, bucketEnd);

        var filtered = centerLat.HasValue && centerLon.HasValue
            ? flashes.Where(f => GeoCalculator.HaversineDistanceKm(centerLat.Value, centerLon.Value, f.Latitude, f.Longitude) <= radiusKm).ToList()
            : flashes.ToList();

        var latestTime = await _repo.GetLatestFlashTimeAsync() ?? DateTime.UtcNow;
        var windowStart = latestTime.AddHours(-RetentionHours);
        var windowDuration = (latestTime - windowStart).TotalSeconds;

        var dto = new FlashFrameDto
        {
            FrameTime    = bucketStart.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            TotalFlashes = (int)filtered.Sum(f => f.FlashCount),
            Points       = filtered.Select(f => new FlashPointDto
            {
                Lat   = f.Latitude,
                Lon   = f.Longitude,
                Count = (int)f.FlashCount,
                Intensity = Math.Clamp((f.FlashTime - windowStart).TotalSeconds / windowDuration, 0.0, 1.0)
            }).ToList()
        };

        _cache.Set(cacheKey, dto, TimeSpan.FromMinutes(4)); // cache until next poll
        return dto;
    }

    public async Task<FlashFrameDto> GetLatestFrameAsync(int unidadeId, double radiusKm)
    {
        var latestTime = await _repo.GetLatestFlashTimeAsync() ?? DateTime.UtcNow;
        var latestBucket = BucketTime(latestTime);
        return await GetFrameAsync(unidadeId, radiusKm, latestBucket);
    }

    public async Task<FlashFrameDto> GetCompositeFrameAsync(int unidadeId, double radiusKm)
    {
        var cacheKey = $"composite:{unidadeId}:{radiusKm}";
        if (_cache.TryGetValue(cacheKey, out FlashFrameDto? cached) && cached != null)
            return cached;

        double minLat, maxLat, minLon, maxLon;
        double? centerLat = null, centerLon = null;

        if (unidadeId == 0)
        {
            // Global bounding box for full EUMETSAT data visibility
            // South America bounding box
            minLat = -60.0; maxLat = 15.0;
            minLon = -90.0; maxLon = -30.0;
        }
        else
        {
            var unidade = await _unidadeRepo.GetByIdAsync(unidadeId)
                ?? throw new ArgumentException($"Unidade {unidadeId} not found");

            (minLat, maxLat, minLon, maxLon) = GeoCalculator.BoundingBox(unidade.Latitude, unidade.Longitude, radiusKm);
            centerLat = unidade.Latitude;
            centerLon = unidade.Longitude;
        }

        var latestTime = await _repo.GetLatestFlashTimeAsync() ?? DateTime.UtcNow;
        var windowStart = latestTime.AddHours(-RetentionHours);
        var windowDuration = (latestTime - windowStart).TotalSeconds;

        var flashes = await _repo.GetFlashesInBoundsAsync(
            minLat, maxLat, minLon, maxLon, windowStart, latestTime);

        var filtered = centerLat.HasValue && centerLon.HasValue
            ? flashes.Where(f => GeoCalculator.HaversineDistanceKm(centerLat.Value, centerLon.Value, f.Latitude, f.Longitude) <= radiusKm).ToList()
            : flashes.ToList();

        var dto = new FlashFrameDto
        {
            FrameTime    = latestTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            TotalFlashes = (int)filtered.Sum(f => f.FlashCount),
            Points       = filtered.Select(f => new FlashPointDto
            {
                Lat   = f.Latitude,
                Lon   = f.Longitude,
                Count = (int)f.FlashCount,
                Intensity = Math.Clamp((f.FlashTime - windowStart).TotalSeconds / windowDuration, 0.0, 1.0)
            }).ToList()
        };

        _cache.Set(cacheKey, dto, TimeSpan.FromMinutes(1));
        return dto;
    }

    private static DateTime BucketTime(DateTime t)
    {
        var mins = (t.Minute / FrameMinutes) * FrameMinutes;
        return new DateTime(t.Year, t.Month, t.Day, t.Hour, mins, 0, DateTimeKind.Utc);
    }
}
