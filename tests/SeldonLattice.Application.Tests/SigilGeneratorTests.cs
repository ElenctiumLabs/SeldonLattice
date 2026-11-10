using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Services;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class SigilGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_AcceptsSha3_256Cid_WithoutPaddingErrors()
    {
        var generator = new SigilGenerator();
        var cid = Cid.Compute("hello world").ToString();

        var result = await generator.GenerateAsync(cid, SigilAlgorithm.FractalTree, 256);

        Assert.Equal(cid, result.Cid);
        Assert.Equal(32, result.HashBytes.Length);
        Assert.False(string.IsNullOrEmpty(result.Svg));
    }

    [Fact]
    public async Task GenerateAllAsync_HandlesSha3_256Cid()
    {
        var generator = new SigilGenerator();
        var cid = Cid.Compute("sigil test content").ToString();

        var results = (await generator.GenerateAllAsync(cid, 128)).ToList();

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(string.IsNullOrEmpty(r.Svg)));
    }

    [Fact]
    public async Task GenerateAsync_IsDeterministic_ForSameCid()
    {
        var generator = new SigilGenerator();
        var cid = Cid.Compute("deterministic").ToString();

        var a = await generator.GenerateAsync(cid, SigilAlgorithm.FractalTree, 256);
        var b = await generator.GenerateAsync(cid, SigilAlgorithm.FractalTree, 256);

        Assert.Equal(a.Svg, b.Svg);
    }

    // Regression finding: Cid.FromString accepts arbitrary strings and can yield a raw
    // hash shorter than 8 bytes ("testcid" -> 5, "abc" -> 2). HashRng used to read
    // a UInt64 from those and throw ArgumentException -> 500. Any CID must work.
    [Theory]
    [InlineData("testcid")]          // 5-byte hash
    [InlineData("abc")]              // 2-byte hash
    [InlineData("test-cid-for-sigil")] // 13-byte hash (wrapped the old cursor)
    public async Task GenerateAsync_WithShortRawHashCid_DoesNotThrow(string cid)
    {
        var generator = new SigilGenerator();

        foreach (var algo in new[]
        {
            SigilAlgorithm.FractalTree, SigilAlgorithm.Lissajous,
            SigilAlgorithm.Mandala
        })
        {
            var result = await generator.GenerateAsync(cid, algo, 64);
            Assert.False(string.IsNullOrEmpty(result.Svg));
        }
    }

    [Fact]
    public async Task GenerateAsync_WithShortRawHashCid_IsDeterministic()
    {
        var generator = new SigilGenerator();

        var a = await generator.GenerateAsync("testcid", SigilAlgorithm.FractalTree, 64);
        var b = await generator.GenerateAsync("testcid", SigilAlgorithm.FractalTree, 64);

        Assert.Equal(a.Svg, b.Svg);
    }
}