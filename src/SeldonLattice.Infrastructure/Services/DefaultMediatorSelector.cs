using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Default mediator selector that rotates through available mediators on each call.
/// Ensures structural variety in stalemate interventions - the same mediator voice
/// back-to-back produces diminishing returns on a stuck dialogue.
/// </summary>
public class DefaultMediatorSelector : IMediatorSelector
{
    private int _index = -1;

    public IMediator? SelectMediator(
        string dialectician1Text,
        string dialectician2Text,
        string moduleKey,
        IMediator[] availableMediators)
    {
        if (availableMediators.Length == 0) return null;

        // Advance index, wrapping around so every mediator gets a turn in sequence.
        _index = (_index + 1) % availableMediators.Length;
        return availableMediators[_index];
    }
}
