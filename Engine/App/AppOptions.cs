using Engine.Graphics;

namespace Engine;
internal sealed record AppOptions (
    GraphicsApi Api,
    int Fps,
    bool Validation,
    bool VSync
);
