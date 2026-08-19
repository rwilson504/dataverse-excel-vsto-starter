using System;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// A normalized pointer to one Dataverse environment, from either a discovery result or
    /// a URL the user typed.
    /// </summary>
    public sealed class DataverseEnvironmentReference
    {
        private DataverseEnvironmentReference(string url, DataverseCloud cloud, bool cloudWasRecognized)
        {
            Url = url;
            Cloud = cloud;
            CloudWasRecognized = cloudWasRecognized;
        }

        /// <summary>Scheme and host only, no trailing slash. Also the OAuth resource.</summary>
        public string Url { get; }

        public DataverseCloud Cloud { get; }

        /// <summary>
        /// False when the host matched no known Dataverse suffix and <see cref="Cloud"/> fell
        /// back to <see cref="DataverseCloud.Commercial"/> — likely a typo or a vanity domain.
        /// </summary>
        public bool CloudWasRecognized { get; }

        public static DataverseEnvironmentReference FromInstance(DataverseInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            var url = instance.ApiUrl ?? instance.Url;

            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("The instance has neither an ApiUrl nor a Url.", nameof(instance));

            return new DataverseEnvironmentReference(Normalize(url), instance.Cloud, true);
        }

        public static DataverseEnvironmentReference Parse(string url)
        {
            if (!TryParse(url, out var reference, out var error))
                throw new FormatException(error);

            return reference;
        }

        /// <summary>
        /// Accepts what a user is likely to paste: with or without scheme, with a trailing
        /// slash, or with a path such as /main.aspx copied out of the browser.
        /// </summary>
        public static bool TryParse(string url, out DataverseEnvironmentReference reference, out string error)
        {
            reference = null;
            error = null;

            if (string.IsNullOrWhiteSpace(url))
            {
                error = "Enter an environment URL, for example https://contoso.crm.dynamics.com.";
                return false;
            }

            var candidate = url.Trim();

            if (candidate.IndexOf("://", StringComparison.Ordinal) < 0)
                candidate = "https://" + candidate;

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                error = $"'{url}' is not a valid URL.";
                return false;
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Dataverse online environments must use https.";
                return false;
            }

            var recognized = DataverseCloudExtensions.TryGetCloudFromUrl(uri.AbsoluteUri, out var cloud);

            reference = new DataverseEnvironmentReference(
                uri.GetLeftPart(UriPartial.Authority), cloud, recognized);

            return true;
        }

        private static string Normalize(string url) =>
            new Uri(url, UriKind.Absolute).GetLeftPart(UriPartial.Authority);

        public override string ToString() => Url;
    }
}
