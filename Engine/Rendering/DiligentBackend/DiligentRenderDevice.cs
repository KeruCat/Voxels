using System.Runtime.CompilerServices;
using Diligent;
using Engine.Graphics;

namespace Engine.Rendering.DiligentBackend;

/// <summary>
/// Реализация IRenderDevice на Diligent. Единственное место, где рендер говорит с Diligent.
/// Имена ресурсов в шейдерах: Slang добавляет к ним суффикс _0 (texture atlas → atlas_0, constants → cb_constants_0).
/// </summary>
internal sealed unsafe class DiligentRenderDevice : IRenderDevice
{
    /// <summary>Суффикс, который компилятор шейдеров добавляет к именам ресурсов.</summary>
    internal const string ResourceSuffix = "_0";

    private readonly GraphicsContext _gfx;
    private readonly ShaderLibrary _library;
    private readonly IReadOnlyDictionary<string, ShaderInfo> _shaderInfos;
    private readonly Dictionary<string, IShader> _shaders = new();
    private readonly Dictionary<string, (IBuffer Buffer, int Size)> _constantBuffers = new();
    private readonly DiligentCommands _commands;

    /// <param name="gfx">Уже созданное графическое окружение.</param>
    /// <param name="shaderRoot">Папка с скомпилированными шейдерами.</param>
    /// <param name="shaders">Имена шейдеров, которые используют пайплайны, и их описания из каталога.</param>
    public DiligentRenderDevice(GraphicsContext gfx, string shaderRoot, IReadOnlyDictionary<string, ShaderInfo> shaders)
    {
        _gfx = gfx;
        _shaderInfos = shaders;
        _library = new ShaderLibrary(shaderRoot, gfx);
        _commands = new DiligentCommands(this, gfx);
    }

    public bool SupportsMultiDrawIndirect => true;

    internal GraphicsContext Gfx => _gfx;

    // ───────────── ресурсы ─────────────

    public IGpuBuffer CreateBuffer(GpuBufferDesc desc)
    {
        bool cpu = desc.Access == BufferAccess.CpuWrite;
        var native = _gfx.Device!.CreateBuffer(new()
        {
            Name = desc.Name,
            Size = desc.Size,
            Usage = cpu ? Usage.Dynamic : Usage.Default,
            BindFlags = ToBindFlags(desc.Usage),
            CPUAccessFlags = cpu ? CpuAccessFlags.Write : CpuAccessFlags.None,
        });
        return new DiligentBuffer(desc.Name, desc.Size, desc.Access, native);
    }

    public ITexture2D CreateTexture(TextureDesc desc, ReadOnlySpan<byte> rgbaPixels)
    {
        if (desc.Format != TextureFormat.Rgba8)
            throw new NotSupportedException($"Texture format {desc.Format} is not supported yet.");
        if (rgbaPixels.Length != desc.Width * desc.Height * 4)
            throw new ArgumentException($"Texture '{desc.Name}': expected {desc.Width * desc.Height * 4} bytes, got {rgbaPixels.Length}.");

        fixed (byte* p = rgbaPixels)
        {
            var native = _gfx.Device!.CreateTexture(new()
            {
                Name = desc.Name,
                Type = ResourceDimension.Tex2d,
                Width = (uint)desc.Width, Height = (uint)desc.Height,
                MipLevels = 1,
                Format = Diligent.TextureFormat.RGBA8_UNorm,
                BindFlags = BindFlags.ShaderResource,
                Usage = Usage.Default,
            }, new TextureData
            {
                SubResources = new[] { new TextureSubResData { Data = (nint)p, Stride = (ulong)(desc.Width * 4) } }
            });
            return new DiligentTexture(desc.Name, desc.Width, desc.Height, native);
        }
    }

    public IPipeline CreatePipeline(PipelineDesc desc)
    {
        if (desc.Blend != BlendMode.Opaque)
            throw new NotSupportedException("AlphaBlend пока не поддерживается адаптером Diligent.");

        var vs = GetShader(desc.VertexShader);
        var ps = GetShader(desc.PixelShader);
        var sc = _gfx.SwapChain!.GetDesc();
        var filter = desc.TextureFilter == SamplerFilter.Point ? FilterType.Point : FilterType.Linear;

        var variables = new List<ShaderResourceVariableDesc>();
        foreach (var t in desc.PixelTextures)
            variables.Add(new() { ShaderStages = ShaderType.Pixel, Name = t + ResourceSuffix, Type = ShaderResourceVariableType.Mutable });
        foreach (var c in desc.ConstantBuffers)
            variables.Add(new() { ShaderStages = ShaderType.Vertex, Name = "cb_" + c + ResourceSuffix, Type = ShaderResourceVariableType.Mutable });

        // сэмплер на каждую текстуру: пиксель-арт — без сглаживания, края зажаты
        var samplers = desc.PixelTextures.Select(t => new ImmutableSamplerDesc
        {
            ShaderStages = ShaderType.Pixel,
            SamplerOrTextureName = t + ResourceSuffix,
            Desc = new()
            {
                MinFilter = filter, MagFilter = filter, MipFilter = FilterType.Point,
                AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp,
            },
        }).ToArray();

        var layout = desc.Inputs.Select(ToLayoutElement).ToArray();

        var pso = _gfx.Device!.CreateGraphicsPipelineState(new()
        {
            PSODesc = new()
            {
                Name = desc.Name,
                ResourceLayout = new()
                {
                    DefaultVariableType = ShaderResourceVariableType.Static,
                    Variables = variables.ToArray(),
                    ImmutableSamplers = samplers,
                }
            },
            Vs = vs,
            Ps = ps,
            GraphicsPipeline = new()
            {
                InputLayout = new() { LayoutElements = layout },
                PrimitiveTopology = PrimitiveTopology.TriangleList,
                RasterizerDesc = new() { CullMode = CullMode.Back, FrontCounterClockwise = desc.CounterClockwiseFront },
                DepthStencilDesc = new()
                {
                    DepthEnable = desc.DepthTest,
                    DepthWriteEnable = desc.DepthWrite,
                    DepthFunc = ComparisonFunction.Less,
                },
                NumRenderTargets = 1,
                RTVFormats = [sc.ColorBufferFormat],
                DSVFormat = sc.DepthBufferFormat,
            }
        }) ?? throw new Exception($"Pipeline '{desc.Name}' creation failed.");

        var srb = pso.CreateShaderResourceBinding(true);
        return new DiligentPipeline(desc, pso, srb);
    }

    public void WriteBuffer<T>(IGpuBuffer buffer, ulong byteOffset, ReadOnlySpan<T> data) where T : unmanaged
        => DiligentCommands.WriteData(_gfx.Context!, AsDiligent(buffer), byteOffset, data);

    public IRenderCommands BeginFrame(System.Numerics.Vector4 clearColor)
    {
        _gfx.BeginFrame(clearColor);
        _commands.OnFrameStart();
        return _commands;
    }

    public void Present() => _gfx.Present();

    public void Resize(uint width, uint height) => _gfx.Resize(width, height);

    /// <summary>Константный буфер для типа T. Один буфер на имя: размер фиксирует первый вызов.</summary>
    internal IBuffer GetConstantBuffer<T>(string name) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        if (_constantBuffers.TryGetValue(name, out var entry))
        {
            if (entry.Size != size)
                throw new InvalidOperationException($"Constant buffer '{name}' already has size {entry.Size}, got {size}.");
            return entry.Buffer;
        }

        var buffer = _gfx.Device!.CreateBuffer(new()
        {
            Name = "cb " + name,
            Size = (ulong)size,
            Usage = Usage.Dynamic,
            BindFlags = BindFlags.UniformBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _constantBuffers[name] = (buffer, size);
        return buffer;
    }

    public void Dispose()
    {
        // устройство и окно принадлежат GraphicsContext; здесь освобождаем только своё
        foreach (var s in _shaders.Values) s.Dispose();
        _shaders.Clear();
        foreach (var c in _constantBuffers.Values) c.Buffer.Dispose();
        _constantBuffers.Clear();
        _library.Dispose();
    }

    // ───────────── внутреннее ─────────────

    private IShader GetShader(string name)
    {
        if (_shaders.TryGetValue(name, out var shader)) return shader;
        if (!_shaderInfos.TryGetValue(name, out var info))
            throw new KeyNotFoundException($"Shader '{name}' is not registered with the render device.");
        shader = _library.Create(info);
        _shaders[name] = shader;
        return shader;
    }

    private static DiligentBuffer AsDiligent(IGpuBuffer buffer) =>
        buffer as DiligentBuffer ?? throw new ArgumentException("Buffer was not created by this render device.", nameof(buffer));

    private static BindFlags ToBindFlags(BufferUsage usage)
    {
        BindFlags flags = BindFlags.None;
        if ((usage & BufferUsage.Vertex) != 0) flags |= BindFlags.VertexBuffer;
        if ((usage & BufferUsage.Index) != 0) flags |= BindFlags.IndexBuffer;
        if ((usage & BufferUsage.Indirect) != 0) flags |= BindFlags.IndirectDrawArgs;
        if ((usage & BufferUsage.Constant) != 0) flags |= BindFlags.UniformBuffer;
        return flags;
    }

    private static LayoutElement ToLayoutElement(VertexInput input)
    {
        var (components, type, normalized) = input.Format switch
        {
            VertexFormat.UInt8x4 => (4u, Diligent.ValueType.UInt8, false),
            VertexFormat.UInt32 => (1u, Diligent.ValueType.UInt32, false),
            VertexFormat.Float32x2 => (2u, Diligent.ValueType.Float32, false),
            VertexFormat.Float32x3 => (3u, Diligent.ValueType.Float32, false),
            VertexFormat.Float32x4 => (4u, Diligent.ValueType.Float32, false),
            _ => throw new NotSupportedException($"Vertex format {input.Format} is not supported."),
        };

        var element = new LayoutElement
        {
            InputIndex = (uint)input.Location,
            BufferSlot = (uint)input.BufferSlot,
            NumComponents = components,
            ValueType = type,
            IsNormalized = normalized,
        };
        if (input.PerInstance)
        {
            element.Frequency = InputElementFrequency.PerInstance;
            element.InstanceDataStepRate = 1;
        }
        return element;
    }
}