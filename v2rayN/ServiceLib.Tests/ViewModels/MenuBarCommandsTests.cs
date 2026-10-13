using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Extensions;
using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

public class MenuBarCommandsTests
{
    private static readonly Lazy<bool> _rxuiInit = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        return true;
    });

    [Test]
    public async Task CoreStateChanged_Roundtrip_PublishesAndReceives()
    {
        var received = new List<bool>();
        using var subscription = AppEvents.CoreStateChanged
            .AsObservable()
            .Subscribe(received.Add);

        AppEvents.CoreStateChanged.Publish(true);
        AppEvents.CoreStateChanged.Publish(false);

        await received.Should().HaveCount(2);
        await received[0].Should().BeTrue();
        await received[1].Should().BeFalse();
    }

    [Test]
    public async Task MenuDisconnectCmd_WhenCoreNotRunning_DoesNotThrow()
    {
        _ = _rxuiInit.Value;
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());

        var vm = new StatusBarViewModel();

        var failures = new List<Exception>();
        using var thrownSub = vm.MenuDisconnectCmd.ThrownExceptions.Subscribe(failures.Add);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var execSub = vm.MenuDisconnectCmd.Execute().Subscribe(_ => finished.TrySetResult(true));

        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await finished.Task.IsCompleted.Should().BeTrue();
        await failures.Should().BeEmpty();
    }
}
