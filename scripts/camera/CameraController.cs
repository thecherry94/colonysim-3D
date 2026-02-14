namespace ColonySim;

using Godot;

/// <summary>
/// RTS-style camera: WASD/arrow pan, scroll zoom, middle-mouse rotate.
/// Operates as a pivot (Node3D) with Camera3D as child at an offset.
/// </summary>
public partial class CameraController : Node3D
{
    // Pan
    private const float PanSpeed = 150.0f;
    private const float EdgePanMargin = 20.0f; // pixels from screen edge
    private const float EdgePanSpeed = 15.0f;

    // Zoom
    private const float ZoomSpeed = 2.0f;
    private const float MinZoom = 8.0f;
    private const float MaxZoom = 60.0f;

    // Rotation
    private const float RotateSpeed = 0.005f;

    private Camera3D _camera;
    private float _zoom = 30.0f;
    private float _yaw; // rotation around Y axis
    private float _pitch = -Mathf.Pi / 4.0f; // ~45 degrees down
    private bool _rotating;

    // Environment background swap for slice mode
    private Environment _environment;
    private static readonly Color SkyColor = new(0.53f, 0.71f, 0.87f, 1f);
    private static readonly Color SliceColor = new(0.15f, 0.15f, 0.18f, 1f); // dark gray

    private const float MinPitch = -Mathf.Pi / 2.0f + 0.1f; // near top-down
    private const float MaxPitch = -0.15f; // near horizontal

    // Y-level slice
    private int _maxSliceY = 192; // default, updated via SetMaxWorldHeight()
    private const int MinSliceY = 1;
    private const int SliceStep = 1; // blocks per Page Up/Down press
    private const int DefaultStartSliceY = 155; // first press starts near typical surface height

    public Camera3D Camera => _camera;

    /// <summary>
    /// Set the maximum world height for Y-level slicing bounds.
    /// Called from Main after world setup.
    /// </summary>
    public void SetMaxWorldHeight(int maxY)
    {
        _maxSliceY = maxY;
    }

    public override void _Ready()
    {
        _camera = new Camera3D();
        _camera.Name = "Camera3D";
        _camera.Fov = 70;
        AddChild(_camera);

        UpdateCameraTransform();

        // Find WorldEnvironment for background color swap during slicing
        CallDeferred(MethodName.FindEnvironment);

        GD.Print($"CameraController ready: pos={Position}, zoom={_zoom}");
    }

    private void FindEnvironment()
    {
        var worldEnv = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        if (worldEnv != null)
        {
            _environment = worldEnv.Environment;
            GD.Print("CameraController: found WorldEnvironment for slice background");
        }
    }

    // Debounce flags for Page Up/Down so they only fire once per press
    private bool _pageDownHeld;
    private bool _pageUpHeld;
    private bool _homeHeld;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        ProcessKeyboardPan(dt);
        ProcessEdgePan(dt);
        ProcessSliceKeys();
        UpdateCameraTransform();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            // Scroll zoom
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                _zoom = Mathf.Max(_zoom - ZoomSpeed, MinZoom);
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                _zoom = Mathf.Min(_zoom + ZoomSpeed, MaxZoom);
            }

            // Middle mouse rotate
            if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _rotating = mouseButton.Pressed;
            }
        }

        if (@event is InputEventMouseMotion mouseMotion && _rotating)
        {
            _yaw -= mouseMotion.Relative.X * RotateSpeed;
            _pitch = Mathf.Clamp(
                _pitch - mouseMotion.Relative.Y * RotateSpeed,
                MinPitch, MaxPitch);
        }

    }

    /// <summary>
    /// Poll-based Y-level slice controls. Using Input.IsKeyPressed with debounce
    /// instead of _UnhandledInput to avoid event routing issues.
    /// </summary>
    private void ProcessSliceKeys()
    {
        // Page Down: lower slice level (reveal underground)
        bool pageDownNow = Input.IsKeyPressed(Key.Pagedown);
        if (pageDownNow && !_pageDownHeld)
        {
            if (!SliceState.Enabled)
            {
                // First press: start at a useful height near terrain surface
                SliceState.YLevel = DefaultStartSliceY;
                SliceState.Enabled = true;
            }
            else
            {
                SliceState.YLevel = Mathf.Max(SliceState.YLevel - SliceStep, MinSliceY);
            }
            ApplySliceToShader();
            GD.Print($"Y-slice: level {SliceState.YLevel}");
        }
        _pageDownHeld = pageDownNow;

        // Page Up: raise slice level
        bool pageUpNow = Input.IsKeyPressed(Key.Pageup);
        if (pageUpNow && !_pageUpHeld)
        {
            if (SliceState.Enabled)
            {
                SliceState.YLevel = Mathf.Min(SliceState.YLevel + SliceStep, _maxSliceY);
                if (SliceState.YLevel >= _maxSliceY)
                {
                    SliceState.Enabled = false;
                    GD.Print("Y-slice: OFF (showing full world)");
                }
                else
                {
                    GD.Print($"Y-slice: level {SliceState.YLevel}");
                }
                ApplySliceToShader();
            }
        }
        _pageUpHeld = pageUpNow;

        // Home: disable slicing entirely
        bool homeNow = Input.IsKeyPressed(Key.Home);
        if (homeNow && !_homeHeld)
        {
            SliceState.Enabled = false;
            SliceState.YLevel = _maxSliceY;
            ApplySliceToShader();
            GD.Print("Y-slice: OFF (showing full world)");
        }
        _homeHeld = homeNow;
    }

    /// <summary>
    /// Push current slice state to global shader uniforms and update background color.
    /// </summary>
    private void ApplySliceToShader()
    {
        RenderingServer.GlobalShaderParameterSet("slice_enabled", SliceState.Enabled);
        RenderingServer.GlobalShaderParameterSet("slice_y_level", (float)SliceState.YLevel);

        // Dark background when slicing so cave voids are visible as dark holes
        if (_environment != null)
        {
            _environment.BackgroundColor = SliceState.Enabled ? SliceColor : SkyColor;
        }
    }

    private void ProcessKeyboardPan(float dt)
    {
        var input = Vector2.Zero;

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            input.Y += 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            input.Y -= 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            input.X += 1;

        if (input == Vector2.Zero) return;

        input = input.Normalized();
        ApplyPan(input, PanSpeed * dt);
    }

    private void ProcessEdgePan(float dt)
    {
        // Don't edge-pan when window doesn't have focus (mouse is outside)
        if (!GetWindow().HasFocus()) return;

        var viewport = GetViewport();
        if (viewport == null) return;
        var mousePos = viewport.GetMousePosition();
        var size = viewport.GetVisibleRect().Size;

        // Ignore if mouse is outside the viewport bounds
        if (mousePos.X < 0 || mousePos.X > size.X || mousePos.Y < 0 || mousePos.Y > size.Y)
            return;

        var input = Vector2.Zero;

        if (mousePos.X < EdgePanMargin) input.X -= 1;
        if (mousePos.X > size.X - EdgePanMargin) input.X += 1;
        if (mousePos.Y < EdgePanMargin) input.Y += 1;  // Top of screen = forward
        if (mousePos.Y > size.Y - EdgePanMargin) input.Y -= 1;  // Bottom = backward

        if (input == Vector2.Zero) return;

        input = input.Normalized();
        ApplyPan(input, EdgePanSpeed * dt);
    }

    /// <summary>
    /// Pan relative to the camera's yaw so movement always feels
    /// aligned with the screen (forward = into the screen).
    /// </summary>
    private void ApplyPan(Vector2 input, float speed)
    {
        // Forward/back aligned to camera yaw (projected onto XZ plane)
        var forward = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));

        var movement = (forward * -input.Y + right * input.X) * speed;
        Position += movement;
    }

    private void UpdateCameraTransform()
    {
        if (_camera == null) return;

        // Camera sits at an offset from the pivot determined by pitch and zoom
        var offset = new Vector3(0, 0, _zoom);

        // Rotate offset by pitch (around X) then yaw (around Y)
        offset = offset.Rotated(Vector3.Right, _pitch);
        offset = offset.Rotated(Vector3.Up, _yaw);

        _camera.Position = offset;
        _camera.LookAt(GlobalPosition, Vector3.Up);
    }
}
