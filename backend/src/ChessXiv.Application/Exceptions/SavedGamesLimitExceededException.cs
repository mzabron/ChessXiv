namespace ChessXiv.Application.Exceptions;

/// <summary>
/// Thrown when saving would push an account past <see cref="Contracts.ChessXivLimits.MaxSavedGamesPerUser"/>.
/// Carries the numbers so the UI can tell the user exactly how much room is left.
/// </summary>
public sealed class SavedGamesLimitExceededException(int currentlySaved, int requested, int limit)
    : Exception($"Saving {requested} games would exceed the {limit} saved-game limit ({currentlySaved} already saved).")
{
    public int CurrentlySaved { get; } = currentlySaved;
    public int Requested { get; } = requested;
    public int Limit { get; } = limit;
    public int Remaining { get; } = Math.Max(0, limit - currentlySaved);
}
