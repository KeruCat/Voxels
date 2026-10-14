using System.Numerics;
using Diligent;
using Engine.Graphics;

namespace Engine.Rendering.DiligentBackend;

/// <summary>
/// Команды кадра поверх немедленного контекста Diligent. Состояние (пайплайн, буферы, текстуры, константы)
/// запоминается и применяется перед отрисовкой, только если изменилось с прошлой отрисовки.
/// </summary>
internal sealed unsafe class DiligentCommands : IRenderCommands
{
    private const int MaxVertexSlots = 4;

    private readonly DiligentRenderDevice _device;
    private readonly GraphicsContext _gfx;

    private DiligentPipeline? _pipeline;
    private readonly Dictionary<string, DiligentTexture> _textures = new();
    private readonly Dictionary<string, IBuffer> _constants = new();

    private readonly DiligentBuffer?[] _vertex = new DiligentBuffer?[MaxVertexSlots];
    private readonly ulong[] _vertexOffsets = new ulong[MaxVertexSlots];
    private DiligentBuffer? _index;
    private ulong _indexOffset;
    private bool _indexUsesUInt32;

    private bool _dirty = true;

    internal DiligentCommands(DiligentRenderDevice device, GraphicsContext gfx)
    {
        _device = device;
        _gfx = gfx;
    }

    private IDeviceContext Ctx => _gfx.Context!;

    /// <summary>Новый кадр: состояние сохраняется, но перед первой отрисовкой его нужно применить заново.</summary>
    internal void OnFrameStart() => _dirty = true;

    // ───────────── состояние ─────────────

    public void SetPipeline(IPipeline pipeline)
    {
        _pipeline = pipeline as DiligentPipeline
            ?? throw new ArgumentException("Pipeline was not created by this render device.", nameof(pipeline));
        _dirty = true;
    }

    public void SetVertexBuffer(int slot, IGpuBuffer buffer, ulong offset = 0)
    {
        if ((uint)slot >= MaxVertexSlots) throw new ArgumentOutOfRangeException(nameof(slot));
        _vertex[slot] = AsBuffer(buffer);
        _vertexOffsets[slot] = offset;
        _dirty = true;
    }

    public void SetIndexBuffer(IGpuBuffer buffer, IndexFormat format, ulong offset = 0)
    {
        _index = AsBuffer(buffer);
        _indexOffset = offset;
        _indexUsesUInt32 = format == IndexFormat.UInt32;
        _dirty = true;
    }

    public void SetTexture(string name, ITexture2D texture)
    {
        _textures[name] = texture as DiligentTexture
            ?? throw new ArgumentException("Texture was not created by this render device.", nameof(texture));
        _dirty = true;
    }

    public void SetConstants<T>(string name, in T value) where T : unmanaged
    {
        var buffer = _device.GetConstantBuffer<T>(name);
        // значения пишутся сразу: буфер Dynamic, Discard — прежнее содержимое не нужно
        var mapped = Ctx.MapBuffer<T>(buffer, MapType.Write, MapFlags.Discard);
        mapped[0] = value;
        Ctx.UnmapBuffer(buffer, MapType.Write);
        _constants[name] = buffer;
        _dirty = true;
    }

    // ───────────── данные ─────────────

    public void Write<T>(IGpuBuffer buffer, ulong byteOffset, ReadOnlySpan<T> data) where T : unmanaged
        => WriteData(Ctx, AsBuffer(buffer), byteOffset, data);

    /// <summary>Запись данных в буфер. Общая для кадра и для записи вне кадра.</summary>
    internal static void WriteData<T>(IDeviceContext ctx, DiligentBuffer b, ulong byteOffset, ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty) return;

        if (b.Access == BufferAccess.GpuOnly)
        {
            fixed (T* p = data)
                ctx.UpdateBuffer(b.Native, byteOffset, (ulong)data.Length * (ulong)sizeof(T), (IntPtr)p, ResourceStateTransitionMode.Transition);
            return;
        }

        // CpuWrite: отображение с Discard сбрасывает буфер — записываем нужный кусок за один раз.
        // Смещение должно быть кратно размеру элемента (индексация идёт по элементам).
        ulong elementSize = (ulong)sizeof(T);
        if (byteOffset % elementSize != 0)
            throw new ArgumentException("Byte offset must be a multiple of the element size for CpuWrite buffers.");
        var mapped = ctx.MapBuffer<T>(b.Native, MapType.Write, MapFlags.Discard);
        int start = (int)(byteOffset / elementSize);
        for (int i = 0; i < data.Length; i++)
            mapped[start + i] = data[i];
        ctx.UnmapBuffer(b.Native, MapType.Write);
    }

    // ───────────── отрисовка ─────────────

    public void Draw(in DrawDesc draw)
    {
        Prepare();
        Ctx.Draw(new DrawAttribs
        {
            NumVertices = draw.VertexCount,
            NumInstances = draw.InstanceCount,
            StartVertexLocation = draw.FirstVertex,
            FirstInstanceLocation = draw.FirstInstance,
        });
    }

    public void DrawIndexed(in DrawIndexedCommand command)
    {
        Prepare();
        Ctx.DrawIndexed(new DrawIndexedAttribs
        {
            IndexType = IndexType(),
            NumIndices = command.IndexCount,
            FirstIndexLocation = command.FirstIndex,
            BaseVertex = (uint)command.BaseVertex,
            FirstInstanceLocation = command.FirstInstance,
            NumInstances = command.InstanceCount,
        });
    }

    public void DrawIndexedIndirect(IGpuBuffer args, ulong offset, uint drawCount, uint stride)
    {
        Prepare();
        Ctx.DrawIndexedIndirect(new DrawIndexedIndirectAttribs
        {
            IndexType = IndexType(),
            AttribsBuffer = AsBuffer(args).Native,
            DrawArgsOffset = offset,
            DrawCount = drawCount,
            DrawArgsStride = stride,
        });
    }

    // ───────────── внутреннее ─────────────

    /// <summary>Применяет накопленное состояние к контексту, если оно изменилось.</summary>
    private void Prepare()
    {
        if (_pipeline == null) throw new InvalidOperationException("SetPipeline must be called before a draw.");
        if (!_dirty) return;
        _dirty = false;

        Ctx.SetPipelineState(_pipeline.Native);

        // только те ресурсы, которые пайплайн объявил: лишнее в шейдер не попадает
        var srb = _pipeline.Srb;
        foreach (var name in _pipeline.Desc.PixelTextures)
        {
            if (!_textures.TryGetValue(name, out var texture)) continue;
            srb.GetVariableByName(ShaderType.Pixel, name + DiligentRenderDevice.ResourceSuffix)
               ?.Set(texture.Native.GetDefaultView(TextureViewType.ShaderResource), SetShaderResourceFlags.None);
        }
        foreach (var name in _pipeline.Desc.ConstantBuffers)
        {
            if (!_constants.TryGetValue(name, out var buffer)) continue;
            srb.GetVariableByName(ShaderType.Vertex, "cb_" + name + DiligentRenderDevice.ResourceSuffix)
               ?.Set(buffer, SetShaderResourceFlags.None);
        }
        Ctx.CommitShaderResources(srb, ResourceStateTransitionMode.Transition);

        // вершинные слоты до последнего занятого; пропуски недопустимы
        int slots = 0;
        for (int s = 0; s < MaxVertexSlots; s++)
            if (_vertex[s] != null) slots = s + 1;
        if (slots > 0)
        {
            var buffers = new IBuffer[slots];
            var offsets = new ulong[slots];
            for (int s = 0; s < slots; s++)
            {
                buffers[s] = _vertex[s]?.Native
                    ?? throw new InvalidOperationException($"Vertex slot {s} is empty but a later slot is bound.");
                offsets[s] = _vertexOffsets[s];
            }
            Ctx.SetVertexBuffers(0, buffers, offsets, ResourceStateTransitionMode.Transition, SetVertexBuffersFlags.Reset);
        }

        if (_index != null)
            Ctx.SetIndexBuffer(_index.Native, _indexOffset, ResourceStateTransitionMode.Transition);
    }

    private Diligent.ValueType IndexType() => _indexUsesUInt32 ? Diligent.ValueType.UInt32 : Diligent.ValueType.UInt16;

    private static DiligentBuffer AsBuffer(IGpuBuffer buffer) =>
        buffer as DiligentBuffer ?? throw new ArgumentException("Buffer was not created by this render device.", nameof(buffer));
}
