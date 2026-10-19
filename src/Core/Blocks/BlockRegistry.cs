namespace ColonySim.Core;

public sealed class BlockRegistry
{
    readonly List<BlockDef> _defs = new();
    readonly Dictionary<string, ushort> _byName = new();
    readonly List<string> _textures = new();
    readonly Dictionary<string, int> _texIndex = new();

    public IReadOnlyList<BlockDef> All => _defs;
    public IReadOnlyList<string> TextureNames => _textures;

    public BlockDef Get(ushort id) => _defs[id];
    public bool TryGetByName(string name, out ushort id) => _byName.TryGetValue(name, out id);
    public int TextureIndex(string name) => _texIndex[name];

    /// <summary>Registers a block; ids are assigned sequentially, texture indices in order of first use. `baseBlock` defaults to the block's own id. Null textures map to index 0 only for air.</summary>
    internal ushort Add(string name, BlockShape shape, bool solid, bool diggable,
        string? top = null, string? side = null, string? bottom = null, Facing facing = Facing.South,
        bool natural = false, float hardness = 0f, int? baseBlock = null)
    {
        if (_byName.ContainsKey(name)) throw new ArgumentException($"Duplicate block name '{name}'");
        ushort id = (ushort)_defs.Count;
        int t = top == null ? 0 : Tex(top);
        int s = side == null ? t : Tex(side);
        int b = bottom == null ? t : Tex(bottom);
        _defs.Add(new BlockDef(id, name, shape, solid, diggable, t, s, b, facing, natural, hardness, (ushort)(baseBlock ?? id)));
        _byName[name] = id;
        return id;
    }

    int Tex(string name)
    {
        if (!_texIndex.TryGetValue(name, out int i))
        {
            i = _textures.Count;
            _textures.Add(name);
            _texIndex[name] = i;
        }
        return i;
    }
}
