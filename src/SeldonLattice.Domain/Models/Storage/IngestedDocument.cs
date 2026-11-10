using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// A normalized document ingested from an external source.
/// Stored in FASTER alongside Merkle nodes, providing the raw material
/// that the dialectical engine debates and the falsifier challenges.
/// </summary>
[MessagePackObject]
public record IngestedDocument : IMerkleNode
{
    /// <summary>Unique identifier within the source system (e.g. Europeana record ID).</summary>
    [Key(0)] public string ExternalId { get; init; } = string.Empty;
    
    /// <summary>Source name, e.g. "europeana", "crossref", "arxiv".</summary>
    [Key(1)] public string SourceId { get; init; } = string.Empty;
    
    /// <summary>Normalized title of the document.</summary>
    [Key(2)] public string Title { get; init; } = string.Empty;
    
    /// <summary>Cleaned full-text content.</summary>
    [Key(3)] public string Body { get; init; } = string.Empty;
    
    /// <summary>Cultural/epistemic frame: "western", "eastern", "indigenous", "scientific", etc.</summary>
    [Key(4)] public string Culture { get; init; } = "unknown";
    
    /// <summary>Domain: "art", "philosophy", "economics", "politics", "science", etc.</summary>
    [Key(5)] public string Domain { get; init; } = "unknown";
    
    /// <summary>When the source document was created/published.</summary>
    [Key(6)] public DateTime SourceTimestamp { get; init; }
    
    /// <summary>When this document was ingested into the lattice.</summary>
    [Key(7)] public DateTime IngestedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Vector embedding of the content for semantic comparison.</summary>
    [Key(8)] public float[] Embedding { get; init; } = [];
    
    /// <summary>Source-specific metadata preserved for traceability.</summary>
    [Key(9)] public Dictionary<string, string> Metadata { get; init; } = [];

    public Cid GetCid() => Cid.Compute(Serialize());
    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}