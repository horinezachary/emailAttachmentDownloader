using Microsoft.Extensions.Logging;

namespace EmailAttachmentDownloader.Core;

/// <summary>Minimal size-capped file logger (1 MB, one rollover file). Passwords are never logged.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 1_000_000;

    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string text)
    {
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(_path, _path + ".old", overwrite: true);
                }

                File.AppendAllText(_path, text + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _owner;
        private readonly string _category;

        public FileLogger(FileLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            _owner.Write(line);
        }
    }
}
