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
    public async Task MenuDisconnectCmd_WhenCoreNotRunning_PublishesCoreStateChangedFalse()
    {
        _ = _rxuiInit.Value;
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());

        var vm = new StatusBarViewModel();

        // Subscribe before Execute so the assertion observes the production publish itself
        // (count + order + value), not a post-hoc replay.
        var received = new List<bool>();
        using var stateSub = AppEvents.CoreStateChanged
            .AsObservable()
            .Subscribe(received.Add);

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var execSub = vm.MenuDisconnectCmd.Execute().Subscribe(_ => finished.TrySetResult(true));

        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await finished.Task.IsCompleted.Should().BeTrue();

        await received.Should().HaveCount(1);
        await received[0].Should().BeFalse();
    }

    [Test]
    public async Task MenuConnectCmd_Executes_PublishesReloadRequested()
    {
        _ = _rxuiInit.Value;
        CoreConfigTestFactory.BindAppManagerConfig(CoreConfigTestFactory.CreateConfig());

        var vm = new StatusBarViewModel();

        // Instance channel on a fresh VM: a local list plus scope-bound unsubscribe keeps
        // the assertion immune to static-channel publications from other tests.
        var received = new List<RxVoid>();
        using var subscription = vm.ReloadRequested
            .AsObservable()
            .Subscribe(received.Add);

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var execSub = vm.MenuConnectCmd.Execute().Subscribe(_ => finished.TrySetResult(true));

        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await finished.Task.IsCompleted.Should().BeTrue();

        await received.Should().HaveCount(1);
    }
}
