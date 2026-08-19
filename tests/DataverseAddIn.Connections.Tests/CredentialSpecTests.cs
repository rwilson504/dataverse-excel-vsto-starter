using System.Collections.Generic;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class CredentialSpecTests
    {
        private static CredentialSpec Spec(
            DataverseCloud cloud = DataverseCloud.Commercial,
            DataverseAuthKind kind = DataverseAuthKind.Interactive,
            string clientId = "app-1",
            string tenantId = "tenant-1",
            string principal = "user@contoso.com") =>
            new CredentialSpec(cloud, kind, clientId, tenantId, principal);

        [Fact]
        public void Identical_specs_are_equal_and_hash_alike()
        {
            Assert.Equal(Spec(), Spec());
            Assert.Equal(Spec().GetHashCode(), Spec().GetHashCode());
        }

        [Fact]
        public void Case_and_surrounding_whitespace_do_not_create_a_new_identity()
        {
            var lower = Spec(clientId: "app-1", principal: "user@contoso.com");
            var upper = Spec(clientId: "  APP-1 ", principal: "User@Contoso.com");

            Assert.Equal(lower, upper);
            Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        }

        [Theory]
        [InlineData(DataverseCloud.UsGovernmentHigh, DataverseAuthKind.Interactive, "app-1", "tenant-1", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.ClientSecret, "app-1", "tenant-1", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-2", "tenant-1", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-1", "tenant-2", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-1", "tenant-1", "other@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, null, "tenant-1", "user@contoso.com")]
        public void Any_differing_field_makes_a_different_identity(
            DataverseCloud cloud, DataverseAuthKind kind, string clientId, string tenantId, string principal)
        {
            Assert.NotEqual(Spec(), Spec(cloud, kind, clientId, tenantId, principal));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_fields_normalize_to_null_so_they_compare_alike(string blank)
        {
            var spec = new CredentialSpec(DataverseCloud.Commercial, DataverseAuthKind.Interactive, blank, blank, blank);

            Assert.Null(spec.ClientId);
            Assert.Null(spec.TenantId);
            Assert.Null(spec.Principal);
            Assert.Equal(new CredentialSpec(DataverseCloud.Commercial), spec);
        }

        /// <summary>The whole point of the type: it is the credential cache key.</summary>
        [Fact]
        public void Works_as_a_dictionary_key()
        {
            var map = new Dictionary<CredentialSpec, string>
            {
                [Spec()] = "first",
                [Spec(clientId: "APP-1")] = "second",
                [Spec(kind: DataverseAuthKind.ClientSecret)] = "third"
            };

            Assert.Equal(2, map.Count);
            Assert.Equal("second", map[Spec()]);
        }

        [Fact]
        public void Discovery_specs_are_an_interactive_sign_in_with_host_defaults()
        {
            var spec = CredentialSpec.ForDiscovery(DataverseCloud.UsGovernmentHigh);

            Assert.Equal(DataverseAuthKind.Interactive, spec.Kind);
            Assert.Equal(DataverseCloud.UsGovernmentHigh, spec.Cloud);
            Assert.Null(spec.ClientId);
            Assert.Null(spec.TenantId);
            Assert.Null(spec.Principal);
        }

        [Fact]
        public void Comparing_with_null_is_false_not_an_exception()
        {
            Assert.False(Spec().Equals(null));
            Assert.False(Spec().Equals((object)null));
        }

        /// <summary>
        /// The dictionary tests survive a broken Equals as long as GetHashCode still separates
        /// the values, so the contract between the two has to be asserted directly.
        /// </summary>
        [Fact]
        public void Equality_and_hash_code_never_disagree()
        {
            var specs = new List<CredentialSpec>();

            foreach (var cloud in new[] { DataverseCloud.Commercial, DataverseCloud.UsGovernmentHigh })
                foreach (var kind in new[] { DataverseAuthKind.Interactive, DataverseAuthKind.ClientSecret })
                    foreach (var clientId in new[] { null, "app-1", "APP-1" })
                        foreach (var principal in new[] { null, "user@contoso.com", "USER@CONTOSO.COM" })
                            specs.Add(new CredentialSpec(cloud, kind, clientId, "tenant-1", principal));

            foreach (var left in specs)
                foreach (var right in specs)
                    if (left.Equals(right))
                        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }
    }
}
