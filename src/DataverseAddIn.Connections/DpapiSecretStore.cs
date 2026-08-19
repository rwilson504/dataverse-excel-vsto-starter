using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Holds connection secrets outside the saved-connections file. Profiles store only a
    /// reference, never the secret itself.
    /// </summary>
    public interface ISecretStore
    {
        /// <summary>Writes a secret and returns its reference, creating one when none is supplied.</summary>
        string Write(string reference, string secret);

        /// <summary>The secret, or null when the reference is unknown or unreadable.</summary>
        string Read(string reference);

        void Delete(string reference);
    }

    /// <summary>
    /// DPAPI-encrypted files under the local (non-roaming) profile, scoped to the current user.
    /// </summary>
    /// <remarks>
    /// Deliberately not the shared-passphrase scheme used by some Dataverse tooling: with a
    /// passphrase compiled into an open-source assembly, a copied secrets file is a decrypt.
    /// DPAPI ties the ciphertext to the Windows user, so copying the file elsewhere yields
    /// nothing. It also means secrets do not survive a move to another machine or account,
    /// which is the correct trade-off for a desktop add-in.
    /// </remarks>
    public sealed class DpapiSecretStore : ISecretStore
    {
        private static readonly byte[] Entropy = { 0x44, 0x56, 0x53, 0x45, 0x43, 0x52, 0x54 };

        // References become file names, so anything that could escape the directory is rejected.
        private static readonly Regex SafeReference = new Regex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

        private readonly string _directory;

        public DpapiSecretStore(string directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DataverseDiscovery",
                "secrets");
        }

        public string Write(string reference, string secret)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));

            reference = string.IsNullOrWhiteSpace(reference)
                ? Guid.NewGuid().ToString("N")
                : Validate(reference);

            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(PathFor(reference), encrypted);

            return reference;
        }

        public string Read(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;

            var path = PathFor(Validate(reference));
            if (!File.Exists(path)) return null;

            try
            {
                var plaintext = ProtectedData.Unprotect(
                    File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException)
            {
                // Written by another user or machine, or corrupted. Treat as absent so the
                // caller re-prompts rather than failing with a crypto error.
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Delete(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return;

            try { File.Delete(PathFor(Validate(reference))); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private string PathFor(string reference) => Path.Combine(_directory, reference + ".bin");

        private static string Validate(string reference)
        {
            if (!SafeReference.IsMatch(reference))
                throw new ArgumentException($"'{reference}' is not a valid secret reference.", nameof(reference));

            return reference;
        }
    }
}
