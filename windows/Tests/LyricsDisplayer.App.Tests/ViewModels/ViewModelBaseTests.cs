using System.Reflection;
using Microsoft.Extensions.Logging;

namespace LyricsDisplayer.App.Tests;

[TestFixture]
public sealed class ViewModelBaseTests
{
    [Test]
    public void BaseRequiresAnLoggerConstructorAndHasNoParameterlessFallback()
    {
        var constructors = typeof(ViewModelBase).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(constructors.Any(constructor => constructor.GetParameters() is [{ ParameterType: var type }] &&
                                                     type == typeof(ILogger)), Is.True);
        Assert.That(constructors.Any(constructor => constructor.GetParameters().Length == 0), Is.False);
    }

    [Test]
    public void SetPropertyRaisesOneNotificationForAChangedValue()
    {
        var viewModel = new ProbeViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.Value = "B";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Value, Is.EqualTo("B"));
            Assert.That(viewModel.LastValueSetChanged, Is.True);
            Assert.That(notifications, Is.EqualTo(new[] { nameof(ProbeViewModel.Value) }));
        });
    }

    [Test]
    public void SetPropertyReturnsFalseAndDoesNotNotifyForAnEqualValue()
    {
        var viewModel = new ProbeViewModel();
        var notifications = CaptureNotifications(viewModel);

        Assert.That(viewModel.TrySetValue("A"), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Value, Is.EqualTo("A"));
            Assert.That(viewModel.LastValueSetChanged, Is.False);
            Assert.That(notifications, Is.Empty);
        });
    }

    [Test]
    public void SetPropertyNotifiesForBothNullTransitions()
    {
        var viewModel = new ProbeViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.NullableValue = null;
        Assert.That(notifications, Is.EqualTo(new[] { nameof(ProbeViewModel.NullableValue) }));

        notifications.Clear();
        viewModel.NullableValue = "restored";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NullableValue, Is.EqualTo("restored"));
            Assert.That(notifications, Is.EqualTo(new[] { nameof(ProbeViewModel.NullableValue) }));
        });
    }

    [Test]
    public void DependentPropertyNotificationIsExplicitAndOccursAfterSourceProperty()
    {
        var viewModel = new ProbeViewModel();
        var notifications = CaptureNotifications(viewModel);

        viewModel.SourceTitle = "Updated";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.EffectiveTitle, Is.EqualTo("Updated"));
            Assert.That(notifications,
                Is.EqualTo(new[] { nameof(ProbeViewModel.SourceTitle), nameof(ProbeViewModel.EffectiveTitle) }));
        });
    }

    private static List<string?> CaptureNotifications(ViewModelBase viewModel)
    {
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        return notifications;
    }

    private sealed class ProbeViewModel : ViewModelBase
    {
        private string _value = "A";
        private string? _nullableValue = "initial";
        private string? _sourceTitle = "Source";

        public ProbeViewModel() : base(NullLogger<ProbeViewModel>.Instance) { }

        public bool LastValueSetChanged { get; private set; }
        public string Value
        {
            get => _value;
            set => LastValueSetChanged = SetProperty(ref _value, value);
        }

        public string? NullableValue
        {
            get => _nullableValue;
            set => SetProperty(ref _nullableValue, value);
        }

        public string? SourceTitle
        {
            get => _sourceTitle;
            set
            {
                if (SetProperty(ref _sourceTitle, value))
                    OnPropertyChanged(nameof(EffectiveTitle));
            }
        }

        public string EffectiveTitle => SourceTitle ?? "Unknown title";

        public bool TrySetValue(string value) => SetProperty(ref _value, value, nameof(Value));
    }
}
