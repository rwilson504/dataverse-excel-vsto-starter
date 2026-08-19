using System;
using System.IO;
using System.Text;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class DpapiSecretStoreTests
    {
        private const string Secret = "sup3r-s3cret-value";

        [Fact]
        public void Generates_a_reference_when_none_is_supplied()
        {
            using (var dir = new TempDirectory())
            {
                var store = new DpapiSecretStore(dir.Path);

                var first = store.Write(null, Secret);
                var second = store.Write(null, Secret);

                Assert.NotEqual(first, second);
                Assert.Equal(Secret, store.Read(first));
                Assert.Equal(Secret, store.Read(second));
            }
        }

        [Fact]
        public void Reuses_and_overwrites_a_supplied_reference()
        {
            using (var dir = new TempDirectory())
            {
                var store = new DpapiSecretStore(dir.Path);

                Assert.Equal("my-ref", store.Write("my-ref", Secret));
                store.Write("my-ref", "rotated");

                Assert.Equal("rotated", store.Read("my-ref"));
                Assert.Single(Directory.GetFiles(dir.Path));
            }
        }

        [Fact]
        public void Never_writes_the_secret_in_the_clear()
        {
            using (var dir = new TempDirectory())
            {
                var reference = new DpapiSecretStore(dir.Path).Write(null, Secret);

                var bytes = File.ReadAllBytes(Path.Combine(dir.Path, reference + ".bin"));

                Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
                Assert.DoesNotContain(Secret, Encoding.Unicode.GetString(bytes), StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Reading_an_unknown_reference_returns_null()
        {
            using (var dir = new TempDirectory())
                Assert.Null(new DpapiSecretStore(dir.Path).Read("nothing-here"));
        }

        [Fact]
        public void Delete_removes_the_secret_and_is_safe_to_repeat()
        {
            using (var dir = new TempDirectory())
            {
                var store = new DpapiSecretStore(dir.Path);
                var reference = store.Write(null, Secret);

                store.Delete(reference);
                store.Delete(reference);

                Assert.Null(store.Read(reference));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Blank_references_are_ignored_rather_than_throwing(string reference)
        {
            using (var dir = new TempDirectory())
            {
                var store = new DpapiSecretStore(dir.Path);

                Assert.Null(store.Read(reference));
                store.Delete(reference);
            }
        }

        /// <summary>References become file names, so anything that could escape the folder must be refused.</summary>
        [Theory]
        [InlineData(@"..\..\connections")]
        [InlineData("../../connections")]
        [InlineData(@"C:\Windows\System32\config")]
        [InlineData("has space")]
        [InlineData("has.dot")]
        public void Rejects_references_that_are_not_safe_file_names(string reference)
        {
            using (var dir = new TempDirectory())
            {
                var store = new DpapiSecretStore(dir.Path);

                Assert.Throws<ArgumentException>(() => store.Read(reference));
                Assert.Throws<ArgumentException>(() => store.Write(reference, Secret));
            }
        }

        /// <summary>A cache copied from another machine must re-prompt, not throw a crypto error.</summary>
        [Fact]
        public void Unreadable_ciphertext_reads_as_null()
        {
            using (var dir = new TempDirectory())
            {
                var store = new DpapiSecretStore(dir.Path);
                var reference = store.Write(null, Secret);

                File.WriteAllBytes(Path.Combine(dir.Path, reference + ".bin"), new byte[] { 1, 2, 3, 4, 5 });

                Assert.Null(store.Read(reference));
            }
        }

        [Fact]
        public void Writing_a_null_secret_throws()
        {
            using (var dir = new TempDirectory())
                Assert.Throws<ArgumentNullException>(() => new DpapiSecretStore(dir.Path).Write(null, null));
        }
    }
}
