using Iderma.Core.Services;
using Microsoft.UI.Xaml;

namespace Iderma_Launcher;

public partial class App : Application
{
    private Window? _window;
    private static readonly Lazy<UserStore> UsersLazy = new(static () => new UserStore());
    private static readonly Lazy<SessionStore> SessionsLazy = new(static () => new SessionStore());
    private static readonly Lazy<MeetingStore> MeetingsLazy = new(static () => new MeetingStore());
    public static UserStore Users => UsersLazy.Value;
    public static SessionStore Sessions => SessionsLazy.Value;
    public static MeetingStore Meetings => MeetingsLazy.Value;
    private static readonly Lazy<SecurityCenter> SecurityLazy = new(static () => new SecurityCenter());
    private static readonly Lazy<LoginLog> LoginsLazy = new(static () => new LoginLog());
    public static SecurityCenter Security => SecurityLazy.Value;
    public static LoginLog Logins => LoginsLazy.Value;

    public App()
    {
        InitializeComponent();
        VigilanciaAcceso.AlEntrarSinRed = () =>
        {
            if (!RedClinica.EstaConectada())
            {
                RedClinica.AvisarYCerrar("bypass");
            }
        };
    }

    public static MainWindow? MainAppWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        TaskbarJumpList.SetProcessId();
        var requested = TaskbarJumpList.ReadOpenRequest();
        if (requested is not null && TaskbarJumpList.TryForward(requested))
        {
            Environment.Exit(0);
            return;
        }

        if (requested is not null)
        {
            TaskbarJumpList.RememberPending(requested);
        }

        if (!RedClinica.EstaConectada())
        {
            RedClinica.AvisarYCerrar("bloqueo");
            return;
        }

        _ = Task.Run(WarmStores);
        TaskbarJumpList.StartListening();
        MainAppWindow = new MainWindow();
        _window = MainAppWindow;
        _window.Activate();
    }

    private static void WarmStores()
    {
        try
        {
            _ = Users.GetAll();
            _ = Sessions.Snapshot();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }
}
