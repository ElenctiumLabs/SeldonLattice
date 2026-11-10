using MessagePack;
using SeldonLattice.Domain.Models.Storage;
using Xunit;
using Xunit.Abstractions;

namespace SeldonLattice.Domain.Tests;

/// <summary>
/// The per-turn metrics widened DialecticalNode from 35 to 44 positional MessagePack
/// keys, which changes every node's CID. These tests record what actually happens when
/// a pre-widening payload is read under the widened schema, because the answer decides
/// whether a stale store fails loudly or silently.
/// </summary>
public class DialecticalNodeSchemaWideningTests(ITestOutputHelper output)
{
    /// <summary>Serialises the node, then truncates the array back to the old 35-key width.</summary>
    private static byte[] ToLegacyWidth(DialecticalNode node)
    {
        var current = MessagePackSerializer.Deserialize<object[]>(
            MessagePackSerializer.Serialize(node));
        var truncated = current.Take(35).ToArray();
        return MessagePackSerializer.Serialize(truncated);
    }

    [Fact]
    public void A_pre_widening_payload_still_deserialises_and_yields_nulls()
    {
        var original = new DialecticalNode
        {
            NodeType = "skeptic_reply",
            Level = 3,
            Content = "skeptic responds",
            SessionId = "s1",
            SpeakerId = "skeptic",
            QualityScore = 78.5,
            WordCount = 210,
        };

        var legacy = ToLegacyWidth(original);
        var read = MessagePackSerializer.Deserialize<DialecticalNode>(legacy);

        // The content survives, so the node still loads and renders.
        Assert.Equal("skeptic_reply", read.NodeType);
        Assert.Equal("skeptic responds", read.Content);

        // The new keys come back null - an old record reads as "never scored", which is
        // honest. The dangerous part is the CID, asserted below.
        Assert.Null(read.QualityScore);
        Assert.Null(read.WordCount);
    }

    [Fact]
    public void A_pre_widening_payload_gets_a_DIFFERENT_cid()
    {
        // This is why the store must be wiped rather than migrated: re-serialising an old
        // record under the new schema produces different bytes, so GetCid() no longer
        // matches the key it is stored under. Parent hashes still point at the old value,
        // so every link dangles and SessionVerifier reports Error severity.
        var original = new DialecticalNode
        {
            NodeType = "skeptic_reply",
            Level = 0,
            Content = "skeptic responds",
            SessionId = "s1",
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        var legacyBytes = ToLegacyWidth(original);
        var read = MessagePackSerializer.Deserialize<DialecticalNode>(legacyBytes);

        var legacyCid = Cid.Compute(legacyBytes);
        var cidAfterWidening = read.GetCid();

        output.WriteLine($"cid at legacy width : {legacyCid}");
        output.WriteLine($"cid at current width: {cidAfterWidening}");

        Assert.NotEqual(legacyCid.ToString(), cidAfterWidening.ToString());
    }
}
