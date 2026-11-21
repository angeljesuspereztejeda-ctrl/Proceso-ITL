using System.Diagnostics;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Application.Services;

public class ExtractionOrchestrator
{
    private readonly IEnumerable<IExtractor> _extractors;
    private readonly IDataLoader _dataLoader;
    private readonly ILoggerService _logger;

    public ExtractionOrchestrator(
        IEnumerable<IExtractor> extractors,
        IDataLoader dataLoader,
        ILoggerService logger)
    {
        _extractors = extractors;
        _dataLoader = dataLoader;
        _logger = logger;
    }

    public async Task ExecuteExtractionAsync(CancellationToken cancellationToken)
    {
        var swTotal = Stopwatch.StartNew();
        _logger.LogInfo("=== INICIANDO CICLO DE EXTRACCIÓN (E DE ETL) ===");

        var tareasExtraccion = _extractors.Select(async extractor =>
        {
            var swFuente = Stopwatch.StartNew();
            try
            {
                _logger.LogInfo($"[{extractor.NombreFuente}] Iniciando extracción...");
                var registros = (await extractor.ExtractAsync(cancellationToken)).ToList();
                swFuente.Stop();
                _logger.LogInfo($"[{extractor.NombreFuente}] Extraídos {registros.Count} registros en {swFuente.ElapsedMilliseconds} ms.");
                return registros;
            }
            catch (Exception ex)
            {
                swFuente.Stop();
                _logger.LogError($"[{extractor.NombreFuente}] Error durante la extracción ({swFuente.ElapsedMilliseconds} ms).", ex);
                return new List<OpinionExtractDto>();
            }
        });

        var resultadosPorFuente = await Task.WhenAll(tareasExtraccion);
        var todosLosDatos = resultadosPorFuente.SelectMany(r => r).ToList();

        if (todosLosDatos.Count == 0)
        {
            _logger.LogWarning("No se extrajeron registros en este ciclo.");
            return;
        }

        var swCarga = Stopwatch.StartNew();
        int guardados = await _dataLoader.SaveToStagingAsync(todosLosDatos, cancellationToken);
        swCarga.Stop();
        swTotal.Stop();

        _logger.LogInfo($"[DataLoader] Guardados {guardados} registros en Staging (SQL + JSON temporal) en {swCarga.ElapsedMilliseconds} ms.");
        _logger.LogInfo($"=== CICLO COMPLETADO | Total extraído: {todosLosDatos.Count} | Tiempo total: {swTotal.ElapsedMilliseconds} ms ===");
    }
}