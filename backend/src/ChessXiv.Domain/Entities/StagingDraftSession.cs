namespace ChessXiv.Domain.Entities;

/// <summary>
/// One row per draft owner (a signed-in user id, or a "guest:..." session subject),
/// tracking when the draft was last touched. Cleanup is idle-based rather than
/// age-based so an import never disappears while it is still being browsed.
/// </summary>
public class StagingDraftSession
{
    public string OwnerUserId { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastAccessedAtUtc { get; set; }
}
