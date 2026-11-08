using Dapper;

using Npgsql;

using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Integration.Persistence;

/// <summary>
///     Decision 3: the request id a T-Invest order goes with is written in <c>moex_orders</c> under the client id of the order before the
///     order is sent, so a send again goes with the same id, and an id of the exchange tells whose order it is.
/// </summary>
public class TInvestOrderIdsTests
{
    private const string Uid = "e6123145-9665-43e0-8413-cd61b8aa9b13";

    private NpgsqlDataSource _dataSource = null!;
    private TInvestOrderIds _ids = null!;

    [SetUp]
    public void SetUp()
    {
        _dataSource = MarketDataDatabase.Open();
        _ids = new TInvestOrderIds(_dataSource, TimeProvider.System);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _dataSource.DisposeAsync();
    }

    [Test]
    public async Task Request_id_is_written_once_and_read_by_the_client_id_and_back()
    {
        Guid request = await _ids.EnsureAsync("b-1", "2000000001", Uid, CancellationToken.None);

        (await _ids.EnsureAsync("b-1", "2000000001", Uid, CancellationToken.None)).ShouldBe(request);
        (await _ids.RequestOfAsync("b-1", CancellationToken.None)).ShouldBe(request);
        (await _ids.ClientIdOfAsync(request, CancellationToken.None)).ShouldBe("b-1");
        (await _ids.EnsureAsync("b-2", "2000000001", Uid, CancellationToken.None)).ShouldNotBe(request);
        (await RowAsync("b-1")).ShouldBe(("2000000001", request, (string?)null, Uid));
    }

    [Test]
    public async Task Order_unknown_to_the_table_has_no_request_and_no_client()
    {
        (await _ids.RequestOfAsync("b-1", CancellationToken.None)).ShouldBeNull();
        (await _ids.ClientIdOfAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Exchange_id_is_added_after_the_send()
    {
        await _ids.EnsureAsync("b-1", "2000000001", Uid, CancellationToken.None);

        await _ids.SetExchangeIdAsync("b-1", "R-777", CancellationToken.None);

        (await RowAsync("b-1")).ExchangeOrderId.ShouldBe("R-777");
    }

    [Test]
    public async Task Request_id_is_unique()
    {
        Guid request = await _ids.EnsureAsync("b-1", "2000000001", Uid, CancellationToken.None);
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        PostgresException refused = await Should.ThrowAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO moex_orders (client_id, account_id, request_id, instrument_uid, created_at) VALUES ('b-2', '2000000001', @request, @uid, now())",
            new { request, uid = Uid }));

        refused.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    private async Task<(string AccountId, Guid RequestId, string? ExchangeOrderId, string InstrumentUid)> RowAsync(string clientId)
    {
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();

        return await connection.QuerySingleAsync<(string, Guid, string?, string)>(
            "SELECT account_id, request_id, exchange_order_id, instrument_uid FROM moex_orders WHERE client_id = @clientId",
            new { clientId });
    }
}