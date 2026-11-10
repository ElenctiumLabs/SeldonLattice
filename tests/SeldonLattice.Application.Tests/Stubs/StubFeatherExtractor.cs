using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Application.Tests.Stubs;

/// <summary>
/// Stub feather extractor for testing. Returns fixed invariants.
/// </summary>
public class StubFeatherExtractor : IFeatherExtractor
{
    private readonly List<string> _invariants;

    public StubFeatherExtractor()
    {
        _invariants = ["invariant1", "invariant2", "invariant3"];
    }

    public StubFeatherExtractor(IReadOnlyList<string> invariants)
    {
        _invariants = [.. invariants];
    }

    public Task<IReadOnlyList<string>> ExtractInvariantsAsync(string rootQuestion, int count, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _invariants.Take(count)]);
    }
}
