using Core.DomainModels;

namespace Core.Interfaces.Services;

public interface IEventImportanceScorer
{
    ImportanceScoreResult Calculate(ImportanceInputs inputs);
}
