using System;
using System.Collections.Generic;
using System.Linq;

namespace DataverseAddIn.WinForms
{
    /// <summary>
    /// Flattens an exception chain for display.
    /// </summary>
    /// <remarks>
    /// ServiceClient reports the useful part in <c>LastException</c> rather than the message it
    /// puts on the surface, so showing only <see cref="Exception.Message"/> loses the reason a
    /// connection failed — the caller is left with "Fault while initializing client" and nothing
    /// to act on.
    /// </remarks>
    public static class ErrorDetail
    {
        public static string Describe(Exception error)
        {
            if (error == null) return string.Empty;

            var seen = new List<string>();

            for (var current = error; current != null; current = current.InnerException)
            {
                var message = current.Message?.Trim();

                // Wrappers often repeat the inner message verbatim; showing it twice helps nobody.
                if (!string.IsNullOrEmpty(message) && !seen.Contains(message))
                    seen.Add(message);
            }

            return string.Join(" — ", seen);
        }

        /// <summary>The innermost type name, which is usually what identifies the failure.</summary>
        public static string Origin(Exception error)
        {
            if (error == null) return string.Empty;

            var current = error;
            while (current.InnerException != null) current = current.InnerException;

            return current.GetType().Name;
        }
    }
}
