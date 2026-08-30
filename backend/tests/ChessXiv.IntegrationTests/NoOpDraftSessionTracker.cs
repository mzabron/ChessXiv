using ChessXiv.Application.Abstractions;

namespace ChessXiv.IntegrationTests;

/// <summary>Draft idle-tracking is orthogonal to the promotion behaviour under test.</summary>
public sealed class NoOpDraftSessionTracker : IDraftSessionTracker
{
    public Task TouchAsync(string ownerUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ClearAsync(string ownerUserId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
