using Microsoft.Extensions.Logging;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Infrastructure.Logging;

public class LoggerService : ILoggerService
{
    private readonly ILogger<LoggerService> _logger;

    public LoggerService(ILogger<LoggerService> logger)
    {
        _logger = logger;
    }

    public void LogInfo(string mensaje) => _logger.LogInformation("{Mensaje}", mensaje);
    public void LogWarning(string mensaje) => _logger.LogWarning("{Mensaje}", mensaje);
    public void LogError(string mensaje, Exception? ex = null) => _logger.LogError(ex, "{Mensaje}", mensaje);
}