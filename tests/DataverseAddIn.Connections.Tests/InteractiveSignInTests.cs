using System;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// Closing the browser mid sign-in tells MSAL nothing — it is still waiting on a loopback
    /// listener for a redirect that will never arrive. Without a deadline the caller's UI stays
    /// busy forever, which is what these pin.
    /// </summary>
    public class InteractiveSignInTests
    {
        /// <summary>A sign-in that never returns is exactly the closed-browser case.</summary>
        [Fact]
        public async Task An_abandoned_sign_in_gives_up_rather_than_waiting_forever()
        {
            var error = await Assert.ThrowsAsync<SignInCanceledException>(
                () => InteractiveSignIn.RunAsync(Never, TimeSpan.FromMilliseconds(150)));

            Assert.Contains("browser", error.Message);
            Assert.Contains("try connecting again", error.Message);
        }

        [Fact]
        public async Task A_completed_sign_in_returns_its_result()
        {
            var token = await InteractiveSignIn.RunAsync(
                _ => Task.FromResult("a-token"), TimeSpan.FromMinutes(5));

            Assert.Equal("a-token", token);
        }

        /// <summary>The deadline must not swallow a cancellation the caller asked for.</summary>
        [Fact]
        public async Task A_caller_cancellation_stays_a_cancellation()
        {
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => InteractiveSignIn.RunAsync(Never, TimeSpan.FromMinutes(5), source.Token));
            }
        }

        [Fact]
        public async Task A_caller_cancellation_is_not_reported_as_a_timeout()
        {
            using (var source = new CancellationTokenSource())
            {
                source.CancelAfter(TimeSpan.FromMilliseconds(50));

                var error = await Record.ExceptionAsync(
                    () => InteractiveSignIn.RunAsync(Never, TimeSpan.FromSeconds(30), source.Token));

                Assert.IsNotType<SignInCanceledException>(error);
            }
        }

        /// <summary>The deadline has to reach the sign-in, or nothing would ever stop it.</summary>
        [Fact]
        public async Task The_deadline_is_passed_through_to_the_sign_in()
        {
            var observed = CancellationToken.None;

            await Assert.ThrowsAsync<SignInCanceledException>(
                () => InteractiveSignIn.RunAsync(
                    token => { observed = token; return Never(token); },
                    TimeSpan.FromMilliseconds(150)));

            Assert.True(observed.IsCancellationRequested);
        }

        [Fact]
        public async Task Failures_that_are_not_cancellations_pass_straight_through()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => InteractiveSignIn.RunAsync<string>(
                    _ => throw new InvalidOperationException("consent required"),
                    TimeSpan.FromMinutes(5)));
        }

        [Fact]
        public async Task A_null_sign_in_is_rejected()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => InteractiveSignIn.RunAsync<string>(null, TimeSpan.FromMinutes(5)));
        }

        [Theory]
        [InlineData(300, "5 minutes")]
        [InlineData(60, "1 minute")]
        [InlineData(30, "30 seconds")]
        public void The_timeout_message_reads_naturally(int seconds, string expected)
        {
            Assert.Contains(expected, InteractiveSignIn.TimeoutMessage(TimeSpan.FromSeconds(seconds)));
        }

        /// <summary>Stands in for a browser the user opened and then closed.</summary>
        private static Task<string> Never(CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<string>();
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));

            return source.Task;
        }
    }
}
