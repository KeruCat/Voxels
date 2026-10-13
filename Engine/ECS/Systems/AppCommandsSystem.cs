using Engine.Input;
using Silk.NET.Input;
using Silk.NET.Windowing;
using Friflo.Engine.ECS.Systems;

namespace Engine.ECS.Systems;
internal sealed class AppCommandsSystem(IWindow window, InputState input) : BaseSystem
{
    protected override void OnUpdateGroup()
    {
        if (input.KeyPressed(Key.Escape)) window.Close();
    }
}
