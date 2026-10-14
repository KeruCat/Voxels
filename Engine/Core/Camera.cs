namespace Engine.Core;

/// <summary>Углы взгляда и параметры проекции. Позиция — у игрока (double, мировая).</summary>
public sealed class Camera
{
    public float Yaw, Pitch;
    public float FovY = 1.047f, Near = 0.2f, Far = 10000f;
}
