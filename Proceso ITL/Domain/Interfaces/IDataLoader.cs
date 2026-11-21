using OpinionesETL.Worker.Domain.Entities;

namespace OpinionesETL.Worker.Domain.Interfaces;

public interface IDataLoader
{
    Task<int> SaveToStagingAsync(IEnumerable<OpinionExtractDto> datos, CancellationToken cancellationToken);
}