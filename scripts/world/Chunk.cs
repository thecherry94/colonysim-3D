using Godot;

namespace ColonySim.World;

/// <summary>
/// Represents a single 16x16x16 chunk of voxel data.
/// Manages block storage, mesh generation, and rendering.
/// [Tool] attribute enables editor preview.
/// </summary>
[Tool]
public partial class Chunk : Node3D
{
    /// <summary>
    /// Chunk dimensions (16x16x16 blocks).
    /// </summary>
    public const int SIZE = 16;

    /// <summary>
    /// 3D array storing block data: blocks[x, y, z] where x,y,z are 0-15.
    /// Y is up in Godot's coordinate system.
    /// </summary>
    private BlockType[,,] _blocks = new BlockType[SIZE, SIZE, SIZE];

    /// <summary>
    /// MeshInstance3D child node for rendering the chunk geometry.
    /// Initialized in _Ready().
    /// </summary>
    private MeshInstance3D _meshInstance = null!;

    /// <summary>
    /// Materials array indexed by BlockType. Index 0 (Air) is unused.
    /// Initialized in _Ready().
    /// </summary>
    private StandardMaterial3D[] _materials = null!;

    /// <summary>
    /// Flag indicating the mesh needs regeneration.
    /// </summary>
    private bool _isDirty = true;

    public override void _Ready()
    {
        InitializeMaterials();
        InitializeMeshInstance();

        // For testing: fill with some blocks
        FillTestData();

        // Generate initial mesh
        RegenerateMesh();
    }

    /// <summary>
    /// Creates materials for each block type.
    /// </summary>
    private void InitializeMaterials()
    {
        // Create one material per block type (including unused Air slot)
        _materials = new StandardMaterial3D[4];

        for (int i = 1; i < _materials.Length; i++)
        {
            _materials[i] = new StandardMaterial3D
            {
                AlbedoColor = BlockData.GetColor((BlockType)i),
                CullMode = BaseMaterial3D.CullModeEnum.Back,  // Standard back-face culling (winding fixed in indices)
            };
        }
    }

    /// <summary>
    /// Creates or finds the MeshInstance3D child node.
    /// </summary>
    private void InitializeMeshInstance()
    {
        // Check if we already have a MeshInstance3D child (editor reload case)
        foreach (var child in GetChildren())
        {
            if (child is MeshInstance3D existingMesh)
            {
                _meshInstance = existingMesh;
                return;
            }
        }

        // Create new MeshInstance3D
        _meshInstance = new MeshInstance3D();
        _meshInstance.Name = "ChunkMesh";
        AddChild(_meshInstance);

        // In editor, set owner so it saves with the scene
        if (Engine.IsEditorHint())
        {
            _meshInstance.Owner = GetTree().EditedSceneRoot;
        }
    }

    /// <summary>
    /// Gets the block type at the specified local position.
    /// Returns Air if the position is out of bounds.
    /// </summary>
    /// <param name="x">X coordinate (0-15)</param>
    /// <param name="y">Y coordinate (0-15)</param>
    /// <param name="z">Z coordinate (0-15)</param>
    /// <returns>The block type, or Air if out of bounds</returns>
    public BlockType GetBlock(int x, int y, int z)
    {
        if (!IsInBounds(x, y, z))
            return BlockType.Air;

        return _blocks[x, y, z];
    }

    /// <summary>
    /// Sets the block type at the specified local position.
    /// Marks the chunk as dirty for mesh regeneration.
    /// </summary>
    /// <param name="x">X coordinate (0-15)</param>
    /// <param name="y">Y coordinate (0-15)</param>
    /// <param name="z">Z coordinate (0-15)</param>
    /// <param name="type">The block type to set</param>
    public void SetBlock(int x, int y, int z, BlockType type)
    {
        if (!IsInBounds(x, y, z))
            return;

        _blocks[x, y, z] = type;
        _isDirty = true;
    }

    /// <summary>
    /// Checks if coordinates are within chunk bounds.
    /// </summary>
    public static bool IsInBounds(int x, int y, int z)
    {
        return x >= 0 && x < SIZE &&
               y >= 0 && y < SIZE &&
               z >= 0 && z < SIZE;
    }

    /// <summary>
    /// Regenerates the chunk mesh from block data.
    /// Call this after modifying blocks.
    /// </summary>
    public void RegenerateMesh()
    {
        if (!_isDirty)
            return;

        // Generate mesh using the static generator
        ArrayMesh mesh = ChunkMeshGenerator.GenerateMesh(this, _materials);
        _meshInstance.Mesh = mesh;

        _isDirty = false;
    }

    /// <summary>
    /// Fills the chunk with test data for initial visualization.
    /// Bottom 4 layers are Stone, layer 5 is Dirt, layer 6 is Grass.
    /// Includes a hole to verify face culling.
    /// </summary>
    private void FillTestData()
    {
        for (int x = 0; x < SIZE; x++)
        {
            for (int z = 0; z < SIZE; z++)
            {
                // Layers 0-3: Stone
                for (int y = 0; y < 4; y++)
                {
                    _blocks[x, y, z] = BlockType.Stone;
                }

                // Layer 4: Dirt
                _blocks[x, 4, z] = BlockType.Dirt;

                // Layer 5: Grass (top surface)
                _blocks[x, 5, z] = BlockType.Grass;

                // Layers 6-15: Air (default, already initialized)
            }
        }

        // Add a hole to verify face culling works correctly
        // This creates a 3-deep pit showing Stone, Dirt, and Grass layers
        _blocks[8, 5, 8] = BlockType.Air;  // Hole in grass
        _blocks[8, 4, 8] = BlockType.Air;  // Hole in dirt
        _blocks[8, 3, 8] = BlockType.Air;  // Hole in stone

        _isDirty = true;
    }
}
