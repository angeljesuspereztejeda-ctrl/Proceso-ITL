using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Infrastructure.Extractors;

public class DatabaseExtractor : IExtractor
{
    private readonly EtlSettings _settings;

    public string NombreFuente => "BD_Relacional_Resenas";

    public DatabaseExtractor(IOptions<EtlSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task<IEnumerable<OpinionExtractDto>> ExtractAsync(CancellationToken cancellationToken)
    {
        var resultados = new List<OpinionExtractDto>();

        const string query = @"
            SELECT 
                CAST(o.IdOpinion AS VARCHAR(50)) AS IdOpinionOrigen,
                CAST(o.IdCliente AS VARCHAR(50)) AS IdClienteOrigen,
                CAST(o.IdProducto AS VARCHAR(50)) AS IdProductoOrigen,
                o.Fecha,
                o.Comentario,
                o.PuntajeSatisfaccion
            FROM Opiniones o
            INNER JOIN FuenteDatos f ON o.IdFuente = f.IdFuente;";

        await using var conn = new SqlConnection(_settings.RelationalDbConnection);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            resultados.Add(new OpinionExtractDto
            {
                IdOpinionOrigen = $"DB-{reader["IdOpinionOrigen"]}",
                IdClienteOrigen = reader["IdClienteOrigen"]?.ToString(),
                IdProductoOrigen = reader["IdProductoOrigen"]?.ToString(),
                Fuente = NombreFuente,
                Fecha = reader["Fecha"] == DBNull.Value ? null : Convert.ToDateTime(reader["Fecha"]),
                Comentario = reader["Comentario"] == DBNull.Value ? null : reader["Comentario"].ToString(),
                Puntaje = reader["PuntajeSatisfaccion"] == DBNull.Value ? null : Convert.ToDecimal(reader["PuntajeSatisfaccion"]),
                FechaExtraccion = DateTime.Now
            });
        }

        return resultados;
    }
}