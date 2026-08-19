using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Identity.Client;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Persists the MSAL token cache to a DPAPI-encrypted file so the user is not prompted
    /// on every Excel session.
    /// </summary>
    /// <remarks>
    /// Deliberately dependency-free. <c>Microsoft.Identity.Client.Extensions.Msal</c> offers the
    /// same thing with cross-process locking, but it drags extra assemblies into the Office
    /// add-in load context, which is a common source of binding-redirect failures in VSTO.
    /// </remarks>
    internal sealed class DpapiTokenCache
    {
        private static readonly object FileLock = new object();
        private static readonly byte[] Entropy = { 0x44, 0x56, 0x44, 0x53, 0x43, 0x56, 0x31 };

        private readonly string _cacheFilePath;

        public DpapiTokenCache(string cacheFilePath)
        {
            if (string.IsNullOrWhiteSpace(cacheFilePath))
                throw new ArgumentException("Cache file path is required.", nameof(cacheFilePath));

            _cacheFilePath = cacheFilePath;
        }

        public void Attach(ITokenCache tokenCache)
        {
            if (tokenCache == null) throw new ArgumentNullException(nameof(tokenCache));

            tokenCache.SetBeforeAccess(OnBeforeAccess);
            tokenCache.SetAfterAccess(OnAfterAccess);
        }

        private void OnBeforeAccess(TokenCacheNotificationArgs args)
        {
            lock (FileLock)
            {
                byte[] data = null;

                if (File.Exists(_cacheFilePath))
                {
                    try
                    {
                        data = ProtectedData.Unprotect(
                            File.ReadAllBytes(_cacheFilePath), Entropy, DataProtectionScope.CurrentUser);
                    }
                    catch (CryptographicException)
                    {
                        // Cache written by another user/machine or corrupted: start clean.
                        TryDelete();
                    }
                }

                args.TokenCache.DeserializeMsalV3(data);
            }
        }

        private void OnAfterAccess(TokenCacheNotificationArgs args)
        {
            if (!args.HasStateChanged) return;

            lock (FileLock)
            {
                var encrypted = ProtectedData.Protect(
                    args.TokenCache.SerializeMsalV3(), Entropy, DataProtectionScope.CurrentUser);

                Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath));
                File.WriteAllBytes(_cacheFilePath, encrypted);
            }
        }

        private void TryDelete()
        {
            try { File.Delete(_cacheFilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
