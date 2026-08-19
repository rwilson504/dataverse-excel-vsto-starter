using System;
using System.Collections.Generic;
using System.Linq;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// What an authentication kind is called, what it needs, and how it behaves. UI reads this
    /// rather than hard-coding a case per kind.
    /// </summary>
    /// <remarks>
    /// Only kinds with a working credential implementation are registered, so
    /// <see cref="Supported"/> is safe to bind straight to a picker.
    /// </remarks>
    public sealed class AuthKindDescriptor
    {
        private AuthKindDescriptor(
            DataverseAuthKind kind,
            string displayName,
            string description,
            AuthField requiredFields,
            AuthField optionalFields,
            bool isInteractive,
            bool supportsGlobalDiscovery,
            string warning = null)
        {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
            RequiredFields = requiredFields;
            OptionalFields = optionalFields;
            IsInteractive = isInteractive;
            SupportsGlobalDiscovery = supportsGlobalDiscovery;
            Warning = warning;
        }

        public DataverseAuthKind Kind { get; }

        public string DisplayName { get; }

        public string Description { get; }

        /// <summary>A caveat worth showing next to the choice, or null when there is none.</summary>
        public string Warning { get; }

        public AuthField RequiredFields { get; }

        public AuthField OptionalFields { get; }

        /// <summary>Mirrors <see cref="IDataverseTokenSource.IsInteractive"/> before a credential exists.</summary>
        public bool IsInteractive { get; }

        /// <summary>Mirrors <see cref="IDataverseTokenSource.SupportsGlobalDiscovery"/> before a credential exists.</summary>
        public bool SupportsGlobalDiscovery { get; }

        private static readonly Dictionary<DataverseAuthKind, AuthKindDescriptor> Registry =
            new[]
            {
                new AuthKindDescriptor(
                    DataverseAuthKind.Interactive,
                    "Sign in interactively",
                    "Opens your browser to sign in with a work or school account. Supports MFA and passwordless sign-in.",
                    requiredFields: AuthField.None,
                    optionalFields: AuthField.ClientId | AuthField.TenantId | AuthField.UserName,
                    isInteractive: true,
                    supportsGlobalDiscovery: true),

                new AuthKindDescriptor(
                    DataverseAuthKind.ClientSecret,
                    "Application user (client secret)",
                    "Connects as a registered application rather than a person. The app registration needs a matching application user with a security role in the environment.",
                    requiredFields: AuthField.ClientId | AuthField.TenantId | AuthField.ClientSecret,
                    optionalFields: AuthField.None,
                    isInteractive: false,
                    supportsGlobalDiscovery: false,
                    warning: "Cannot list environments — enter the environment URL. Needs a specific tenant, not 'organizations'.")
            }
            .ToDictionary(d => d.Kind);

        /// <summary>Kinds that can actually be used, in the order they should be offered.</summary>
        public static IReadOnlyList<AuthKindDescriptor> Supported { get; } =
            Registry.Values.OrderBy(d => d.Kind).ToList();

        public static bool TryGet(DataverseAuthKind kind, out AuthKindDescriptor descriptor) =>
            Registry.TryGetValue(kind, out descriptor);

        public static AuthKindDescriptor For(DataverseAuthKind kind)
        {
            if (Registry.TryGetValue(kind, out var descriptor))
                return descriptor;

            throw new NotSupportedException(
                $"Authentication kind '{kind}' is not supported yet. Supported kinds: " +
                string.Join(", ", Supported.Select(d => d.Kind.ToString())) + ".");
        }

        public override string ToString() => DisplayName;
    }
}
