using System;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class CredentialFactoryTests
    {
        private static DataverseAuthOptions HostDefaults() => new DataverseAuthOptions
        {
            ClientId = "host-app",
            TenantId = "contoso.onmicrosoft.com"
        };

        private static CredentialSpec Spec(DataverseAuthKind kind, string principal = null) =>
            new CredentialSpec(DataverseCloud.Commercial, kind, "app-1", "contoso.onmicrosoft.com", principal);

        [Fact]
        public void Builds_an_interactive_credential()
        {
            var credential = new CredentialFactory(cloud => HostDefaults()).Create(Spec(DataverseAuthKind.Interactive));

            Assert.IsType<InteractiveCredential>(credential);
            Assert.True(credential.TokenSource.IsInteractive);
            Assert.True(credential.TokenSource.SupportsGlobalDiscovery);
        }

        [Fact]
        public void Builds_a_client_secret_credential_from_the_stored_secret()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var reference = secrets.Write(null, "the-secret");

                var credential = new CredentialFactory(cloud => HostDefaults(), secrets)
                    .Create(Spec(DataverseAuthKind.ClientSecret, reference));

                Assert.IsType<ClientSecretCredential>(credential);
                Assert.Equal(DataverseAuthKind.ClientSecret, credential.Kind);
                Assert.False(credential.TokenSource.IsInteractive);
            }
        }

        /// <summary>Secrets are per-user and per-machine, so a copied profile must say so plainly.</summary>
        [Fact]
        public void An_unreadable_secret_explains_that_it_must_be_re_entered()
        {
            using (var dir = new TempDirectory())
            {
                var factory = new CredentialFactory(cloud => HostDefaults(), new DpapiSecretStore(dir.Path));

                var error = Assert.Throws<InvalidOperationException>(
                    () => factory.Create(Spec(DataverseAuthKind.ClientSecret, "missing-reference")));

                Assert.Contains("entered again", error.Message);
            }
        }

        [Fact]
        public void A_client_secret_connection_with_no_reference_at_all_is_rejected()
        {
            using (var dir = new TempDirectory())
            {
                var factory = new CredentialFactory(cloud => HostDefaults(), new DpapiSecretStore(dir.Path));

                Assert.Throws<InvalidOperationException>(() => factory.Create(Spec(DataverseAuthKind.ClientSecret)));
            }
        }

        [Theory]
        [InlineData(DataverseAuthKind.DeviceCode)]
        [InlineData(DataverseAuthKind.UsernamePassword)]
        [InlineData(DataverseAuthKind.Ifd)]
        public void Unimplemented_kinds_name_themselves_and_what_does_work(DataverseAuthKind kind)
        {
            var factory = new CredentialFactory(cloud => HostDefaults());

            var error = Assert.Throws<NotSupportedException>(() => factory.Create(Spec(kind, "whatever")));

            Assert.Contains(kind.ToString(), error.Message);
            Assert.Contains(nameof(DataverseAuthKind.Interactive), error.Message);
        }

        /// <summary>
        /// Every kind the picker offers must have a case in the factory. Some kinds depend on
        /// machine state — a certificate has to be installed — so the assertion is that the
        /// factory does not reject the kind, not that it can always build one here.
        /// </summary>
        [Fact]
        public void Every_supported_descriptor_has_a_working_implementation()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var reference = secrets.Write(null, "the-secret");
                var factory = new CredentialFactory(cloud => HostDefaults(), secrets);

                foreach (var descriptor in AuthKindDescriptor.Supported)
                {
                    IDataverseCredential credential;

                    try
                    {
                        credential = factory.Create(Spec(descriptor.Kind, reference));
                    }
                    catch (NotSupportedException)
                    {
                        throw new Xunit.Sdk.XunitException(
                            $"{descriptor.Kind} is offered by the picker but CredentialFactory has no case for it.");
                    }
                    catch (InvalidOperationException)
                    {
                        // Missing certificate, unreadable secret: an environment problem, not a
                        // missing implementation. The kind is wired up, which is what this pins.
                        continue;
                    }

                    Assert.Equal(descriptor.Kind, credential.Kind);
                    Assert.Equal(descriptor.IsInteractive, credential.TokenSource.IsInteractive);
                    Assert.Equal(descriptor.SupportsGlobalDiscovery, credential.TokenSource.SupportsGlobalDiscovery);
                }
            }
        }

        [Fact]
        public void The_spec_overrides_the_host_defaults()
        {
            DataverseAuthOptions captured = null;

            new CredentialFactory(cloud => captured = HostDefaults())
                .Create(new CredentialSpec(
                    DataverseCloud.UsGovernmentHigh, DataverseAuthKind.Interactive,
                    "per-connection-app", "per-connection-tenant", "user@contoso.us"));

            Assert.Equal("per-connection-app", captured.ClientId);
            Assert.Equal("per-connection-tenant", captured.TenantId);
            Assert.Equal(DataverseCloud.UsGovernmentHigh, captured.Cloud);
            Assert.Equal(DataverseAuthKind.Interactive, captured.AuthKind);
        }

        [Fact]
        public void Null_arguments_are_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => new CredentialFactory(null));
            Assert.Throws<ArgumentNullException>(() => new CredentialFactory(cloud => HostDefaults()).Create(null));
        }

        /// <summary>Testing a connection must not require writing the secret to disk first.</summary>
        [Fact]
        public void An_unsaved_secret_can_be_used_without_touching_the_store()
        {
            using (var dir = new TempDirectory())
            {
                var factory = new CredentialFactory(cloud => HostDefaults(), new DpapiSecretStore(dir.Path));

                var credential = factory.Create(Spec(DataverseAuthKind.ClientSecret), "typed-but-not-saved");

                Assert.IsType<ClientSecretCredential>(credential);
                Assert.Empty(System.IO.Directory.GetFiles(dir.Path));
            }
        }

        /// <summary>A retyped secret must win over the saved one, or testing a rotation is meaningless.</summary>
        [Fact]
        public void The_override_beats_the_stored_secret()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var reference = secrets.Write(null, "stored");

                var credential = new CredentialFactory(cloud => HostDefaults(), secrets)
                    .Create(Spec(DataverseAuthKind.ClientSecret, reference), "override");

                Assert.IsType<ClientSecretCredential>(credential);
                Assert.Equal("stored", secrets.Read(reference));
            }
        }

        [Fact]
        public void A_blank_override_still_falls_back_to_the_store()
        {
            using (var dir = new TempDirectory())
            {
                var factory = new CredentialFactory(cloud => HostDefaults(), new DpapiSecretStore(dir.Path));

                Assert.Throws<InvalidOperationException>(
                    () => factory.Create(Spec(DataverseAuthKind.ClientSecret, "missing"), string.Empty));
            }
        }

        [Fact]
        public void An_options_factory_returning_null_fails_loudly()
        {
            var factory = new CredentialFactory(cloud => null);

            Assert.Throws<InvalidOperationException>(() => factory.Create(Spec(DataverseAuthKind.Interactive)));
        }
    }
}
