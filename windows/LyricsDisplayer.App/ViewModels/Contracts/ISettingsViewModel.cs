using System.ComponentModel;

namespace LyricsDisplayer;

public interface ISettingsViewModel : INotifyPropertyChanged
{
    IDebugSettingsPageViewModel DebugPage { get; }
    IReadOnlyList<ISettingsPageViewModel> Pages { get; }
    ISettingsPageViewModel SelectedPage { get; set; }
}
