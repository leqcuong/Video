using Dapper;
using Microsoft.EntityFrameworkCore;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Infrastructure.Research;

public sealed record QueryPage(IReadOnlyList<RecordedQuery> Items, int Page, int PageSize, int Total);

public sealed class EvaluationReadRepository(IDbContextFactory<ThesisDemoDbContext> factory)
{
    public async Task<QueryPage> GetQueriesAsync(Guid evaluationId, string? outcome, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        if (outcome is not null && outcome is not ("success" or "failure" or "improved" or "worsened" or "unchanged")) throw new ArgumentException("Invalid outcome.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        var filter = "WHERE EvaluationId = @evaluationId AND (@outcome IS NULL OR Outcome = @outcome)";
        var parameters = new { evaluationId = db.Database.IsSqlite() ? evaluationId.ToString("D") : (object)evaluationId, outcome, take = pageSize, skip = checked((page - 1) * pageSize) };
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM RecordedQueries {filter}", parameters, cancellationToken: ct));
        var sql = db.Database.IsSqlServer()
            ? $"SELECT * FROM RecordedQueries {filter} ORDER BY Ordinal OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY"
            : $"SELECT * FROM RecordedQueries {filter} ORDER BY Ordinal LIMIT @take OFFSET @skip";
        var rows = (await connection.QueryAsync<RecordedQuery>(new CommandDefinition(sql, parameters, cancellationToken: ct))).ToList();
        return new QueryPage(rows, page, pageSize, total);
    }
}
