using LightningViewer.Web.Models.Domain;
using LightningViewer.Web.Models.ViewModels;
using LightningViewer.Web.Repositories;

namespace LightningViewer.Web.Services;

public class UnidadeService : IUnidadeService
{
    private readonly IUnidadeRepository _repo;

    public UnidadeService(IUnidadeRepository repo) => _repo = repo;

    public Task<List<UnidadeTomadora>> GetAllAsync() => _repo.GetAllAsync();

    public Task<UnidadeTomadora?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);

    public async Task<UnidadeDto?> GetNearestAsync(double lat, double lon)
    {
        var all = await _repo.GetAllAsync();
        if (!all.Any()) return null;

        var nearest = all
            .OrderBy(u => GeoCalculator.HaversineDistanceKm(lat, lon, u.Latitude, u.Longitude))
            .First();

        return new UnidadeDto
        {
            Id        = nearest.Id,
            Nome      = nearest.Nome,
            Municipio = nearest.Municipio,
            Latitude  = nearest.Latitude,
            Longitude = nearest.Longitude
        };
    }
}
