using Diligent;
using Silk.NET.Windowing;
using IDeviceContext = Diligent.IDeviceContext;

namespace Engine.Graphics;

internal sealed class GraphicsContext : IDisposable
{
    internal GraphicsApi Api { get; private set; }
    internal IEngineFactory? Factory { get; private set; }
    internal IRenderDevice? Device { get; private set; }
    internal IDeviceContext? Context { get; private set; }
    internal ISwapChain? SwapChain { get; private set; }
    internal bool VSync { get; set; }

    internal void Create(GraphicsApi api, IWindow window, bool validation = false)
    {
        var hwnd = GetHwnd(window);

        IEngineFactory factory = api switch
        {
            GraphicsApi.D3D11 => Native.CreateEngineFactory<IEngineFactoryD3D11>(),
            GraphicsApi.D3D12 => Native.CreateEngineFactory<IEngineFactoryD3D12>(),
            GraphicsApi.VK => Native.CreateEngineFactory<IEngineFactoryVk>(),
            GraphicsApi.GL => Native.CreateEngineFactory<IEngineFactoryOpenGL>(),
            _ => throw new ArgumentOutOfRangeException(nameof(api), api, "Unexpected graphics API")
        };

        SetMessageCallback(factory);
        CreateDeviceAndSwapChain(factory, hwnd, validation, out var device, out var context, out var swapChain);

        if (device == null || context == null || swapChain == null)
        {
            factory.Dispose();
            throw new Exception($"Failed to initialize {api}: device={device != null}, context={context != null}, swapChain={swapChain != null}. См. сообщения Diligent Engine выше.");
        }

        Api = api;
        Factory = factory;
        Device = device;
        Context = context;
        SwapChain = swapChain;
    }

    internal void Resize(uint width, uint height)
    {
        if (width == 0 || height == 0)
            return;
        SwapChain?.Resize(width, height, SurfaceTransform.Optimal);
    }

    /// <summary>Назначает бэкбуфер и буфер глубины текущими целями и очищает их. Вызывать в начале каждого кадра.</summary>
    internal void BeginFrame(System.Numerics.Vector4 clearColor)
    {
        var rtv = SwapChain!.GetCurrentBackBufferRTV();
        var dsv = SwapChain.GetDepthBufferDSV();
        var desc = SwapChain.GetDesc();

        Context!.SetRenderTargets(new[] { rtv }, dsv, ResourceStateTransitionMode.Transition);

        // В GL glClear подчиняется scissor-тесту, а ImGuiRenderer оставляет включённым scissor
        // от последней команды. Без сброса очищается только этот прямоугольник.
        Context.SetScissorRects(new[]
        {
            new Rect { Left = 0, Top = 0, Right = (int)desc.Width, Bottom = (int)desc.Height }
        }, desc.Width, desc.Height);

        Context.ClearRenderTarget(rtv, new(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W), ResourceStateTransitionMode.Transition);
        Context.ClearDepthStencil(dsv, ClearDepthStencilFlags.Depth, 1.0f, 0, ResourceStateTransitionMode.Transition);
    }

    internal void Present() => SwapChain?.Present(VSync ? 1u : 0u);

    public void Dispose()
    {
        // Освобождаем в порядке, обратном созданию
        SwapChain?.Dispose();
        Context?.Dispose();
        Device?.Dispose();
        Factory?.Dispose();
    }

    // ---------------------------------------------------------------------

    private static IntPtr GetHwnd(IWindow window)
    {
        var native = window.Native ?? throw new InvalidOperationException("No native window");
        if (native.Win32 is not { } win32)
            throw new PlatformNotSupportedException("Пока поддерживается только Win32");
        return win32.Hwnd;
    }

    private static void SetMessageCallback(IEngineFactory engineFactory)
    {
        engineFactory.SetMessageCallback((severity, message, function, file, line) =>
        {
            // Исключение внутри нативного колбэка роняет процесс, поэтому здесь только вывод.
            if (severity == DebugMessageSeverity.Info)
                Console.WriteLine($"Diligent Engine: {severity} {message}");
            else
                Console.WriteLine($"Diligent Engine: {severity} in {function}() ({file}, {line}): {message}");
        });
    }

    private static void CreateDeviceAndSwapChain(
        IEngineFactory engineFactory,
        IntPtr hwnd,
        bool validation,
        out IRenderDevice renderDevice,
        out IDeviceContext deviceContext,
        out ISwapChain swapChain)
    {
        IDeviceContext[]? contextsOut;
        switch (engineFactory)
        {
            case IEngineFactoryD3D11 d3d11:
                d3d11.CreateDeviceAndContextsD3D11(new() { EnableValidation = validation }, out renderDevice, out contextsOut);
                swapChain = d3d11.CreateSwapChainD3D11(renderDevice, contextsOut[0], new(), new(), new() { Wnd = hwnd });
                break;

            case IEngineFactoryD3D12 d3d12:
                d3d12.CreateDeviceAndContextsD3D12(new() { EnableValidation = validation }, out renderDevice, out contextsOut);
                swapChain = d3d12.CreateSwapChainD3D12(renderDevice, contextsOut[0], new(), new(), new() { Wnd = hwnd });
                break;

            case IEngineFactoryVk vk:
                vk.CreateDeviceAndContextsVk(new() { EnableValidation = validation }, out renderDevice, out contextsOut);
                swapChain = vk.CreateSwapChainVk(renderDevice, contextsOut[0], new(), new() { Wnd = hwnd });
                break;

            case IEngineFactoryOpenGL gl:
                gl.CreateDeviceAndSwapChainGL(new() { EnableValidation = validation, Window = new() 
                { Wnd = hwnd } }, out renderDevice, out var glContext, new(), out swapChain);
                contextsOut = glContext != null ? new[] { glContext } : null;
                break;

            default:
                throw new Exception("Unexpected engine factory type");
        }

        if (contextsOut is not { Length: > 0 })
            throw new InvalidOperationException("Diligent Engine не вернул контекст устройства.");

        deviceContext = contextsOut[0];
    }
}
