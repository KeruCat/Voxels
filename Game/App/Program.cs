using System.Runtime.InteropServices;

namespace Engine;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);

    private static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
            AttachConsole(AttachParentProcess);   

        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject.ToString());

        return new AppCli(Run).Invoke(args);
    }

    private static int Run(AppOptions options)
    {
        try
        {
            using var game = new Game.App.GameApp();
            game.Run(options);
            return 0;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex.ToString());
            Console.Error.WriteLine($"Критическая ошибка: {ex}");
            Console.Error.WriteLine("Подробности записаны в crash.txt. Enter, чтобы закрыть...");
            Console.ReadLine();
            return 1;
        }
    }
}
