using System;
using System.IO;

namespace CommandEditor.Core.Services;

public class LogService
{
    private readonly string _logFilePath;
    private readonly object _lock = new object();
    private bool _isEnabled;

    public LogService(string dataDirectory)
    {
        _logFilePath = Path.Combine(dataDirectory, "debug.log");
        _isEnabled = false; // Disabled by default
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public void Log(string message)
    {
        if (!_isEnabled)
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}";
                
                File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
            }
        }
        catch
        {
            // Silently ignore logging errors
        }
    }

    public void ClearLog()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }
            }
        }
        catch
        {
            // Silently ignore errors
        }
    }

    public string GetLogFilePath() => _logFilePath;
}
