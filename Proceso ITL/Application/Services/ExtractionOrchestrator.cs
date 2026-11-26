using System.Diagnostics;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Application.Services;

public class ExtractionOrchestrator
{
    private readonly IEnumerable<IExtractor> _extractors;
    private readonly IDataLoader _dataLoader;
    private readonly IDimensionLoader _dimensionLoader;
    private readonly ILoggerService _logger;

    public ExtractionOrchestrator(
        IEnumerable<IExtractor> extractors,
        IDataLoader dataLoader,
        IDimensionLoader dimensionLoader,
        ILoggerService logger)
    {
        _extractors = extractors;
        _dataLoader = dataLoader;
        _dimensionLoader = dimensionLoader;
        _logger = logger;
    }

    public async Task ExecuteExtractionAsync(CancellationToken cancellationToken)
    {
        var swTotal = Stopwatch.StartNew();
        _logger.LogInfo("=== INICIANDO CICLO ETL (EXTRACCIÓN + CARGA DE DIMENSIONES) ===");

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

        var swStaging = Stopwatch.StartNew();
        int guardados = await _dataLoader.SaveToStagingAsync(todosLosDatos, cancellationToken);
        swStaging.Stop();
        _logger.LogInfo($"[DataLoader] Guardados {guardados} registros en Staging (SQL + JSON temporal) en {swStaging.ElapsedMilliseconds} ms.");

        var swDims = Stopwatch.StartNew();
        var resumenDims = await _dimensionLoader.LoadDimensionsAsync(cancellationToken);
        swDims.Stop();
        swTotal.Stop();

        _logger.LogInfo($"[DimensionLoader] Total de registros procesados en dimensiones: {resumenDims.TotalDimensiones} en {swDims.ElapsedMilliseconds} ms.");
        _logger.LogInfo($"=== CICLO COMPLETADO | Staging: {guardados} | Dimensiones: {resumenDims.TotalDimensiones} | Tiempo total: {swTotal.ElapsedMilliseconds} ms ===");
    }
}