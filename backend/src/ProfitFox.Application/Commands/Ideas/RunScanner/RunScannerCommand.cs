using MediatR;

namespace ProfitFox.Application.Commands.Ideas.RunScanner;

/// <summary>
///     Keeps the live data of the bench and counts ideas (change add-ideas-and-market, design.md, decision 1) as the Worker
///     <see cref="Worker" /> that holds the lease of the scanner, until the request is cancelled: when the Worker stops or loses
///     the lease. It ends with <c>true</c>.
/// </summary>
public sealed record RunScannerCommand(string Worker) : IRequest<bool>;
