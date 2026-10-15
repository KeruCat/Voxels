using System.CommandLine;
using Engine.Graphics;

namespace Engine.App;
internal sealed class AppCli {
    private const GraphicsApi DefaultApi = GraphicsApi.VK;
    private const int DefaultFps = 144;
#if DEBUG
    private const bool DefaultValidation = true;
#else
    private const bool DefaultValidation = false;
#endif
    private const bool DefaultVSync = true;

    private readonly Option<GraphicsApi> _mode;
    private readonly Option<int> _fps;
    private readonly Option<bool> _validation;
    private readonly Option<bool> _vsync;
    private readonly RootCommand _root;

    public AppCli(Func<AppOptions, int> run)
    {
        _mode = new("--mode", "--MODE", "-m")
        {
            Description = "Графический API",
            DefaultValueFactory = _ => DefaultApi
        };

        _fps = new("--fps", "--FPS", "-f")
        {
            Description = "Лимит кадров в секунду",
            DefaultValueFactory = _ => DefaultFps
        };

        _validation = new("--validation")
        {
            Description = "Слой валидации графического API",
            DefaultValueFactory = _ => DefaultValidation
        };

        _vsync = new("--vsync")
        {
            Description = "Вертикальная синхронизация",
            DefaultValueFactory = _ => DefaultVSync
        };

        _root = [];
        _root.Options.Add(_mode);
        _root.Options.Add(_fps);
        _root.Options.Add(_validation);
        _root.Options.Add(_vsync);
        _root.SetAction(parseResult => run(Read(parseResult)));
    }

    /// <summary>Разбирает аргументы и вызывает делегат запуска. Возвращает код выхода.</summary>
    public int Invoke(string[] args) => _root.Parse(args).Invoke();

    private AppOptions Read(ParseResult parseResult) => new(
        parseResult.GetValue(_mode),
        Math.Max(0, parseResult.GetValue(_fps)),
        parseResult.GetValue(_validation),
        parseResult.GetValue(_vsync));
}
