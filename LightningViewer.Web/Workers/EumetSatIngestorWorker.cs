using LightningViewer.Web.Data;
using LightningViewer.Web.Infrastructure;
using LightningViewer.Web.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LightningViewer.Web.Workers;

/// <summary>
/// Background worker that polls the EUMETSAT Data Store every 5 minutes for new
/// MTG-LI AF (Accumulated Flashes) products, parses the NetCDF-4 files, and stores
/// the lightning data in PostgreSQL.
///
/// Data retention: Automatically deletes records older than 3 hours on each cycle.
/// </summary>
public class EumetSatIngestorWorker : BackgroundService
{
    private readonly IServiceScopeFactory  _scopeFactory;
    private readonly EumetSatApiClient     _apiClient;
    private readonly ILogger<EumetSatIngestorWorker> _logger;
    private readonly IConfiguration        _config;

    // Poll interval: 5 minutes (matches the minimum frame resolution)
    private static readonly TimeSpan PollInterval     = TimeSpan.FromMinutes(5);

    // Small overlap to avoid missing files at period boundaries
    private static readonly TimeSpan SearchOverlap    = TimeSpan.FromMinutes(6);

    public EumetSatIngestorWorker(
        IServiceScopeFactory  scopeFactory,
        EumetSatApiClient     apiClient,
        ILogger<EumetSatIngestorWorker> logger,
        IConfiguration        config)
    {
        _scopeFactory = scopeFactory;
        _apiClient    = apiClient;
        _logger       = logger;
        _config       = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("EUMETSAT Ingestor Worker started");

        // On first run, backfill the full retention window
        await RunIngestCycleAsync(backfill: true, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(PollInterval, stoppingToken);

            try
            {
                await RunIngestCycleAsync(backfill: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during EUMETSAT ingestion cycle");
                // Continue running — transient failures should not stop the worker
            }
        }

        _logger.LogInformation("EUMETSAT Ingestor Worker stopped");
    }

    private async Task RunIngestCycleAsync(bool backfill, CancellationToken ct)
    {
        var retentionHours = _config.GetValue<int>("EumetSat:RetentionHours", 24);
        var retentionWindow = TimeSpan.FromHours(retentionHours);

        var now       = DateTime.UtcNow;
        var searchEnd = now;
        // On backfill, fetch last N hours; on normal poll, fetch last 6 minutes with overlap
        var searchStart = backfill
            ? now - retentionWindow
            : now - SearchOverlap;

        if (backfill)
        {
            _logger.LogInformation("=========================================================");
            _logger.LogInformation("Iniciando varredura do passado (Backfill) das últimas {H} horas", retentionHours);
            _logger.LogInformation("Buscando arquivos da EUMETSAT de {start} até {end}...", searchStart.ToLocalTime(), searchEnd.ToLocalTime());
            _logger.LogInformation("=========================================================");
        }
        else
        {
            _logger.LogInformation("Procurando novos arquivos EUMETSAT (últimos {min} minutos)...", SearchOverlap.TotalMinutes);
        }

        List<EumetSatProduct> products;
        try
        {
            products = await _apiClient.SearchProductsAsync(searchStart, searchEnd, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search EUMETSAT products");
            return;
        }

        _logger.LogInformation("Found {count} products in search window", products.Count);

        // Sort descending so we process the most recent files first (immediate UI gratification)
        products = products.OrderByDescending(p => p.SensingTime).ToList();

        int processed = 0, skipped = 0;

        foreach (var product in products)
        {
            if (ct.IsCancellationRequested) break;

            using var scope  = _scopeFactory.CreateScope();
            var repo         = scope.ServiceProvider.GetRequiredService<ILightningRepository>();

            // Skip files we have already ingested (idempotency)
            if (await repo.IsFileProcessedAsync(product.Id))
            {
                skipped++;
                continue;
            }

            try
            {
                _logger.LogInformation("⬇️ BAIXANDO ARQUIVO DO PASSADO: {id}", product.Id);
                var data = await _apiClient.DownloadProductAsync(product.DownloadUrl, ct);
                
                await System.IO.File.WriteAllBytesAsync(@"D:\PLANB\LightningViewer\debug.nc", data);

                _logger.LogDebug("Parsing NetCDF-4 file ({kb} KB)", data.Length / 1024);
                var flashes = NetCdfParser.Parse(data, product.Id, product.SensingTime).ToList();

                _logger.LogInformation("Product {id}: {count} flash points in Brazil region",
                    product.Id, flashes.Count);

                if (flashes.Count > 0)
                    await repo.BulkInsertAsync(flashes);

                await repo.MarkFileProcessedAsync(product.Id);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process product {id}", product.Id);
            }
        }

        // Cleanup: remove data outside the retention window
        using var cleanupScope = _scopeFactory.CreateScope();
        var cleanupRepo = cleanupScope.ServiceProvider.GetRequiredService<ILightningRepository>();
        var deleted = await cleanupRepo.DeleteOlderThanAsync(now - retentionWindow);

        if (deleted > 0)
            _logger.LogInformation("Retention cleanup: removed {count} old flash records", deleted);

        _logger.LogInformation(
            "Ingestion cycle complete — processed: {p}, skipped (already done): {s}",
            processed, skipped);
    }
}
