using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <summary>
    /// Raises the statistics target on Positions.PosKey.
    /// </summary>
    /// <remarks>
    /// PosKey holds tens of millions of distinct values, and the default statistics target
    /// of 100 samples far too few rows to notice that a handful of keys are wildly more
    /// common than the rest - the starting position appears in every single game. Without
    /// that in the most-common-values list the planner assumes a uniform distribution and
    /// estimates a few dozen rows where the real answer is millions.
    ///
    /// The consequence is not just a wrong number. On a 1.6M-game database the opening-tree
    /// query was estimated at 80 rows against an actual 1,614,263, which made it look too
    /// cheap to be worth parallelising; with a realistic estimate the same query picks a
    /// parallel index-only scan and runs in roughly half the time. The same misestimate is
    /// what would push a filtered opening-tree query (by player or year) into a nested loop
    /// where it should hash join.
    /// </remarks>
    public partial class SetPosKeyStatisticsTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Positions"" ALTER COLUMN ""PosKey"" SET STATISTICS 1000;");

            // The target only takes effect at the next ANALYZE. An empty table analyzes
            // instantly, and an already-populated one gets corrected here rather than
            // waiting for autovacuum to notice.
            migrationBuilder.Sql(@"ANALYZE ""Positions"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // -1 restores the system default (default_statistics_target).
            migrationBuilder.Sql(@"ALTER TABLE ""Positions"" ALTER COLUMN ""PosKey"" SET STATISTICS -1;");
        }
    }
}
