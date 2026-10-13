using System.Runtime.InteropServices;

namespace Engine.Graphics;

/// <summary>Раскладка совпадает с аргументами DrawIndexedIndirect (20 байт).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawIndexedCmd
{
    public uint IndexCount;
    public uint InstanceCount;
    public uint FirstIndex;
    public int BaseVertex;
    public uint FirstInstance;     // индекс в instance-буфере с origin чанка
}