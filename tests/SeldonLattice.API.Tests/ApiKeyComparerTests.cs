using SeldonLattice.API.Authentication;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// Verifies the constant-time API key comparison helper.
/// </summary>
public class ApiKeyComparerTests
{
    [Theory]
    [InlineData("dev-key-admin", "dev-key-admin")]
    [InlineData("x", "x")]
    [InlineData("", "")]
    public void FixedTimeEquals_EqualKeys_ReturnsTrue(string a, string b)
    {
        Assert.True(ApiKeyComparer.FixedTimeEquals(a, b));
    }

    [Theory]
    [InlineData("dev-key-admin", "dev-key-adminx")]
    [InlineData("dev-key-admin", "dev-key-admiN")]
    [InlineData("abc", "abd")]
    [InlineData("dev-key-admin", null)]
    [InlineData(null, "dev-key-admin")]
    [InlineData(null, null)]
    public void FixedTimeEquals_DifferentKeys_ReturnsFalse(string? a, string? b)
    {
        Assert.False(ApiKeyComparer.FixedTimeEquals(a, b));
    }

    [Fact]
    public void FixedTimeEquals_DifferenceOnlyInLastCharacter_ReturnsFalse()
    {
        // A key differing only in the final character must not match - and the
        // comparison path (SHA-256 + FixedTimeEquals) is identical for both.
        Assert.False(ApiKeyComparer.FixedTimeEquals("dev-key-admin", "dev-key-admiN"));
    }

    [Fact]
    public void FixedTimeEquals_VaryingLengthKeys_StillDistinguishes()
    {
        Assert.False(ApiKeyComparer.FixedTimeEquals("dev-key-admin", "dev-key-adminx"));
    }
}
