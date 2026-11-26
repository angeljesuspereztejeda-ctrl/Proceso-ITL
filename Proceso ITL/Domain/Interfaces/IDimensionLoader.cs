using OpinionesETL.Worker.Domain.Entities;

namespace OpinionesETL.Worker.Domain.Interfaces;

public interface IDimensionLoader
{
    Task<DimensionLoadResult> LoadDimensionsAsync(CancellationToken cancellationToken);
}