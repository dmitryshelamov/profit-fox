using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611230003)]
public sealed class AddIdeaExitIndexMigration : Migration
{
    // The journal sorts and picks its period by the time of exit of the closed ideas (change add-ideas-and-market, task
    // 18.16).
    public override void Up()
    {
        Execute.Sql("CREATE INDEX ideas_state_exit_time_ix ON ideas (state, exit_time DESC);");
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX ideas_state_exit_time_ix;");
    }
}
