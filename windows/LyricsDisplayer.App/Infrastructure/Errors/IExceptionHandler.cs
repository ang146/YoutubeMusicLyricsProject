namespace LyricsDisplayer.Infrastructure.Errors;

public enum ExceptionHandlingDisposition
{
    Handled,
    Escalate
}

public interface IExceptionHandler
{
    ExceptionHandlingDisposition Handle(Exception exception, string operationName);
}

/// <summary>Marks an operation failure that cannot safely be treated as recoverable.</summary>
public sealed class FatalApplicationException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public interface IRecoverableExceptionPresenter
{
    void ShowOperationFailure(string operationName, string message);
}
