using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThesisDemo.Domain.Library;
using ThesisDemo.Domain.Processing;
using ThesisDemo.Domain.Search;
using ThesisDemo.Infrastructure.Jobs;

namespace ThesisDemo.Infrastructure.Persistence;

public enum DatabaseProvider { Sqlite, SqlServer }

public class ThesisDemoDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<VideoAsset> VideoAssets => Set<VideoAsset>();
    public DbSet<ProcessingRun> ProcessingRuns => Set<ProcessingRun>();
    public DbSet<ProcessingAssetAttempt> ProcessingAssetAttempts => Set<ProcessingAssetAttempt>();
    public DbSet<IndexSnapshot> IndexSnapshots => Set<IndexSnapshot>();
    public DbSet<SegmentSet> SegmentSets => Set<SegmentSet>();
    public DbSet<VideoSegment> VideoSegments => Set<VideoSegment>();
    public DbSet<IndexMember> IndexMembers => Set<IndexMember>();
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();
    public DbSet<DurableJob> Jobs => Set<DurableJob>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Collection>().Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Entity<VideoAsset>().HasIndex(x => new { x.CollectionId, x.LegacyAssetId }).IsUnique();
        b.Entity<ProcessingAssetAttempt>().HasIndex(x => new { x.RunId, x.AssetId, x.AttemptToken }).IsUnique();
        b.Entity<IndexSnapshot>().HasIndex(x => new { x.CollectionId, x.State });
        b.Entity<ProcessingRun>().HasIndex(x => new { x.State, x.CreatedUtc });
        b.Entity<DurableJob>().HasIndex(x => new { x.State, x.CreatedUtc });
        b.Entity<DurableJob>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<VideoSegment>().HasIndex(x => new { x.SegmentSetId, x.LegacySegmentId }).IsUnique();
        b.Entity<VideoSegment>().HasIndex(x => new { x.SegmentSetId, x.StartSec });
        b.Entity<IndexMember>().HasKey(x => new { x.IndexId, x.AssetId });
        b.Entity<ArtifactRecord>().HasIndex(x => x.RelativePath).IsUnique();
        foreach (var type in new[] { typeof(Collection), typeof(VideoAsset), typeof(ProcessingRun), typeof(ProcessingAssetAttempt), typeof(IndexSnapshot), typeof(SegmentSet), typeof(VideoSegment), typeof(ArtifactRecord) })
        {
            var entity = b.Entity(type);
            if (Database.IsSqlServer()) entity.Property<byte[]>("RowVersion").IsRowVersion().IsConcurrencyToken();
            else entity.Property(nameof(VersionedEntity.Version)).IsConcurrencyToken();
        }
    }
}

public static class PersistenceRegistration
{
    public static IServiceCollection AddThesisPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var value = configuration["Database:Provider"] ?? throw new InvalidOperationException("Database:Provider must be Sqlite or SqlServer; no fallback is used.");
        if (!Enum.TryParse<DatabaseProvider>(value, true, out var provider) || !Enum.IsDefined(provider)) throw new InvalidOperationException($"Unsupported database provider '{value}'.");
        var connection = configuration.GetConnectionString(provider.ToString()) ?? throw new InvalidOperationException($"Missing {provider} connection string.");
        services.AddSingleton<IDbContextFactory<ThesisDemoDbContext>>(
            new ProviderDbContextFactory(provider, connection));
        return services;
    }
}

public sealed class SqliteThesisDemoDbContext(DbContextOptions<SqliteThesisDemoDbContext> options) : ThesisDemoDbContext(options);
public sealed class SqlServerThesisDemoDbContext(DbContextOptions<SqlServerThesisDemoDbContext> options) : ThesisDemoDbContext(options);

public sealed class ProviderDbContextFactory(DatabaseProvider provider, string connection) : IDbContextFactory<ThesisDemoDbContext>
{
    public ThesisDemoDbContext CreateDbContext()
    {
        if (provider == DatabaseProvider.Sqlite)
        {
            var options = new DbContextOptionsBuilder<SqliteThesisDemoDbContext>().UseSqlite(connection).Options;
            return new SqliteThesisDemoDbContext(options);
        }
        var sqlOptions = new DbContextOptionsBuilder<SqlServerThesisDemoDbContext>().UseSqlServer(connection).Options;
        return new SqlServerThesisDemoDbContext(sqlOptions);
    }
    public Task<ThesisDemoDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
}
