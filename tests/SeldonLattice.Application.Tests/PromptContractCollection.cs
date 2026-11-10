using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Serialises every test class that reads or writes <see cref="PromptContracts.Enabled"/>.
///
/// That flag is process-wide mutable state, and three classes flip it:
/// <see cref="PromptContractTests"/>, <see cref="SynthesisClaimsContractTests"/>, and any
/// synthesis test that asserts on prompt shape. xUnit runs test CLASSES in parallel by
/// default, so without this a class could observe the flag mid-flip and fail on a prompt
/// another test had just reconfigured.
///
/// The symptom is a test that passes alone and fails in the full run - and it gets worse as
/// the suite grows, because more parallelism means more chances to interleave. Putting every
/// affected class in one collection removes the class of failure rather than tuning around it.
///
/// Add <c>[Collection(PromptContractCollection.Name)]</c> to any test class that touches the
/// flag.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class PromptContractCollection
{
    public const string Name = "PromptContracts (shared static state)";
}
