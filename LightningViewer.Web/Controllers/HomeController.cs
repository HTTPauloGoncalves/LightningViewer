using LightningViewer.Web.Models.ViewModels;
using LightningViewer.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LightningViewer.Web.Controllers;

public class HomeController : Controller
{
    private readonly IUnidadeService _unidadeService;

    public HomeController(IUnidadeService unidadeService)
    {
        _unidadeService = unidadeService;
    }

    public async Task<IActionResult> Index()
    {
        var unidades = await _unidadeService.GetAllAsync();

        var vm = new MapViewModel
        {
            Unidades = unidades.Select(u => new UnidadeDto
            {
                Id        = u.Id,
                Nome      = u.Nome,
                Municipio = u.Municipio,
                Latitude  = u.Latitude,
                Longitude = u.Longitude
            }).ToList()
        };

        return View(vm);
    }
}
