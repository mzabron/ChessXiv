namespace ChessBase.Domain.Entities;

public class Player
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public string NormalizedFullName { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? NormalizedFirstName { get; set; }
    public string? NormalizedLastName { get; set; }
    public ICollection<Game> GamesAsWhite { get; set; } = new List<Game>();
    public ICollection<Game> GamesAsBlack { get; set; } = new List<Game>();
}
