using Godot;

namespace ColonySim.Colonist;

/// <summary>
/// Movement states for the colonist's state machine.
/// </summary>
public enum MovementState
{
    Idle,           // Not moving
    Walking,        // Normal ground movement following path
    PreparingJump,  // Detected jump ahead, moving to precise launch point
    Jumping,        // Airborne executing jump arc
    Landing,        // Just landed, resuming normal movement
}

/// <summary>
/// Stores details about a planned jump.
/// </summary>
public class JumpPlan
{
    public Vector3 LaunchPosition;    // Where to start jump
    public Vector3 TargetPosition;    // Where to land
    public float HorizontalDistance;  // XZ distance
    public float HeightDelta;         // Y difference
    public Vector3 LaunchVelocity;    // Calculated initial velocity (XZ + Y)
    public bool IsActive;
}

/// <summary>
/// Advanced colonist with predictive navigation pathfinding and physics-based jumping.
/// Uses NavigationAgent3D for pathfinding and a state machine for reliable height traversal.
/// </summary>
public partial class Colonist : CharacterBody3D
{
    // Movement parameters
    /// <summary>
    /// Movement speed in units per second.
    /// </summary>
    [Export]
    public float MovementSpeed { get; set; } = 5.0f;

    /// <summary>
    /// Gravity acceleration (units per second squared).
    /// </summary>
    [Export]
    public float Gravity { get; set; } = 20.0f;

    // Jump detection parameters
    /// <summary>
    /// Start preparing for jump this many units ahead.
    /// </summary>
    [Export]
    public float JumpDetectionDistance { get; set; } = 2.0f;

    /// <summary>
    /// Minimum height difference to consider as jump.
    /// </summary>
    [Export]
    public float JumpHeightThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Maximum horizontal distance for vertical transitions.
    /// </summary>
    [Export]
    public float JumpMaxHorizontalDist { get; set; } = 1.5f;

    // Jump execution parameters
    /// <summary>
    /// How close to launch point before jumping (in units).
    /// </summary>
    [Export]
    public float LaunchAlignmentTolerance { get; set; } = 0.1f;

    /// <summary>
    /// Extra height for jump clearance.
    /// </summary>
    [Export]
    public float JumpHeightMargin { get; set; } = 0.3f;

    /// <summary>
    /// Time multiplier for jump arc (safety margin).
    /// </summary>
    [Export]
    public float JumpTimeMultiplier { get; set; } = 1.1f;

    /// <summary>
    /// Maximum time allowed airborne before forcing landing.
    /// </summary>
    [Export]
    public float JumpTimeout { get; set; } = 2.0f;

    // State machine fields
    /// <summary>
    /// Current movement state.
    /// </summary>
    private MovementState _movementState = MovementState.Idle;

    /// <summary>
    /// Active jump plan (null when not jumping).
    /// </summary>
    private JumpPlan? _activeJumpPlan = null;

    /// <summary>
    /// Time when jump started (for timeout detection).
    /// </summary>
    private double _jumpStartTime = 0.0;

    /// <summary>
    /// Last completed jump launch position (to prevent retriggering same jump).
    /// </summary>
    private Vector3? _lastJumpLaunch = null;

    /// <summary>
    /// Minimum distance from last jump launch before allowing new jumps.
    /// </summary>
    [Export]
    public float JumpCooldownDistance { get; set; } = 0.5f;

    // Navigation fields
    /// <summary>
    /// Reference to the NavigationAgent3D child node.
    /// </summary>
    private NavigationAgent3D _navigationAgent = null!;

    /// <summary>
    /// Flag to track if NavigationAgent is ready (needs at least 1 physics frame).
    /// </summary>
    private bool _navigationReady = false;

    /// <summary>
    /// Timer to periodically log navigation state for debugging.
    /// </summary>
    private float _debugTimer = 0f;

    // Stuck detection fields
    /// <summary>
    /// Timer for detecting stuck conditions.
    /// </summary>
    private float _stuckTimer = 0f;

    /// <summary>
    /// Last position for stuck detection.
    /// </summary>
    private Vector3 _lastPosition;

    public override void _Ready()
    {
        _navigationAgent = GetNode<NavigationAgent3D>("NavigationAgent3D");

        // Connect to navigation signals
        _navigationAgent.TargetReached += OnTargetReached;
        _navigationAgent.VelocityComputed += OnVelocityComputed;

        // Wait for navigation to be ready
        CallDeferred(MethodName.SetupNavigation);

        // Initialize position tracking
        _lastPosition = GlobalPosition;
    }

    private void SetupNavigation()
    {
        // NavigationAgent needs at least one physics frame to initialize
        _navigationReady = true;
        _movementState = MovementState.Idle;
        GD.Print($"[Colonist] Navigation ready at {GlobalPosition}");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Wait for navigation to be ready
        if (!_navigationReady)
            return;

        // Debug logging every 1 second
        _debugTimer += (float)delta;
        if (_debugTimer >= 1.0f)
        {
            _debugTimer = 0f;
            if (_movementState != MovementState.Idle)
            {
                GD.Print($"[Colonist] State={_movementState}, pos={GlobalPosition}, target={_navigationAgent.TargetPosition}, dist={_navigationAgent.DistanceToTarget():F2}");
            }
        }

        // Stuck detection (no movement for 2 seconds)
        if (GlobalPosition.DistanceTo(_lastPosition) < 0.1f)
        {
            _stuckTimer += (float)delta;
            if (_stuckTimer > 2.0f && _movementState != MovementState.Idle)
            {
                GD.PushWarning("[Colonist] Stuck detected! Resetting navigation.");
                _movementState = MovementState.Idle;
                _activeJumpPlan = null;
                _lastJumpLaunch = null; // Clear jump cooldown to allow retrying
                _navigationAgent.TargetPosition = GlobalPosition;
                _stuckTimer = 0f;
            }
        }
        else
        {
            _stuckTimer = 0f;
            _lastPosition = GlobalPosition;
        }

        // State machine
        switch (_movementState)
        {
            case MovementState.Idle:
                ProcessIdleState(delta);
                break;
            case MovementState.Walking:
                ProcessWalkingState(delta);
                break;
            case MovementState.PreparingJump:
                ProcessPreparingJumpState(delta);
                break;
            case MovementState.Jumping:
                ProcessJumpingState(delta);
                break;
            case MovementState.Landing:
                ProcessLandingState(delta);
                break;
        }

        MoveAndSlide();
    }

    private void OnVelocityComputed(Vector3 safeVelocity)
    {
        // This is called by NavigationAgent when avoidance is enabled
        // We're not using avoidance, but keep the handler for future use
    }

    /// <summary>
    /// Sets the colonist's destination for pathfinding.
    /// </summary>
    public void SetDestination(Vector3 targetPosition)
    {
        if (!_navigationReady)
        {
            GD.Print($"[Colonist] Navigation not ready yet, waiting...");
            return;
        }

        _navigationAgent.TargetPosition = targetPosition;
        _movementState = MovementState.Walking;
        _activeJumpPlan = null;
        _lastJumpLaunch = null; // Clear jump cooldown on new destination
        GD.Print($"[Colonist] Moving to {targetPosition} from {GlobalPosition}");
    }

    private void OnTargetReached()
    {
        GD.Print("[Colonist] Destination reached!");
        _movementState = MovementState.Idle;
        _activeJumpPlan = null;
    }

    // ===== State Processing Methods =====

    private void ProcessIdleState(double delta)
    {
        // Stop all movement
        Velocity = new Vector3(0, Velocity.Y, 0);

        // Apply gravity
        if (!IsOnFloor())
        {
            Vector3 velocity = Velocity;
            velocity.Y -= Gravity * (float)delta;
            Velocity = velocity;
        }
    }

    private void ProcessWalkingState(double delta)
    {
        Vector3 velocity = Velocity;

        // Apply gravity when not on floor
        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * (float)delta;
        }

        // Check if navigation finished
        if (_navigationAgent.IsNavigationFinished())
        {
            // Only stop if we're at the correct height (within 0.6 units of target)
            float heightDiff = Mathf.Abs(GlobalPosition.Y - _navigationAgent.TargetPosition.Y);
            if (heightDiff < 0.6f)
            {
                _movementState = MovementState.Idle;
                velocity.X = 0;
                velocity.Z = 0;
                Velocity = velocity;
                return;
            }
            else
            {
                // Still at wrong height - force navigation to continue by resetting target
                GD.Print($"[Colonist] Navigation finished but still {heightDiff:F2}m from target height. Retrying...");
                _navigationAgent.TargetPosition = _navigationAgent.TargetPosition; // Re-trigger pathfinding
                _lastJumpLaunch = null; // Clear cooldown for retry
            }
        }

        // Check for upcoming jumps
        JumpPlan? detectedJump = DetectUpcomingJump();
        if (detectedJump != null)
        {
            _activeJumpPlan = detectedJump;
            _movementState = MovementState.PreparingJump;
            GD.Print($"[Colonist] Jump detected! Preparing to launch from {detectedJump.LaunchPosition}");
            Velocity = velocity;
            return;
        }

        // Normal walking movement
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
        else
        {
            // Very close to waypoint, stop horizontal movement
            velocity.X = 0;
            velocity.Z = 0;
        }

        Velocity = velocity;
    }

    private void ProcessPreparingJumpState(double delta)
    {
        if (_activeJumpPlan == null)
        {
            GD.PushWarning("[Colonist] PreparingJump state but no active plan!");
            _movementState = MovementState.Walking;
            return;
        }

        Vector3 velocity = Velocity;

        // Apply gravity
        if (!IsOnFloor())
        {
            velocity.Y -= Gravity * (float)delta;
        }

        // Check alignment to launch point (horizontal only)
        Vector3 toLaunch = _activeJumpPlan.LaunchPosition - GlobalPosition;
        toLaunch.Y = 0;

        if (toLaunch.LengthSquared() < LaunchAlignmentTolerance * LaunchAlignmentTolerance)
        {
            // Aligned! Calculate jump trajectory and execute
            CalculateJumpVelocity();
            _movementState = MovementState.Jumping;
            _jumpStartTime = Time.GetTicksMsec() / 1000.0;
            velocity = _activeJumpPlan.LaunchVelocity;
            GD.Print($"[Colonist] Launching jump! Velocity={velocity}");
        }
        else
        {
            // Move toward launch point (half speed for precision)
            Vector3 direction = toLaunch.Normalized();
            velocity.X = direction.X * MovementSpeed * 0.5f;
            velocity.Z = direction.Z * MovementSpeed * 0.5f;
        }

        Velocity = velocity;
    }

    private void ProcessJumpingState(double delta)
    {
        if (_activeJumpPlan == null)
        {
            GD.PushWarning("[Colonist] Jumping state but no active plan!");
            _movementState = MovementState.Walking;
            return;
        }

        Vector3 velocity = Velocity;

        // Apply gravity (horizontal velocity maintained from launch)
        velocity.Y -= Gravity * (float)delta;
        Velocity = velocity;

        // Check for landing
        if (IsOnFloor())
        {
            GD.Print($"[Colonist] Landed at {GlobalPosition}");
            _movementState = MovementState.Landing;
            return;
        }

        // Timeout safety: if airborne too long, assume stuck
        double airborneTime = (Time.GetTicksMsec() / 1000.0) - _jumpStartTime;
        if (airborneTime > JumpTimeout)
        {
            GD.PushWarning($"[Colonist] Jump timeout! Forcing landing.");
            _movementState = MovementState.Walking;
            _activeJumpPlan = null;
            _lastJumpLaunch = null; // Clear cooldown to allow retrying
        }
    }

    private void ProcessLandingState(double delta)
    {
        // Only set cooldown if jump was successful (landed higher than launch)
        if (_activeJumpPlan != null)
        {
            float heightGain = GlobalPosition.Y - _activeJumpPlan.LaunchPosition.Y;

            if (heightGain > 0.5f)
            {
                // Successful jump - set cooldown to prevent retriggering
                _lastJumpLaunch = _activeJumpPlan.LaunchPosition;
                GD.Print($"[Colonist] Successful jump! Height gain: {heightGain:F2}m. Cooldown active.");
            }
            else
            {
                // Failed jump - clear cooldown to allow immediate retry
                _lastJumpLaunch = null;
                GD.Print($"[Colonist] Jump failed (height gain: {heightGain:F2}m). Cooldown cleared for retry.");
            }
        }

        // One frame to stabilize, then resume normal movement
        _movementState = MovementState.Walking;
        _activeJumpPlan = null;
        GD.Print($"[Colonist] Resuming normal movement after landing");
    }

    // ===== Jump Detection and Planning Methods =====

    /// <summary>
    /// Scans ahead in the navigation path to detect upcoming jumps.
    /// Uses path lookahead to detect jumps before reaching them (2 units ahead).
    /// Returns JumpPlan if jump needed, null otherwise.
    /// </summary>
    private JumpPlan? DetectUpcomingJump()
    {
        if (_navigationAgent.IsNavigationFinished())
            return null;

        // Check cooldown first - don't retrigger same jump
        if (_lastJumpLaunch.HasValue)
        {
            float distToLastJump = GlobalPosition.DistanceTo(_lastJumpLaunch.Value);
            if (distToLastJump < JumpCooldownDistance)
            {
                return null; // Still in cooldown
            }
        }

        // Get the full navigation path
        Vector3 currentPos = GlobalPosition;
        Vector3[] pathWaypoints = GetNavigationPath();

        if (pathWaypoints.Length < 2)
            return null;

        // Scan path segments for height transitions within detection range
        float distanceAlongPath = 0f;

        for (int i = 0; i < pathWaypoints.Length - 1; i++)
        {
            Vector3 segmentStart = pathWaypoints[i];
            Vector3 segmentEnd = pathWaypoints[i + 1];

            // Calculate distance from current position to this segment
            if (i == 0)
            {
                // First segment - measure from current position to first waypoint
                distanceAlongPath = currentPos.DistanceTo(segmentStart);
            }
            else
            {
                // Add this segment's length to cumulative distance
                distanceAlongPath += pathWaypoints[i - 1].DistanceTo(segmentStart);
            }

            // Only scan ahead up to JumpDetectionDistance
            if (distanceAlongPath > JumpDetectionDistance)
                break;

            // Check this segment for height transition
            float heightDelta = segmentEnd.Y - segmentStart.Y;
            Vector2 horizontalVec = new Vector2(segmentEnd.X - segmentStart.X, segmentEnd.Z - segmentStart.Z);
            float horizontalDist = horizontalVec.Length();

            // Jump criteria: 1-block height jump (0.7-1.3m) with small horizontal distance
            if (heightDelta > JumpHeightThreshold && heightDelta < 1.3f && horizontalDist < JumpMaxHorizontalDist)
            {
                // Found a jump! Plan it from the start of this segment
                Vector3 launchPos = segmentStart;
                Vector3 targetPos = segmentEnd;

                // Adjust launch position to be at ground level (not floating waypoint)
                launchPos.Y = Mathf.Floor(launchPos.Y) + 0.1f;
                targetPos.Y = Mathf.Floor(targetPos.Y) + 1.1f; // Top of upper block

                // Ensure minimum horizontal distance for momentum
                Vector2 horizontalDir = horizontalVec.Normalized();
                float minHorizontalDist = Mathf.Max(horizontalDist, 0.8f);

                // Recalculate target with minimum distance
                Vector3 finalTarget = new Vector3(
                    launchPos.X + horizontalDir.X * minHorizontalDist,
                    targetPos.Y,
                    launchPos.Z + horizontalDir.Y * minHorizontalDist
                );

                GD.Print($"[Colonist] Jump detected {distanceAlongPath:F2}m ahead: {launchPos} -> {finalTarget} (height={heightDelta:F2}, dist={horizontalDist:F2})");

                return new JumpPlan
                {
                    LaunchPosition = launchPos,
                    TargetPosition = finalTarget,
                    HorizontalDistance = minHorizontalDist,
                    HeightDelta = 1.0f, // Always 1 block for now
                    IsActive = true
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the current navigation path as an array of waypoints.
    /// </summary>
    private Vector3[] GetNavigationPath()
    {
        NavigationPathQueryResult3D pathResult = _navigationAgent.GetCurrentNavigationPath();
        if (pathResult == null)
            return System.Array.Empty<Vector3>();

        var waypoints = pathResult.GetPath();
        if (waypoints == null || waypoints.Length == 0)
            return System.Array.Empty<Vector3>();

        // Convert PackedVector3Array to Vector3[]
        Vector3[] result = new Vector3[waypoints.Length];
        for (int i = 0; i < waypoints.Length; i++)
        {
            result[i] = waypoints[i];
        }

        return result;
    }

    /// <summary>
    /// Calculates launch velocity for the active jump plan using ballistic trajectory.
    /// </summary>
    private void CalculateJumpVelocity()
    {
        if (_activeJumpPlan == null)
            return;

        float h = _activeJumpPlan.HorizontalDistance;
        float deltaY = _activeJumpPlan.HeightDelta;

        // Calculate vertical velocity needed to reach target height with margin
        // Using: vy = sqrt(2 * g * height)
        float verticalVelocity = Mathf.Sqrt(2.0f * Gravity * (deltaY + JumpHeightMargin));

        // Calculate time to reach apex
        float timeToApex = verticalVelocity / Gravity;

        // Total flight time (up + down) with safety margin
        float totalTime = 2.0f * timeToApex * JumpTimeMultiplier;

        // Calculate horizontal velocity needed to cover distance
        Vector3 horizontalDirection = new Vector3(
            _activeJumpPlan.TargetPosition.X - _activeJumpPlan.LaunchPosition.X,
            0,
            _activeJumpPlan.TargetPosition.Z - _activeJumpPlan.LaunchPosition.Z
        ).Normalized();

        float horizontalSpeed = h / totalTime;

        // Ensure minimum horizontal velocity to clear edges (4.0 units/sec minimum)
        // This needs to be high enough to carry the colonist forward and onto the upper block
        float minHorizontalSpeed = 4.0f;
        if (horizontalSpeed < minHorizontalSpeed)
        {
            horizontalSpeed = minHorizontalSpeed;
            GD.Print($"[Colonist] Boosting horizontal velocity from {h / totalTime:F2} to {minHorizontalSpeed:F2} for edge clearance");
        }

        // Set launch velocity
        _activeJumpPlan.LaunchVelocity = new Vector3(
            horizontalDirection.X * horizontalSpeed,
            verticalVelocity,
            horizontalDirection.Z * horizontalSpeed
        );

        GD.Print($"[Colonist] Jump trajectory calculated: vy={verticalVelocity:F2}, vh={horizontalSpeed:F2}, t={totalTime:F2}, h={h:F2}, deltaY={deltaY:F2}");
    }
}
