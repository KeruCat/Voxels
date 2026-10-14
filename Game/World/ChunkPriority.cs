using System.Numerics;

namespace Game.World;

/// <summary>
/// Приоритет чанка (меньше = раньше): расстояние до игрока, ослабленное для чанков по направлению взгляда.
/// Считается от floating origin: focusLocal — позиция игрока в локальных координатах (float, маленькая и точная).
/// </summary>
public readonly struct ChunkPriority
{
    private const float LookBias = 0.5f;
    private const float Half = Chunk.Size * 0.5f;

    private readonly Vector3 _focus, _forward;
    private readonly int _ox, _oy, _oz;

    public ChunkPriority(int originX, int originY, int originZ, Vector3 focusLocal, Vector3 forward)
    {
        (_ox, _oy, _oz) = (originX, originY, originZ);
        _focus = focusLocal;
        float fl = forward.Length();
        _forward = fl > 1e-6f ? forward / fl : -Vector3.UnitZ;
    }

    public float Of(ChunkKey k)
    {
        var d = new Vector3((k.X - _ox) * Chunk.Size + Half - _focus.X,
                            (k.Y - _oy) * Chunk.Size + Half - _focus.Y,
                            (k.Z - _oz) * Chunk.Size + Half - _focus.Z);
        float dist = d.Length();
        float cos = dist > Chunk.Size ? Vector3.Dot(d / dist, _forward) : 1f;
        return dist * (1f - LookBias * cos);
    }
}
