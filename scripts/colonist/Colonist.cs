using Godot;

namespace ColonySim.Colonist;

/// <summary>
/// Basic colonist with navigation pathfinding.
/// Uses NavigationAgent3D to move across walkable terrain.
/// </summary>
public partial class Colonist : CharacterBody3D
{
    /// <summary>
    /// Movement speed in units per second.
    /// </summary>
    [Export]
    public float MovementSpeed { get; set; } = 5.0f;

    /// <summary>
    /// Reference to the NavigationAgent3D child node.
    /// </summary>
    private NavigationAgent3D _navigationAgent = null!;

    public override void _Ready()
    {
        _navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");

        // Connect to target_reached signal for logging/debugging
        _navigationAgent.TargetReached += OnTargetReached;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Don't move if we've reached destination
        if (_navigationAgent.IsNavigationFinished())
        {
            Velocity = Vector3.Zero;
            return;
        }

        // Get next position to move toward
        Vector3 nextPosition = _navigationAgent.GetNextPathPosition();

        // Calculate direction and set velocity
        Vector3 direction = GlobalPosition.DirectionTo(nextPosition);
        Velocity = direction * MovementSpeed;

        // Move the character
        MoveAndSlide();
    }

    /// <summary>
    /// Sets the colonist's destination for pathfinding.
    /// </summary>
    public void SetDestination(Vector3 targetPosition)
    {
        _navigationAgent.TargetPosition = targetPosition;
        GD.Print($"Colonist moving to {targetPosition}");
    }

    private void OnTargetReached()
    {
        GD.Print("Colonist reached destination!");
    }
}
