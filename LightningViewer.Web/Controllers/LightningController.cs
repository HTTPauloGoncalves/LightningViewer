using LightningViewer.Web.Models.ViewModels;
using LightningViewer.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace LightningViewer.Web.Controllers;

[Route("api/lightning")]
[ApiController]
public class LightningController : ControllerBase
{
    private readonly ILightningService _service;

    public LightningController(ILightningService service) => _service = service;

    /// <summary>
    /// GET /api/lightning/frames?unidadeId=1&raio=200
    /// Returns all available 5-minute frame metadata for the last 3 hours.
    /// </summary>
    [HttpGet("frames")]
    public async Task<ActionResult<List<FrameMetaDto>>> GetFrames(
        [FromQuery] int    unidadeId,
        [FromQuery] double raio = 200)
    {
        if (unidadeId < 0) return BadRequest("unidadeId is required");
        raio = ValidateRadius(raio);

        try
        {
            var metas = await _service.GetFrameMetasAsync(unidadeId, raio);
            return Ok(metas);
        }
        catch (ArgumentException ex) { return NotFound(ex.Message); }
    }

    /// <summary>
    /// GET /api/lightning/frame?unidadeId=1&raio=200&ts=2024-01-01T12:00:00Z
    /// Returns all flash points within a specific 5-minute frame bucket.
    /// </summary>
    [HttpGet("frame")]
    public async Task<ActionResult<FlashFrameDto>> GetFrame(
        [FromQuery] int    unidadeId,
        [FromQuery] double raio = 200,
        [FromQuery] string? ts  = null)
    {
        if (unidadeId < 0) return BadRequest("unidadeId is required");
        raio = ValidateRadius(raio);

        DateTime frameTime;
        if (string.IsNullOrEmpty(ts) || !DateTime.TryParse(ts, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out frameTime))
        {
            frameTime = DateTime.UtcNow;
        }

        try
        {
            var frame = await _service.GetFrameAsync(unidadeId, raio, frameTime);
            return Ok(frame);
        }
        catch (ArgumentException ex) { return NotFound(ex.Message); }
    }

    /// <summary>
    /// GET /api/lightning/latest?unidadeId=1&raio=200
    /// Returns the most recent frame (latest 5-minute bucket).
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<FlashFrameDto>> GetLatest(
        [FromQuery] int    unidadeId,
        [FromQuery] double raio = 200)
    {
        if (unidadeId < 0) return BadRequest("unidadeId is required");
        raio = ValidateRadius(raio);

        try
        {
            var frame = await _service.GetLatestFrameAsync(unidadeId, raio);
            return Ok(frame);
        }
        catch (ArgumentException ex) { return NotFound(ex.Message); }
    }

    /// <summary>
    /// GET /api/lightning/composite?unidadeId=1&raio=200
    /// Returns a single composite frame for the last 3 hours.
    /// </summary>
    [HttpGet("composite")]
    public async Task<ActionResult<FlashFrameDto>> GetComposite(
        [FromQuery] int    unidadeId,
        [FromQuery] double raio = 200)
    {
        if (unidadeId < 0) return BadRequest("unidadeId is required");
        raio = ValidateRadius(raio);

        try
        {
            var frame = await _service.GetCompositeFrameAsync(unidadeId, raio);
            return Ok(frame);
        }
        catch (ArgumentException ex) { return NotFound(ex.Message); }
    }

    // Only allow predefined radii: 30, 50, 100, 200 km
    private static double ValidateRadius(double raio) =>
        raio switch { 30 => 30, 50 => 50, 100 => 100, _ => 200 };
}
