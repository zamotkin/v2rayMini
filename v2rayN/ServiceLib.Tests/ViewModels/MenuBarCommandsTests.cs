using ReactiveUI.Builder;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Extensions;
using ServiceLib.Tests.CoreConfig;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

// MINOR 8: TUnit 1.73 has no DisableParallelization attribute; [NotInParallel] is its
// exact equivalent and runs every test in this class alone, keeping the static
// AppEvents.CoreStateChanged count assertions deterministic.
[NotInParallel]
public class MenuBarCommandsTests
{
    private static readonly Lazy<bool> _rxuiInit = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
        return true;
    });

    /// <summary>
    /// MAJOR 2 sandbox: CreateConfig leaves SysProxyType at its default (ForcedClear),
    /// which UpdateSysProxy maps to a real system-proxy reset on Linux/macOS hosts.
    /// ESysProxyType.Unchanged — the tray's "do not change" option value — hits no case
    /// in SysProxyHandler.UpdateSysProxy's switch, so StatusBarViewModel.Init()
    /// (ChangeSystemProxyAsync) becomes a no-op and the host proxy stays untouched.
    /// </summary>
    private static void BindSandboxedConfig()
    {
        var config = CoreConfigTestFactory.CreateConfig();
        config.SystemProxyItem.SysProxyType = ESysProxyType.Unchanged;
        CoreConfigTestFactory.BindAppManagerConfig(config);
    }

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
    public async Task MenuDisconnectCmd_PublishesDisconnectRequested()
    {
        _ = _rxuiInit.Value;
        BindSandboxedConfig();

        var vm = new StatusBarViewModel();

        // Instance channel on a fresh VM: a local list plus scope-bound unsubscribe keeps
        // the assertion immune to static-channel publications from other tests.
        var received = new List<bool>();
        using var subscription = vm.DisconnectRequested
            .AsObservable()
            .Subscribe(received.Add);

        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var execSub = vm.MenuDisconnectCmd.Execute().Subscribe(_ => finished.TrySetResult(true));

        await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        await finished.Task.IsCompleted.Should().BeTrue();

        await received.Should().HaveCount(1);
        await received[0].Should().BeTrue();
    }

    [Test]
    public async Task CoreStop_WhenIdle_PublishesCoreStateChangedFalse()
    {
        _ = _rxuiInit.Value;

        var received = new List<bool>();
        using var subscription = AppEvents.CoreStateChanged
            .AsObservable()
            .Subscribe(received.Add);

        await CoreManager.Instance.CoreStop();

        await received.Should().HaveCount(1);
        await received[0].Should().BeFalse();
    }

    [Test]
    public async Task MenuConnectCmd_Executes_PublishesReloadRequested()
    {
        _ = _rxuiInit.Value;
        BindSandboxedConfig();

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
