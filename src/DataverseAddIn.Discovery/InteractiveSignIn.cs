using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// The user did not finish signing in — they closed the browser, or the attempt ran out of
    /// time. Distinct from a failure, because nothing went wrong and the message should say so.
    /// </summary>
    public sealed class SignInCanceledException : Exception
    {
        public SignInCanceledException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Bounds an interactive sign-in.
    /// </summary>
    /// <remarks>
    /// With the system browser MSAL waits on a loopback listener for the redirect. Closing the
    /// browser sends nothing to that listener, so the wait never ends on its own: without a
    /// deadline the caller's UI stays busy forever. The embedded web view does raise a
    /// cancellation, but this add-in deliberately uses the system browser.
    /// </remarks>
    public static class InteractiveSignIn
    {
        public static async Task<T> RunAsync<T>(
            Func<CancellationToken, Task<T>> acquire,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (acquire == null) throw new ArgumentNullException(nameof(acquire));

            using (var deadline = new CancellationTokenSource(timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token))
            {
                try
                {
                    return await acquire(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
                                                        !cancellationToken.IsCancellationRequested)
                {
                    throw new SignInCanceledException(TimeoutMessage(timeout));
                }
            }
        }

        /// <summary>Public so a host can show the same wording from its own timeout handling.</summary>
        public static string TimeoutMessage(TimeSpan timeout) =>
            $"Sign-in was not completed within {Describe(timeout)}. The browser may have been closed " +
            "before signing in — try connecting again.";

        private static string Describe(TimeSpan timeout) =>
            timeout.TotalMinutes >= 1
                ? $"{timeout.TotalMinutes:0} minute{(timeout.TotalMinutes >= 2 ? "s" : string.Empty)}"
                : $"{timeout.TotalSeconds:0} seconds";
    }
}
