namespace OpinionesETL.Worker.Domain.Entities;

public record DimClienteDto(
    int IdCliente,
    int IdClienteTransaccional,
    string Nombre,
    string? Email,
    string Pais,
    string TipoCliente,
    string RangoEdad);

public record DimProductoDto(
    int IdProducto,
    int IdProductoTransaccional,
    string Nombre,
    string Categoria);

public record DimFuenteDatosDto(
    int IdFuente,
    string TipoFuente,
    string Canal);

public record DimTiempoDto(
    int IdTiempo,
    DateTime Fecha,
    int Dia,
    int Mes,
    int Anio,
    int Trimestre,
    string NombreMes);

public record DimCampanaDto(
    int IdCampana,
    string NombreCampana,
    string TipoEvento);

public class DimensionLoadResult
{
    public int ClientesProcesados { get; set; }
    public int ProductosProcesados { get; set; }
    public int FuentesProcesadas { get; set; }
    public int FechasProcesadas { get; set; }
    public int CampanasProcesadas { get; set; }
    public int TotalDimensiones => ClientesProcesados + ProductosProcesados + FuentesProcesadas + FechasProcesadas + CampanasProcesadas;
}