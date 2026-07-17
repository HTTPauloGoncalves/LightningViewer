namespace LightningViewer.Web.Services;

/// <summary>
/// Provides geographic calculation utilities (Haversine distance, bounding boxes).
/// </summary>
public static class GeoCalculator
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Calculates the great-circle distance between two geographic points using the Haversine formula.
    /// </summary>
    /// <returns>Distance in kilometers.</returns>
    public static double HaversineDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    /// <summary>
    /// Returns an approximate bounding box (min/max lat/lon) for a circle of the given radius.
    /// Used for SQL pre-filtering before exact Haversine check.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon)
        BoundingBox(double lat, double lon, double radiusKm)
    {
        double latDelta = radiusKm / EarthRadiusKm * (180.0 / Math.PI);
        double lonDelta = radiusKm / (EarthRadiusKm * Math.Cos(ToRadians(lat))) * (180.0 / Math.PI);
        return (lat - latDelta, lat + latDelta, lon - lonDelta, lon + lonDelta);
    }

    public static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
