namespace Infrastructure.Configuration;

public class EventImportanceOptions
{
    public const string SectionName = "EventImportance";

    public ImportanceWeights Weights { get; set; } = new();
    public ImportanceCaps Caps { get; set; } = new();
    public double HalfLifeHours { get; set; } = 12;
    public ImportanceTiers Tiers { get; set; } = new();
}

public class ImportanceWeights
{
    public double Volume { get; set; } = 0.20;
    public double Sources { get; set; } = 0.30;
    public double Velocity { get; set; } = 0.20;
    public double Ai { get; set; } = 0.30;
}

public class ImportanceCaps
{
    public int Volume { get; set; } = 20;
    public int Sources { get; set; } = 5;
    public int Velocity { get; set; } = 5;
}

public class ImportanceTiers
{
    public double BreakingThreshold { get; set; } = 75;
    public double HighThreshold { get; set; } = 50;
    public double NormalThreshold { get; set; } = 25;
}
