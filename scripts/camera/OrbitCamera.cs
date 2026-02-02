using Godot;

namespace ColonySim.Camera;

/// <summary>
/// Simple orbit camera that rotates around a target point.
/// </summary>
public partial class OrbitCamera : Camera3D
{
	/// <summary>
	/// The point to orbit around.
	/// </summary>
	[Export]
	public Vector3 Target { get; set; } = new Vector3(24, 6, 24);

	/// <summary>
	/// Distance from the target.
	/// </summary>
	[Export]
	public float Distance { get; set; } = 60f;

	/// <summary>
	/// Rotation speed in radians per second.
	/// </summary>
	[Export]
	public float RotationSpeed { get; set; } = 0.5f;

	/// <summary>
	/// Vertical angle (pitch) in radians. 0.7 is roughly 40 degrees.
	/// </summary>
	[Export]
	public float Pitch { get; set; } = 0.7f;

	private float _angle = 0f;

	public override void _Process(double delta)
	{
		_angle += RotationSpeed * (float)delta;

		// Calculate camera position on a circle around the target
		float x = Target.X + Distance * Mathf.Cos(_angle) * Mathf.Cos(Pitch);
		float z = Target.Z + Distance * Mathf.Sin(_angle) * Mathf.Cos(Pitch);
		float y = Target.Y + Distance * Mathf.Sin(Pitch);

		Position = new Vector3(x, y, z);
		LookAt(Target);
	}
}
