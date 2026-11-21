using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Infrastructure.Loaders;

public class DataLoader : IDataLoader
{
    private readonly EtlSettings _settings;
    private readonly ILoggerService _logger;

    public DataLoader(IOptions<EtlSettings> settings, ILoggerService logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<int> SaveToStagingAsync(IEnumerable<OpinionExtractDto> datos, CancellationToken cancellationToken)
    {
        var lista = datos.ToList();

        Directory.CreateDirectory(_settings.StagingJsonFolder);
        string archivoJson = Path.Combine(
            _settings.StagingJsonFolder,
            $"staging_opiniones_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        var json = JsonSerializer.Serialize(lista, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(archivoJson, json, cancellationToken);
        _logger.LogInfo($"[DataLoader] Archivo temporal generado: {archivoJson}");

        var tabla = CrearDataTable(lista);

        await using var conn = new SqlConnection(_settings.AnalyticalDbConnection);
        await conn.OpenAsync(cancellationToken);

        await using (var truncateCmd = new SqlCommand("TRUNCATE TABLE dbo.Staging_Opiniones;", conn))
        {
            await truncateCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        using var bulkCopy = new SqlBulkCopy(conn)
        {
            DestinationTableName = "dbo.Staging_Opiniones",
            BatchSize = 500,
            BulkCopyTimeout = 120
        };

        bulkCopy.ColumnMappings.Add("IdOpinionOrigen", "IdOpinionOrigen");
        bulkCopy.ColumnMappings.Add("IdClienteOrigen", "IdClienteOrigen");
        bulkCopy.ColumnMappings.Add("IdProductoOrigen", "IdProductoOrigen");
        bulkCopy.ColumnMappings.Add("Fuente", "Fuente");
        bulkCopy.ColumnMappings.Add("Fecha", "Fecha");
        bulkCopy.ColumnMappings.Add("Comentario", "Comentario");
        bulkCopy.ColumnMappings.Add("Puntaje", "Puntaje");
        bulkCopy.ColumnMappings.Add("FechaExtraccion", "FechaExtraccion");

        await bulkCopy.WriteToServerAsync(tabla, cancellationToken);
        return lista.Count;
    }

    private static DataTable CrearDataTable(List<OpinionExtractDto> lista)
    {
        var dt = new DataTable();
        dt.Columns.Add("IdOpinionOrigen", typeof(string));
        dt.Columns.Add("IdClienteOrigen", typeof(string));
        dt.Columns.Add("IdProductoOrigen", typeof(string));
        dt.Columns.Add("Fuente", typeof(string));
        dt.Columns.Add("Fecha", typeof(DateTime));
        dt.Columns.Add("Comentario", typeof(string));
        dt.Columns.Add("Puntaje", typeof(decimal));
        dt.Columns.Add("FechaExtraccion", typeof(DateTime));

        foreach (var item in lista)
        {
            dt.Rows.Add(
                item.IdOpinionOrigen,
                (object?)item.IdClienteOrigen ?? DBNull.Value,
                (object?)item.IdProductoOrigen ?? DBNull.Value,
                item.Fuente,
                (object?)item.Fecha ?? DBNull.Value,
                (object?)item.Comentario ?? DBNull.Value,
                (object?)item.Puntaje ?? DBNull.Value,
                item.FechaExtraccion);
        }

        return dt;
    }
}