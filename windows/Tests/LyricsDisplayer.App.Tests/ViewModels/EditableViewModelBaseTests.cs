namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class EditableViewModelBaseTests
{
    [Test]
    public void NewEditableViewModelStartsClean()
    {
        var viewModel = new ProbeEditableViewModel();

        Assert.That(viewModel.IsDirty, Is.False);
    }

    [Test]
    public void MarkDirtyNotifiesOnlyWhenTransitioningFromClean()
    {
        var viewModel = new ProbeEditableViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.SetDirty();
        viewModel.SetDirty();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsDirty, Is.True);
            Assert.That(notifications, Is.EqualTo(new[] { nameof(EditableViewModelBase.IsDirty) }));
        });
    }

    [Test]
    public void MarkCleanNotifiesOnlyWhenTransitioningFromDirty()
    {
        var viewModel = new ProbeEditableViewModel();
        viewModel.SetDirty();
        var notifications = CaptureNotifications(viewModel);

        viewModel.SetClean();
        viewModel.SetClean();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsDirty, Is.False);
            Assert.That(notifications, Is.EqualTo(new[] { nameof(EditableViewModelBase.IsDirty) }));
        });
    }

    [Test]
    public void ChangedEditablePropertyNotifiesPropertyThenDirtyState()
    {
        var viewModel = new ProbeEditableViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.Name = "B";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Name, Is.EqualTo("B"));
            Assert.That(viewModel.IsDirty, Is.True);
            Assert.That(notifications,
                Is.EqualTo(new[] { nameof(ProbeEditableViewModel.Name), nameof(EditableViewModelBase.IsDirty) }));
        });
    }

    [Test]
    public void EqualEditableValueDoesNotNotifyOrMarkDirty()
    {
        var viewModel = new ProbeEditableViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.Name = "A";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsDirty, Is.False);
            Assert.That(notifications, Is.Empty);
        });
    }

    [Test]
    public void SecondEditWhileDirtyNotifiesItsPropertyWithoutRepeatingDirtyNotification()
    {
        var viewModel = new ProbeEditableViewModel();
        viewModel.Name = "B";
        var notifications = CaptureNotifications(viewModel);

        viewModel.Name = "C";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Name, Is.EqualTo("C"));
            Assert.That(viewModel.IsDirty, Is.True);
            Assert.That(notifications, Is.EqualTo(new[] { nameof(ProbeEditableViewModel.Name) }));
        });
    }

    [Test]
    public void EditingAfterMarkCleanTransitionsBackToDirty()
    {
        var viewModel = new ProbeEditableViewModel();
        viewModel.Name = "B";
        viewModel.SetClean();
        var notifications = CaptureNotifications(viewModel);

        viewModel.Name = "C";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsDirty, Is.True);
            Assert.That(notifications,
                Is.EqualTo(new[] { nameof(ProbeEditableViewModel.Name), nameof(EditableViewModelBase.IsDirty) }));
        });
    }

    [Test]
    public void NullableEditablePropertySupportsBothNullTransitions()
    {
        var viewModel = new ProbeEditableViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.OptionalValue = "value";
        Assert.That(notifications,
            Is.EqualTo(new[] { nameof(ProbeEditableViewModel.OptionalValue), nameof(EditableViewModelBase.IsDirty) }));

        notifications.Clear();
        viewModel.SetClean();
        notifications.Clear();
        viewModel.OptionalValue = null;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.OptionalValue, Is.Null);
            Assert.That(viewModel.IsDirty, Is.True);
            Assert.That(notifications,
                Is.EqualTo(new[] { nameof(ProbeEditableViewModel.OptionalValue), nameof(EditableViewModelBase.IsDirty) }));
        });
    }

    private static List<string?> CaptureNotifications(ViewModelBase viewModel)
    {
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        return notifications;
    }

    private sealed class ProbeEditableViewModel : EditableViewModelBase
    {
        private string _name = "A";
        private string? _optionalValue;

        public ProbeEditableViewModel() : base(NullLogger<ProbeEditableViewModel>.Instance) { }

        public string Name
        {
            get => _name;
            set => SetEditableProperty(ref _name, value);
        }

        public string? OptionalValue
        {
            get => _optionalValue;
            set => SetEditableProperty(ref _optionalValue, value);
        }

        public void SetDirty() => MarkDirty();
        public void SetClean() => MarkClean();
    }
}
