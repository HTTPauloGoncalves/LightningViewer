using LightningViewer.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace LightningViewer.Web.Data;

public static class DataSeeder
{
    private const string CsvPath = @"D:\PLANB\Tomadores_de_servico_latlon.csv";

    public static async Task SeedUnidadesAsync(AppDbContext context)
    {
        if (await context.UnidadesTomadoras.AnyAsync())
            return; // Already seeded

        if (!File.Exists(CsvPath))
            throw new FileNotFoundException($"CSV das unidades não encontrado: {CsvPath}");

        var lines = await File.ReadAllLinesAsync(CsvPath, System.Text.Encoding.UTF8);
        var unidades = new List<UnidadeTomadora>();

        foreach (var line in lines.Skip(1)) // skip header row
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(';');
            if (parts.Length < 5) continue;

            // First column is the sequential number
            if (!int.TryParse(parts[0].Trim(), out int numero)) continue;

            // CSV uses comma as decimal separator (Brazilian format)
            var latStr = parts[3].Trim().Replace(',', '.');
            var lonStr = parts[4].Trim().Replace(',', '.');

            if (!double.TryParse(latStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double lat)) continue;

            if (!double.TryParse(lonStr,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double lon)) continue;

            unidades.Add(new UnidadeTomadora
            {
                Numero  = numero,
                Municipio = parts[1].Trim(),
                Nome    = parts[2].Trim(),
                Latitude  = lat,
                Longitude = lon,
                Cnpj    = parts.Length > 5 ? parts[5].Trim() : null,
                Endereco  = parts.Length > 6 ? parts[6].Trim() : null
            });
        }

        context.UnidadesTomadoras.AddRange(unidades);
        await context.SaveChangesAsync();
    }
}
