using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Edit_on_release
{
    public class MainForm : Form
    {
        // Win32 Hook API declarations
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // Win32 constants
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;

        // Input Injection Structures
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint LLKHF_INJECTED = 0x00000010;
        private const uint LLMHF_INJECTED = 0x00000001;

        private delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

        // State variables
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private LowLevelHookProc _keyboardHookProc;
        private LowLevelHookProc _mouseHookProc;

        private bool _isActive = true;
        private bool _isBinding = false;

        // Custom Edit Key definition
        public class BoundKey
        {
            public bool IsMouse { get; set; }
            public Keys KeyCode { get; set; }
            public MouseButton MouseBtn { get; set; }

            public override string ToString()
            {
                return IsMouse ? $"Mouse {MouseBtn}" : KeyCode.ToString();
            }
        }

        public enum MouseButton
        {
            Right,
            Middle,
            XButton1,
            XButton2
        }

        private BoundKey _editKey = new BoundKey { IsMouse = false, KeyCode = Keys.F }; // Default edit key is 'F'
        private bool _editKeyPressed = false;
        private bool _holdingLMB = false;
        private DateTime _editKeyTime = DateTime.MinValue;
        private readonly TimeSpan _editTimeout = TimeSpan.FromSeconds(2.0); // Reset edit state if LMB isn't pressed within 2s

        // UI Controls
        private Label _titleLabel;
        private Label _statusLabel;
        private Label _toggleInfoLabel;
        private Button _bindButton;
        private Panel _statusIndicator;

        public MainForm()
        {
            _keyboardHookProc = HookCallbackKeyboard;
            _mouseHookProc = HookCallbackMouse;

            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "Fortnite Edit on Release";
            this.Size = new Size(380, 260);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 24, 28);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Title
            _titleLabel = new Label
            {
                Text = "Edit on Release Macro",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 245),
                Location = new Point(20, 20),
                Size = new Size(340, 35),
                TextAlign = ContentAlignment.MiddleLeft
            };
            this.Controls.Add(_titleLabel);

            // Bind Button
            _bindButton = new Button
            {
                Text = $"Edit Key: {_editKey}",
                Font = new Font("Segoe UI", 11, FontStyle.Regular),
                BackColor = Color.FromArgb(45, 45, 52),
                ForeColor = Color.FromArgb(230, 230, 235),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(20, 75),
                Size = new Size(320, 45),
                Cursor = Cursors.Hand
            };
            _bindButton.FlatAppearance.BorderSize = 0;
            _bindButton.Click += BindButton_Click;
            this.Controls.Add(_bindButton);

            // Status Panel Indicator
            _statusIndicator = new Panel
            {
                Location = new Point(23, 145),
                Size = new Size(15, 15),
                BackColor = Color.FromArgb(46, 204, 113) // Green initially
            };
            this.Controls.Add(_statusIndicator);

            // Status Label text
            _statusLabel = new Label
            {
                Text = "Macro Active",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(46, 204, 113),
                Location = new Point(45, 142),
                Size = new Size(300, 20)
            };
            this.Controls.Add(_statusLabel);

            // Toggle hotkey info
            _toggleInfoLabel = new Label
            {
                Text = "Press F10 to toggle (Enable/Disable) globally",
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.FromArgb(150, 150, 160),
                Location = new Point(20, 180),
                Size = new Size(320, 20)
            };
            this.Controls.Add(_toggleInfoLabel);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _keyboardHookId = SetHook(_keyboardHookProc, WH_KEYBOARD_LL);
            _mouseHookId = SetHook(_mouseHookProc, WH_MOUSE_LL);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            UnhookWindowsHookEx(_mouseHookId);
            base.OnFormClosing(e);
        }

        private IntPtr SetHook(LowLevelHookProc proc, int hookId)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookId, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private void BindButton_Click(object sender, EventArgs e)
        {
            _isBinding = true;
            _bindButton.Text = "Press any Key or Mouse Button...";
            _bindButton.BackColor = Color.FromArgb(90, 50, 150);
        }

        private void RegisterKey(BoundKey key)
        {
            _editKey = key;
            _isBinding = false;
            _bindButton.Text = $"Edit Key: {_editKey}";
            _bindButton.BackColor = Color.FromArgb(45, 45, 52);
        }

        private void ToggleActive()
        {
            _isActive = !_isActive;
            if (_isActive)
            {
                _statusIndicator.BackColor = Color.FromArgb(46, 204, 113);
                _statusLabel.Text = "Macro Active";
                _statusLabel.ForeColor = Color.FromArgb(46, 204, 113);
            }
            else
            {
                _statusIndicator.BackColor = Color.FromArgb(231, 76, 60);
                _statusLabel.Text = "Macro Inactive";
                _statusLabel.ForeColor = Color.FromArgb(231, 76, 60);
                _editKeyPressed = false;
                _holdingLMB = false;
            }
        }

        private IntPtr HookCallbackKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var kb = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool isInjected = (kb.flags & LLKHF_INJECTED) != 0;

                Keys key = (Keys)kb.vkCode;

                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    // Global Hotkey F10 to toggle macro
                    if (key == Keys.F10 && !isInjected)
                    {
                        ToggleActive();
                        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
                    }

                    if (_isBinding)
                    {
                        // Skip system keys or Alt/Ctrl keys if desired, but here we just bind whatever they press
                        if (key != Keys.F10)
                        {
                            RegisterKey(new BoundKey { IsMouse = false, KeyCode = key });
                            return (IntPtr)1; // Block the key press so it doesn't type/trigger anything during bind
                        }
                    }

                    if (_isActive && !_editKey.IsMouse && key == _editKey.KeyCode)
                    {
                        if (!isInjected)
                        {
                            _editKeyPressed = true;
                            _editKeyTime = DateTime.UtcNow;
                        }
                    }
                }
            }
            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr HookCallbackMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var mouse = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                bool isInjected = (mouse.flags & LLMHF_INJECTED) != 0;

                int message = (int)wParam;

                if (_isBinding)
                {
                    if (message == WM_RBUTTONDOWN)
                    {
                        RegisterKey(new BoundKey { IsMouse = true, MouseBtn = MouseButton.Right });
                        return (IntPtr)1;
                    }
                    else if (message == WM_MBUTTONDOWN)
                    {
                        RegisterKey(new BoundKey { IsMouse = true, MouseBtn = MouseButton.Middle });
                        return (IntPtr)1;
                    }
                    else if (message == WM_XBUTTONDOWN)
                    {
                        ushort xBtn = (ushort)(mouse.mouseData >> 16);
                        MouseButton btn = xBtn == 1 ? MouseButton.XButton1 : MouseButton.XButton2;
                        RegisterKey(new BoundKey { IsMouse = true, MouseBtn = btn });
                        return (IntPtr)1;
                    }
                }

                if (_isActive && !isInjected)
                {
                    // Check if edit key is a mouse button and was pressed
                    if (_editKey.IsMouse)
                    {
                        bool editMousePressed = false;
                        if (_editKey.MouseBtn == MouseButton.Right && message == WM_RBUTTONDOWN)
                            editMousePressed = true;
                        else if (_editKey.MouseBtn == MouseButton.Middle && message == WM_MBUTTONDOWN)
                            editMousePressed = true;
                        else if (_editKey.MouseBtn == MouseButton.XButton1 && message == WM_XBUTTONDOWN && ((mouse.mouseData >> 16) == 1))
                            editMousePressed = true;
                        else if (_editKey.MouseBtn == MouseButton.XButton2 && message == WM_XBUTTONDOWN && ((mouse.mouseData >> 16) == 2))
                            editMousePressed = true;

                        if (editMousePressed)
                        {
                            _editKeyPressed = true;
                            _editKeyTime = DateTime.UtcNow;
                        }
                    }

                    // Left Mouse Button Down
                    if (message == WM_LBUTTONDOWN)
                    {
                        // Check if we pressed the edit key recently (within timeout)
                        if (_editKeyPressed && (DateTime.UtcNow - _editKeyTime) < _editTimeout)
                        {
                            _holdingLMB = true;
                        }
                        else
                        {
                            _editKeyPressed = false; // Reset if expired
                        }
                    }

                    // Left Mouse Button Up
                    if (message == WM_LBUTTONUP)
                    {
                        if (_holdingLMB)
                        {
                            _editKeyPressed = false;
                            _holdingLMB = false;

                            // Release the hooks block momentarily or run simulation asynchronously?
                            // Win32 hooks run on the GUI thread, so simulating directly is fine as long as we flag injected
                            TriggerEditKey();
                        }
                    }
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private void TriggerEditKey()
        {
            if (_editKey.IsMouse)
            {
                uint downFlag = 0;
                uint upFlag = 0;
                uint mouseData = 0;

                switch (_editKey.MouseBtn)
                {
                    case MouseButton.Right:
                        downFlag = MOUSEEVENTF_RIGHTDOWN;
                        upFlag = MOUSEEVENTF_RIGHTUP;
                        break;
                    case MouseButton.Middle:
                        downFlag = MOUSEEVENTF_MIDDLEDOWN;
                        upFlag = MOUSEEVENTF_MIDDLEUP;
                        break;
                    case MouseButton.XButton1:
                        downFlag = MOUSEEVENTF_XDOWN;
                        upFlag = MOUSEEVENTF_XUP;
                        mouseData = 1;
                        break;
                    case MouseButton.XButton2:
                        downFlag = MOUSEEVENTF_XDOWN;
                        upFlag = MOUSEEVENTF_XUP;
                        mouseData = 2;
                        break;
                }

                uint dFlag = downFlag;
                uint uFlag = upFlag;
                uint mData = mouseData;

                Task.Run(async () =>
                {
                    INPUT[] downInput = new INPUT[1];
                    downInput[0] = new INPUT { type = INPUT_MOUSE };
                    downInput[0].U.mi.dwFlags = dFlag;
                    downInput[0].U.mi.mouseData = mData;
                    SendInput(1, downInput, Marshal.SizeOf(typeof(INPUT)));

                    await Task.Delay(15);

                    INPUT[] upInput = new INPUT[1];
                    upInput[0] = new INPUT { type = INPUT_MOUSE };
                    upInput[0].U.mi.dwFlags = uFlag;
                    upInput[0].U.mi.mouseData = mData;
                    SendInput(1, upInput, Marshal.SizeOf(typeof(INPUT)));
                });
            }
            else
            {
                ushort vkCode = (ushort)_editKey.KeyCode;
                ushort scanCode = (ushort)MapVirtualKey(vkCode, 0);

                Task.Run(async () =>
                {
                    INPUT[] downInput = new INPUT[1];
                    downInput[0] = new INPUT { type = INPUT_KEYBOARD };
                    downInput[0].U.ki.wScan = scanCode;
                    downInput[0].U.ki.dwFlags = KEYEVENTF_SCANCODE;
                    SendInput(1, downInput, Marshal.SizeOf(typeof(INPUT)));

                    await Task.Delay(15);

                    INPUT[] upInput = new INPUT[1];
                    upInput[0] = new INPUT { type = INPUT_KEYBOARD };
                    upInput[0].U.ki.wScan = scanCode;
                    upInput[0].U.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;
                    SendInput(1, upInput, Marshal.SizeOf(typeof(INPUT)));
                });
            }
        }
    }
}
