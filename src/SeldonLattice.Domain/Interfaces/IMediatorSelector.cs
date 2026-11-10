namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for dynamically selecting a mediator from a set of available mediators
/// based on the tension type between two dialecticians. Used when a single mediator
/// should be chosen per turn (e.g., "SingleAuto" strategy).
/// </summary>
public interface IMediatorSelector
{
    /// <summary>
    /// Selects the most appropriate mediator based on the current tension between dialecticians.
    /// </summary>
    /// <param name="dialectician1Text">The most recent text from dialectician 1.</param>
    /// <param name="dialectician2Text">The most recent text from dialectician 2.</param>
    /// <param name="moduleKey">The module key (e.g., "philosophy").</param>
    /// <param name="availableMediators">The set of available mediators to choose from.</param>
    /// <returns>The selected mediator, or null if none is appropriate.</returns>
    IMediator? SelectMediator(
        string dialectician1Text,
        string dialectician2Text,
        string moduleKey,
        IMediator[] availableMediators);
}
