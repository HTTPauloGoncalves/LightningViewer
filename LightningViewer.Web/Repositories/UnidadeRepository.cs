using LightningViewer.Web.Data;
using LightningViewer.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace LightningViewer.Web.Repositories;

public class UnidadeRepository : IUnidadeRepository
{
    private readonly AppDbContext _context;

    public UnidadeRepository(AppDbContext context) => _context = context;

    public Task<List<UnidadeTomadora>> GetAllAsync()
        => _context.UnidadesTomadoras.OrderBy(u => u.Numero).ToListAsync();

    public Task<UnidadeTomadora?> GetByIdAsync(int id)
        => _context.UnidadesTomadoras.FirstOrDefaultAsync(u => u.Id == id);
}
