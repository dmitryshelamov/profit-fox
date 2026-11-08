namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>The group of a gRPC method of Invest API by its service, and the codes of the groups in the metrics and the settings.</summary>
internal static class TInvestMethods
{
    /// <summary>The group of a service such as <c>tinkoff.public.invest.api.contract.v1.MarketDataService</c>; the streams count with their service.</summary>
    public static TInvestMethodGroup GroupOf(string serviceName)
    {
        string service = serviceName[(serviceName.LastIndexOf('.') + 1)..];

        return service switch
        {
            "UsersService" => TInvestMethodGroup.Users,
            "InstrumentsService" => TInvestMethodGroup.Instruments,
            "MarketDataService" or "MarketDataStreamService" => TInvestMethodGroup.MarketData,
            "OperationsService" or "OperationsStreamService" => TInvestMethodGroup.Operations,
            "OrdersService" or "OrdersStreamService" or "StopOrdersService" => TInvestMethodGroup.Orders,
            _ => throw new ArgumentOutOfRangeException(nameof(serviceName), serviceName, "Not a service of Invest API the system calls.")
        };
    }

    /// <summary>The label of a group in the metrics: <c>users</c>, <c>instruments</c>, <c>market_data</c>, <c>operations</c>, <c>orders</c>.</summary>
    public static string ToCode(this TInvestMethodGroup group)
    {
        return group switch
        {
            TInvestMethodGroup.Users => "users",
            TInvestMethodGroup.Instruments => "instruments",
            TInvestMethodGroup.MarketData => "market_data",
            TInvestMethodGroup.Operations => "operations",
            _ => "orders"
        };
    }

    /// <summary>The key of a group in <c>TInvest:Limits</c>.</summary>
    public static string SettingsKey(this TInvestMethodGroup group)
    {
        return group.ToString();
    }
}