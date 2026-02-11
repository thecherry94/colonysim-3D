namespace ColonySim;

using Godot;

[Tool]
public partial class Main : Node3D
{
    private World _world;

    public override void _Ready()
    {
        GD.Print("=== ColonySim Starting ===");

        SetupWorld();

        if (!Engine.IsEditorHint())
        {
            // Runtime only: reposition camera, spawn colonist, set up interaction
            var camera = GetNode<Camera3D>("Camera3D");
            camera.Position = new Vector3(8, 30, 45);
            camera.LookAt(new Vector3(8, 4, 8), Vector3.Up);

            // Spawn colonist
            var pathfinder = new VoxelPathfinder(_world);
            var colonist = new Colonist();
            colonist.Name = "Colonist";
            colonist.Position = new Vector3(8, 15, 8); // Drop above terrain, will fall to surface
            AddChild(colonist);
            colonist.Initialize(_world, pathfinder);

            // Block interaction: left-click remove, right-click command colonist
            var blockInteraction = new BlockInteraction();
            blockInteraction.Name = "BlockInteraction";
            AddChild(blockInteraction);
            blockInteraction.Initialize(camera, _world, colonist);
        }
    }

    private void SetupWorld()
    {
        _world = new World();
        _world.Name = "World";
        AddChild(_world);

        // Load a 3x3 grid of chunks centered at origin
        _world.LoadChunkArea(Vector3I.Zero, 1);
        GD.Print("World initialized: 3x3 chunk grid (48x48 blocks)");
    }
}
