namespace LightningViewer.Web.Models.Domain;

public class UnidadeTomadora
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public string Municipio { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Cnpj { get; set; }
    public string? Endereco { get; set; }
}
