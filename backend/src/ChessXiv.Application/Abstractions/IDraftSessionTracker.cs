namespace ChessXiv.Application.Abstractions;

public interface IDraftSessionTracker
{
    /// <summary>
    /// Marks the owner's draft as still in use, resetting the idle-cleanup clock.
    /// Called on every read and write of a draft.
    /// </summary>
    Task TouchAsync(string ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Forgets the owner's draft session, e.g. after the draft is cleared or promoted.</summary>
    Task ClearAsync(string ownerUserId, CancellationToken cancellationToken = default);
}
