using FluentMigrator;

namespace ProfitFox.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611070011)]
public sealed class AddIdeaAutosMigration : ForwardOnlyMigration
{
    // Decision 13: an auto is deleted softly and its name is unique among those that are not. The account is no foreign key: a deleted auto keeps
    // the id of an account that may go; the delete of an account checks the autos that are not deleted. Decision 3: a paper trade has the columns of a real one but the lease, the orders, the children and the fills,
    // and no foreign key to the account or the idea (the replay cleans up ideas); stepped_until is the open time of the next minute to step it by.
    // Decision 6: an entry request of an auto with money carries the account, the amount and the lowest reward to risk of the minute of the idea, and the place
    // of the auto then (rank); fired is the order of the ideas of the minute. Decision 12: a refusal copies the fields of the idea it shows, one per idea
    // and auto. idea_trades.auto_id has no foreign key: a deleted auto keeps its trades.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE idea_autos (
                id uuid NOT NULL,
                name text NOT NULL,
                account_id uuid NOT NULL,
                amount numeric NOT NULL,
                mode text NOT NULL,
                setups text[] NOT NULL,
                types text[] NOT NULL,
                intervals text[] NOT NULL,
                side text NULL,
                category text NULL,
                badge text NULL,
                min_risk_reward numeric NOT NULL,
                min_cases integer NOT NULL,
                position integer NOT NULL,
                created_at timestamp with time zone NOT NULL,
                deleted_at timestamp with time zone NULL,
                CONSTRAINT "PK_idea_autos" PRIMARY KEY (id),
                CONSTRAINT "CK_idea_autos_mode" CHECK (mode IN ('off', 'paper', 'live'))
            );

            CREATE UNIQUE INDEX "UX_idea_autos_name" ON idea_autos (name) WHERE deleted_at IS NULL;

            CREATE TABLE idea_paper_trades (
                id uuid NOT NULL,
                idea_id uuid NOT NULL,
                auto_id uuid NOT NULL,
                account_id uuid NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                setup text NOT NULL,
                interval text NOT NULL,
                type text NOT NULL,
                side text NOT NULL,
                entry_price numeric NOT NULL,
                stop numeric NOT NULL,
                target numeric NOT NULL,
                quantity numeric NOT NULL,
                amount numeric NOT NULL,
                risk numeric NOT NULL,
                entry_deadline timestamp with time zone NULL,
                status text NOT NULL,
                status_at timestamp with time zone NOT NULL,
                entry_quantity numeric NOT NULL,
                entry_value numeric NOT NULL,
                entry_fee numeric NOT NULL,
                take_profit_quantity numeric NOT NULL,
                take_profit_value numeric NOT NULL,
                take_profit_fee numeric NOT NULL,
                stop_loss_quantity numeric NOT NULL,
                stop_loss_value numeric NOT NULL,
                stop_loss_fee numeric NOT NULL,
                created_at timestamp with time zone NOT NULL,
                opened_at timestamp with time zone NULL,
                promised_badge text NULL,
                promised_probability numeric NULL,
                promised_expectancy numeric NULL,
                promised_cases integer NULL,
                break_even numeric NULL,
                net_risk_reward numeric NULL,
                idea_status text NULL,
                idea_status_at timestamp with time zone NULL,
                idea_result numeric NULL,
                stepped_until timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_paper_trades" PRIMARY KEY (id),
                CONSTRAINT "CK_idea_paper_trades_status" CHECK (status IN ('awaiting_entry', 'open', 'closed_at_target', 'closed_at_stop', 'cancelled')),
                CONSTRAINT "CK_idea_paper_trades_side" CHECK (side IN ('long', 'short'))
            );

            CREATE INDEX "IX_idea_paper_trades_idea" ON idea_paper_trades (idea_id);
            CREATE INDEX "IX_idea_paper_trades_auto" ON idea_paper_trades (auto_id);
            CREATE INDEX "IX_idea_paper_trades_kept" ON idea_paper_trades (symbol) WHERE status IN ('awaiting_entry', 'open');

            CREATE TABLE idea_auto_entries (
                id uuid NOT NULL,
                idea_id uuid NOT NULL,
                auto_id uuid NOT NULL,
                account_id uuid NOT NULL,
                amount numeric NOT NULL,
                min_risk_reward numeric NOT NULL,
                rank integer NOT NULL,
                fired integer NOT NULL,
                minute timestamp with time zone NOT NULL,
                created_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_idea_auto_entries" PRIMARY KEY (id),
                CONSTRAINT "UX_idea_auto_entries_idea_auto" UNIQUE (idea_id, auto_id)
            );

            CREATE INDEX "IX_idea_auto_entries_order" ON idea_auto_entries (minute, rank, fired, id);

            CREATE TABLE idea_auto_refusals (
                id uuid NOT NULL,
                idea_id uuid NOT NULL,
                auto_id uuid NOT NULL,
                mode text NOT NULL,
                created_at timestamp with time zone NOT NULL,
                category text NOT NULL,
                symbol text NOT NULL,
                setup text NOT NULL,
                interval text NOT NULL,
                type text NOT NULL,
                side text NOT NULL,
                reasons text[] NOT NULL,
                detail jsonb NULL,
                CONSTRAINT "PK_idea_auto_refusals" PRIMARY KEY (id),
                CONSTRAINT "UX_idea_auto_refusals_idea_auto" UNIQUE (idea_id, auto_id)
            );

            CREATE INDEX "IX_idea_auto_refusals_created_at" ON idea_auto_refusals (created_at DESC);

            ALTER TABLE idea_trades ADD COLUMN auto_id uuid NULL;
            ALTER TABLE idea_trading_settings ADD COLUMN daily_loss_limit numeric NULL, ADD COLUMN max_same_direction integer NULL;
            """);
    }
}