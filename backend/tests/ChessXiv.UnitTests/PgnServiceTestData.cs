namespace ChessXiv.UnitTests;

internal static class PgnServiceTestData
{
    public static string LoadGamesSamplePgn()
    {
        var root = FindProjectRoot();
        var pgnPath = Path.Combine(root, "../TestData", "games_sample.pgn");
        return File.ReadAllText(pgnPath);
    }

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var csproj = Path.Combine(current.FullName, "ChessXiv.UnitTests.csproj");
            if (File.Exists(csproj))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ChessXiv.UnitTests project root.");
    }
}
