using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using DataverseAddIn.Connections;
using DataverseAddIn.Discovery;
using Microsoft.Crm.Sdk.Messages;

namespace DataverseAddIn.Samples.ConsoleHost
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            try
            {
                // Direct mode: an environment URL was supplied, so discovery is skipped entirely.
                if (args.Length > 0)
                    return await ConnectDirectAsync(args[0]).ConfigureAwait(false);

                var clouds = ReadClouds();
                var authenticators = clouds.Select(BuildAuthenticator).ToList();

                Console.WriteLine("Clouds to query:");
                foreach (var cloud in clouds)
                    Console.WriteLine($"  {cloud,-24} {cloud.GetGlobalDiscoveryUrl(),-46} {cloud.GetAuthorityHost()}");

                Console.WriteLine();
                Console.WriteLine("A sign-in prompt appears once per identity authority. Commercial and GCC share");
                Console.WriteLine("public Entra ID; GCC High and DoD use Entra Government and prompt separately.");
                Console.WriteLine();

                using (var discovery = new MultiCloudDiscoveryClient(authenticators))
                {
                    var result = await discovery.GetInstancesAsync().ConfigureAwait(false);

                    foreach (var failure in result.Failures)
                        Console.WriteLine($"! {failure.Cloud}: {failure.Error.Message}");

                    if (result.Failures.Count > 0) Console.WriteLine();

                    if (result.Instances.Count == 0)
                    {
                        Console.WriteLine("No environments returned. The account may be disabled, filtered out by an");
                        Console.WriteLine("environment security group, or a delegated admin (none are reported by GDS).");
                        return 0;
                    }

                    PrintInstances(result.Instances);

                    var selected = Prompt(result.Instances);
                    if (selected == null) return 0;

                    var authenticator = authenticators.FirstOrDefault(a => a.Cloud == selected.Cloud)
                                        ?? BuildAuthenticator(selected.Cloud);

                    await ConnectAndDescribeAsync(authenticator, selected).ConfigureAwait(false);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// The "I already know my org URL" path: infer the cloud from the host, build the
        /// matching authenticator, and connect without ever calling Global Discovery.
        /// </summary>
        private static async Task<int> ConnectDirectAsync(string url)
        {
            if (!DataverseEnvironmentReference.TryParse(url, out var environment, out var error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            if (!environment.CloudWasRecognized)
                Console.WriteLine($"! Host matched no known Dataverse suffix; assuming {environment.Cloud}.");

            Console.WriteLine($"Skipping discovery.");
            Console.WriteLine($"  Environment : {environment.Url}");
            Console.WriteLine($"  Cloud       : {environment.Cloud}");
            Console.WriteLine($"  Authority   : {environment.Cloud.GetAuthorityHost()}");

            var authenticator = BuildAuthenticator(environment.Cloud);
            await ConnectAndDescribeAsync(authenticator, environment).ConfigureAwait(false);

            return 0;
        }

        /// <summary>
        /// Runs both paths against the chosen environment: the raw Web API, and the SDK's
        /// IOrganizationService via ServiceClient.
        /// </summary>
        private static async Task ConnectAndDescribeAsync(
            DataverseAuthenticator authenticator,
            DataverseEnvironmentReference environment)
        {
            Console.WriteLine();
            Console.WriteLine($"Web API WhoAmI against {environment.Url} ...");

            using (var client = new DataverseWebApiClient(authenticator, environment.Url))
            {
                var whoAmI = await client.WhoAmIAsync().ConfigureAwait(false);
                Console.WriteLine($"  UserId         : {whoAmI.UserId}");
                Console.WriteLine($"  OrganizationId : {whoAmI.OrganizationId}");
            }

            Console.WriteLine();
            Console.WriteLine($"SDK ServiceClient against {environment.Url} ...");

            using (var service = await DataverseServiceClientFactory
                       .CreateAsync(authenticator, environment).ConfigureAwait(false))
            {
                var response = (WhoAmIResponse)service.Execute(new WhoAmIRequest());

                Console.WriteLine($"  UserId         : {response.UserId}");
                Console.WriteLine($"  BusinessUnitId : {response.BusinessUnitId}");
                Console.WriteLine($"  OrganizationId : {response.OrganizationId}");
                Console.WriteLine($"  Org version    : {service.ConnectedOrgVersion}");
                Console.WriteLine($"  Friendly name  : {service.ConnectedOrgFriendlyName}");
            }
        }

        private static IReadOnlyList<DataverseCloud> ReadClouds()
        {
            var raw = ConfigurationManager.AppSettings["Clouds"];

            if (string.IsNullOrWhiteSpace(raw))
                return new[] { DataverseCloud.Commercial };

            var clouds = raw
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => (DataverseCloud)Enum.Parse(typeof(DataverseCloud), name.Trim(), ignoreCase: true))
                .Distinct()
                .ToList();

            if (clouds.Count == 0)
                throw new InvalidOperationException("The Clouds setting is empty.");

            return clouds;
        }

        private static DataverseAuthenticator BuildAuthenticator(DataverseCloud cloud)
        {
            var clientId = Setting("ClientId", cloud);

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException(cloud.UsesGovernmentIdentity()
                    ? $"Set ClientId.{cloud} in App.config. {cloud} uses Microsoft Entra Government, so it " +
                      "needs its own app registration, created at https://portal.azure.us."
                    : "Set ClientId in App.config to your Entra app registration's Application (client) ID.");
            }

            return new DataverseAuthenticator(new DataverseAuthOptions
            {
                ClientId = clientId,
                TenantId = Setting("TenantId", cloud) ?? "organizations",
                RedirectUri = Setting("RedirectUri", cloud) ?? "http://localhost",
                Cloud = cloud
            });
        }

        /// <summary>Reads "Key.Cloud" if present, otherwise falls back to "Key".</summary>
        private static string Setting(string key, DataverseCloud cloud)
        {
            var scoped = ConfigurationManager.AppSettings[$"{key}.{cloud}"];
            return string.IsNullOrWhiteSpace(scoped) ? ConfigurationManager.AppSettings[key] : scoped;
        }

        private static void PrintInstances(IReadOnlyList<DataverseInstance> instances)
        {
            var nameWidth = Math.Min(36, Math.Max(13, instances.Max(i => (i.FriendlyName ?? string.Empty).Length)));
            var indexWidth = instances.Count.ToString().Length;
            const int cloudWidth = 22;

            Console.WriteLine($"{instances.Count} environment(s):");
            Console.WriteLine();
            Console.WriteLine($"  {"#".PadLeft(indexWidth)}  {"Cloud".PadRight(cloudWidth)}  {"Friendly name".PadRight(nameWidth)}  {"Admin".PadRight(5)}  Api URL");
            Console.WriteLine($"  {new string('-', indexWidth)}  {new string('-', cloudWidth)}  {new string('-', nameWidth)}  -----  -------");

            for (var i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                var index = (i + 1).ToString().PadLeft(indexWidth);
                var cloud = instance.Cloud.ToString().PadRight(cloudWidth);
                var name = Truncate(instance.FriendlyName, nameWidth).PadRight(nameWidth);
                var admin = (instance.IsUserSysAdmin ? "yes" : "no").PadRight(5);

                Console.WriteLine($"  {index}  {cloud}  {name}  {admin}  {instance.ApiUrl}");
            }
        }

        private static DataverseEnvironmentReference Prompt(IReadOnlyList<DataverseInstance> instances)
        {
            Console.WriteLine();
            Console.WriteLine("Enter a row number, or paste an environment URL to skip discovery.");
            Console.Write("Selection (blank to exit): ");

            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                return null;

            if (int.TryParse(input, out var index))
            {
                return index >= 1 && index <= instances.Count
                    ? DataverseEnvironmentReference.FromInstance(instances[index - 1])
                    : null;
            }

            if (!DataverseEnvironmentReference.TryParse(input, out var reference, out var error))
            {
                Console.WriteLine(error);
                return null;
            }

            if (!reference.CloudWasRecognized)
                Console.WriteLine($"! Host matched no known Dataverse suffix; assuming {reference.Cloud}.");

            return reference;
        }

        private static string Truncate(string value, int max)
        {
            value = value ?? string.Empty;
            return value.Length <= max ? value : value.Substring(0, max - 1) + "\u2026";
        }
    }
}
