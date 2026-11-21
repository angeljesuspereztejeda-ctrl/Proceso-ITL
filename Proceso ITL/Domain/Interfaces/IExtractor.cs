using OpinionesETL.Worker.Domain.Entities;

namespace OpinionesETL.Worker.Domain.Interfaces;

public interface IExtractor
{
    string NombreFuente { get; }
    Task<IEnumerable<OpinionExtractDto>> ExtractAsync(CancellationToken cancellationToken);
}