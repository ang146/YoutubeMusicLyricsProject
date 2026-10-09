namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class ObservableObjectBaseTests
{
    [Test]
    public void LightweightObservableObjectNotifiesChangedAndUnchangedValuesOnlyOnce()
    {
        var item = new ProbeObservableItem();
        var notifications = new List<string?>();
        item.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        item.Name = "Updated";
        item.Name = "Updated";

        Assert.Multiple(() =>
        {
            Assert.That(item.Name, Is.EqualTo("Updated"));
            Assert.That(notifications, Is.EqualTo(new[] { nameof(ProbeObservableItem.Name) }));
        });
    }

    private sealed class ProbeObservableItem : ObservableObjectBase
    {
        private string _name = "Initial";

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
    }
}
