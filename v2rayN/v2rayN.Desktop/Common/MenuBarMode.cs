namespace v2rayN.Desktop.Common;

internal static class MenuBarMode
{
    private const string _tag = "MenuBarMode";
    private const string IconOnResource = "menubar_on.png";
    private const string IconOffResource = "menubar_off.png";

    private static Window? _mainWindow;
    private static WindowIcon? _iconOn;
    private static WindowIcon? _iconOff;

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

            var menu = BuildMenu(exitAction);

            // The XAML TrayIcon may not be materialized yet; defer once to the UI thread.
            Dispatcher.UIThread.Post(() => ApplyTray(menu));

            AppEvents.CoreStateChanged
                .AsObservable()
                .Subscribe(isRunning => Dispatcher.UIThread.Post(() => SetTrayIcon(isRunning)));
        }
        catch (Exception ex)
        {
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

    private static NativeMenu BuildMenu(Func<Task> exitAction)
    {
        var vm = StatusBarViewModel.Instance;

        var exitItem = new NativeMenuItem { Header = ResUI.menuExit };
        exitItem.Click += async (sender, e) => await exitAction();

        return new NativeMenu
        {
            Items =
            {
                new NativeMenuItem { Header = ResUI.menuConnect, Command = vm.MenuConnectCmd },
                new NativeMenuItem { Header = ResUI.menuDisconnect, Command = vm.MenuDisconnectCmd },
                new NativeMenuItem { Header = ResUI.TbDisplayGUI, Command = vm.ShowWindowCmd },
                exitItem,
            },
        };
    }

    private static void ApplyTray(NativeMenu menu)
    {
        var icons = TrayIcon.GetIcons(Application.Current);
        if (icons is not { Count: > 0 })
        {
            return;
        }

        icons[0].Menu = menu;

        // Core not started yet; the CoreStateChanged event flips it to ON after boot auto-connect.
        icons[0].Icon = _iconOff;
    }

    private static void SetTrayIcon(bool isRunning)
    {
        var icons = TrayIcon.GetIcons(Application.Current);
        if (icons is not { Count: > 0 })
        {
            return;
        }

        icons[0].Icon = isRunning ? _iconOn : _iconOff;
    }

    private static WindowIcon LoadMenuBarIcon(string fileName)
    {
        var uri = new Uri(Path.Combine(Global.AvaAssets, fileName));
        using var bitmap = new Bitmap(AssetLoader.Open(uri));
        return new(bitmap);
    }
}
