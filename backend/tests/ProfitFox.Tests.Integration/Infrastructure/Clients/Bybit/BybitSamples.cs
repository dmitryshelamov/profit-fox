namespace ProfitFox.Tests.Integration.Infrastructure.Clients.Bybit;

/// <summary>
///     Answers of Bybit v5 for the tests of the clients of the trades (change take-idea-trades, design.md, decision 15), in
///     the form of the documentation of Bybit: a long of 0.4 <c>SOLUSDT</c> at 237.40 with the stop at 231.38 and the target
///     at 245.54, entered at 14:00 UTC of 2026-11-28 (<c>1795874400000</c>). The same records come by the stream with the
///     envelope of its topic.
/// </summary>
internal static class BybitSamples
{
    public const string LinkId = "t0a1b2c3d4e5f6g7h8i9j0k1l2-1";

    public const string Position =
        """{"positionIdx":0,"riskId":1,"riskLimitValue":"200000","symbol":"SOLUSDT","side":"Buy","size":"0.4","avgPrice":"237.4","positionValue":"94.96","tradeMode":0,"positionStatus":"Normal","autoAddMargin":0,"adlRankIndicator":2,"leverage":"3","positionBalance":"31.7","markPrice":"237.41","liqPrice":"159.5","bustPrice":"158.3","positionMM":"0.5","positionIM":"31.7","tpslMode":"Full","takeProfit":"245.54","stopLoss":"231.38","trailingStop":"0","unrealisedPnl":"0.004","curRealisedPnl":"-0.0522","cumRealisedPnl":"-0.0522","seq":4688002127,"isReduceOnly":false,"mmrSysUpdatedTime":"","leverageSysUpdatedTime":"","createdTime":"1795874400000","updatedTime":"1795874400123"}""";

    public const string EmptyPosition =
        """{"positionIdx":0,"riskId":1,"riskLimitValue":"200000","symbol":"XRPUSDT","side":"","size":"0","avgPrice":"0","positionValue":"0","tradeMode":0,"positionStatus":"Normal","autoAddMargin":0,"adlRankIndicator":0,"leverage":"10","positionBalance":"0","markPrice":"1.4126","liqPrice":"","bustPrice":"","positionMM":"0","positionIM":"0","tpslMode":"Full","takeProfit":"","stopLoss":"","trailingStop":"0","unrealisedPnl":"","curRealisedPnl":"0","cumRealisedPnl":"0","seq":1,"isReduceOnly":false,"mmrSysUpdatedTime":"","leverageSysUpdatedTime":"","createdTime":"1795874400000","updatedTime":"1795874400000"}""";

    public const string HedgePosition =
        """{"positionIdx":1,"riskId":1,"riskLimitValue":"200000","symbol":"SOLUSDT","side":"","size":"0","avgPrice":"0","positionValue":"0","tradeMode":0,"positionStatus":"Normal","autoAddMargin":0,"adlRankIndicator":0,"leverage":"3","positionBalance":"0","markPrice":"237.41","liqPrice":"","bustPrice":"","positionMM":"0","positionIM":"0","tpslMode":"Full","takeProfit":"","stopLoss":"","trailingStop":"0","unrealisedPnl":"","curRealisedPnl":"0","cumRealisedPnl":"0","seq":1,"isReduceOnly":false,"mmrSysUpdatedTime":"","leverageSysUpdatedTime":"","createdTime":"1795874400000","updatedTime":"1795874400000"}""";

    public const string EntryFill =
        $$"""{"symbol":"SOLUSDT","orderId":"5f1a9c2e-0001","orderLinkId":"{{LinkId}}","side":"Buy","orderPrice":"249.27","orderQty":"0.4","leavesQty":"0","createType":"CreateByUser","orderType":"Market","stopOrderType":"","execFee":"0.0522","execId":"e-0001","execPrice":"237.4","execQty":"0.4","execType":"Trade","execValue":"94.96","execTime":"1795874400123","feeCurrency":"USDT","isMaker":false,"feeRate":"0.00055","tradeIv":"","markIv":"","markPrice":"237.41","indexPrice":"","underlyingPrice":"","blockTradeId":"","closedSize":"0","seq":4688002127,"marketUnit":""}""";

    public const string StopFill =
        """{"symbol":"SOLUSDT","orderId":"5f1a9c2e-0002","orderLinkId":"","side":"Sell","orderPrice":"219.81","orderQty":"0","leavesQty":"0","createType":"CreateByStopLoss","orderType":"Market","stopOrderType":"StopLoss","execFee":"0.0509","execId":"e-0002","execPrice":"231.38","execQty":"0.4","execType":"Trade","execValue":"92.552","execTime":"1795878000456","feeCurrency":"USDT","isMaker":false,"feeRate":"0.00055","tradeIv":"","markIv":"","markPrice":"231.4","indexPrice":"","underlyingPrice":"","blockTradeId":"","closedSize":"0.4","seq":4688002200,"marketUnit":""}""";

    public const string BustFill =
        """{"symbol":"SOLUSDT","orderId":"","orderLinkId":"","side":"Sell","orderPrice":"158.3","orderQty":"0.4","leavesQty":"0","createType":"","orderType":"Market","stopOrderType":"","execFee":"0","execId":"e-0003","execPrice":"158.3","execQty":"0.4","execType":"BustTrade","execValue":"63.32","execTime":"1795878000789","feeCurrency":"USDT","isMaker":false,"feeRate":"0","tradeIv":"","markIv":"","markPrice":"158","indexPrice":"","underlyingPrice":"","blockTradeId":"","closedSize":"0.4","seq":4688002300,"marketUnit":""}""";

    public const string FundingFill =
        """{"symbol":"SOLUSDT","orderId":"","orderLinkId":"","side":"Buy","orderPrice":"0","orderQty":"0","leavesQty":"0","createType":"","orderType":"UNKNOWN","stopOrderType":"","execFee":"-0.05","execId":"e-0004","execPrice":"237.5","execQty":"0.4","execType":"Funding","execValue":"95","execTime":"1795881600000","feeCurrency":"USDT","isMaker":false,"feeRate":"-0.0005","tradeIv":"","markIv":"","markPrice":"237.5","indexPrice":"","underlyingPrice":"","blockTradeId":"","closedSize":"0","seq":4688002400,"marketUnit":""}""";

    public const string Order =
        $$"""{"orderId":"5f1a9c2e-0001","orderLinkId":"{{LinkId}}","blockTradeId":"","symbol":"SOLUSDT","price":"249.27","qty":"0.4","side":"Buy","isLeverage":"","positionIdx":0,"orderStatus":"Filled","cancelType":"UNKNOWN","rejectReason":"EC_NoError","avgPrice":"237.4","leavesQty":"0","leavesValue":"0","cumExecQty":"0.4","cumExecValue":"94.96","cumExecFee":"0.0522","timeInForce":"IOC","orderType":"Market","stopOrderType":"","orderIv":"","triggerPrice":"","takeProfit":"245.54","stopLoss":"231.38","tpTriggerBy":"LastPrice","slTriggerBy":"LastPrice","triggerDirection":0,"triggerBy":"","lastPriceOnCreated":"237.39","reduceOnly":false,"closeOnTrigger":false,"smpType":"None","smpGroup":0,"smpOrderId":"","tpslMode":"Full","tpLimitPrice":"","slLimitPrice":"","placeType":"","createdTime":"1795874400100","updatedTime":"1795874400123"}""";

    public const string RejectedOrder =
        $$"""{"orderId":"5f1a9c2e-0009","orderLinkId":"{{LinkId}}","blockTradeId":"","symbol":"SOLUSDT","price":"249.27","qty":"0.4","side":"Buy","isLeverage":"","positionIdx":0,"orderStatus":"Rejected","cancelType":"UNKNOWN","rejectReason":"EC_InsufficientMargin","avgPrice":"","leavesQty":"0","leavesValue":"0","cumExecQty":"0","cumExecValue":"0","cumExecFee":"0","timeInForce":"IOC","orderType":"Market","stopOrderType":"","orderIv":"","triggerPrice":"","takeProfit":"","stopLoss":"","tpTriggerBy":"","slTriggerBy":"","triggerDirection":0,"triggerBy":"","lastPriceOnCreated":"237.39","reduceOnly":false,"closeOnTrigger":false,"smpType":"None","smpGroup":0,"smpOrderId":"","tpslMode":"","tpLimitPrice":"","slLimitPrice":"","placeType":"","createdTime":"1795874400100","updatedTime":"1795874400123"}""";

    public static string Ok(string result)
    {
        return $$"""{"retCode":0,"retMsg":"OK","result":{{result}},"retExtInfo":{},"time":1795874400200}""";
    }

    public static string List(string category, string cursor, params string[] items)
    {
        return Ok($$"""{"category":"{{category}}","list":[{{string.Join(",", items)}}],"nextPageCursor":"{{cursor}}"}""");
    }

    public static string Error(int code, string reason)
    {
        return $$"""{"retCode":{{code}},"retMsg":"{{reason}}","result":{},"retExtInfo":{},"time":1795874400200}""";
    }

    /// <summary>
    ///     A message of the private stream as Bybit sends it, with the records of its topic.
    /// </summary>
    public static string Topic(string topic, params string[] items)
    {
        string category = string.Join(",", items.Select(item => item.Insert(1, "\"category\":\"linear\",")));
        return $$"""{"id":"5923240c6880ab-c59f-420b-9adb-3639adc9dd90","topic":"{{topic}}","creationTime":1795874400130,"data":[{{category}}]}""";
    }
}
