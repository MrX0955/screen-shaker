using System.Runtime.InteropServices;

internal static class Program
{
    private const int HotkeyToggleId = 1;
    private const int HotkeyExitId = 2;

    private const uint ModNoRepeat = 0x4000;
    private const uint VkV = 0x56;
    private const uint VkF9 = 0x78;

    private const uint WmHotkey = 0x0312;
    private const uint InputMouse = 0;
    private const uint MouseEventfMove = 0x0001;

    private static readonly object StateLock = new();

    private static bool IsShaking;
    private static int AmplitudePixels = 5000;
    private static int IntervalMs = 4;

    private static CancellationTokenSource? ShakeCts;
    private static Task? ShakeTask;

    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This app only runs on Windows.");
            return;
        }

        ParseArguments(args);

        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        bool toggleRegistered = false;
        bool exitRegistered = false;

        try
        {
            toggleRegistered = RegisterHotKey(IntPtr.Zero, HotkeyToggleId, ModNoRepeat, VkV);
            exitRegistered = RegisterHotKey(IntPtr.Zero, HotkeyExitId, ModNoRepeat, VkF9);

            if (!toggleRegistered || !exitRegistered)
            {
                Console.WriteLine("Hotkey registration failed. Try running as administrator.");
                return;
            }

            Console.WriteLine("V: mouse shake ON/OFF");
            Console.WriteLine("F9: exit");
            Console.WriteLine($"Speed: {IntervalMs} ms | Amplitude: {AmplitudePixels} px");
            Console.WriteLine("This mode simulates rapid left-right mouse movement.");

            while (true)
            {
                int getMessageResult = GetMessage(out MSG message, IntPtr.Zero, 0, 0);
                if (getMessageResult <= 0)
                {
                    break;
                }

                if (message.message != WmHotkey)
                {
                    continue;
                }

                int hotkeyId = unchecked((int)message.wParam.ToUInt64());
                if (hotkeyId == HotkeyToggleId)
                {
                    ToggleShake();
                }
                else if (hotkeyId == HotkeyExitId)
                {
                    break;
                }
            }
        }
        finally
        {
            StopShake();

            if (toggleRegistered)
            {
                UnregisterHotKey(IntPtr.Zero, HotkeyToggleId);
            }

            if (exitRegistered)
            {
                UnregisterHotKey(IntPtr.Zero, HotkeyExitId);
            }
        }
    }

    private static void ParseArguments(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out int amplitude) && amplitude >= 1 && amplitude <= 200)
        {
            AmplitudePixels = amplitude;
        }

        if (args.Length > 1 && int.TryParse(args[1], out int interval) && interval >= 1 && interval <= 50)
        {
            IntervalMs = interval;
        }
    }

    private static void ToggleShake()
    {
        lock (StateLock)
        {
            if (IsShaking)
            {
                StopShakeUnsafe();
            }
            else
            {
                StartShakeUnsafe();
            }
        }
    }

    private static void StartShakeUnsafe()
    {
        ShakeCts = new CancellationTokenSource();
        CancellationToken token = ShakeCts.Token;

        ShakeTask = Task.Run(() => ShakeMouseLoop(token), token);
        IsShaking = true;

        Console.WriteLine("Mouse shake ON");
    }

    private static void StopShake()
    {
        lock (StateLock)
        {
            StopShakeUnsafe();
        }
    }

    private static void StopShakeUnsafe()
    {
        if (!IsShaking)
        {
            return;
        }

        ShakeCts?.Cancel();

        try
        {
            ShakeTask?.Wait(500);
        }
        catch (AggregateException)
        {
        }

        ShakeTask = null;
        ShakeCts?.Dispose();
        ShakeCts = null;

        IsShaking = false;
        Console.WriteLine("Mouse shake OFF");
    }

    private static void ShakeMouseLoop(CancellationToken token)
    {
        int direction = 1;
        int lastOffset = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                int offset = direction * AmplitudePixels;
                SendRelativeMouseMove(offset, 0);

                lastOffset = offset;
                direction = -direction;

                Thread.Sleep(IntervalMs);
            }
        }
        finally
        {
            if (lastOffset != 0)
            {
                SendRelativeMouseMove(-lastOffset, 0);
            }
        }
    }

    private static void SendRelativeMouseMove(int deltaX, int deltaY)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = InputMouse;
        inputs[0].u.mouseInput = new MOUSEINPUT
        {
            dx = deltaX,
            dy = deltaY,
            mouseData = 0,
            dwFlags = MouseEventfMove,
            time = 0,
            dwExtraInfo = IntPtr.Zero
        };

        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true;
        PostQuitMessage(0);
    }

    private static void OnProcessExit(object? sender, EventArgs eventArgs)
    {
        StopShake();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT mouseInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);
}
