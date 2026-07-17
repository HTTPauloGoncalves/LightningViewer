using LightningViewer.Web.Models.Domain;
using LightningViewer.Web.Models.ViewModels;

namespace LightningViewer.Web.Services;

public interface IUnidadeService
{
    Task<List<UnidadeTomadora>> GetAllAsync();
    Task<UnidadeTomadora?> GetByIdAsync(int id);

    /// <summary>
    /// Returns the unit closest to the given geographic coordinates.
    /// Uses Haversine distance.
    /// </summary>
    Task<UnidadeDto?> GetNearestAsync(double lat, double lon);
}
