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
            // Disable the editor camera — RTS camera takes over
            var editorCamera = GetNodeOrNull<Camera3D>("Camera3D");
            if (editorCamera != null)
                editorCamera.QueueFree();

            // RTS camera: pivot centered on world, camera orbits around it
            var cameraController = new CameraController();
            cameraController.Name = "CameraController";
            cameraController.Position = new Vector3(40, 6, 40); // Pivot at world center
            AddChild(cameraController);
            var camera = cameraController.Camera;

            // Spawn colonist
            var pathfinder = new VoxelPathfinder(_world);
            var colonist = new Colonist();
            colonist.Name = "Colonist";
            colonist.Position = new Vector3(40, 15, 40); // Drop above terrain, will fall to surface
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
        // In editor, remove any previously-created World to avoid duplicates on re-run
        if (Engine.IsEditorHint())
        {
            var existing = GetNodeOrNull<World>("World");
            if (existing != null)
            {
                RemoveChild(existing);
                existing.QueueFree();
            }
        }

        _world = new World();
        _world.Name = "World";
        AddChild(_world);

        if (Engine.IsEditorHint())
            _world.Owner = GetTree().EditedSceneRoot;

        // Load a 5x5 grid of chunks (80x80 blocks)
        _world.LoadChunkArea(new Vector3I(2, 0, 2), 2);
        GD.Print("World initialized: 5x5 chunk grid (80x80 blocks)");
    }
}
