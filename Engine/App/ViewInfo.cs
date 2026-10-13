using System.Numerics;

/// <summary>Точка интереса для стриминга: позиция игрока и направление взгляда (мировые координаты).</summary>
internal static class StreamingFocus
{
    public static Vector3 Position;
    public static Vector3 Forward = -Vector3.UnitZ;
}