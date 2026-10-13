namespace v2rayN.Desktop.Common;

internal static class MenuBarMode
{
    private const string _tag = "MenuBarMode";
    private const string IconOnResource = "menubar_on.png";
    private const string IconOffResource = "menubar_off.png";

    private static Window? _mainWindow;
    private static WindowIcon? _iconOn;
    private static WindowIcon? _iconOff;
    private static IDisposable? _coreStateChangedSubscription;

    public static bool IsActive { get; private set; }

    public static void Activate(Window mainWindow, Func<Task> exitAction)
    {
        if (IsActive || !Utils.IsMacOS())
        {
            return;
        }

        // Set before touching icons or subscribing, so StatusBarView.RefreshIcon
        // can never overwrite the menu-bar icon once mode is active.
        IsActive = true;

        try
        {
            _mainWindow = mainWindow;

            // The desktop lifetime shows MainWindow only after OnFrameworkInitializationCompleted,
            // and Window.Hide() is a no-op before the first Show; hide on the first Opened, which
            // runs synchronously inside that Show call, before the main loop gets a chance to draw.
            // Hide() only, never Close(): the window stays assigned and remains the dialog owner.
            _mainWindow.Opened += MainWindow_OnFirstOpened;

            _iconOn = LoadMenuBarIcon(IconOnResource);
            _iconOff = LoadMenuBarIcon(IconOffResource);

            // Dispose the CoreStateChanged subscription before the app starts shutting down,
            // so CoreStop's publish during AppExitAsync cannot reach the tray handler anymore.
            var items = BuildMenuItems(async () =>
            {
                _coreStateChangedSubscription?.Dispose();
                _coreStateChangedSubscription = null;
                await exitAction();
            });

            // The XAML TrayIcon may not be materialized yet; defer once to the UI thread.
            Dispatcher.UIThread.Post(() => ApplyTray(items));

            _coreStateChangedSubscription = AppEvents.CoreStateChanged
                .AsObservable()
                .Subscribe(isRunning => Dispatcher.UIThread.Post(() => SetTrayIcon(isRunning)));
        }
        catch (Exception ex)
        {
            // The tray was not actually taken over; let StatusBarView.RefreshIcon resume.
            IsActive = false;
            Logging.SaveLog(_tag, ex);
        }
    }

    private static void MainWindow_OnFirstOpened(object? sender, EventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Opened -= MainWindow_OnFirstOpened;
        _mainWindow.Hide();
    }

    private static NativeMenuItem[] BuildMenuItems(Func<Task> exitAction)
    {
        var vm = StatusBarViewModel.Instance;

        var exitItem = new NativeMenuItem { Header = ResUI.menuExit };
        exitItem.Click += async (sender, e) => await exitAction();

        // Parentless items: adding a still-parented item to another menu trips
        // the AvaloniaList validator ("already has a parent NativeMenu").
        return
        [
            new NativeMenuItem { Header = ResUI.menuConnect, Command = vm.MenuConnectCmd },
            new NativeMenuItem { Header = ResUI.menuDisconnect, Command = vm.MenuDisconnectCmd },
            new NativeMenuItem { Header = ResUI.TbDisplayGUI, Command = vm.ShowWindowCmd },
            exitItem,
        ];
    }

    private static void ApplyTray(NativeMenuItem[] items)
    {
        // Runs via Dispatcher.UIThread.Post, i.e. outside Activate's try/catch —
        // must guard itself or a tray failure kills the app.
        try
        {
            var icons = GetTrayIcons();
            if (icons is not { Count: > 0 })
            {
                return;
            }

            // The macOS exporter tracks the XAML-declared NativeMenu instance and rejects
            // a different instance ("The menu being updated does not match"), so mutate
            // the existing menu in place instead of replacing it.
            var target = icons[0].Menu;
            if (target is null)
            {
                icons[0].Menu = new NativeMenu { Items = { items } };
            }
            else
            {
                target.Items.Clear();
                foreach (var item in items)
                {
                    target.Items.Add(item);
                }
            }

            // Core not started yet; the CoreStateChanged event flips it to ON after boot auto-connect.
            icons[0].Icon = _iconOff;
        }
        catch (Exception ex)
        {
            // The tray was not taken over; let StatusBarView.RefreshIcon resume.
            IsActive = false;
            Logging.SaveLog(_tag, ex);
        }
    }

    private static void SetTrayIcon(bool isRunning)
    {
        var icons = GetTrayIcons();
        if (icons is not { Count: > 0 })
        {
            return;
        }

        icons[0].Icon = isRunning ? _iconOn : _iconOff;
    }

    /// <summary>
    /// A late CoreStateChanged publish during shutdown cannot be allowed to throw:
    /// Application.Current is already gone by then.
    /// </summary>
    private static TrayIcons? GetTrayIcons()
    {
        if (Application.Current is null)
        {
            return null;
        }

        return TrayIcon.GetIcons(Application.Current);
    }

    private static WindowIcon LoadMenuBarIcon(string fileName)
    {
        var uri = new Uri(Path.Combine(Global.AvaAssets, fileName));
        using var bitmap = new Bitmap(AssetLoader.Open(uri));
        return new(bitmap);
    }
}
