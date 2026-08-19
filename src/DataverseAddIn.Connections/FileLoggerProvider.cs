using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DataverseAddIn.Connections
{
    /// <summary>One line of the log, so a UI can show recent activity without reading the file.</summary>
    public sealed class LogEntryEventArgs : EventArgs
    {
        public LogEntryEventArgs(LogLevel level, string category, string message)
        {
            Level = level;
            Category = category;
            Message = message;
        }

        public LogLevel Level { get; }

        public string Category { get; }

        public string Message { get; }
    }

    /// <summary>
    /// Writes to a daily file under the user's roaming profile, beside the saved connections.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than taking a logging package: a VSTO add-in has to deploy every
    /// assembly it references, and <c>ILogger</c> itself already arrives with the Dataverse SDK.
    /// <para>
    /// Defaults to <see cref="LogLevel.Information"/>. The Dataverse client is extremely chatty
    /// below that, and its lower levels can carry request headers — this file is plain text in
    /// the user's profile, so it must not become somewhere tokens end up.
    /// </para>
    /// </remarks>
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly LogLevel _minimum;
        private readonly int _keepDays;

        private bool _disabled;

        public FileLoggerProvider(
            string directory = null,
            LogLevel minimum = LogLevel.Information,
            int keepDays = 7)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DataverseDiscovery",
                "logs");

            _minimum = minimum;
            _keepDays = keepDays;

            Purge();
        }

        /// <summary>Raised for every written entry, on the thread that logged it.</summary>
        public event EventHandler<LogEntryEventArgs> Written;

        public string Directory => _directory;

        /// <summary>The file currently being written, for a "show me the log" affordance.</summary>
        public string CurrentFile =>
            Path.Combine(_directory, $"addin-{DateTime.Now:yyyyMMdd}.log");

        public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

        public void Dispose()
        {
        }

        internal bool IsEnabled(LogLevel level) => !_disabled && level >= _minimum && level != LogLevel.None;

        internal void Write(LogLevel level, string category, string message, Exception error)
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append("  ").Append(Abbreviate(level))
                .Append("  ").Append(Shorten(category))
                .Append("  ").Append(message)
                .ToString();

            if (error != null)
                line += System.Environment.NewLine + "    " + error;

            Append(line);

            Written?.Invoke(this, new LogEntryEventArgs(level, category, message));
        }

        private void Append(string line)
        {
            // Logging must never be the reason something fails, so a write that cannot happen
            // disables the file rather than propagating — the UI keeps working without it.
            try
            {
                lock (_gate)
                {
                    System.IO.Directory.CreateDirectory(_directory);
                    File.AppendAllText(CurrentFile, line + System.Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                _disabled = true;
            }
        }

        private void Purge()
        {
            try
            {
                if (!System.IO.Directory.Exists(_directory)) return;

                var cutoff = DateTime.Now.AddDays(-_keepDays);

                foreach (var file in System.IO.Directory.GetFiles(_directory, "addin-*.log")
                             .Where(f => File.GetLastWriteTime(f) < cutoff))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // A profile we cannot tidy is not a reason to fail startup.
            }
        }

        private static string Abbreviate(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Information: return "INFO ";
                case LogLevel.Warning: return "WARN ";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Critical: return "CRIT ";
                default: return "     ";
            }
        }

        /// <summary>Keeps the last namespace segment: full type names dwarf the message otherwise.</summary>
        private static string Shorten(string category)
        {
            if (string.IsNullOrEmpty(category)) return string.Empty;

            var index = category.LastIndexOf('.');
            return index >= 0 && index < category.Length - 1 ? category.Substring(index + 1) : category;
        }

        private sealed class FileLogger : ILogger
        {
            private static readonly IDisposable NullScope = new Scope();

            private readonly FileLoggerProvider _provider;
            private readonly string _category;

            internal FileLogger(FileLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) => NullScope;

            public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel) || formatter == null) return;

                _provider.Write(logLevel, _category, formatter(state, exception), exception);
            }

            private sealed class Scope : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
