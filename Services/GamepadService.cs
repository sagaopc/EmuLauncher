using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace EmuLauncher.Services;

public enum PadButton
{
    Up,
    Down,
    Left,
    Right,
    A,
    B,
    X,
    Y,
    LeftShoulder,
    RightShoulder,
    Start,
    Back
}

/// <summary>
/// Reads every connected XInput controller (Xbox pads and anything presented as one, e.g. through
/// Steam Input or DS4Windows) and reports button presses on the UI thread. Directions repeat while
/// held, and the left stick counts as the d-pad.
/// </summary>
public sealed class GamepadService
{
    private const ushort DpadUp = 0x0001;
    private const ushort DpadDown = 0x0002;
    private const ushort DpadLeft = 0x0004;
    private const ushort DpadRight = 0x0008;
    private const ushort StartMask = 0x0010;
    private const ushort BackMask = 0x0020;
    private const ushort LeftShoulderMask = 0x0100;
    private const ushort RightShoulderMask = 0x0200;
    private const ushort AMask = 0x1000;
    private const ushort BMask = 0x2000;
    private const ushort XMask = 0x4000;
    private const ushort YMask = 0x8000;

    private const short StickThreshold = 18000;
    private const long FirstRepeatMs = 420;
    private const long NextRepeatMs = 110;
    private const long ProbeMs = 2000;

    private static readonly (ushort Mask, PadButton Button, bool Repeats)[] Map =
    {
        (DpadUp, PadButton.Up, true),
        (DpadDown, PadButton.Down, true),
        (DpadLeft, PadButton.Left, true),
        (DpadRight, PadButton.Right, true),
        (AMask, PadButton.A, false),
        (BMask, PadButton.B, false),
        (XMask, PadButton.X, false),
        (YMask, PadButton.Y, false),
        (LeftShoulderMask, PadButton.LeftShoulder, false),
        (RightShoulderMask, PadButton.RightShoulder, false),
        (StartMask, PadButton.Start, false),
        (BackMask, PadButton.Back, false)
    };

    private readonly DispatcherTimer _timer;
    private readonly bool[] _connected = new bool[4];
    private readonly long[] _nextProbe = new long[4];
    private readonly long[] _nextRepeat = new long[Map.Length];

    private int _api; // 0 = xinput1_4, 1 = xinput9_1_0, -1 = no XInput on this system
    private ushort _previous;
    private bool _enabled;
    private bool _waitForRelease = true;

    public event Action<PadButton>? Pressed;

    /// <summary>True while at least one controller is connected.</summary>
    public bool AnyConnected { get; private set; }

    public event Action? ConnectionChanged;

    public GamepadService()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>
    /// Presses are only reported while enabled (the launcher window is active). XInput itself
    /// keeps answering in the background, so without this an emulator's inputs would also
    /// drive the launcher behind it.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;

            // Whatever is held while the window comes back (the button that closed the emulator,
            // for example) must be let go before it counts as a press here.
            if (value) _waitForRelease = true;
        }
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        if (_api < 0) return;

        var now = Environment.TickCount64;
        ushort buttons = 0;
        var any = false;

        for (uint index = 0; index < 4; index++)
        {
            if (!_connected[index] && now < _nextProbe[index]) continue;

            if (!TryGetState(index, out var state))
            {
                // Asking for an absent controller is slow, so empty slots are only probed now and then.
                _connected[index] = false;
                _nextProbe[index] = now + ProbeMs;
                continue;
            }

            _connected[index] = true;
            any = true;

            buttons |= state.Gamepad.Buttons;
            if (state.Gamepad.ThumbLY > StickThreshold) buttons |= DpadUp;
            if (state.Gamepad.ThumbLY < -StickThreshold) buttons |= DpadDown;
            if (state.Gamepad.ThumbLX < -StickThreshold) buttons |= DpadLeft;
            if (state.Gamepad.ThumbLX > StickThreshold) buttons |= DpadRight;
        }

        if (any != AnyConnected)
        {
            AnyConnected = any;
            ConnectionChanged?.Invoke();
        }

        if (!_enabled)
        {
            _previous = buttons;
            return;
        }

        if (_waitForRelease)
        {
            if (buttons == 0) _waitForRelease = false;
            _previous = buttons;
            return;
        }

        var previous = _previous;
        _previous = buttons;

        for (var i = 0; i < Map.Length; i++)
        {
            var (mask, button, repeats) = Map[i];
            var down = (buttons & mask) != 0;
            var wasDown = (previous & mask) != 0;

            if (down && !wasDown)
            {
                _nextRepeat[i] = now + FirstRepeatMs;
                Pressed?.Invoke(button);
            }
            else if (down && repeats && now >= _nextRepeat[i])
            {
                _nextRepeat[i] = now + NextRepeatMs;
                Pressed?.Invoke(button);
            }

            // A press can open a dialog or leave the window; stop if that disabled input.
            if (!_enabled || _waitForRelease) break;
        }
    }

    private bool TryGetState(uint index, out XInputState state)
    {
        state = default;

        try
        {
            return _api == 0
                ? XInputGetState14(index, out state) == 0
                : XInputGetState910(index, out state) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // Fall back to the XInput that ships with every Windows, then give up quietly.
            _api = _api == 0 ? 1 : -1;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint userIndex, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState910(uint userIndex, out XInputState state);
}
