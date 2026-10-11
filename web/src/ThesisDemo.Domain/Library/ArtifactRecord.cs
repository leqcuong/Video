namespace ThesisDemo.Domain.Library;

public sealed class ArtifactRecord : VersionedEntity
{
    private ArtifactRecord() { }
    public ArtifactRecord(Guid id, string kind, string relativePath, string sha256, long byteLength, string contentType, string provenanceJson)
    {
        if (id == Guid.Empty || byteLength < 0 || string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Invalid artifact metadata.");
        Id = id; Kind = kind; RelativePath = relativePath; Sha256 = sha256; ByteLength = byteLength;
        ContentType = contentType; ProvenanceJson = provenanceJson;
    }
    public string Kind { get; private set; } = "";
    public string RelativePath { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public long ByteLength { get; private set; }
    public string ContentType { get; private set; } = "";
    public string ProvenanceJson { get; private set; } = "{}";
}
