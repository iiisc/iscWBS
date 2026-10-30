using Microsoft.Data.Sqlite;
using iscWBS.Core.Data;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Reads and edits the open project's risk log.</summary>
public sealed class RiskService(ProjectStateService projectStateService)
{
    private const string Columns = "Id, ProjectId, Title, Description, Likelihood, Impact, Owner, Response, IsClosed, CreatedAt";

    private SqliteConnection Db => projectStateService.Connection;

    private ProjectHistory History => projectStateService.History;

    /// <summary>Open risks first, the most serious at the top.</summary>
    public async Task<IReadOnlyList<Risk>> GetByProjectAsync(Guid projectId)
        => (await ReadByProjectAsync(Db, projectId)).OrderBy(r => r.IsClosed).ThenByDescending(r => r.Score).ToList();

    /// <summary>In the order they were logged.</summary>
    internal static Task<List<Risk>> ReadByProjectAsync(SqliteConnection db, Guid projectId)
        => db.QueryAsync($"SELECT {Columns} FROM Risks WHERE ProjectId = $1 ORDER BY CreatedAt", Read, projectId);

    public async Task<Risk> CreateAsync(Guid projectId, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new WbsException("Risk title is required.").WithMessageKey("Error_RiskTitleRequired");

        var risk = new Risk { ProjectId = projectId, Title = title.Trim() };
        await History.ChangeAsync(() => InsertAsync(Db, risk));
        return risk;
    }

    public async Task UpdateAsync(Risk risk)
    {
        if (string.IsNullOrWhiteSpace(risk.Title))
            throw new WbsException("Risk title is required.").WithMessageKey("Error_RiskTitleRequired");

        await History.ChangeAsync(() => Db.ExecuteAsync(
            "UPDATE Risks SET Title = $1, Description = $2, Likelihood = $3, Impact = $4, Owner = $5, Response = $6, IsClosed = $7 WHERE Id = $8",
            risk.Title.Trim(), risk.Description, risk.Likelihood, risk.Impact, risk.Owner.Trim(), risk.Response, risk.IsClosed, risk.Id));
    }

    public async Task DeleteAsync(Guid id)
    {
        await History.ChangeAsync(() => Db.ExecuteAsync("DELETE FROM Risks WHERE Id = $1", id));
    }

    internal static Task<int> InsertAsync(SqliteConnection db, Risk risk)
        => db.ExecuteAsync(
            $"INSERT INTO Risks ({Columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)",
            risk.Id, risk.ProjectId, risk.Title, risk.Description, risk.Likelihood, risk.Impact, risk.Owner, risk.Response,
            risk.IsClosed, risk.CreatedAt);

    private static Risk Read(SqliteDataReader r) => new()
    {
        Id = r.GetGuidText(0),
        ProjectId = r.GetGuidText(1),
        Title = r.GetText(2),
        Description = r.GetText(3),
        Likelihood = (RiskLevel)r.GetInt32(4),
        Impact = (RiskLevel)r.GetInt32(5),
        Owner = r.GetText(6),
        Response = r.GetText(7),
        IsClosed = r.GetBoolean(8),
        CreatedAt = r.GetNullableDateTime(9) ?? default
    };
}
