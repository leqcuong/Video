namespace ThesisDemo.Application.Collections;

public sealed class CollectionHandlers(ICollectionStore store)
{
    public Task<PageResult<CollectionDto>> ListAsync(int page, int pageSize, CancellationToken ct) =>
        store.ListAsync(NormalizePage(page), NormalizePageSize(pageSize), ct);

    public Task<CollectionDto?> GetAsync(Guid id, CancellationToken ct) => store.GetAsync(id, ct);

    public Task<CollectionDto> CreateAsync(CreateCollectionCommand command, CancellationToken ct) =>
        store.CreateAsync(command with { Name = command.Name.Trim() }, ct);

    public Task<CollectionDto> UpdateAsync(Guid id, UpdateCollectionCommand command, CancellationToken ct) =>
        store.UpdateAsync(id, command with { Name = command.Name?.Trim() }, ct);

    public Task<PageResult<AssetDto>> ListAssetsAsync(Guid collectionId, ListAssetsQuery query, CancellationToken ct) =>
        store.ListAssetsAsync(collectionId, query with { Page = NormalizePage(query.Page), PageSize = NormalizePageSize(query.PageSize), Name = query.Name?.Trim() }, ct);

    private static int NormalizePage(int page) => page < 1 ? 1 : page;
    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 100);
}

public sealed class CollectionImportHandlers(ILegacyCorpusImporter importer)
{
    public async Task<object> HandleAsync(Guid collectionId, ImportCollectionCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RegisteredSourceId)) throw new ArgumentException("A registered source is required.");
        return command.Action switch
        {
            ImportAction.Preview => await importer.PreviewAsync(collectionId, command.RegisteredSourceId.Trim(), ct),
            ImportAction.Commit => await importer.CommitAsync(collectionId, command.RegisteredSourceId.Trim(), command.PreviewId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(command.Action), "Unsupported import action.")
        };
    }
}
