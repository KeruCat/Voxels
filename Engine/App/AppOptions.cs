using Engine.Graphics;

namespace Engine.App;
internal sealed record AppOptions (
    GraphicsApi Api,
    int Fps,
    bool Validation,
    bool VSync
);
