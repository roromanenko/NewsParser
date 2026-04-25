using Core.DomainModels.AI;

namespace Core.Interfaces.AI;

public interface IAiCostCalculator
{
    decimal Calculate(AiUsage usage, string provider, string model);
}
