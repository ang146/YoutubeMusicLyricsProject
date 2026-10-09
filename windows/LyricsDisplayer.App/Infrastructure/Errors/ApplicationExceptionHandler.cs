using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.Infrastructure.Errors;

public sealed class ApplicationExceptionHandler(
    ILogger<ApplicationExceptionHandler> logger,
    IRecoverableExceptionPresenter presenter) : IExceptionHandler
{
    public ExceptionHandlingDisposition Handle(Exception exception, string operationName)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (exception is FatalApplicationException or OutOfMemoryException or AccessViolationException)
        {
            logger.LogCritical(exception, "Operation {OperationName} encountered an unrecoverable failure.",
                operationName);
            return ExceptionHandlingDisposition.Escalate;
        }

        presenter.ShowOperationFailure(operationName, exception.Message);
        return ExceptionHandlingDisposition.Handled;
    }
}
