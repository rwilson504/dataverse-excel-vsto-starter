using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Persists saved connections as JSON under the user's roaming profile.
    /// Contains no secrets — tokens stay in the MSAL cache.
    /// </summary>
    public sealed class ConnectionStore
    {
        private readonly string _filePath;
        private readonly List<ConnectionProfile> _profiles = new List<ConnectionProfile>();

        public ConnectionStore(string filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DataverseDiscovery",
                "connections.json");

            Load();
        }

        public IReadOnlyList<ConnectionProfile> Profiles => _profiles;

        public void Add(ConnectionProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            _profiles.Add(profile);
            Save();
        }

        public void Remove(ConnectionProfile profile)
        {
            if (profile == null) return;

            _profiles.RemoveAll(p => string.Equals(p.Id, profile.Id, StringComparison.Ordinal));
            Save();
        }

        public bool ContainsUrl(string environmentUrl) =>
            _profiles.Any(p => string.Equals(p.EnvironmentUrl, environmentUrl, StringComparison.OrdinalIgnoreCase));

        /// <summary>Persists in-place edits to a profile already in the store.</summary>
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));

            var serializer = new DataContractJsonSerializer(typeof(List<ConnectionProfile>));

            using (var stream = File.Create(_filePath))
                serializer.WriteObject(stream, _profiles);
        }

        private void Load()
        {
            _profiles.Clear();

            if (!File.Exists(_filePath)) return;

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(List<ConnectionProfile>));

                using (var stream = File.OpenRead(_filePath))
                {
                    if (serializer.ReadObject(stream) is List<ConnectionProfile> loaded)
                        _profiles.AddRange(loaded);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is System.Runtime.Serialization.SerializationException)
            {
                // A corrupt or partially written file must not stop the add-in from loading.
                _profiles.Clear();
            }
        }
    }
}
