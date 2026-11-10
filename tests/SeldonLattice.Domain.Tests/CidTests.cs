using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.Domain.Tests;

public class CidTests
{
    [Fact]
    public void Compute_uses_sha3_256_algorithm()
    {
        var cid = Cid.Compute("hello world");

        Assert.Equal("sha3-256", cid.Algorithm);
        Assert.Equal(32, cid.RawHash.Length);
    }

    [Fact]
    public void FromString_should_round_trip_a_computed_cid()
    {
        var original = Cid.Compute("hello world");

        var roundTripped = Cid.FromString(original.ToString());

        Assert.Equal(original.Algorithm, roundTripped.Algorithm);
        Assert.Equal(original.Hash, roundTripped.Hash);
        Assert.Equal(original.ToString(), roundTripped.ToString());
    }

    [Fact]
    public void FromString_should_parse_legacy_sha256_cid()
    {
        var legacy = Cid.FromString("sha256-AbCdEf");

        Assert.Equal("sha256", legacy.Algorithm);
        Assert.Equal("AbCdEf", legacy.Hash);
    }
}
