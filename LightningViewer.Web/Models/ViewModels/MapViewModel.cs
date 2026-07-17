namespace LightningViewer.Web.Models.ViewModels;

public class MapViewModel
{
    public List<UnidadeDto> Unidades { get; set; } = new();
    public UnidadeDto? DefaultUnidade { get; set; }
}

public class UnidadeDto
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Municipio { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class FlashFrameDto
{
    public string FrameTime { get; set; } = string.Empty;   // ISO 8601 UTC
    public int TotalFlashes { get; set; }
    public List<FlashPointDto> Points { get; set; } = new();
}

public class FlashPointDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public int Count { get; set; }
    public double Intensity { get; set; }
}

public class FrameMetaDto
{
    public string FrameTime { get; set; } = string.Empty;
    public int TotalFlashes { get; set; }
}
