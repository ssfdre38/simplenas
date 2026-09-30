using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Certes;
using Certes.Acme;

namespace SimpleNAS;

public record SslConfig(
    bool Enabled,
    int HttpsPort,
    string CertType,
    string? Domain,
    string? Email,
    string? PfxPath,
    string? PfxPassword,
    DateTime? ExpiresAt,
    string? Issuer,
    string? Subject
);

public static class SslManager
{
    private static readonly string ConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "ssl_config.json");
    private static readonly string PfxPath = Path.Combine(Directory.GetCurrentDirectory(), "simplenas_cert.pfx");
    
    // In-memory challenge storage for ACME HTTP-01 validation
    public static readonly Dictionary<string, string> AcmeChallenges = new();

    public static SslConfig GetConfig()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<SslConfig>(json);
                if (config != null) return config;
            }
            catch { }
        }

        return new SslConfig(
            Enabled: File.Exists(PfxPath),
            HttpsPort: 8443,
            CertType: File.Exists(PfxPath) ? "Detected" : "None",
            Domain: null,
            Email: null,
            PfxPath: File.Exists(PfxPath) ? PfxPath : null,
            PfxPassword: null,
            ExpiresAt: null,
            Issuer: null,
            Subject: null
        );
    }

    public static void SaveConfig(SslConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    public static object GetStatus()
    {
        var config = GetConfig();
        var certFileExists = File.Exists(PfxPath);

        X509Certificate2? cert = null;
        if (certFileExists)
        {
            try
            {
                cert = X509CertificateLoader.LoadPkcs12FromFile(PfxPath, config.PfxPassword);
            }
            catch { }
        }

        int? daysRemaining = null;
        DateTime? expiresAt = cert?.NotAfter;
        if (expiresAt.HasValue)
        {
            daysRemaining = (int)Math.Max(0, (expiresAt.Value - DateTime.UtcNow).TotalDays);
        }

        return new
        {
            enabled = config.Enabled && certFileExists,
            httpsPort = config.HttpsPort,
            certType = config.CertType,
            domain = config.Domain ?? (cert?.GetNameInfo(X509NameType.DnsName, false) ?? "localhost"),
            email = config.Email ?? "",
            certExists = certFileExists,
            expiresAt = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss UTC"),
            daysRemaining = daysRemaining,
            issuer = cert?.Issuer ?? config.Issuer ?? "N/A",
            subject = cert?.Subject ?? config.Subject ?? "N/A",
            thumbprint = cert?.Thumbprint ?? ""
        };
    }

    public static async Task<(bool Success, string Message)> RequestLetsEncryptCertificate(string domain, string email, bool staging = false)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(email))
        {
            return (false, "Domain and email are required.");
        }

        try
        {
            var serverUri = staging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;
            var acme = new AcmeContext(serverUri);

            // Create account
            await acme.NewAccount(email, termsOfServiceAgreed: true);

            // Order certificate
            var order = await acme.NewOrder(new[] { domain });
            var authzList = await order.Authorizations();
            var authz = authzList.FirstOrDefault();
            if (authz == null)
            {
                return (false, "Could not acquire ACME authorization for domain.");
            }

            // Get HTTP-01 challenge
            var httpChallenge = await authz.Http();
            if (httpChallenge == null)
            {
                return (false, "HTTP-01 challenge is not available for this domain.");
            }

            var token = httpChallenge.Token;
            var keyAuth = httpChallenge.KeyAuthz;

            // Register challenge in memory for /.well-known/acme-challenge/{token}
            AcmeChallenges[token] = keyAuth;

            // Notify ACME server that challenge is ready
            await httpChallenge.Validate();

            // Poll order status until ready or invalid (up to 60 seconds)
            var attempts = 0;
            while (attempts++ < 30)
            {
                await Task.Delay(2000);
                var currentOrder = await order.Resource();
                if (currentOrder.Status == Certes.Acme.Resource.OrderStatus.Ready ||
                    currentOrder.Status == Certes.Acme.Resource.OrderStatus.Valid)
                {
                    break;
                }
                if (currentOrder.Status == Certes.Acme.Resource.OrderStatus.Invalid)
                {
                    AcmeChallenges.Remove(token);
                    return (false, "Let's Encrypt validation failed. Ensure port 80/8000 is open and DNS points to this server.");
                }
            }

            AcmeChallenges.Remove(token);

            // Generate private key and finalize order
            var certKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
            await order.Finalize(new CsrInfo
            {
                CommonName = domain
            }, certKey);

            // Download certificate chain
            var certChain = await order.Download();
            var pfxPassword = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Substring(0, 16);
            var pfxBuilder = certChain.ToPfx(certKey);
            var pfxBytes = pfxBuilder.Build(domain, pfxPassword);

            await File.WriteAllBytesAsync(PfxPath, pfxBytes);

            var newConfig = new SslConfig(
                Enabled: true,
                HttpsPort: 8443,
                CertType: staging ? "LetsEncrypt-Staging" : "LetsEncrypt",
                Domain: domain,
                Email: email,
                PfxPath: PfxPath,
                PfxPassword: pfxPassword,
                ExpiresAt: DateTime.UtcNow.AddDays(90),
                Issuer: staging ? "Let's Encrypt Staging" : "Let's Encrypt Authority",
                Subject: $"CN={domain}"
            );

            SaveConfig(newConfig);
            return (true, $"Let's Encrypt SSL certificate for '{domain}' generated and installed successfully! Restart or bind HTTPS port 8443 to activate.");
        }
        catch (Exception ex)
        {
            return (false, $"Let's Encrypt error: {ex.Message}");
        }
    }

    public static (bool Success, string Message) GenerateSelfSignedCertificate(string domainName = "localhost")
    {
        try
        {
            domainName = string.IsNullOrWhiteSpace(domainName) ? "localhost" : domainName.Trim();
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest($"CN={domainName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(domainName);
            if (domainName != "localhost") sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);

            // Add local IPv4 addresses
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList.Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    sanBuilder.AddIpAddress(ip);
                }
            }
            catch { }

            req.CertificateExtensions.Add(sanBuilder.Build());
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
            req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

            var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            var pfxPassword = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Substring(0, 16);
            var pfxBytes = cert.Export(X509ContentType.Pfx, pfxPassword);

            File.WriteAllBytes(PfxPath, pfxBytes);

            var newConfig = new SslConfig(
                Enabled: true,
                HttpsPort: 8443,
                CertType: "SelfSigned",
                Domain: domainName,
                Email: null,
                PfxPath: PfxPath,
                PfxPassword: pfxPassword,
                ExpiresAt: DateTime.UtcNow.AddYears(2),
                Issuer: $"CN={domainName} (Self-Signed)",
                Subject: $"CN={domainName}"
            );

            SaveConfig(newConfig);
            return (true, $"Self-signed SSL certificate generated successfully for '{domainName}' (valid for 2 years).");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to generate self-signed certificate: {ex.Message}");
        }
    }
}
