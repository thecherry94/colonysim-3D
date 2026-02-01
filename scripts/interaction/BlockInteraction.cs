using Godot;
using ColonySim.World;

namespace ColonySim.Interaction;

/// <summary>
/// Handles mouse interaction for block placement/removal.
/// Attach as child of World node.
/// </summary>
public partial class BlockInteraction : Node3D
{
    /// <summary>
    /// Maximum raycast distance for block selection.
    /// </summary>
    [Export]
    public float MaxRayDistance { get; set; } = 100f;

    private World.World? _world;
    private Camera3D? _camera;

    public override void _Ready()
    {
        // Get reference to World (parent node)
        _world = GetParent() as World.World;
        if (_world == null)
        {
            GD.PrintErr("BlockInteraction: Parent is not a World node!");
            return;
        }

        // Get camera from viewport
        _camera = GetViewport().GetCamera3D();
        if (_camera == null)
        {
            GD.PrintErr("BlockInteraction: No Camera3D found in viewport!");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_world == null || _camera == null)
            return;

        // Skip input handling in editor
        if (Engine.IsEditorHint())
            return;

        // Left click = remove block
        if (Input.IsActionJustPressed("click_left"))
        {
            TryModifyBlock(remove: true);
        }
        // Right click = place block
        else if (Input.IsActionJustPressed("click_right"))
        {
            TryModifyBlock(remove: false);
        }
    }

    /// <summary>
    /// Performs raycast from mouse position and modifies the hit block.
    /// </summary>
    private void TryModifyBlock(bool remove)
    {
        var mousePos = GetViewport().GetMousePosition();
        var rayOrigin = _camera!.ProjectRayOrigin(mousePos);
        var rayDirection = _camera.ProjectRayNormal(mousePos);
        var rayEnd = rayOrigin + rayDirection * MaxRayDistance;

        var spaceState = GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
        var result = spaceState.IntersectRay(query);

        if (result.Count == 0)
            return;

        var hitPos = result["position"].AsVector3();
        var hitNormal = result["normal"].AsVector3();

        if (remove)
        {
            // Remove the block that was hit
            // Offset slightly into the block to get correct position
            var blockPos = (hitPos - hitNormal * 0.1f).Floor();
            var worldBlockPos = new Vector3I((int)blockPos.X, (int)blockPos.Y, (int)blockPos.Z);
            _world!.SetBlock(worldBlockPos, BlockType.Air);
            GD.Print($"Removed block at {worldBlockPos}");
        }
        else
        {
            // Place block adjacent to the hit face
            var blockPos = (hitPos + hitNormal * 0.1f).Floor();
            var worldBlockPos = new Vector3I((int)blockPos.X, (int)blockPos.Y, (int)blockPos.Z);
            _world!.SetBlock(worldBlockPos, BlockType.Dirt);
            GD.Print($"Placed block at {worldBlockPos}");
        }
    }
}
