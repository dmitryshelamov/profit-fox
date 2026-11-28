using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitFox.Application.Ports.Security;

namespace ProfitFox.Infrastructure.Security;

internal static class SecurityRegistrar
{
    /// <summary>
    ///     The protector of the secrets under the master key <c>ExchangeAccounts:MasterKey</c>, in every process, so every
    ///     handler can be built; it reads the key only when it is first used, and a key that is not there or not 32 bytes in
    ///     base64 fails that use with a message that names the setting without its value. A Worker never uses it.
    /// </summary>
    public static void Configure(IConfiguration configuration, IServiceCollection services)
    {
        services.AddOptions<ExchangeAccountsSettings>()
            .Bind(configuration.GetSection("ExchangeAccounts"))
            .Validate(settings => MasterKey.Parse(settings.MasterKey) is not null, MasterKey.Refusal);
        services.AddSingleton<ISecretProtector>(provider =>
            new AesGcmSecretProtector(MasterKey.Parse(provider.GetRequiredService<IOptions<ExchangeAccountsSettings>>().Value.MasterKey)
                                      ?? throw new InvalidOperationException(MasterKey.Refusal)));
    }

    /// <summary>
    ///     The check of the master key when the host starts (design.md, decision 1): without the key, or with a key of another
    ///     kind, the host does not start.
    /// </summary>
    public static void RequireAtStart(IServiceCollection services)
    {
        services.AddOptions<ExchangeAccountsSettings>().ValidateOnStart();
    }
}
