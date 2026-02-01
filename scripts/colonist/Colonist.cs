using Godot;

namespace ColonySim.Colonist;

/// <summary>
/// Basic colonist with navigation pathfinding and jump ability.
/// Uses NavigationAgent3D to move across walkable terrain.
/// Can jump up 1-block height differences via NavigationLinks.
/// </summary>
public partial class Colonist : CharacterBody3D
{
    /// <summary>
    /// Movement speed in units per second.
    /// </summary>
    [Export]
    public float MovementSpeed { get; set; } = 5.0f;

    /// <summary>
    /// Upward velocity applied when jumping.
    /// </summary>
    [Export]
    public float JumpVelocity { get; set; } = 6.0f;

    /// <summary>
    /// Gravity acceleration (units per second squared).
    /// </summary>
    [Export]
    public float Gravity { get; set; } = 20.0f;

    /// <summary>
    /// Reference to the NavigationAgent3D child node.
    /// </summary>
    private NavigationAgent3D _navigationAgent = null!;

    /// <summary>
    /// Flag to trigger jump on next physics frame when on floor.
    /// </summary>
    private bool _shouldJump = false;

    public override void _Ready()
    {
        _navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");

        // Connect to navigation signals
        _navigationAgent.TargetReached += OnTargetReached;
        _navigationAgent.LinkReached += OnLinkReached;
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 velocity = Velocity;

        // Apply gravity when not on floor
        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * (float)delta;
        }
        else if (_shouldJump)
        {
            // Execute jump when on floor
            velocity.Y = JumpVelocity;
            _shouldJump = false;
            GD.Print("Colonist jumped!");
        }

        // Handle horizontal movement
        if (!_navigationAgent.IsNavigationFinished())
        {
            Vector3 nextPosition = _navigationAgent.GetNextPathPosition();
            Vector3 direction = GlobalPosition.DirectionTo(nextPosition);

            // Only use horizontal direction for movement
            direction.Y = 0;
            if (direction.LengthSquared() > 0.001f)
            {
                direction = direction.Normalized();
                velocity.X = direction.X * MovementSpeed;
                velocity.Z = direction.Z * MovementSpeed;
            }
        }
        else
        {
            // Stop horizontal movement when done
            velocity.X = 0;
            velocity.Z = 0;
        }

        Velocity = velocity;
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

    private void OnLinkReached(Godot.Collections.Dictionary details)
    {
        // NavigationLink reached - trigger jump if going up
        Vector3 linkStart = (Vector3)details["link_entry_position"];
        Vector3 linkEnd = (Vector3)details["link_exit_position"];

        if (linkEnd.Y > linkStart.Y + 0.5f)
        {
            // Going up - need to jump
            _shouldJump = true;
            GD.Print($"Jump link reached: {linkStart.Y:F1} -> {linkEnd.Y:F1}");
        }
        // Going down - gravity handles it automatically
    }
}
