using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Engine;
using Engine.App;
using Engine.Core;
using Engine.Core.MathUtil;
using Engine.Core.Time;
using Engine.Graphics;
using Engine.Graphics.Memory;
using Engine.Input;
using Engine.Jobs;
using Engine.Memory;
using Engine.Platform;
using Engine.UI.Debug;
using Engine.UI.ImGUI;
using Game.Gameplay;
using Game.Render;
using Game.Voxels;
using Game.Voxels.Generators;
using Game.World;
using Hexa.NET.ImGui;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using BindFlags = Diligent.BindFlags;

namespace Game.App;

/// <summary>
/// Сборка игры: окно, графика, ввод, мир, игрок. Порядок кадра явный (без систем):
///   Update: ввод → игрок → floating origin → мир (стриминг, генерация, меш, загрузка)
///   Render: камера → видимость → команды отрисовки → GPU
/// </summary>
internal sealed class GameApp : IDisposable
{
    private static readonly Vector4 ClearColor = new(0.55f, 0.72f, 0.92f, 1f);

    private readonly InputState _input = new();
    private readonly GraphicsContext _gfx = new();
    private readonly DebugUi _debugUi = new();
    private readonly FrameContext _frame = new();
    private readonly FloatingOrigin _origin = new(Chunk.Size);
    private readonly WorldSettings _settings = new();
    private readonly Player _player = new();
    private readonly CpuPool _cpu = new();

    private AppOptions _options = null!;
    private string _title = "";
    private bool _isOpenGl;
    private TimerResolution? _timer;
    private FramePacer _pacer = null!;
    private IWindow _window = null!;
    private IInputContext? _inputContext;
    private ImGuiLayer _imgui = null!;
    private MemoryPool _memoryPool = null!;
    private Viewport _viewport = null!;
    private GpuArena? _vtxArena, _idxArena;
    private VoxelRenderer? _renderer;
    private GpuMeshSink? _meshSink;
    private VoxelWorld? _world;
    private TerrainGenerator _terrain = null!;

    private DrawIndexedCmd[] _draws = new DrawIndexedCmd[1024];
    private Vector4[] _origins = new Vector4[1024];
    private int _drawn;
    private double _drawMs;

    private double _time;
    private bool _disposed;

    private bool Minimized => _window.FramebufferSize is { X: 0 } or { Y: 0 };

    internal void Run(AppOptions options)
    {
        _options = options;
        _title = $"GameApp [{options.Api}]";
        _isOpenGl = options.Api == GraphicsApi.GL;
        _timer = new TimerResolution();
        _pacer = new FramePacer(options.Fps);

        CreateWindow();
        CreateGraphics();
        CreateInput();
        _viewport = new Viewport();
        _viewport.Resize(_window.FramebufferSize.X, _window.FramebufferSize.Y);
        _debugUi.Extra = DrawWorldPanel;
        CreateWorld();
        CreatePlayer();

        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnResize;
        _window.Run();
    }

    // ───────────── инициализация ─────────────

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
        _memoryPool = new MemoryPool(64L << 20);                  // растёт сам при нехватке
        _terrain = new TerrainGenerator(_settings.Seed);

        // Все меши живут в двух больших GPU-буферах
        _vtxArena = new GpuArena(_gfx.Device!, "Chunk vertices", BindFlags.VertexBuffer,
                                 (uint)Unsafe.SizeOf<VoxelVertex>(), 40_000_000);
        _idxArena = new GpuArena(_gfx.Device!, "Chunk indices", BindFlags.IndexBuffer, sizeof(ushort), 60_000_000);
        _renderer = new VoxelRenderer(_gfx, _vtxArena, _idxArena);
        _meshSink = new GpuMeshSink(_gfx, _vtxArena, _idxArena);

        _world = new VoxelWorld(_memoryPool, _meshSink, _cpu, _settings, () => new TerrainGenerator(_settings.Seed));

        _debugUi.Pool = _memoryPool;
        _debugUi.VertexArena = _vtxArena;
        _debugUi.IndexArena = _idxArena;
    }

    private void CreatePlayer()
    {
        // старт над поверхностью в (0, 0)
        int ground = Math.Max(_terrain.SurfaceHeight(0, 0), TerrainGenerator.SeaLevel);
        _player.X = 0.5;
        _player.Y = ground + 6;
        _player.Z = 0.5;
    }

    // ───────────── кадр ─────────────

    private void OnUpdate(double dt)
    {
        if (Minimized) return;

        float delta = (float)dt;
        _time += dt;

        _imgui.BeginFrame(delta);
        _debugUi.Draw(delta, _title);

        _input.UiWantsMouse = _imgui.WantsMouse;
        _input.UiWantsKeyboard = _imgui.WantsKeyboard;

        if (_input.KeyPressed(Key.Escape)) _window.Close();

        _player.Update(delta, _input);
        _origin.Follow(_player.X, _player.Y, _player.Z);

        var local = _player.LocalPosition(_origin);
        int pcx = (int)Math.Floor(_player.X / Chunk.Size);
        int pcz = (int)Math.Floor(_player.Z / Chunk.Size);
        var prio = new ChunkPriority(_origin.X, _origin.Y, _origin.Z, local, CameraMath.Forward(_player.Camera));
        _world!.Update(pcx, pcz, prio);

        _input.EndFrame();
    }

    private void OnRender(double dt)
    {
        if (Minimized) return;

        _gfx.BeginFrame(ClearColor);

        long t0 = Stopwatch.GetTimestamp();
        UpdateCamera();
        _world!.Cull(_frame, _origin.X, _origin.Y, _origin.Z, AppClock.Seconds);

        int n = _world.VisibleCount;
        if (n > _draws.Length)
        {
            int cap = Math.Max(_draws.Length * 2, n);
            _draws = new DrawIndexedCmd[cap];
            _origins = new Vector4[cap];
        }
        _drawn = _world.BuildDraws(_origin.X, _origin.Y, _origin.Z, _origins, _draws);
        if (_drawn > 0) _renderer!.Draw(_frame, _origins.AsSpan(0, _drawn), _draws.AsSpan(0, _drawn));
        _drawMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        _imgui.Render();
        _gfx.Present();

        if (!_options.VSync) _pacer.Wait();
    }

    private void UpdateCamera()
    {
        var cam = _player.Camera;
        Vector3 eye = _player.LocalPosition(_origin);
        Vector3 fwd = CameraMath.Forward(cam);
        Vector3 right = CameraMath.Right(cam);
        Vector3 up = Vector3.Cross(right, fwd);

        var view = Matrix4x4.CreateLookAt(eye, eye + fwd, up);
        var proj = MathUtil.CreatePerspectiveFieldOfView(cam.FovY, _viewport.Aspect, cam.Near, cam.Far, _isOpenGl);
        _frame.Set(view, proj, eye, _isOpenGl);
    }

    private void OnResize(Vector2D<int> size)
    {
        if (Minimized) return;
        _gfx.Resize((uint)size.X, (uint)size.Y);
        _viewport.Resize(size.X, size.Y);
    }

    // ───────────── отладочная панель игры ─────────────

    private void DrawWorldPanel()
    {
        if (_world == null) return;

        ImGui.Separator();
        ImGui.Text(FormattableString.Invariant($"Chunks: {_world.ChunkCount}  visible: {_world.VisibleCount}  drawn: {_drawn}"));
        ImGui.Text(FormattableString.Invariant($"World pos: {_player.X:F1}  {_player.Y:F1}  {_player.Z:F1}"));
        ImGui.Text($"Float origin (chunk): {_origin.X}  {_origin.Y}  {_origin.Z}");
        ImGui.Text(FormattableString.Invariant($"Queues: spawn {_world.PendingSpawn}  gen {_world.PendingGenerate}  mesh {_world.PendingMesh}  upload {_world.PendingUpload}"));
        ImGui.Text(FormattableString.Invariant($"ms: recenter {_world.LastRecenterMs:F2}  spawn {_world.LastSpawnMs:F2}  gen {_world.LastGenMs:F2}  mesh {_world.LastMeshMs:F2}  upload {_world.LastUploadMs:F2}"));
        ImGui.SliderInt("Render distance", ref _settings.RenderDistance, 4, 512);
        if (_renderer != null && ImGui.Button($"Draw mode: {_renderer.Mode}"))
        {
            _renderer.Mode = _renderer.Mode switch
            {
                DrawMode.Direct => DrawMode.IndirectSingle,
                DrawMode.IndirectSingle => DrawMode.IndirectMulti,
                _ => DrawMode.Direct,
            };
        }
        ImGui.Text(FormattableString.Invariant($"draw prep + submit: {_drawMs:F2} ms"));
    }

    // ───────────── завершение (обратно созданию) ─────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _world?.Dispose();
        _imgui?.Dispose();
        _renderer?.Dispose();
        _vtxArena?.Dispose();
        _idxArena?.Dispose();
        _memoryPool?.Dispose();
        _gfx.Dispose();
        _inputContext?.Dispose();
        _timer?.Dispose();
    }
}