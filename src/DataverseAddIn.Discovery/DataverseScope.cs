using System;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Builds the OAuth scope for a Dataverse resource. The resource is the environment or
    /// discovery root only — Microsoft Entra ID rejects a scope carrying a path or a trailing
    /// slash, and Dataverse hands back URLs with both.
    /// </summary>
    public static class DataverseScope
    {
        /// <summary>
        /// Scope for a public client acting as the signed-in user. Confidential clients need a
        /// different one; see the note on <see cref="DataverseAuthKind.ClientSecret"/>.
        /// </summary>
        public static string Delegated(string resourceUrl) => Resource(resourceUrl) + "/user_impersonation";

        /// <summary>
        /// Scope for a confidential client acting as itself. Microsoft's guidance is explicit:
        /// public clients use <c>/user_impersonation</c>, confidential clients use
        /// <c>/.default</c>. Sending the delegated scope on a client-credentials request fails.
        /// </summary>
        public static string Application(string resourceUrl) => Resource(resourceUrl) + "/.default";

        /// <summary>Scheme, host and port of the resource, with nothing after it.</summary>
        public static string Resource(string resourceUrl)
        {
            if (string.IsNullOrWhiteSpace(resourceUrl))
                throw new ArgumentException("Resource URL is required.", nameof(resourceUrl));

            if (!Uri.TryCreate(resourceUrl, UriKind.Absolute, out var uri))
                throw new ArgumentException($"'{resourceUrl}' is not an absolute URL.", nameof(resourceUrl));

            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
}
