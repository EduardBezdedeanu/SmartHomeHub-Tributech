using System.Security.Cryptography;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Xunit;
using SmartHomeHub;



namespace SmartHomeHub.Tests
{
    public class CertificateTests
    {
        [Fact]
      
        public void StaticCertificateProvider_Instance_Should_Not_Be_Null()
        {
            var instance = StaticCertificateProvider.Instance;

            Assert.NotNull(instance);
            Assert.IsType<StaticCertificateProvider>(instance);
        }
            
        [Fact]
        public void GetCertificates_Should_Load_Valid_Certificate()
        {
            // Arrange – load secrets.json from test directory
            var path = Path.Combine(AppContext.BaseDirectory, "config", "secrets.json");
            AppSecrets.Load(path);

            var provider = StaticCertificateProvider.Instance;

            // Call GetCertificates method
            var certs = provider.GetCertificates();

            // Assert
            Assert.NotNull(certs);
            Assert.NotEmpty(certs);
            Assert.IsType<X509CertificateCollection>(certs);

            var cert = certs[0];
            Assert.False(string.IsNullOrWhiteSpace(cert.Subject));            
        }        
    }
}


