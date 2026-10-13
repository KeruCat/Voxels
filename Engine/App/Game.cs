using System.Numerics;
using System.Runtime.CompilerServices;
using Engine.App;
using Engine.Core;
using Engine.Core.Time;
using Engine.ECS;
using Engine.ECS.Components;
using Engine.Graphics;
using Engine.Input;
using Engine.Memory;
using Engine.Platform;
using Engine.UI.Debug;
using Engine.UI.ImGUI;
using Engine.Voxels;
using Engine.Voxels.Generators;
using Engine.Graphics.Memory;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using BindFlags = Diligent.BindFlags;

namespace Engine;

internal sealed class Game : IDisposable
{
    private static readonly Vector4 _clearColor = new(0.55f, 0.72f, 0.92f, 1f);

    private readonly InputState _input = new();
    private readonly GraphicsContext _gfx = new();
    private readonly DebugUi _debugUi = new();

    private AppOptions _options = null!;
    private string _title = "";
    private TimerResolution? _timer;
    private FramePacer _pacer = null!;
    private IWindow _window = null!;
    private IInputContext? _inputContext;
    private ImGuiLayer _imgui = null!;
    private EntityStore _world = null!;
    private SystemRoot _updateSystems = null!;
    private SystemRoot _renderSystems = null!;
    private MemoryPool _memoryPool = null!;
    private Viewport _viewport = null!;
    private GpuArena? _vtxArena, _idxArena;
    private ChunkRenderer? _chunkRenderer;
    private TerrainGenerator _terrain = null!;
    private readonly FloatingOrigin _origin = new();
    private readonly WorldSettings _settings = new();
    private readonly WorldStats _stats = new();

    private double _time;
    private bool _disposed;

    private bool Minimized => _window.FramebufferSize is { X: 0 } or { Y: 0 };

    internal void Run(AppOptions options)
    {
        _options = options;
        _title = $"GameApp [{options.Api}]";
        _timer = new TimerResolution();
        _pacer = new FramePacer(options.Fps);

        CreateWindow();
        CreateGraphics();
        CreateInput();
        _viewport = new Viewport();
        _viewport.Resize(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        _debugUi.Stats = _stats;
        _debugUi.Settings = _settings;
        CreateWorld();
        CreateEcs(options.Api == GraphicsApi.GL);

        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnResize;
        _window.Run();
    }

    // ───────────── Инициализация (порядок важен: каждый шаг зависит от предыдущих) ─────────────

    private void CreateWindow()
    {
        _window = Window.Create(WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            Title = _title,
            Size = new Vector2D<int>(1280, 720),
            WindowState = WindowState.Maximized,
            VSync = false
        });
        _window.Initialize();
    }

    private void CreateGraphics()
    {
        _gfx.Create(_options.Api, _window, _options.Validation);
        _gfx.VSync = _options.VSync;
    }

    private void CreateInput()
    {
        _inputContext = _window.CreateInput();
        InputRouter.Attach(_inputContext, _input);
        _imgui = new ImGuiLayer(_window, _inputContext, _gfx);
    }
    private void CreateWorld()
    {
        _memoryPool = new MemoryPool(64L << 20);               // растёт сам при нехватке
        _terrain = new TerrainGenerator(_settings.Seed);

        // Все чанки живут в двух больших GPU-буферах: ~4M вершин (32 МБ) и ~6M индексов (12 МБ)
        _vtxArena = new GpuArena(_gfx.Device!, "Chunk vertices", BindFlags.VertexBuffer,
                                 (uint)Unsafe.SizeOf<VoxelVertex>(), 40_000_000);
        _idxArena = new GpuArena(_gfx.Device!, "Chunk indices", BindFlags.IndexBuffer, sizeof(ushort), 60_000_000);
        _chunkRenderer = new ChunkRenderer(_gfx, _vtxArena, _idxArena);
        _debugUi.Pool = _memoryPool;
        _debugUi.VertexArena = _vtxArena;
        _debugUi.IndexArena = _idxArena;
    }

    private void CreateEcs(bool isOpenGl)
    {
        (_world, _updateSystems, _renderSystems) = RegisterECS.Build(
            _window, _input, _memoryPool, _gfx, _vtxArena!, _idxArena!, _chunkRenderer!, _viewport,
            _origin, _settings, _stats, () => new TerrainGenerator(_settings.Seed), isOpenGl);

        // старт: над поверхностью в (0, 0). Истинная позиция — double; float-Position пересчитывает FloatingOriginSystem
        int ground = Math.Max(_terrain.SurfaceHeight(0, 0), TerrainGenerator.SeaLevel);

        Entity player = _world.CreateEntity();
        player.AddComponent(new Velocity());
        player.AddComponent(new WorldPosition { X = 0.5, Y = ground + 6, Z = 0.5 });
        player.AddComponent(new ECS.Components.Position());
        player.AddComponent(new Camera
        {
            Target = Vector3.UnitZ,
            Yaw = 0,
            Pitch = -0.3f,
            Distance = 0,
            FovY = 1.047f,
            Near = 0.2f,
            Far = 10000
        });
    }
    // ───────────── Кадр ─────────────

    private void OnUpdate(double dt)
    {
        if (Minimized) return;

        float delta = (float)dt;
        _time += dt;

        _imgui.BeginFrame(delta);
        _debugUi.Draw(delta, _title);

        _input.UiWantsMouse = _imgui.WantsMouse;
        _input.UiWantsKeyboard = _imgui.WantsKeyboard;

        _updateSystems.Update(new UpdateTick(delta, (float)_time));
        _input.EndFrame();
    }

    private void OnRender(double dt)
    {
        if (Minimized) return;

        _gfx.BeginFrame(_clearColor);
        _renderSystems.Update(new UpdateTick((float)dt, (float)_time));
        _imgui.Render();
        _gfx.Present();

        if (!_options.VSync) _pacer.Wait();
    }

    private void OnResize(Vector2D<int> size)
    {
        if (Minimized) return;
        _gfx.Resize((uint)size.X, (uint)size.Y);
        _viewport.Resize(size.X, size.Y);
    }

    // ───────────── Завершение (в порядке, обратном созданию) ─────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _imgui?.Dispose();
        _chunkRenderer?.Dispose();
        _vtxArena?.Dispose();
        _idxArena?.Dispose();
        _memoryPool?.Dispose();
        _gfx.Dispose();
        _inputContext?.Dispose();
        _timer?.Dispose();
    }
}
