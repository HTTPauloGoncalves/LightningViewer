using LightningViewer.Web.Models.ViewModels;
using LightningViewer.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LightningViewer.Web.Controllers;

[Route("api/geo")]
[ApiController]
public class GeoController : ControllerBase
{
    private readonly IUnidadeService _service;

    public GeoController(IUnidadeService service) => _service = service;

    /// <summary>
    /// GET /api/geo/nearest?lat=-23.5&lon=-46.6
    /// Returns the service unit closest to the given coordinates (Haversine distance).
    /// Called automatically on page load after the browser's Geolocation API fires.
    /// </summary>
    [HttpGet("nearest")]
    public async Task<ActionResult<UnidadeDto>> GetNearest(
        [FromQuery] double lat,
        [FromQuery] double lon)
    {
        if (lat < -90 || lat > 90)  return BadRequest("Invalid latitude");
        if (lon < -180 || lon > 180) return BadRequest("Invalid longitude");

        var nearest = await _service.GetNearestAsync(lat, lon);
        if (nearest == null) return NotFound("No units found");

        return Ok(nearest);
    }
}
