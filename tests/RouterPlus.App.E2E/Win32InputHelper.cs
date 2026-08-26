using System.Runtime.InteropServices;

namespace RouterPlus.App.E2E;

// Caller: LiveGoogleAutoLoginTests.RightClickProfileReliablyAsync
// Purpose: Hardware-level right-click simulation using Win32 SendInput API
// User instruction: "fix lỗi RightClick trên WPF ListBox ListItem fail"
// Data schemas: Win32 INPUT/MOUSEINPUT structs for mouse event injection
internal static class Win32InputHelper
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private const int INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public MOUSEINPUT mi;
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

    public static void RightClick(int x, int y)
    {
        // Move cursor to position
        SetCursorPos(x, y);
        Thread.Sleep(50);

        // Send right mouse button down
        var inputDown = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_RIGHTDOWN,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        // Send right mouse button up
        var inputUp = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = 0,
                dwFlags = MOUSEEVENTF_RIGHTUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };

        var inputs = new[] { inputDown, inputUp };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
