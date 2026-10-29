using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using DPIShift.Core.Models;
using DPIShift.Core.Persistence;
using DPIShift.Core.Scaling;

namespace DPIShift;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Fast-path CLI launcher mode (`DPIShift.exe --launch "<profileId>" [extraArgs...]`)
        // Bypasses WPF startup completely for <10ms zero-overhead scaled app launching!
        if (args.Length >= 2 && args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase))
        {
            return RunFastCliLaunch(args[1], args.Skip(2).ToArray());
        }

        bool startMinimized = args.Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        int sessionId = Process.GetCurrentProcess().SessionId;
        string mutexName = $@"Local\DPIShift_SingleInstance_S{sessionId}";
        string pipeName = $"DPIShift_WakePipe_S{sessionId}";

        using var mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            // Wake existing tray/background instance and bring it to foreground
            TrySignalExistingInstance(pipeName);
            return 0;
        }

        var app = new App(startMinimized, pipeName);
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunFastCliLaunch(string profileIdOrPath, string[] extraArgs)
    {
        try
        {
            var store = new ConfigStore();
            DpiShiftConfiguration config = store.Load();

            AppScaleProfile? profile = config.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, profileIdOrPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.ExecutablePath, profileIdOrPath, StringComparison.OrdinalIgnoreCase));

            if (profile is null)
                return 2;

            return ScaledProcessLauncher.LaunchScaledFromCliAsync(profile, extraArgs)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return 1;
        }
    }

    private static void TrySignalExistingInstance(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(timeout: 1200);
            byte[] payload = Encoding.UTF8.GetBytes("SHOW");
            client.Write(payload, 0, payload.Length);
            client.Flush();
        }
        catch
        {
            // Ignore if existing instance is shutting down
        }
    }
}
