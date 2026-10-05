using LyricsDisplayer.Infrastructure.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class ApplicationExceptionHandlerTests
{
    [Test]
    public void RecoverableFailureIsPresentedAndDoesNotEscalate()
    {
        var presenter = Substitute.For<IRecoverableExceptionPresenter>();
        var handler = new ApplicationExceptionHandler(NullLogger<ApplicationExceptionHandler>.Instance, presenter);
        var failure = new IOException("file is unavailable");

        Assert.That(handler.Handle(failure, "Editor.Save"), Is.EqualTo(ExceptionHandlingDisposition.Handled));
        presenter.Received(1).ShowOperationFailure("Editor.Save", failure.Message);
    }

    [Test]
    public void FatalFailureIsEscalatedWithoutShowingRecoverableDialog()
    {
        var presenter = Substitute.For<IRecoverableExceptionPresenter>();
        var handler = new ApplicationExceptionHandler(NullLogger<ApplicationExceptionHandler>.Instance, presenter);

        Assert.That(handler.Handle(new FatalApplicationException("unsafe state"), "Editor.Save"),
            Is.EqualTo(ExceptionHandlingDisposition.Escalate));
        presenter.DidNotReceiveWithAnyArgs().ShowOperationFailure(default!, default!);
    }
}
