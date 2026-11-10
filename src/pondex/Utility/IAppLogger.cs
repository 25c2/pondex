namespace pondex.Utility;

public interface IAppLogger
{
    LoggerDiagnostics Diagnostics => default;
    bool DetailedLoggingEnabled { get => false; set { } }

    void Debug(string message) { }

    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}

