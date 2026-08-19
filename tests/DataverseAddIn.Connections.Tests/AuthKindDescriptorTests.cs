using System;
using System.Linq;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class AuthKindDescriptorTests
    {
        [Fact]
        public void Interactive_is_supported_and_describes_itself()
        {
            var descriptor = AuthKindDescriptor.For(DataverseAuthKind.Interactive);

            Assert.True(descriptor.IsInteractive);
            Assert.True(descriptor.SupportsGlobalDiscovery);
            Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        }

        /// <summary>A picker binds straight to Supported, so a listed kind must be usable.</summary>
        [Fact]
        public void Every_supported_kind_resolves_and_is_listed_once()
        {
            Assert.NotEmpty(AuthKindDescriptor.Supported);

            foreach (var descriptor in AuthKindDescriptor.Supported)
                Assert.Same(descriptor, AuthKindDescriptor.For(descriptor.Kind));

            Assert.Equal(
                AuthKindDescriptor.Supported.Count,
                AuthKindDescriptor.Supported.Select(d => d.Kind).Distinct().Count());
        }

        [Fact]
        public void Supported_kinds_are_offered_in_enum_order()
        {
            var kinds = AuthKindDescriptor.Supported.Select(d => d.Kind).ToList();

            Assert.Equal(kinds.OrderBy(k => k).ToList(), kinds);
        }

        [Fact]
        public void Every_descriptor_is_presentable()
        {
            foreach (var descriptor in AuthKindDescriptor.Supported)
            {
                Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
                Assert.Equal(descriptor.DisplayName, descriptor.ToString());

                // A warning is optional, but an empty one would render as a blank line.
                Assert.True(descriptor.Warning == null || descriptor.Warning.Trim().Length > 0);
            }
        }

        [Fact]
        public void An_unimplemented_kind_says_so_and_lists_what_does_work()
        {
            var error = Assert.Throws<NotSupportedException>(
                () => AuthKindDescriptor.For(DataverseAuthKind.Ifd));

            Assert.Contains("Ifd", error.Message);
            Assert.Contains(nameof(DataverseAuthKind.Interactive), error.Message);
        }

        [Fact]
        public void TryGet_reports_rather_than_throws()
        {
            Assert.True(AuthKindDescriptor.TryGet(DataverseAuthKind.Interactive, out var supported));
            Assert.NotNull(supported);

            Assert.False(AuthKindDescriptor.TryGet(DataverseAuthKind.Certificate, out var missing));
            Assert.Null(missing);
        }

        /// <summary>Every kind the enum declares must either be supported or fail cleanly.</summary>
        [Fact]
        public void No_declared_kind_blows_up_the_lookup()
        {
            foreach (DataverseAuthKind kind in Enum.GetValues(typeof(DataverseAuthKind)))
            {
                if (AuthKindDescriptor.TryGet(kind, out _)) continue;

                Assert.Throws<NotSupportedException>(() => AuthKindDescriptor.For(kind));
            }
        }
    }
}
