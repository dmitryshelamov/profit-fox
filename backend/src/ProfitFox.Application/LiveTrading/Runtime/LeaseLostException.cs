namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>Another take of the bot holds its lease: this session must stop at once and write nothing more.</summary>
internal sealed class LeaseLostException() : Exception("The lease of the bot is lost.");