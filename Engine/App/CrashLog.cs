namespace Engine.App;
internal static class CrashLog
{
    public static void Write(string? text)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), text ?? "");
        }
        catch
        {
        }
    }
}
