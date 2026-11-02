namespace ProfitFox.Application.Ports.LiveTrading;

/// <summary>Writes what live trading did to the service log; never a key, a secret or an amount of money of the account.</summary>
public interface ILiveTradingJournal
{
    void Took(Guid botId, long epoch);

    void Lost(Guid botId);

    void Sent(Guid botId, string clientId);

    void Refused(Guid botId, string clientId, string reason);

    void Failed(Guid botId, string reason);
}