using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProfitFox.Infrastructure.Exchanges.TInvest;

/// <summary>
///     The pinned chain of the server certificate of T-Invest: the Russian Trusted Root CA and the Sub CA that issues the server
///     certificate, loaded from the <c>.cer</c> files next to the app by the SHA-256 thumbprints of the settings. A server certificate
///     the system trusts passes; one the system does not trust passes only when it chains to the pinned root through the pinned Sub CA
///     and its name matches. Without thumbprints nothing is pinned and the system trust alone decides.
/// </summary>
internal sealed class TInvestCertificates(X509Certificate2? root, X509Certificate2? sub)
{
    /// <summary>Pins nothing.</summary>
    public static readonly TInvestCertificates None = new TInvestCertificates(null, null);

    /// <summary>Loads the pinned certificates from the <c>.cer</c> files of the folder, each by its thumbprint; a file with another thumbprint is refused.</summary>
    /// <exception cref="InvalidOperationException">A file has a thumbprint the settings do not name, or a named certificate has no file.</exception>
    public static TInvestCertificates Load(TInvestCertificateSettings settings, string baseDirectory)
    {
        if (string.IsNullOrEmpty(settings.RootThumbprint) && string.IsNullOrEmpty(settings.SubThumbprint))
        {
            return None;
        }

        X509Certificate2? root = null;
        X509Certificate2? sub = null;
        string directory = Path.Combine(baseDirectory, settings.Directory);

        IEnumerable<string> files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.cer").Order(StringComparer.Ordinal) : [];

        foreach (string file in files)
        {
            X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(file);
            string thumbprint = Thumbprint(certificate);

            if (Matches(thumbprint, settings.RootThumbprint))
            {
                root = certificate;
            }
            else if (Matches(thumbprint, settings.SubThumbprint))
            {
                sub = certificate;
            }
            else
            {
                throw new InvalidOperationException($"The certificate {Path.GetFileName(file)} has the thumbprint {thumbprint}, which TInvest:Certificates does not name.");
            }
        }

        if (root is null || sub is null)
        {
            throw new InvalidOperationException($"TInvest:Certificates names a root and a Sub CA, but {directory} holds no file with {(root is null ? "the root" : "the Sub CA")} thumbprint.");
        }

        return new TInvestCertificates(root, sub);
    }

    /// <summary>The SHA-256 thumbprint of a certificate as the settings write it: upper-case hex without separators.</summary>
    public static string Thumbprint(X509Certificate2 certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    /// <summary>Whether the server certificate is accepted; the callback of the TLS handshake.</summary>
    public bool Accept(X509Certificate2? certificate, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // Only an untrusted chain is ours to judge: a wrong name or no certificate at all never passes.
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || certificate is null || root is null || sub is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.ExtraStore.Add(sub);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        return chain.Build(certificate) && chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(root.RawData);
    }

    private static bool Matches(string thumbprint, string expected)
    {
        return !string.IsNullOrEmpty(expected) && string.Equals(thumbprint, expected.Replace(":", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }
}