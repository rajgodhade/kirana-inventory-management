using Kirana.App.Views;
using Kirana.Application.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(15);

    private readonly ManagementSession _session;
    private readonly DispatcherTimer _idleCheckTimer;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Kirana";

        _session = App.Services.GetRequiredService<ManagementSession>();

        // Any pointer/keyboard activity anywhere in the app resets the Dashboard inactivity
        // clock (PRD §8) — handledEventsToo so this fires even when a child control (a
        // TextBox, a Button) has already marked the routed event handled.
        RootFrame.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)((_, _) => _session.Touch()), handledEventsToo: true);
        RootFrame.AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)((_, _) => _session.Touch()), handledEventsToo: true);
        RootFrame.AddHandler(UIElement.KeyDownEvent, (KeyEventHandler)((_, _) => _session.Touch()), handledEventsToo: true);

        _idleCheckTimer = new DispatcherTimer { Interval = IdleCheckInterval };
        _idleCheckTimer.Tick += OnIdleCheckTick;
        _idleCheckTimer.Start();
    }

    /// <summary>
    /// Setup-completed stores go straight to POS Billing; otherwise show the first-time
    /// setup wizard (PRD §4-5).
    /// </summary>
    public void NavigateToInitialPage(bool setupCompleted)
    {
        RootFrame.Navigate(setupCompleted ? typeof(PosShellPage) : typeof(SetupWizardPage));
    }

    private void OnIdleCheckTick(object? sender, object e)
    {
        if (!_session.IsUnlocked)
        {
            return;
        }

        if (!_session.IsIdleTimeoutExceeded(TimeSpan.FromMinutes(_session.AutoLockMinutes)))
        {
            return;
        }

        // Auto-lock: clear the session and any sensitive management state immediately, and
        // return to Billing — same effect as clicking "Lock & Return to Billing" manually.
        App.Services.GetRequiredService<IAuthenticationService>().LockAndReturnToBilling();
        if (RootFrame.CurrentSourcePageType != typeof(PosShellPage))
        {
            RootFrame.Navigate(typeof(PosShellPage));
        }
    }
}
