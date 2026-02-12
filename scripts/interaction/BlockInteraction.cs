namespace ColonySim;

using Godot;

/// <summary>
/// Handles mouse raycast for block identification and modification.
/// Left-click: remove block. Right-click: (reserved for colonist commands later).
/// </summary>
public partial class BlockInteraction : Node
{
    private Camera3D _camera;
    private World _world;
    private Colonist _colonist;
    private float _rayLength = 200.0f;

    public void Initialize(Camera3D camera, World world, Colonist colonist, int chunkRenderDistance = 5)
    {
        _camera = camera;
        _world = world;
        _colonist = colonist;
        _rayLength = (2 * chunkRenderDistance + 1) * Chunk.SIZE;
        GD.Print($"BlockInteraction initialized (ray length={_rayLength})");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                TryRemoveBlock(mouseButton.Position);
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                TryCommandColonist(mouseButton.Position);
            }
        }
    }

    private void TryCommandColonist(Vector2 screenPos)
    {
        if (_camera == null || _colonist == null) return;

        var from = _camera.ProjectRayOrigin(screenPos);
        var dir = _camera.ProjectRayNormal(screenPos);
        var to = from + dir * _rayLength;

        var spaceState = _camera.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        // When slicing, pierce through blocks above the slice level
        int pierceAttempts = 0;
        while (result != null && result.Count > 0 && pierceAttempts < 10)
        {
            var pos = (Vector3)result["position"];
            if (!SliceState.Enabled || pos.Y <= SliceState.YLevel + 0.5f)
                break; // Valid hit below slice

            // Hit is above slice — continue ray past this point
            from = pos + dir * 0.1f;
            query = PhysicsRayQueryParameters3D.Create(from, to);
            result = spaceState.IntersectRay(query);
            pierceAttempts++;
        }

        if (result == null || result.Count == 0) return;

        var hitPos = (Vector3)result["position"];
        GD.Print($"Right-click: commanding colonist to {hitPos}");
        _colonist.SetDestination(hitPos);
    }

    private void TryRemoveBlock(Vector2 screenPos)
    {
        if (_camera == null || _world == null) return;

        var from = _camera.ProjectRayOrigin(screenPos);
        var dir = _camera.ProjectRayNormal(screenPos);
        var to = from + dir * _rayLength;

        var spaceState = _camera.GetWorld3D().DirectSpaceState;
        var query = PhysicsRayQueryParameters3D.Create(from, to);
        var result = spaceState.IntersectRay(query);

        // When slicing, pierce through blocks above the slice level
        int pierceAttempts = 0;
        while (result != null && result.Count > 0 && pierceAttempts < 10)
        {
            var pos = (Vector3)result["position"];
            if (!SliceState.Enabled || pos.Y <= SliceState.YLevel + 0.5f)
                break; // Valid hit below slice

            // Hit is above slice — continue ray past this point
            from = pos + dir * 0.1f;
            query = PhysicsRayQueryParameters3D.Create(from, to);
            result = spaceState.IntersectRay(query);
            pierceAttempts++;
        }

        if (result == null || result.Count == 0) return;

        var hitPos = (Vector3)result["position"];
        var hitNormal = (Vector3)result["normal"];

        // Offset slightly INTO the block (against the normal) to identify which block was hit
        var blockWorldPos = hitPos - hitNormal * 0.1f;
        var worldBlock = new Vector3I(
            Mathf.FloorToInt(blockWorldPos.X),
            Mathf.FloorToInt(blockWorldPos.Y),
            Mathf.FloorToInt(blockWorldPos.Z)
        );

        var blockType = _world.GetBlock(worldBlock);
        if (blockType == BlockType.Air) return;

        GD.Print($"Removed block at ({worldBlock.X}, {worldBlock.Y}, {worldBlock.Z}) — was {blockType}");
        _world.SetBlock(worldBlock, BlockType.Air);

        // Regenerate the affected chunk
        var chunkCoord = World.WorldToChunkCoord(worldBlock);
        _world.RegenerateChunkMesh(chunkCoord);

        // Also regenerate adjacent chunks if the block is on a chunk boundary
        var local = World.WorldToLocalCoord(worldBlock);
        if (local.X == 0)  _world.RegenerateChunkMesh(chunkCoord + new Vector3I(-1, 0, 0));
        if (local.X == 15) _world.RegenerateChunkMesh(chunkCoord + new Vector3I(1, 0, 0));
        if (local.Y == 0)  _world.RegenerateChunkMesh(chunkCoord + new Vector3I(0, -1, 0));
        if (local.Y == 15) _world.RegenerateChunkMesh(chunkCoord + new Vector3I(0, 1, 0));
        if (local.Z == 0)  _world.RegenerateChunkMesh(chunkCoord + new Vector3I(0, 0, -1));
        if (local.Z == 15) _world.RegenerateChunkMesh(chunkCoord + new Vector3I(0, 0, 1));
    }
}
