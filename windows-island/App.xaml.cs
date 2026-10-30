using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;

namespace CcfDeadlineIsland;

public partial class App : Application
{
    private Mutex? _instance;
    private readonly CancellationTokenSource _shutdown = new();
    private string _pipeName = "CcfDeadlineIsland.v1";
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        int renderIndex = Array.IndexOf(e.Args, "--render-preview");
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            try { PreviewRenderer.Render(e.Args[renderIndex + 1]); Shutdown(0); }
            catch (Exception error) { File.WriteAllText(Path.Combine(e.Args[renderIndex + 1], "render-error.txt"), error.ToString()); Shutdown(1); }
            return;
        }
        bool preview = e.Args.Contains("--preview");
        if (preview) _pipeName += ".Preview";
        _instance = new Mutex(true, @"Local\" + _pipeName, out bool first);
        if (!first)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000);
                await using var writer = new StreamWriter(pipe);
                await writer.WriteLineAsync("show");
            }
            catch (Exception error) when (error is IOException or TimeoutException) { }
            Shutdown();
            return;
        }
        var window = new MainWindow(preview, e.Args.Contains("--demo"));
        MainWindow = window;
        window.Show();
        _ = ListenAsync(window);
    }

    private async Task ListenAsync(MainWindow window)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(pipe);
                if (await reader.ReadLineAsync(_shutdown.Token) == "show")
                    await Dispatcher.InvokeAsync(window.Reveal);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
