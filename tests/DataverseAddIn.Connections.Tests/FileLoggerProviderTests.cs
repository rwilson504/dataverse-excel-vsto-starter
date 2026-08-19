using System;
using System.IO;
using System.Linq;
using DataverseAddIn.Connections;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class FileLoggerProviderTests
    {
        [Fact]
        public void An_entry_reaches_the_file()
        {
            using (var directory = new TempDirectory())
            {
                var provider = new FileLoggerProvider(directory.Path);
                provider.CreateLogger("Dataverse").LogInformation("Connecting to {Url}.", "https://contoso.crm.dynamics.com");

                Assert.Contains("https://contoso.crm.dynamics.com", File.ReadAllText(provider.CurrentFile));
            }
        }

        [Fact]
        public void Entries_below_the_minimum_are_not_written()
        {
            using (var directory = new TempDirectory())
            {
                var provider = new FileLoggerProvider(directory.Path, LogLevel.Warning);
                var logger = provider.CreateLogger("Dataverse");

                logger.LogInformation("routine");
                logger.LogWarning("worth reading");

                var written = File.ReadAllText(provider.CurrentFile);

                Assert.DoesNotContain("routine", written);
                Assert.Contains("worth reading", written);
            }
        }

        /// <summary>
        /// Below Information the Dataverse client can log request headers, and this file is plain
        /// text in the user's profile — so the default must not be one that collects tokens.
        /// </summary>
        [Fact]
        public void The_default_minimum_excludes_the_levels_that_carry_request_detail()
        {
            using (var directory = new TempDirectory())
            {
                var provider = new FileLoggerProvider(directory.Path);
                var logger = provider.CreateLogger("Dataverse");

                Assert.False(logger.IsEnabled(LogLevel.Trace));
                Assert.False(logger.IsEnabled(LogLevel.Debug));
                Assert.True(logger.IsEnabled(LogLevel.Information));
            }
        }

        [Fact]
        public void An_exception_is_written_with_its_inner_chain()
        {
            using (var directory = new TempDirectory())
            {
                var provider = new FileLoggerProvider(directory.Path);
                var error = new InvalidOperationException("outer", new TimeoutException("the real cause"));

                provider.CreateLogger("Dataverse").LogError(error, "Connecting failed.");

                Assert.Contains("the real cause", File.ReadAllText(provider.CurrentFile));
            }
        }

        [Fact]
        public void Subscribers_see_each_entry()
        {
            using (var directory = new TempDirectory())
            {
                var provider = new FileLoggerProvider(directory.Path);
                var seen = new System.Collections.Generic.List<LogLevel>();

                provider.Written += (s, e) => seen.Add(e.Level);

                provider.CreateLogger("Dataverse").LogWarning("something");

                Assert.Equal(new[] { LogLevel.Warning }, seen);
            }
        }

        /// <summary>Logging must never be the reason the add-in fails.</summary>
        [Fact]
        public void A_directory_that_cannot_be_written_does_not_throw()
        {
            var provider = new FileLoggerProvider(Path.Combine("Z:", "no-such-volume", "logs"));

            var error = Record.Exception(() => provider.CreateLogger("Dataverse").LogError("boom"));

            Assert.Null(error);
        }

        [Fact]
        public void Old_files_are_purged_and_current_ones_kept()
        {
            using (var directory = new TempDirectory())
            {
                var stale = Path.Combine(directory.Path, "addin-20000101.log");
                var fresh = Path.Combine(directory.Path, "addin-29991231.log");

                File.WriteAllText(stale, "old");
                File.WriteAllText(fresh, "new");
                File.SetLastWriteTime(stale, DateTime.Now.AddDays(-30));

                new FileLoggerProvider(directory.Path, keepDays: 7);

                Assert.False(File.Exists(stale));
                Assert.True(File.Exists(fresh));
            }
        }
    }
}
