using System.Threading;
using System.Windows;
using P1Z.Subtitler.Services;

namespace P1Z.Subtitler;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\P1Z.Subtitler.SingleInstance", out var created);
        if (!created)
        {
            System.Windows.MessageBox.Show("p1z.subtitler is already running in the system tray.",
                "p1z.subtitler", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            _controller = new AppController();
            _controller.Initialize();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(exception.ToString(), "p1z.subtitler startup failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); }
            catch (ApplicationException) { }
            _singleInstance.Dispose();
        }
        base.OnExit(e);
    }
}
