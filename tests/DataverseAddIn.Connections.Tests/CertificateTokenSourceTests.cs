using System;
using System.Security.Cryptography.X509Certificates;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// Covers the parts that do not need a real certificate. The constructor guards — private
    /// key present, single tenant — are deliberately uncovered: building a certificate in
    /// memory needs <c>CertificateRequest</c>, which .NET Framework 4.6.2 does not have, and
    /// committing a test PFX would put a private key in the repository for the sake of a test.
    /// </summary>
    public class CertificateTokenSourceTests
    {
        /// <summary>
        /// Thumbprints get pasted out of certmgr with spaces, lower case, and an invisible
        /// left-to-right mark on the front. The certificate store matches none of those, and the
        /// resulting "not found" sends people hunting the wrong problem.
        /// </summary>
        [Theory]
        [InlineData("DC6C689022C905EA5F812B51F1574ED10F256FF6")]
        [InlineData("dc6c689022c905ea5f812b51f1574ed10f256ff6")]
        [InlineData("dc 6c 68 90 22 c9 05 ea 5f 81 2b 51 f1 57 4e d1 0f 25 6f f6")]
        [InlineData("\u200edc6c689022c905ea5f812b51f1574ed10f256ff6")]
        [InlineData("DC6C-6890-22C9-05EA-5F81-2B51-F157-4ED1-0F25-6FF6")]
        public void Thumbprints_normalize_to_what_the_store_matches(string pasted)
        {
            Assert.Equal("DC6C689022C905EA5F812B51F1574ED10F256FF6", CertificateTokenSource.Normalize(pasted));
        }

        [Fact]
        public void Normalizing_drops_anything_that_is_not_hex()
        {
            Assert.Equal(string.Empty, CertificateTokenSource.Normalize("   "));
            Assert.Equal("ABCDEF", CertificateTokenSource.Normalize("a-b c:d/e_f"));
        }

        /// <summary>The error has to say where it looked, or the user cannot tell what to fix.</summary>
        [Fact]
        public void A_missing_certificate_names_the_stores_it_searched()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => CertificateTokenSource.Find("DC6C689022C905EA5F812B51F1574ED10F256FF6"));

            Assert.Contains("CurrentUser", error.Message);
            Assert.Contains("LocalMachine", error.Message);
            Assert.Contains("DC6C689022C905EA5F812B51F1574ED10F256FF6", error.Message);
        }

        [Fact]
        public void A_scoped_search_reports_only_the_store_it_was_told_to_use()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => CertificateTokenSource.Find(
                    "DC6C689022C905EA5F812B51F1574ED10F256FF6", StoreLocation.CurrentUser));

            Assert.Contains("CurrentUser", error.Message);
            Assert.DoesNotContain("LocalMachine", error.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_thumbprint_is_rejected(string thumbprint)
        {
            Assert.Throws<ArgumentException>(() => CertificateTokenSource.Find(thumbprint));
        }

        /// <summary>A pasted thumbprint must search for the same certificate as a clean one.</summary>
        [Fact]
        public void Find_normalizes_before_searching()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => CertificateTokenSource.Find("dc 6c 68 90 22 c9 05 ea 5f 81 2b 51 f1 57 4e d1 0f 25 6f f6"));

            Assert.Contains("DC6C689022C905EA5F812B51F1574ED10F256FF6", error.Message);
        }

        /// <summary>
        /// The picker list must never include a certificate that cannot authenticate. A machine
        /// that has run a debugging proxy accumulates hundreds of expired interception
        /// certificates, all with private keys — this filter is the difference between a usable
        /// picker and a thousand-row one.
        /// </summary>
        [Fact]
        public void Only_currently_valid_certificates_with_a_private_key_are_offered()
        {
            var now = DateTime.Now;

            foreach (var certificate in CertificateTokenSource.FindUsable())
            {
                Assert.True(certificate.HasPrivateKey, $"{certificate.Thumbprint} has no private key.");
                Assert.True(certificate.NotAfter > now, $"{certificate.Thumbprint} expired {certificate.NotAfter:d}.");
                Assert.True(certificate.NotBefore <= now, $"{certificate.Thumbprint} is not valid yet.");
            }
        }

        /// <summary>Freshest first, so the one a user just installed is at the top.</summary>
        [Fact]
        public void The_offered_certificates_are_sorted_by_expiry()
        {
            var offered = CertificateTokenSource.FindUsable();

            for (var i = 1; i < offered.Count; i++)
                Assert.True(offered[i - 1].NotAfter >= offered[i].NotAfter, "Certificates are not sorted by expiry.");
        }

        [Fact]
        public void Describe_rejects_a_null_certificate()
        {
            Assert.Throws<ArgumentNullException>(() => CertificateTokenSource.Describe(null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not hex at all")]
        public void TryFind_reports_rather_than_throwing(string thumbprint)
        {
            Assert.False(CertificateTokenSource.TryFind(thumbprint, out var certificate));
            Assert.Null(certificate);
        }

        /// <summary>A certificate connection carries the thumbprint as its identity, not a user name.</summary>
        [Fact]
        public void Certificate_profiles_are_identified_by_thumbprint()        {
            var profile = new ConnectionProfile
            {
                CloudName = "Commercial",
                AuthKindName = "Certificate",
                ClientId = "app-1",
                TenantId = "contoso.onmicrosoft.com",
                CertificateThumbprint = "DC6C689022C905EA5F812B51F1574ED10F256FF6"
            };

            Assert.Equal("DC6C689022C905EA5F812B51F1574ED10F256FF6", profile.ToCredentialSpec().Principal);
        }

        /// <summary>Certificate connections keep nothing in the secret store, so nothing can leak from it.</summary>
        [Fact]
        public void Choosing_a_certificate_discards_any_stored_secret()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(dir.File("connections.json")), secrets);

                var profile = manager.Add(null, "contoso.crm.dynamics.com", null, new ConnectionAuthentication
                {
                    Kind = DataverseAuthKind.ClientSecret,
                    ClientId = "app-1",
                    TenantId = "contoso.onmicrosoft.com",
                    ClientSecret = "the-secret"
                });

                var reference = profile.SecretRef;

                manager.UpdateAuthentication(profile, new ConnectionAuthentication
                {
                    Kind = DataverseAuthKind.Certificate,
                    ClientId = "app-1",
                    TenantId = "contoso.onmicrosoft.com",
                    CertificateThumbprint = "DC6C689022C905EA5F812B51F1574ED10F256FF6"
                });

                Assert.Equal(DataverseAuthKind.Certificate, profile.AuthKind);
                Assert.Null(profile.SecretRef);
                Assert.Null(secrets.Read(reference));
            }
        }
    }
}
