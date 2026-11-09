namespace PondashiEX.Utility;

public interface IAppLogger
{
    bool DetailedLoggingEnabled { get => false; set { } }

    void Debug(string message) { }

    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}

