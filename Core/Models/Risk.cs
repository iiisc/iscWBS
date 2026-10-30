namespace iscWBS.Core.Models;

public enum RiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}

public class Risk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RiskLevel Likelihood { get; set; } = RiskLevel.Medium;
    public RiskLevel Impact { get; set; } = RiskLevel.Medium;
    public string Owner { get; set; } = string.Empty;

    /// <summary>What is being done to prevent the risk or soften it.</summary>
    public string Response { get; set; } = string.Empty;

    public bool IsClosed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Likelihood times impact, from 1 to 9.</summary>
    public int Score => (int)Likelihood * (int)Impact;

    /// <summary>The usual 3×3 matrix: high from 6 (high and medium), medium from 3.</summary>
    public RiskLevel Severity => Score >= 6 ? RiskLevel.High : Score >= 3 ? RiskLevel.Medium : RiskLevel.Low;
}
