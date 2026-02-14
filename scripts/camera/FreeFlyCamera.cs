namespace ColonySim;

using Godot;

/// <summary>
/// Noclip free-fly camera for debugging and cave exploration.
/// WASD to move, mouse to look, Shift for speed boost, scroll to adjust speed.
/// Toggled via F4 in Main.cs — swaps with the RTS CameraController.
///
/// Captures mouse on activation, releases on deactivation.
/// Supports Y-level slice keys (Page Up/Down/Home) same as RTS camera.
/// </summary>
public partial class FreeFlyCamera : Node3D
{
    // Movement
    private const float BaseSpeed = 20.0f;
    private const float ShiftMultiplier = 4.0f;
    private const float SpeedScrollStep = 5.0f;
    private const float MinSpeed = 5.0f;
    private const float MaxSpeed = 200.0f;

    // Mouse look
    private const float MouseSensitivity = 0.002f;

    // Headlight
    private const float LightRange = 40.0f;
    private const float LightEnergy = 2.0f;

    private Camera3D _camera;
    private OmniLight3D _headlight;
    private float _yaw;
    private float _pitch;
    private float _currentSpeed = BaseSpeed;
    private bool _active;

    // Y-level slice (same as CameraController)
    private int _maxSliceY = 192;
    private const int MinSliceY = 1;
    private const int SliceStep = 1;
    private const int DefaultStartSliceY = 155;

    // Debounce flags for slice keys
    private bool _pageDownHeld;
    private bool _pageUpHeld;
    private bool _homeHeld;

    // Environment background swap for slice mode
    private Environment _environment;
    private static readonly Color SkyColor = new(0.53f, 0.71f, 0.87f, 1f);
    private static readonly Color SliceColor = new(0.15f, 0.15f, 0.18f, 1f);

    public Camera3D Camera => _camera;

    public void SetMaxWorldHeight(int maxY)
    {
        _maxSliceY = maxY;
    }

    public override void _Ready()
    {
        _camera = new Camera3D();
        _camera.Name = "Camera3D";
        _camera.Fov = 90; // wider FOV for exploration
        AddChild(_camera);

        // Headlight attached to camera so it always points forward
        _headlight = new OmniLight3D();
        _headlight.Name = "Headlight";
        _headlight.OmniRange = LightRange;
        _headlight.LightEnergy = LightEnergy;
        _headlight.LightColor = new Color(1.0f, 0.95f, 0.85f); // warm white
        _headlight.OmniAttenuation = 1.5f; // smooth falloff
        _headlight.ShadowEnabled = true;
        _camera.AddChild(_headlight);

        // Start looking forward along -Z (Godot default)
        _yaw = 0f;
        _pitch = 0f;
        UpdateCameraRotation();

        // Find WorldEnvironment for background color swap during slicing
        CallDeferred(MethodName.FindEnvironment);
    }

    private void FindEnvironment()
    {
        var worldEnv = GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment;
        if (worldEnv != null)
            _environment = worldEnv.Environment;
    }

    /// <summary>
    /// Activate: make camera current, capture mouse.
    /// </summary>
    public void Activate()
    {
        _active = true;
        _camera.MakeCurrent();
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print($"FreeFlyCamera: ACTIVE at {Position}, speed={_currentSpeed}");
    }

    /// <summary>
    /// Deactivate: release mouse. The RTS camera will call MakeCurrent() on its own camera.
    /// </summary>
    public void Deactivate()
    {
        _active = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print("FreeFlyCamera: deactivated");
    }

    public override void _Process(double delta)
    {
        if (!_active) return;

        float dt = (float)delta;
        ProcessMovement(dt);
        ProcessSliceKeys();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active) return;

        // Mouse look
        if (@event is InputEventMouseMotion mouseMotion)
        {
            _yaw -= mouseMotion.Relative.X * MouseSensitivity;
            _pitch -= mouseMotion.Relative.Y * MouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, -Mathf.Pi / 2f + 0.01f, Mathf.Pi / 2f - 0.01f);
            UpdateCameraRotation();
        }

        // Scroll wheel adjusts speed
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                _currentSpeed = Mathf.Min(_currentSpeed + SpeedScrollStep, MaxSpeed);
                GD.Print($"FreeFlyCamera: speed={_currentSpeed}");
            }
            else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                _currentSpeed = Mathf.Max(_currentSpeed - SpeedScrollStep, MinSpeed);
                GD.Print($"FreeFlyCamera: speed={_currentSpeed}");
            }
        }
    }

    private void ProcessMovement(float dt)
    {
        var input = Vector3.Zero;

        // Forward/back (W/S)
        if (Input.IsKeyPressed(Key.W))
            input.Z -= 1;
        if (Input.IsKeyPressed(Key.S))
            input.Z += 1;

        // Left/right strafe (A/D)
        if (Input.IsKeyPressed(Key.A))
            input.X -= 1;
        if (Input.IsKeyPressed(Key.D))
            input.X += 1;

        // Up/down (Space/Ctrl)
        if (Input.IsKeyPressed(Key.Space))
            input.Y += 1;
        if (Input.IsKeyPressed(Key.Ctrl))
            input.Y -= 1;

        if (input == Vector3.Zero) return;

        input = input.Normalized();

        float speed = _currentSpeed;
        if (Input.IsKeyPressed(Key.Shift))
            speed *= ShiftMultiplier;

        // Transform input by camera rotation (yaw + pitch for forward, yaw only for strafe)
        // At yaw=0 the camera looks along -Z, so forward must point along -Z
        var forward = new Vector3(
            -Mathf.Sin(_yaw) * Mathf.Cos(_pitch),
            Mathf.Sin(_pitch),
            -Mathf.Cos(_yaw) * Mathf.Cos(_pitch)
        ).Normalized();

        var right = new Vector3(
            Mathf.Cos(_yaw),
            0,
            -Mathf.Sin(_yaw)
        ).Normalized();

        var up = Vector3.Up;

        // W sets input.Z = -1, so -input.Z = +1 → moves along forward (into screen)
        var movement = forward * -input.Z + right * input.X + up * input.Y;
        Position += movement * speed * dt;
    }

    private void UpdateCameraRotation()
    {
        if (_camera == null) return;

        // Camera is a child at (0,0,0) — rotate via Basis
        var basis = Basis.Identity;
        basis = basis.Rotated(Vector3.Up, _yaw);
        basis = basis.Rotated(basis.X, _pitch);
        _camera.Basis = basis;
    }

    /// <summary>
    /// Y-level slice controls — identical to CameraController.
    /// </summary>
    private void ProcessSliceKeys()
    {
        bool pageDownNow = Input.IsKeyPressed(Key.Pagedown);
        if (pageDownNow && !_pageDownHeld)
        {
            if (!SliceState.Enabled)
            {
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

    private void ApplySliceToShader()
    {
        RenderingServer.GlobalShaderParameterSet("slice_enabled", SliceState.Enabled);
        RenderingServer.GlobalShaderParameterSet("slice_y_level", (float)SliceState.YLevel);

        if (_environment != null)
            _environment.BackgroundColor = SliceState.Enabled ? SliceColor : SkyColor;
    }
}
