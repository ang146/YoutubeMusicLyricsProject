namespace LyricsDisplayer.Infrastructure.Commands;

/// <summary>Application command visibility/lifetime scope, independent of any one feature.</summary>
public enum CommandScope
{
    Global,
    Application,
    Editor
}
