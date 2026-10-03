namespace LyricsDisplayer;

public enum DirtyEditorCloseChoice
{
    Save,
    Discard,
    Cancel
}

/// <summary>Applies the shared fatal-shutdown bypass before normal dirty-editor close behavior.</summary>
public static class EditorCloseGuard
{
    public static bool CanClose(IApplicationLifetimeState lifetime, Action commitPendingEdits,
        Func<bool> isDirty, Func<DirtyEditorCloseChoice> prompt, Action save)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(commitPendingEdits);
        ArgumentNullException.ThrowIfNull(isDirty);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(save);

        if (lifetime.IsFatalShutdown) return true;

        commitPendingEdits();
        if (lifetime.IsFatalShutdown || !isDirty()) return true;

        var choice = prompt();
        if (lifetime.IsFatalShutdown) return true;

        switch (choice)
        {
            case DirtyEditorCloseChoice.Save:
                if (lifetime.IsFatalShutdown) return true;
                save();
                return lifetime.IsFatalShutdown || !isDirty();
            case DirtyEditorCloseChoice.Discard:
                return true;
            case DirtyEditorCloseChoice.Cancel:
            default:
                return false;
        }
    }
}
