using ChessXiv.Application.Abstractions;

namespace ChessXiv.UnitTests;

/// <summary>Draft idle-tracking is a persistence concern; tests exercise it separately.</summary>
public sealed class NoOpDraftSessionTracker : IDraftSessionTracker
{
    public Task TouchAsync(string ownerUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ClearAsync(string ownerUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
