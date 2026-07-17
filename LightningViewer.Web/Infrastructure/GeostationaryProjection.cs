namespace LightningViewer.Web.Infrastructure;

/// <summary>
/// Implements the inverse geostationary projection used by the MTG/FCI instruments.
/// Converts scan angles (x, y) in radians to geographic latitude/longitude in degrees.
///
/// Based on: EUMETSAT MTG Level 1 & 2 Format Specification + LRIT/HRIT Global Specification.
/// Satellite: MTG-I1 at sub-satellite longitude 0.0°.
/// </summary>
public static class GeostationaryProjection
{
    // MTG-I / FCI geostationary orbit parameters (EUMETSAT values)
    private const double H    = 42164160.0;  // satellite distance from Earth center, metres
    private const double A_E  = 6378169.0;   // Earth equatorial semi-major axis, metres
    private const double B_E  = 6356583.8;   // Earth polar semi-minor axis, metres
    private const double SAT_LON_RAD = 0.0;  // sub-satellite longitude (MTG-I at 0°) in radians

    private static readonly double RatioSq = (A_E * A_E) / (B_E * B_E); // (a/b)²

    /// <summary>
    /// Converts geostationary scan angles to geographic coordinates.
    /// </summary>
    /// <param name="x">East-West scan angle in radians (positive = east of sub-satellite point)</param>
    /// <param name="y">North-South scan angle in radians (positive = north)</param>
    /// <returns>Tuple (latitude, longitude) in degrees, or null for space pixels.</returns>
    public static (double Lat, double Lon)? ScanAnglesToLatLon(double x, double y)
    {
        double cosX = Math.Cos(x);
        double cosY = Math.Cos(y);
        double sinX = Math.Sin(x);
        double sinY = Math.Sin(y);

        // Quadratic discriminant for the line-sphere intersection
        double a = sinX * sinX + cosX * cosX * (cosY * cosY + RatioSq * sinY * sinY);
        double b = -2.0 * H * cosX * cosY;
        double c = H * H - A_E * A_E;

        double discriminant = b * b - 4.0 * a * c;
        if (discriminant < 0)
            return null; // scan angle points into space

        double S_n = (-b - Math.Sqrt(discriminant)) / (2.0 * a);

        // Cartesian components of the Earth-surface point relative to satellite
        double S1 =  H - S_n * cosX * cosY;  // toward sub-satellite point
        double S2 =  S_n * sinX;              // eastward
        double S3 =  S_n * cosX * sinY;       // northward

        double lat = Math.Atan(RatioSq * S3 / Math.Sqrt(S1 * S1 + S2 * S2));
        double lon = Math.Atan2(S2, S1) + SAT_LON_RAD;

        return (lat * 180.0 / Math.PI, lon * 180.0 / Math.PI);
    }

    /// <summary>
    /// Pre-computes and returns the scan-angle bounding box that covers the
    /// given geographic bounding box. Useful for fast pre-filtering without
    /// reading the full grid.
    /// </summary>
    public static (double MinX, double MaxX, double MinY, double MaxY)
        LatLonToScanBounds(double minLat, double maxLat, double minLon, double maxLon)
    {
        // Convert the four corners + midpoints and take the envelope
        var points = new (double lat, double lon)[]
        {
            (minLat, minLon), (minLat, maxLon),
            (maxLat, minLon), (maxLat, maxLon),
            ((minLat + maxLat) / 2, minLon), ((minLat + maxLat) / 2, maxLon),
            (minLat, (minLon + maxLon) / 2), (maxLat, (minLon + maxLon) / 2),
        };

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        foreach (var (lat, lon) in points)
        {
            var (sx, sy) = LatLonToScanAngles(lat, lon);
            minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
            minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
        }

        // Add 5% margin
        double dX = (maxX - minX) * 0.05;
        double dY = (maxY - minY) * 0.05;
        return (minX - dX, maxX + dX, minY - dY, maxY + dY);
    }

    /// <summary>
    /// Forward projection: geographic lat/lon → geostationary scan angles.
    /// </summary>
    public static (double X, double Y) LatLonToScanAngles(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0;
        double lon = (lonDeg - SAT_LON_RAD * 180.0 / Math.PI) * Math.PI / 180.0;

        // Geocentric latitude
        double latGc = Math.Atan(B_E * B_E / (A_E * A_E) * Math.Tan(lat));

        double cosLatGc = Math.Cos(latGc);
        double cosLon   = Math.Cos(lon);
        double sinLon   = Math.Sin(lon);
        double sinLatGc = Math.Sin(latGc);

        double re = B_E / Math.Sqrt(1 - (1 - (B_E * B_E) / (A_E * A_E)) * cosLatGc * cosLatGc);

        double S1 = H - re * cosLatGc * cosLon;
        double S2 = -re * cosLatGc * sinLon;
        double S3 = re * sinLatGc;
        double S  = Math.Sqrt(S1 * S1 + S2 * S2 + S3 * S3);

        double x = Math.Atan2(-S2, S1);
        double y = Math.Asin(-S3 / S);

        return (x, y);
    }
}
