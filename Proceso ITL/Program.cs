using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Application.Services;
using OpinionesETL.Worker.Domain.Interfaces;
using OpinionesETL.Worker.Infrastructure.Extractors;
using OpinionesETL.Worker.Infrastructure.Loaders;
using OpinionesETL.Worker.Infrastructure.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<EtlSettings>(builder.Configuration.GetSection("EtlSettings"));

builder.Services.AddHttpClient("SocialMediaApi", (sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<EtlSettings>>().Value;
    client.BaseAddress = new Uri(settings.ApiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);

    if (!string.IsNullOrWhiteSpace(settings.ApiKey))
    {
        client.DefaultRequestHeaders.Add("X-API-Key", settings.ApiKey);
    }
});

builder.Services.AddSingleton<ILoggerService, LoggerService>();
builder.Services.AddTransient<IExtractor, CsvExtractor>();
builder.Services.AddTransient<IExtractor, DatabaseExtractor>();
builder.Services.AddTransient<IExtractor, ApiExtractor>();
builder.Services.AddTransient<IDataLoader, DataLoader>();
builder.Services.AddTransient<IDimensionLoader, DimensionLoader>();
builder.Services.AddTransient<ExtractionOrchestrator>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();