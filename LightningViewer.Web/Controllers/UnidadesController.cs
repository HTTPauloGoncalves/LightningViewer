using LightningViewer.Web.Models.ViewModels;
using LightningViewer.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LightningViewer.Web.Controllers;

[Route("api/unidades")]
[ApiController]
public class UnidadesController : ControllerBase
{
    private readonly IUnidadeService _service;

    public UnidadesController(IUnidadeService service) => _service = service;

    /// <summary>GET /api/unidades — returns all service units.</summary>
    [HttpGet]
    public async Task<ActionResult<List<UnidadeDto>>> GetAll()
    {
        var unidades = await _service.GetAllAsync();
        return unidades.Select(u => new UnidadeDto
        {
            Id        = u.Id,
            Nome      = u.Nome,
            Municipio = u.Municipio,
            Latitude  = u.Latitude,
            Longitude = u.Longitude
        }).ToList();
    }

    /// <summary>GET /api/unidades/{id} — returns a single unit.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<UnidadeDto>> GetById(int id)
    {
        var u = await _service.GetByIdAsync(id);
        if (u == null) return NotFound();

        return new UnidadeDto
        {
            Id        = u.Id,
            Nome      = u.Nome,
            Municipio = u.Municipio,
            Latitude  = u.Latitude,
            Longitude = u.Longitude
        };
    }
}
