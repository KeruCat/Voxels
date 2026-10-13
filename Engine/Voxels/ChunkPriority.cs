using System.Numerics;
using Engine.Core;

namespace Engine.Voxels;

/// <summary>
/// Приоритет чанка (меньше = раньше): расстояние до камеры, ослабленное для чанков по ходу взгляда.
/// Один и тот же порядок используют генерация, меширование и загрузка в GPU — иначе чанки
/// готовились бы «как попало» (в порядке хранения), и при быстром полёте ближняя территория отставала бы от дальней.
/// StreamingFocus — в ЛОКАЛЬНЫХ координатах (относительно floating origin), поэтому чанки считаются от origin.
/// </summary>
internal readonly struct ChunkPriority
{
    private const float LookBias = 0.5f;    // 0 = только расстояние
    private const float Half = Chunk.Size * 0.5f;

    private readonly Vector3 _pos, _fwd;
    private readonly int _ox, _oy, _oz;

    public ChunkPriority(FloatingOrigin origin)
    {
        _pos = StreamingFocus.Position;
        Vector3 f = StreamingFocus.Forward;
        float fl = f.Length();
        _fwd = fl > 1e-6f ? f / fl : -Vector3.UnitZ;
        (_ox, _oy, _oz) = (origin.X, origin.Y, origin.Z);
    }

    public float Of(int cx, int cy, int cz)
    {
        var d = new Vector3((cx - _ox) * Chunk.Size + Half - _pos.X,
                            (cy - _oy) * Chunk.Size + Half - _pos.Y,
                            (cz - _oz) * Chunk.Size + Half - _pos.Z);
        float dist = d.Length();
        float cos = dist > Chunk.Size ? Vector3.Dot(d / dist, _fwd) : 1f;
        return dist * (1f - LookBias * cos);
    }
}
