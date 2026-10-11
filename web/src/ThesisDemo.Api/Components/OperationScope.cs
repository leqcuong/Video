using Microsoft.EntityFrameworkCore;
using ThesisDemo.Infrastructure.Persistence;

namespace ThesisDemo.Api.Components;

public sealed class OperationScope(IServiceScopeFactory scopes)
{
    public async Task<T> ExecuteAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await operation(scope.ServiceProvider, ct);
    }
}

public sealed class CollectionOverview(IDbContextFactory<ThesisDemoDbContext> factory)
{
    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Collections.CountAsync(ct);
    }
}
