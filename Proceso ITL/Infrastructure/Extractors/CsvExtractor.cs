using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Infrastructure.Extractors;

public class CsvExtractor : IExtractor
{
    private readonly EtlSettings _settings;
    private readonly ILoggerService _logger;

    public string NombreFuente => "CSV_Encuestas";

    public CsvExtractor(IOptions<EtlSettings> settings, ILoggerService logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IEnumerable<OpinionExtractDto>> ExtractAsync(CancellationToken cancellationToken)
    {
        var resultados = new List<OpinionExtractDto>();

        if (!File.Exists(_settings.CsvPath))
        {
            _logger.LogWarning($"[{NombreFuente}] Archivo no encontrado en: {_settings.CsvPath}");
            return resultados;
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var reader = new StreamReader(_settings.CsvPath);
        using var csv = new CsvReader(reader, config);

        await csv.ReadAsync();
        csv.ReadHeader();

        int fila = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            fila++;

            try
            {
                string idOrigen = LeerCampo(csv, "IdOpinion", "IdEncuesta", "IdReview") ?? $"CSV-{fila}";
                string? idCliente = LeerCampo(csv, "IdCliente");
                string? idProducto = LeerCampo(csv, "IdProducto");
                string? fechaStr = LeerCampo(csv, "Fecha");
                string? comentario = LeerCampo(csv, "Comentario");
                string? puntajeStr = LeerCampo(csv, "PuntajeSatisfaccion", "Rating", "Puntuacion");

                DateTime? fecha = DateTime.TryParse(fechaStr, out var f) ? f : null;
                decimal? puntaje = decimal.TryParse(puntajeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : null;

                resultados.Add(new OpinionExtractDto
                {
                    IdOpinionOrigen = idOrigen,
                    IdClienteOrigen = string.IsNullOrWhiteSpace(idCliente) ? "0" : idCliente.Trim(),
                    IdProductoOrigen = idProducto?.Trim(),
                    Fuente = NombreFuente,
                    Fecha = fecha,
                    Comentario = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim(),
                    Puntaje = puntaje,
                    FechaExtraccion = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[{NombreFuente}] Fila {fila} omitida por formato inválido: {ex.Message}");
            }
        }

        return resultados;
    }

    private static string? LeerCampo(CsvReader csv, params string[] posiblesNombres)
    {
        foreach (var nombre in posiblesNombres)
        {
            if (csv.TryGetField<string>(nombre, out var valor) && valor is not null)
                return valor;
        }
        return null;
    }
}