using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OpinionesETL.Worker.Application.Configuration;
using OpinionesETL.Worker.Domain.Entities;
using OpinionesETL.Worker.Domain.Interfaces;

namespace OpinionesETL.Worker.Infrastructure.Extractors;

public class ApiExtractor : IExtractor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EtlSettings _settings;

    public string NombreFuente => "API_REST_RedesSociales";

    public ApiExtractor(IHttpClientFactory httpClientFactory, IOptions<EtlSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<IEnumerable<OpinionExtractDto>> ExtractAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("SocialMediaApi");
        var response = await client.GetAsync(_settings.ApiEndpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        var apiComments = await response.Content.ReadFromJsonAsync<List<ApiCommentResponse>>(cancellationToken: cancellationToken)
                          ?? new List<ApiCommentResponse>();

        return apiComments.Select(c => new OpinionExtractDto
        {
            IdOpinionOrigen = $"API-{c.Id}",
            IdClienteOrigen = "0", // Anónimo / red social
            IdProductoOrigen = $"P{c.PostId:D3}",
            Fuente = NombreFuente,
            Fecha = DateTime.Now,
            Comentario = c.Body?.Replace("\n", " ").Trim(),
            Puntaje = null,
            FechaExtraccion = DateTime.Now
        }).ToList();
    }

    private sealed class ApiCommentResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("postId")]
        public int PostId { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }
}