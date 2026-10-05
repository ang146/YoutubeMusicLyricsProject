namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class SettingsViewModelTests
{
    [Test]
    public void GeneralIsDefaultAndAllSixSettingsDestinationsAreAvailable()
    {
        var viewModel = new SettingsViewModel();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedPage.Id, Is.EqualTo(SettingsPageId.General));
            Assert.That(viewModel.Pages.Select(page => page.Id), Is.EqualTo(new[]
            {
                SettingsPageId.General,
                SettingsPageId.LyricsOverlay,
                SettingsPageId.LyricsWindow,
                SettingsPageId.MediaController,
                SettingsPageId.Editor,
                SettingsPageId.Debug
            }));
        });
    }

    [Test]
    public void SelectedPageCanChangeAndPublishesNavigationState()
    {
        var viewModel = new SettingsViewModel();
        var propertyChanges = new List<string?>();
        viewModel.PropertyChanged += (_, args) => propertyChanges.Add(args.PropertyName);
        var targetPage = viewModel.Pages.Single(page => page.Id == SettingsPageId.LyricsWindow);

        viewModel.SelectedPage = targetPage;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedPage, Is.SameAs(targetPage));
            Assert.That(propertyChanges, Is.EqualTo(new[] { nameof(viewModel.SelectedPage) }));
        });
    }
}
