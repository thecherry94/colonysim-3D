using Godot;

namespace ColonySim.Camera;

/// <summary>
/// RTS-style camera with manual pan, zoom, and rotate controls.
/// </summary>
public partial class RTSCamera : Camera3D
{
	// Target and position
	[Export] public Vector3 Target { get; set; } = new Vector3(24, 6, 24);
	[Export] public float Distance { get; set; } = 60f;
	[Export] public float Pitch { get; set; } = 0.7f; // Fixed for now (~40 degrees)
	[Export] public float Yaw { get; set; } = 0f;

	// Movement speeds
	[Export] public float PanSpeed { get; set; } = 20f; // Units per second
	[Export] public float ZoomSpeed { get; set; } = 5f; // Units per wheel tick
	[Export] public float RotationSpeed { get; set; } = 1.5f; // Radians per second

	// Constraints
	[Export] public float MinDistance { get; set; } = 10f;
	[Export] public float MaxDistance { get; set; } = 150f;

	public override void _Ready()
	{
		UpdateCameraPosition();
	}

	public override void _Process(double delta)
	{
		HandlePanning((float)delta);
		HandleRotation((float)delta);
		UpdateCameraPosition();
	}

	public override void _Input(InputEvent @event)
	{
		HandleZoom(@event);
	}

	private void HandlePanning(float delta)
	{
		// Get camera's forward and right directions (flattened to XZ plane)
		Vector3 forward = -Transform.Basis.Z;
		forward.Y = 0;
		forward = forward.Normalized();

		Vector3 right = Transform.Basis.X;
		right.Y = 0;
		right = right.Normalized();

		// Apply input
		float panDelta = PanSpeed * delta;

		if (Input.IsActionPressed("camera_forward"))
			Target += forward * panDelta;
		if (Input.IsActionPressed("camera_backward"))
			Target -= forward * panDelta;
		if (Input.IsActionPressed("camera_left"))
			Target -= right * panDelta;
		if (Input.IsActionPressed("camera_right"))
			Target += right * panDelta;
	}

	private void HandleRotation(float delta)
	{
		float rotDelta = RotationSpeed * delta;

		if (Input.IsActionPressed("camera_rotate_left"))
			Yaw -= rotDelta;
		if (Input.IsActionPressed("camera_rotate_right"))
			Yaw += rotDelta;

		// Normalize yaw to [-PI, PI] range
		Yaw = Mathf.PosMod(Yaw + Mathf.Pi, Mathf.Tau) - Mathf.Pi;
	}

	private void HandleZoom(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			if (mouseEvent.ButtonIndex == MouseButton.WheelUp)
			{
				Distance = Mathf.Clamp(Distance - ZoomSpeed, MinDistance, MaxDistance);
			}
			else if (mouseEvent.ButtonIndex == MouseButton.WheelDown)
			{
				Distance = Mathf.Clamp(Distance + ZoomSpeed, MinDistance, MaxDistance);
			}
		}
	}

	private void UpdateCameraPosition()
	{
		// Calculate camera position using spherical coordinates
		float x = Target.X + Distance * Mathf.Cos(Yaw) * Mathf.Cos(Pitch);
		float y = Target.Y + Distance * Mathf.Sin(Pitch);
		float z = Target.Z + Distance * Mathf.Sin(Yaw) * Mathf.Cos(Pitch);

		Position = new Vector3(x, y, z);
		LookAt(Target);
	}
}
