namespace OpinionesETL.Worker.Domain.Entities;

public class OpinionExtractDto
{
    public string IdOpinionOrigen { get; set; } = string.Empty;
    public string? IdClienteOrigen { get; set; }
    public string? IdProductoOrigen { get; set; }
    public string Fuente { get; set; } = string.Empty;
    public DateTime? Fecha { get; set; }
    public string? Comentario { get; set; }
    public decimal? Puntaje { get; set; }
    public DateTime FechaExtraccion { get; set; } = DateTime.Now;
}