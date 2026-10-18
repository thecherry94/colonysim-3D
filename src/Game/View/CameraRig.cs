using ColonySim.Core;
using Godot;

namespace ColonySim.Game;

/// <summary>
/// RTS overseer camera: orbits a focus point on the map. Keys pan/rotate, wheel zooms, right-drag orbits,
/// middle-drag pans. Shift+wheel is left to the view-level controls; left button is left to the tools.
/// </summary>
public partial class CameraRig : Node3D
{
    public const float MinDistance = 10, MaxDistance = 450, DefaultDistance = 120;
    public const float MinPitch = 30, MaxPitch = 85, DefaultPitch = 50;
    const float RotateKeyDegPerSec = 90;
    const float DragDegPerPixel = 0.3f;
    const float ZoomPerNotch = 1.1f;
    const float FocusYSmoothing = 10;

    ColonyMap _map = null!;
    float _focusX, _focusZ, _targetY;
    int _slicedLevel;
    bool _sliced;
    float _yaw, _pitch = DefaultPitch, _distance = DefaultDistance;

    public Camera3D Camera { get; private set; } = null!;

    /// <summary>Smoothed vertical focus; eases toward the target set by <see cref="FocusOnLevel"/>.</summary>
    public float FocusY { get; set; }

    public Vector3 FocusPoint => new(_focusX, FocusY, _focusZ);

    /// <summary>Heading in degrees; 0 looks along -Z.</summary>
    public float Yaw { get => _yaw; set { _yaw = Mathf.Wrap(value, 0, 360); UpdateTransform(); } }

    public float Pitch { get => _pitch; set { _pitch = Mathf.Clamp(value, MinPitch, MaxPitch); UpdateTransform(); } }

    public float Distance { get => _distance; set { _distance = Mathf.Clamp(value, MinDistance, MaxDistance); UpdateTransform(); } }

    public override void _Ready()
    {
        Camera = new Camera3D { Name = "Camera", Far = 2000, Current = true };
        AddChild(Camera);
    }

    /// <summary>Starts at the map center, focused at the surface there.</summary>
    public void Setup(ColonyMap map)
    {
        _map = map;
        _focusX = map.World.SizeX / 2f;
        _focusZ = map.World.SizeZ / 2f;
        _sliced = false;
        FocusY = _targetY = SurfaceAtFocus() + 1;
        UpdateTransform();
    }

    /// <summary>Moves the focus to map column (x, z), clamped to the map; the height keeps following its target.</summary>
    public void SetFocus(float x, float z)
    {
        _focusX = Mathf.Clamp(x, 0, _map.World.SizeX);
        _focusZ = Mathf.Clamp(z, 0, _map.World.SizeZ);
        UpdateTargetY();
        UpdateTransform();
    }

    /// <summary>Sliced views look at the layer just above the cut; unsliced views at the terrain surface.</summary>
    public void FocusOnLevel(int level, bool sliced)
    {
        _sliced = sliced;
        _slicedLevel = level;
        UpdateTargetY();
    }

    /// <summary>Unsliced views keep following the surface under the focus column as the camera pans.</summary>
    void UpdateTargetY() => _targetY = _sliced ? _slicedLevel + 1 : SurfaceAtFocus() + 1;

    static bool ModifierHeld() =>
        Input.IsKeyPressed(Key.Ctrl) || Input.IsKeyPressed(Key.Alt) || Input.IsKeyPressed(Key.Meta);

    float SurfaceAtFocus() =>
        _map.World.GetSurfaceHeight(
            Math.Clamp((int)_focusX, 0, _map.World.SizeX - 1),
            Math.Clamp((int)_focusZ, 0, _map.World.SizeZ - 1));

    /// <summary>Horizontal unit vectors (x, z) for "forward" and "right" at the current yaw.</summary>
    (Vector2 Forward, Vector2 Right) Axes()
    {
        float yaw = Mathf.DegToRad(_yaw);
        return (new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)), new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw)));
    }

    void MoveFocus(Vector2 delta)
    {
        _focusX = Mathf.Clamp(_focusX + delta.X, 0, _map.World.SizeX);
        _focusZ = Mathf.Clamp(_focusZ + delta.Y, 0, _map.World.SizeZ);
    }

    public override void _Process(double delta)
    {
        if (_map == null) return;
        float dt = (float)delta;

        var (fwd, right) = Axes();
        var move = Vector2.Zero;
        bool keys = !ModifierHeld(); // Ctrl+S, Ctrl+Q etc. belong to other actions
        if (keys && Input.IsActionPressed(InputActions.CamForward)) move += fwd;
        if (keys && Input.IsActionPressed(InputActions.CamBack)) move -= fwd;
        if (keys && Input.IsActionPressed(InputActions.CamRight)) move += right;
        if (keys && Input.IsActionPressed(InputActions.CamLeft)) move -= right;
        if (move != Vector2.Zero) MoveFocus(move.Normalized() * _distance * dt);

        float rot = 0;
        if (keys && Input.IsActionPressed(InputActions.CamRotLeft)) rot += 1;
        if (keys && Input.IsActionPressed(InputActions.CamRotRight)) rot -= 1;
        if (rot != 0) _yaw = Mathf.Wrap(_yaw + rot * RotateKeyDegPerSec * dt, 0, 360);

        UpdateTargetY();
        FocusY = Mathf.Lerp(FocusY, _targetY, 1 - Mathf.Exp(-FocusYSmoothing * dt));
        UpdateTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_map == null) return;
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ShiftPressed: false } wheel
                when wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                float factor = wheel.ButtonIndex == MouseButton.WheelUp ? 1 / ZoomPerNotch : ZoomPerNotch;
                _distance = Mathf.Clamp(_distance * factor, MinDistance, MaxDistance);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Right):
                _yaw = Mathf.Wrap(_yaw - motion.Relative.X * DragDegPerPixel, 0, 360);
                _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * DragDegPerPixel, MinPitch, MaxPitch);
                break;

            case InputEventMouseMotion motion when motion.ButtonMask.HasFlag(MouseButtonMask.Middle):
                // Ground follows the cursor: world units per pixel at the focus depth.
                float height = Math.Max(1, GetViewport().GetVisibleRect().Size.Y);
                float perPixel = 2 * _distance * Mathf.Tan(Mathf.DegToRad(Camera.Fov / 2)) / height;
                var (fwd, right) = Axes();
                MoveFocus((-right * motion.Relative.X + fwd * motion.Relative.Y / Mathf.Sin(Mathf.DegToRad(_pitch))) * perPixel);
                break;
        }
    }

    void UpdateTransform()
    {
        if (Camera == null) return;
        float yaw = Mathf.DegToRad(_yaw), pitch = Mathf.DegToRad(_pitch);
        var offset = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * _distance;
        Camera.LookAtFromPosition(FocusPoint + offset, FocusPoint, Vector3.Up);
    }
}
