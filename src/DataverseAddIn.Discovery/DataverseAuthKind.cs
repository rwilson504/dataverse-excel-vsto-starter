using System;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// The ways a connection can authenticate to Dataverse. Persisted by name, so entries may
    /// be added but must not be renamed or reordered.
    /// </summary>
    public enum DataverseAuthKind
    {
        /// <summary>Signs a user in through the system browser. The default for a desktop add-in.</summary>
        Interactive = 0,

        /// <summary>
        /// Shows a code for the user to enter on another device. For remote sessions and locked
        /// down desktops where the loopback redirect or the system browser is unavailable.
        /// </summary>
        DeviceCode = 1,

        /// <summary>Service principal with an application secret. No signed-in user.</summary>
        ClientSecret = 2,

        /// <summary>Service principal with an X.509 certificate. No signed-in user.</summary>
        Certificate = 3,

        /// <summary>Resource owner password credentials. Fails under MFA and most Conditional Access policies.</summary>
        UsernamePassword = 4,

        /// <summary>A token supplied by the host, for example from the Azure CLI or a managed identity.</summary>
        ExternalToken = 5,

        /// <summary>A raw <c>ServiceClient</c> connection string, as the escape hatch.</summary>
        ConnectionString = 6,

        /// <summary>On-premises Active Directory. No bearer token.</summary>
        WindowsIntegrated = 7,

        /// <summary>On-premises internet-facing deployment. No bearer token.</summary>
        Ifd = 8
    }

    /// <summary>
    /// The inputs an authentication kind needs from the user. Lets the connection editor render
    /// itself from <see cref="AuthKindDescriptor"/> instead of switching on the kind.
    /// </summary>
    [Flags]
    public enum AuthField
    {
        None = 0,
        ClientId = 1,
        TenantId = 2,
        UserName = 4,
        Password = 8,
        ClientSecret = 16,
        CertificateThumbprint = 32,
        ConnectionString = 64,
        Domain = 128
    }
}
