using LightningViewer.Web.Models.Domain;

namespace LightningViewer.Web.Repositories;

public interface IUnidadeRepository
{
    Task<List<UnidadeTomadora>> GetAllAsync();
    Task<UnidadeTomadora?> GetByIdAsync(int id);
}
