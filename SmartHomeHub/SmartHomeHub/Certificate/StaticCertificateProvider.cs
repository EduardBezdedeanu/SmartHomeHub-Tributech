using System.Security.Cryptography.X509Certificates;
using MQTTnet;
public class StaticCertificateProvider : IMqttClientCertificatesProvider
{
    private static StaticCertificateProvider? _instance;
    public static StaticCertificateProvider Instance => _instance ??= new StaticCertificateProvider();

    private X509CertificateCollection? _certs;

    public X509CertificateCollection GetCertificates()
    {
        if (_certs != null)
            return _certs;

        string certPath = Path.Combine(AppContext.BaseDirectory, "cert", AppSecrets.Instance.PfxName);

        var cert = X509CertificateLoader.LoadPkcs12FromFile(
            path: certPath,
            password: AppSecrets.Instance.PfxPassword,
            keyStorageFlags: X509KeyStorageFlags.DefaultKeySet
        );

        _certs = new X509CertificateCollection { cert };
        return _certs;
    }
}