using PureHDF;
using LightningViewer.Web.Models.Domain;

namespace LightningViewer.Web.Infrastructure;

/// <summary>
/// Parses MTG-LI Level 2 Accumulated Flashes (AF) NetCDF-4 files using PureHDF.
/// NetCDF-4 is stored as HDF5, so PureHDF reads them natively without external libraries.
///
/// Expected NetCDF-4 variables:
///   x              — 1-D array of E-W scan angles in radians [Nx]
///   y              — 1-D array of N-S scan angles in radians [Ny]
///   flash_accumulation  — 2-D grid of flash counts [Ny × Nx]
///   time           — scalar or 1-D time variable (seconds since epoch)
/// </summary>
public static class NetCdfParser
{
    // Geographic bounding box that pre-filters to all units + 200 km margin
    // Covers the full area of all 40 service units in Brazil
    private const double MinLat = -60.0;
    private const double MaxLat =  15.0;
    private const double MinLon = -90.0;
    private const double MaxLon = -30.0;

    // Names to try for the primary flash data variable (in priority order)
    private static readonly string[] FlashVarNames =
    {
        "flash_accumulation",
        "accumulated_flash_area",
        "flash_count",
        "flash_counts",
        "FlashAccumulation"
    };

    // Names to try for x/y coordinate variables
    private static readonly string[] XVarNames = { "x", "X", "col", "column" };
    private static readonly string[] YVarNames = { "y", "Y", "row", "line" };

    /// <summary>
    /// Parses an in-memory NetCDF-4 byte array and returns flash points within
    /// the Brazil bounding box, with a best-effort sensing time.
    /// </summary>
    /// <param name="data">Raw bytes of the .nc file.</param>
    /// <param name="productFileName">Used for traceability in LightningFlash records.</param>
    /// <param name="fallbackTime">Sensing time to use if not found in file attributes.</param>
    /// <returns>Enumerable of LightningFlash records (non-zero flash cells in bounding box).</returns>
    public static IEnumerable<LightningFlash> Parse(
        byte[]   data,
        string   productFileName,
        DateTime fallbackTime)
    {
        // Check if the data is a ZIP file (starts with PK.. magic bytes)
        if (data.Length > 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04)
        {
            using var ms = new MemoryStream(data);
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                // We only care about NetCDF data files (BODY or TRAIL, but usually we just want .nc)
                if (entry.Name.EndsWith(".nc", StringComparison.OrdinalIgnoreCase))
                {
                    using var entryStream = entry.Open();
                    using var msEntry = new MemoryStream();
                    entryStream.CopyTo(msEntry);
                    
                    foreach (var flash in ParseCore(msEntry.ToArray(), productFileName, fallbackTime))
                        yield return flash;
                }
            }
        }
        else
        {
            // It's likely a raw NetCDF file already
            foreach (var flash in ParseCore(data, productFileName, fallbackTime))
                yield return flash;
        }
    }

    private static IEnumerable<LightningFlash> ParseCore(
        byte[]   data,
        string   productFileName,
        DateTime fallbackTime)
    {
        using var stream = new MemoryStream(data);
        using var file   = H5File.Open(stream);

        var sensingTime = ReadSensingTime(file, fallbackTime);

        // Check if this is a point-based product (like LI Lightning Flashes - LFL)
        if (file.Children().Any(c => c.Name == "latitude") && file.Children().Any(c => c.Name == "longitude"))
        {
            float[]? lats = TryReadScaledArray(file, "latitude");
            float[]? lons = TryReadScaledArray(file, "longitude");

            if (lats != null && lons != null)
            {
                int len = Math.Min(lats.Length, lons.Length);
                for (int i = 0; i < len; i++)
                {
                    float lat = lats[i];
                    float lon = lons[i];

                    // Geographic bounding box check
                    if (lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon)
                    {
                        yield return new LightningFlash
                        {
                            FlashTime   = sensingTime,
                            Latitude    = lat,
                            Longitude   = lon,
                            FlashCount  = 1,
                            ProductFile = productFileName,
                            IngestedAt  = DateTime.UtcNow
                        };
                    }
                }
            }
            yield break;
        }

        // Fallback: 2D Grid Product (like LI Accumulated Flashes - AF)
        var group = FindDataGroup(file);

        float[]? xCoords = TryReadFloatArray(group, XVarNames);
        float[]? yCoords = TryReadFloatArray(group, YVarNames);

        if (xCoords == null || yCoords == null)
            yield break; // can't project without coordinates

        int Nx = xCoords.Length;
        int Ny = yCoords.Length;

        float[]? flashData = TryReadFlashData(group, FlashVarNames, Nx, Ny);
        if (flashData == null)
            yield break;

        var (scanMinX, scanMaxX, scanMinY, scanMaxY) =
            GeostationaryProjection.LatLonToScanBounds(MinLat, MaxLat, MinLon, MaxLon);

        for (int iy = 0; iy < Ny; iy++)
        {
            float sy = yCoords[iy];
            if (sy < scanMinY || sy > scanMaxY) continue;

            for (int ix = 0; ix < Nx; ix++)
            {
                float flashCount = flashData[iy * Nx + ix];
                if (flashCount <= 0) continue; 

                float sx = xCoords[ix];
                if (sx < scanMinX || sx > scanMaxX) continue;

                var geo = GeostationaryProjection.ScanAnglesToLatLon(sx, sy);
                if (geo == null) continue;

                var (lat, lon) = geo.Value;
                if (lat < MinLat || lat > MaxLat || lon < MinLon || lon > MaxLon)
                    continue;

                yield return new LightningFlash
                {
                    FlashTime   = sensingTime,
                    Latitude    = lat,
                    Longitude   = lon,
                    FlashCount  = flashCount,
                    ProductFile = productFileName,
                    IngestedAt  = DateTime.UtcNow
                };
            }
        }
    }

    // ─── Helper Methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Reads the sensing/start time from global attributes of the HDF5/NetCDF file.
    /// </summary>
    private static DateTime ReadSensingTime(IH5Group root, DateTime fallback)
    {
        foreach (var attrName in new[] { "time_coverage_start", "start_time", "sensing_start" })
        {
            try
            {
                if (root.AttributeExists(attrName))
                {
                    var val = root.Attribute(attrName).Read<string>();
                    if (DateTime.TryParse(val,
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var dt))
                    {
                        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                    }
                }
            }
            catch { /* try next */ }
        }
        return fallback;
    }

    /// <summary>
    /// Some MTG products store variables under a named sub-group. This returns
    /// the first group that contains recognisable flash variables.
    /// </summary>
    private static IH5Group FindDataGroup(IH5Group root)
    {
        // Try root first
        foreach (var name in FlashVarNames)
        {
            if (root.Children().Any(c => c.Name == name))
                return root;
        }

        // Try one level of sub-groups (e.g. /data/MTG-LI1-AF-01/...)
        foreach (var child in root.Children().OfType<IH5Group>())
        {
            foreach (var name in FlashVarNames)
            {
                if (child.Children().Any(c => c.Name == name))
                    return child;
            }
            // two levels deep
            foreach (var grandchild in child.Children().OfType<IH5Group>())
            {
                foreach (var name in FlashVarNames)
                {
                    if (grandchild.Children().Any(c => c.Name == name))
                        return grandchild;
                }
            }
        }

        return root; // fallback to root
    }

    private static float[]? TryReadFloatArray(IH5Group group, string[] names)
    {
        foreach (var name in names)
        {
            var arr = TryReadScaledArray(group, name);
            if (arr != null) return arr;
        }
        return null;
    }

    private static float[]? TryReadScaledArray(IH5Group group, string name)
    {
        try
        {
            if (!group.Children().Any(c => c.Name == name)) return null;

            var ds = group.Dataset(name);

            // Read fill value to skip invalid data
            float fillValue = float.MinValue;
            try
            {
                if (ds.AttributeExists("_FillValue"))
                    fillValue = ds.Attribute("_FillValue").Read<float[]>()[0];
            }
            catch { }

            float[] raw;
            try { raw = ds.Read<float[]>(); }
            catch
            {
                try
                {
                    raw = ds.Read<double[]>().Select(v => (float)v).ToArray();
                }
                catch
                {
                    try
                    {
                        var shorts = ds.Read<short[]>();
                        float scale = 1f, offset = 0f;
                        try { scale = ReadFloatAttribute(ds, "scale_factor", 1f); } catch { }
                        try { offset = ReadFloatAttribute(ds, "add_offset", 0f); } catch { }
                        
                        short fvShort = short.MinValue;
                        try { fvShort = ds.Attribute("_FillValue").Read<short[]>()[0]; } catch { }

                        raw = shorts.Select(s => s == fvShort ? 0f : s * scale + offset).ToArray();
                    }
                    catch
                    {
                        try
                        {
                            var ints = ds.Read<int[]>();
                        float scale = 1f, offset = 0f;
                        try { scale = ReadFloatAttribute(ds, "scale_factor", 1f); } catch { }
                        try { offset = ReadFloatAttribute(ds, "add_offset", 0f); } catch { }
                            
                            int fvInt = int.MinValue;
                            try { fvInt = ds.Attribute("_FillValue").Read<int[]>()[0]; } catch { }

                            raw = ints.Select(s => s == fvInt ? 0f : s * scale + offset).ToArray();
                        }
                        catch
                        {
                            return null;
                        }
                    }
                }
            }

            // Replace fill values with 0 (or some invalid flag)
            if (fillValue != float.MinValue)
                for (int i = 0; i < raw.Length; i++)
                    if (Math.Abs(raw[i] - fillValue) < 0.001f) raw[i] = 0f;

            return raw;
        }
        catch { return null; }
    }

    private static float[]? TryReadFlashData(IH5Group group, string[] names, int Nx, int Ny)
    {
        foreach (var name in names)
        {
            try
            {
                if (!group.Children().Any(c => c.Name == name)) continue;

                var ds = group.Dataset(name);

                // Read fill value to skip invalid data
                float fillValue = float.MinValue;
                try
                {
                    if (ds.AttributeExists("_FillValue"))
                        fillValue = ds.Attribute("_FillValue").Read<float[]>()[0];
                }
                catch { }

                float[] raw;
                // Try various element types
                try   { raw = ds.Read<float[]>(); }
                catch
                {
                    try
                    {
                        var shorts = ds.Read<short[]>();
                        float scale  = 1f, offset = 0f;
                        try { scale  = ReadFloatAttribute(ds, "scale_factor", 1f); } catch { }
                        try { offset = ReadFloatAttribute(ds, "add_offset", 0f);   } catch { }
                        short fvShort = short.MinValue;
                        try { fvShort = ds.Attribute("_FillValue").Read<short[]>()[0]; } catch { }

                        raw = shorts.Select(s =>
                            s == fvShort ? 0f : s * scale + offset).ToArray();
                    }
                    catch
                    {
                        continue; // can't read this variable, try next name
                    }
                }

                // Replace fill values with 0
                if (fillValue != float.MinValue)
                    for (int i = 0; i < raw.Length; i++)
                        if (Math.Abs(raw[i] - fillValue) < 0.001f) raw[i] = 0f;

                if (raw.Length == Nx * Ny) return raw;
            }
            catch { }
        }
        return null;
    }

    private static float ReadFloatAttribute(IH5Dataset ds, string attrName, float fallback)
    {
        if (!ds.AttributeExists(attrName)) return fallback;
        var attr = ds.Attribute(attrName);
        try { return attr.Read<float[]>()[0]; } catch { }
        try { return (float)attr.Read<double[]>()[0]; } catch { }
        try { return attr.Read<float>(); } catch { }
        try { return (float)attr.Read<double>(); } catch { }
        return fallback;
    }
}
