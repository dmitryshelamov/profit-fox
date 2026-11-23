using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611230004)]
public sealed class OrderIdeaExitIndexMigration : Migration
{
    // The index of the journal in the order of the journal (change add-ideas-and-market, task 19.6): the time of exit
    // descending with the missing last, as the list orders it, then its tie-break — a page is read from the index without a
    // sort. The index of task 18.16 put the missing first, and the sort stayed over it.
    public override void Up()
    {
        Execute.Sql("DROP INDEX ideas_state_exit_time_ix;");
        Execute.Sql("CREATE INDEX ideas_state_exit_time_ix ON ideas (state, exit_time DESC NULLS LAST, moment DESC, id);");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX ideas_state_exit_time_ix;");
        Execute.Sql("CREATE INDEX ideas_state_exit_time_ix ON ideas (state, exit_time DESC);");
    }
}
