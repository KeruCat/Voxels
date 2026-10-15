namespace Game.World;

/// <summary>
/// Хранилище компонентов: инвентарь сундука, текст таблички и т.п. Вокселю достаточно ссылки (VoxelMeta.Ref).
/// Ссылка 0 — пусто. Компоненты здесь управляемые объекты: это данные игры, а не чанка.
/// Сохранение на диск пока не реализовано.
/// </summary>
public sealed class ComponentStore
{
    private readonly Dictionary<uint, object> _items = new();
    private uint _next = 1;

    public int Count => _items.Count;

    public uint Add(object component)
    {
        uint id = _next++;
        _items[id] = component;
        return id;
    }

    public bool TryGet<T>(uint id, out T? component) where T : class
    {
        if (_items.TryGetValue(id, out var obj) && obj is T typed)
        {
            component = typed;
            return true;
        }
        component = null;
        return false;
    }

    public void Remove(uint id) => _items.Remove(id);
}
