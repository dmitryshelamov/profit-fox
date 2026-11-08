using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using ProfitFox.Infrastructure.Exchanges.TInvest;

using Shouldly;

namespace ProfitFox.Tests.Unit.Infrastructure.Exchanges.TInvest;

/// <summary>Decision 7: the server certificate chains to the pinned root through the pinned Sub CA, or the system trusts it.</summary>
public class TInvestCertificatesTests
{
    // The chain is checked at the real time of the handshake, so the test certificates are valid around it.
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.AddDays(-1);

    private X509Certificate2 _root = null!;
    private X509Certificate2 _sub = null!;
    private X509Certificate2 _server = null!;
    private X509Certificate2 _otherRoot = null!;
    private X509Certificate2 _otherSub = null!;
    private X509Certificate2 _otherServer = null!;
    private X509Certificate2 _clientOnly = null!;

    [SetUp]
    public void SetUp()
    {
        using var rootKey = RSA.Create(2048);
        using var subKey = RSA.Create(2048);
        using var serverKey = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        using var otherSubKey = RSA.Create(2048);
        using var otherServerKey = RSA.Create(2048);
        using var clientOnlyKey = RSA.Create(2048);
        _root = Authority("CN=Test Trusted Root CA", rootKey, issuer: null, issuerKey: null);
        _sub = Authority("CN=Test Trusted Sub CA", subKey, _root, rootKey);
        _server = Server("invest-public-api.tbank.ru", serverKey, _sub, subKey);
        _otherRoot = Authority("CN=Other Root CA", otherKey, issuer: null, issuerKey: null);
        _otherSub = Authority("CN=Other Sub CA of the same root", otherSubKey, _root, rootKey);
        _otherServer = Server("invest-public-api.tbank.ru", otherServerKey, _otherSub, otherSubKey);
        _clientOnly = Server("invest-public-api.tbank.ru", clientOnlyKey, _sub, subKey, ClientAuth);
    }

    [TearDown]
    public void TearDown()
    {
        _root.Dispose();
        _sub.Dispose();
        _server.Dispose();
        _otherRoot.Dispose();
        _otherSub.Dispose();
        _otherServer.Dispose();
        _clientOnly.Dispose();
    }

    // After the review: the Sub CA is pinned, not only the root. A server of another Sub CA of the same root, whose chain the server
    // itself presents in the handshake, is refused.
    [Test]
    public void Chain_through_another_sub_ca_of_the_pinned_root_fails()
    {
        new TInvestCertificates(_root, _sub).Accept(_otherServer, [_otherSub], SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
    }

    [Test]
    public void Chain_the_server_presents_through_the_pinned_sub_ca_passes()
    {
        new TInvestCertificates(_root, _sub).Accept(_server, [_sub], SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
    }

    // After the review: the pinned chain is checked for the server authentication usage, as the system trust would.
    [Test]
    public void Certificate_without_the_server_authentication_usage_fails()
    {
        new TInvestCertificates(_root, _sub).Accept(_clientOnly, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
    }

    [Test]
    public void Certificate_the_system_trusts_passes()
    {
        TInvestCertificates.None.Accept(_server, SslPolicyErrors.None).ShouldBeTrue();
    }

    [Test]
    public void Chain_to_the_pinned_root_through_the_pinned_sub_ca_passes()
    {
        new TInvestCertificates(_root, _sub).Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
    }

    [Test]
    public void Chain_to_another_root_fails()
    {
        new TInvestCertificates(_otherRoot, _sub).Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
    }

    [Test]
    public void Name_that_does_not_match_fails_even_on_the_pinned_chain()
    {
        new TInvestCertificates(_root, _sub).Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch).ShouldBeFalse();
    }

    [Test]
    public void Untrusted_chain_without_a_certificate_fails()
    {
        new TInvestCertificates(_root, _sub).Accept(null, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
    }

    [Test]
    public void Thumbprints_with_colons_match_the_files()
    {
        string directory = Folder(_root, _sub);
        var settings = new TInvestCertificateSettings
        {
            Directory = "certs",
            RootThumbprint = string.Join(':', TInvestCertificates.Thumbprint(_root).Chunk(2).Select(pair => new string(pair))).ToLowerInvariant(),
            SubThumbprint = TInvestCertificates.Thumbprint(_sub)
        };

        TInvestCertificates.Load(settings, directory).Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
    }

    [Test]
    public void Missing_folder_of_named_certificates_is_refused()
    {
        var settings = new TInvestCertificateSettings { Directory = "no-such-certs", RootThumbprint = TInvestCertificates.Thumbprint(_root), SubThumbprint = TInvestCertificates.Thumbprint(_sub) };

        Should.Throw<InvalidOperationException>(() => TInvestCertificates.Load(settings, Path.GetTempPath()));
    }

    [Test]
    public void Untrusted_chain_without_pins_fails()
    {
        TInvestCertificates.None.Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
    }

    [Test]
    public void Files_are_loaded_by_their_thumbprints()
    {
        string directory = Folder(_root, _sub);
        var settings = new TInvestCertificateSettings { Directory = "certs", RootThumbprint = TInvestCertificates.Thumbprint(_root), SubThumbprint = TInvestCertificates.Thumbprint(_sub) };

        var loaded = TInvestCertificates.Load(settings, directory);

        loaded.Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeTrue();
    }

    [Test]
    public void File_with_another_thumbprint_is_not_loaded()
    {
        string directory = Folder(_root, _otherRoot);
        var settings = new TInvestCertificateSettings { Directory = "certs", RootThumbprint = TInvestCertificates.Thumbprint(_root), SubThumbprint = TInvestCertificates.Thumbprint(_sub) };

        Should.Throw<InvalidOperationException>(() => TInvestCertificates.Load(settings, directory)).Message.ShouldContain(TInvestCertificates.Thumbprint(_otherRoot));
    }

    [Test]
    public void Missing_file_of_a_named_certificate_is_refused()
    {
        string directory = Folder(_root);
        var settings = new TInvestCertificateSettings { Directory = "certs", RootThumbprint = TInvestCertificates.Thumbprint(_root), SubThumbprint = TInvestCertificates.Thumbprint(_sub) };

        Should.Throw<InvalidOperationException>(() => TInvestCertificates.Load(settings, directory));
    }

    [Test]
    public void Without_thumbprints_nothing_is_pinned()
    {
        var loaded = TInvestCertificates.Load(new TInvestCertificateSettings(), Path.GetTempPath());

        loaded.Accept(_server, SslPolicyErrors.RemoteCertificateChainErrors).ShouldBeFalse();
        loaded.Accept(_server, SslPolicyErrors.None).ShouldBeTrue();
    }

    [Test]
    public void Channel_is_built_for_the_endpoint_without_connecting()
    {
        using var channel = new TInvestChannel(new TInvestSettings { Endpoint = "https://tinvest.invalid:443" }, TInvestCertificates.None);

        channel.Invoker.ShouldNotBeNull();
    }

    private static string Folder(params X509Certificate2[] certificates)
    {
        string root = Path.Combine(Path.GetTempPath(), $"profit-fox-tinvest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "certs"));

        for (var i = 0; i < certificates.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(root, "certs", $"{i}.cer"), certificates[i].Export(X509ContentType.Cert));
        }

        return root;
    }

    private static X509Certificate2 Authority(string name, RSA key, X509Certificate2? issuer, RSA? issuerKey)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return issuer is null
            ? request.CreateSelfSigned(Now, Now.AddYears(10))
            : request.Create(issuer.SubjectName, X509SignatureGenerator.CreateForRSA(issuerKey!, RSASignaturePadding.Pkcs1), Now, Now.AddYears(5), RandomNumberGenerator.GetBytes(16));
    }

    private static readonly Oid ClientAuth = new Oid("1.3.6.1.5.5.7.3.2");

    private static X509Certificate2 Server(string host, RSA key, X509Certificate2 issuer, RSA issuerKey, Oid? usage = null)
    {
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());

        if (usage is not null)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { usage }, false));
        }

        return request.Create(issuer.SubjectName, X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1), Now, Now.AddYears(1), RandomNumberGenerator.GetBytes(16));
    }
}