using Grpc.Core;

namespace ProfitFox.Tests.Shared.TInvest;

/// <summary>A call <see cref="MockTInvestApi" /> received: the method name, the request, the metadata and the deadline it came with.</summary>
public sealed record RecordedCall(string Method, object Request, Metadata Headers, DateTime? Deadline)
{
    /// <summary>The value of a metadata entry, or <c>null</c>.</summary>
    public string? Header(string key)
    {
        return Headers.GetValue(key);
    }
}