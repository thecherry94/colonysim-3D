using Godot;
using System.Collections.Generic;

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
    /// StaticBody3D for chunk collision.
    /// </summary>
    private StaticBody3D _staticBody = null!;

    /// <summary>
    /// CollisionShape3D child of the StaticBody3D.
    /// </summary>
    private CollisionShape3D _collisionShape = null!;

    /// <summary>
    /// NavigationRegion3D for per-chunk pathfinding.
    /// </summary>
    private NavigationRegion3D _navigationRegion = null!;

    /// <summary>
    /// NavigationLink3D nodes for height transitions within this chunk.
    /// </summary>
    private List<NavigationLink3D> _navigationLinks = new();

    /// <summary>
    /// Flag indicating the mesh needs regeneration.
    /// </summary>
    private bool _isDirty = true;

    public override void _Ready()
    {
        InitializeMaterials();
        InitializeMeshInstance();
        InitializeCollision();
        InitializeNavigation();

        // World handles terrain generation via SetBlock() calls
        // Mesh will be generated when World calls ForceRegenerateMesh()
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
    /// Creates or finds the StaticBody3D and CollisionShape3D nodes.
    /// </summary>
    private void InitializeCollision()
    {
        // Check if we already have a StaticBody3D child (editor reload case)
        foreach (var child in GetChildren())
        {
            if (child is StaticBody3D existingBody)
            {
                _staticBody = existingBody;
                foreach (var bodyChild in _staticBody.GetChildren())
                {
                    if (bodyChild is CollisionShape3D existingShape)
                    {
                        _collisionShape = existingShape;
                        return;
                    }
                }
            }
        }

        // Create new StaticBody3D
        _staticBody = new StaticBody3D();
        _staticBody.Name = "ChunkCollision";
        AddChild(_staticBody);

        // Create CollisionShape3D inside StaticBody3D
        _collisionShape = new CollisionShape3D();
        _collisionShape.Name = "CollisionShape";
        _staticBody.AddChild(_collisionShape);

        // In editor, set owner so it saves with the scene
        if (Engine.IsEditorHint())
        {
            _staticBody.Owner = GetTree().EditedSceneRoot;
            _collisionShape.Owner = GetTree().EditedSceneRoot;
        }
    }

    /// <summary>
    /// Creates or finds the NavigationRegion3D node.
    /// </summary>
    private void InitializeNavigation()
    {
        // Check if we already have a NavigationRegion3D child (editor reload case)
        foreach (var child in GetChildren())
        {
            if (child is NavigationRegion3D existingRegion)
            {
                _navigationRegion = existingRegion;
                return;
            }
        }

        // Create new NavigationRegion3D
        _navigationRegion = new NavigationRegion3D();
        _navigationRegion.Name = "ChunkNavigation";
        AddChild(_navigationRegion);

        // In editor, set owner so it saves with the scene
        if (Engine.IsEditorHint())
        {
            _navigationRegion.Owner = GetTree().EditedSceneRoot;
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

        // Regenerate collision to match the new mesh
        RegenerateCollision();

        // Regenerate navigation to match the new terrain
        RegenerateNavigation();

        _isDirty = false;
    }

    /// <summary>
    /// Forces mesh regeneration regardless of dirty flag.
    /// Called by World after terrain generation.
    /// </summary>
    public void ForceRegenerateMesh()
    {
        _isDirty = true;
        RegenerateMesh();
    }

    /// <summary>
    /// Regenerates the chunk collision shape from the current mesh.
    /// Must be called after RegenerateMesh() when _meshInstance.Mesh is valid.
    /// </summary>
    private void RegenerateCollision()
    {
        var mesh = _meshInstance.Mesh as ArrayMesh;
        if (mesh == null || mesh.GetSurfaceCount() == 0)
        {
            _collisionShape.Shape = null;
            return;
        }

        // Collect all faces from all surfaces into flat vertex list
        var collisionVertices = new System.Collections.Generic.List<Vector3>();

        for (int surfaceIdx = 0; surfaceIdx < mesh.GetSurfaceCount(); surfaceIdx++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIdx);
            var vertices = (Vector3[])arrays[(int)Mesh.ArrayType.Vertex];
            var indices = (int[])arrays[(int)Mesh.ArrayType.Index];

            // Expand indexed triangles to flat vertex list
            // ConcavePolygonShape3D expects sequential triples: [v0, v1, v2, v3, v4, v5, ...]
            for (int i = 0; i < indices.Length; i++)
            {
                collisionVertices.Add(vertices[indices[i]]);
            }
        }

        if (collisionVertices.Count == 0)
        {
            _collisionShape.Shape = null;
            return;
        }

        // Create the collision shape
        var shape = new ConcavePolygonShape3D();
        shape.BackfaceCollision = true;  // Collide from both sides
        shape.SetFaces(collisionVertices.ToArray());

        _collisionShape.Shape = shape;

        GD.Print($"Collision: {collisionVertices.Count / 3} triangles");
    }

    /// <summary>
    /// Regenerates the chunk navigation mesh from block data.
    /// </summary>
    private void RegenerateNavigation()
    {
        var navMesh = ChunkNavigation.GenerateNavigationMesh(this);
        _navigationRegion.NavigationMesh = navMesh;

        // Also regenerate navigation links for height transitions
        RegenerateNavigationLinks();

        GD.Print($"Navigation: {navMesh.GetPolygonCount()} walkable surfaces");
    }

    /// <summary>
    /// Regenerates NavigationLink3D nodes for 1-block height transitions.
    /// </summary>
    private void RegenerateNavigationLinks()
    {
        // Remove old links
        foreach (var link in _navigationLinks)
        {
            link.QueueFree();
        }
        _navigationLinks.Clear();

        // Find and create new links
        var heightLinks = ChunkNavigation.FindHeightLinks(this);

        foreach (var heightLink in heightLinks)
        {
            var link = new NavigationLink3D();
            link.StartPosition = heightLink.LowerPosition;
            link.EndPosition = heightLink.UpperPosition;
            link.Bidirectional = true;
            link.EnterCost = 1.0f;   // Same as flat travel (robust jumping makes vertical traversal reliable)
            link.TravelCost = 1.0f;
            AddChild(link);
            _navigationLinks.Add(link);
        }

        if (heightLinks.Count > 0)
        {
            GD.Print($"Navigation links: {heightLinks.Count} height transitions");
        }
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
