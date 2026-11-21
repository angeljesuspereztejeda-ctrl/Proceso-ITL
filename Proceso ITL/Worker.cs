using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Application.Services;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker;

public class Worker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EtlSettings _settings;
    private readonly ILoggerService _logger;

    public Worker(
        IServiceScopeFactory scopeFactory,
        IOptions<EtlSettings> settings,
        ILoggerService logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInfo("Worker Service ETL iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ExtractionOrchestrator>();

            await orchestrator.ExecuteExtractionAsync(stoppingToken);

            _logger.LogInfo($"Esperando {_settings.ExecutionIntervalSeconds} segundos para el próximo ciclo...");
            await Task.Delay(TimeSpan.FromSeconds(_settings.ExecutionIntervalSeconds), stoppingToken);
        }
    }
}