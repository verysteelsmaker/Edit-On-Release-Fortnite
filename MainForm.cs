using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Edit_on_release
{
    public class MainForm : Form
    {
        // ===================== Theme =====================
        private static class Theme
        {
            public static readonly Color Background   = Color.FromArgb(17, 17, 21);
            public static readonly Color Card         = Color.FromArgb(26, 26, 32);
            public static readonly Color CardHover    = Color.FromArgb(34, 34, 42);
            public static readonly Color Border       = Color.FromArgb(44, 44, 54);
            public static readonly Color Accent       = Color.FromArgb(139, 92, 246);   // violet
            public static readonly Color AccentSoft   = Color.FromArgb(46, 38, 70);
            public static readonly Color Text         = Color.FromArgb(240, 240, 245);
            public static readonly Color TextMuted    = Color.FromArgb(140, 140, 152);
            public static readonly Color Green        = Color.FromArgb(52, 211, 153);
            public static readonly Color GreenSoft    = Color.FromArgb(22, 46, 40);
            public static readonly Color Red          = Color.FromArgb(248, 113, 113);
            public static readonly Color RedSoft      = Color.FromArgb(52, 26, 30);
        }

        // ===================== Rounded UI helpers =====================
        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d - 1, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d - 1, bounds.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private class RoundedButton : Button
        {
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int CornerRadius { get; set; } = 8;
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color BorderColor { get; set; } = Theme.Border;
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color HoverColor { get; set; } = Theme.CardHover;
            private bool _hovered;

            public RoundedButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using var path = RoundedRect(rect, CornerRadius);
                using var fill = new SolidBrush(_hovered ? HoverColor : BackColor);
                using var pen = new Pen(BorderColor, 1);

                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);

                TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private class CardPanel : Panel
        {
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public int CornerRadius { get; set; } = 10;
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            public Color BorderColor { get; set; } = Theme.Border;

            public CardPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using var path = RoundedRect(rect, CornerRadius);
                using var fill = new SolidBrush(BackColor);
                using var pen = new Pen(BorderColor, 1);

                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        // ===================== Win32 Hook API declarations =====================
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

        // Window dragging
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HT_CAPTION = 0x2;

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

        // ===================== State variables =====================
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

        // ===================== UI Controls =====================
        private Panel _titleBar;
        private Label _titleLabel;
        private RoundedButton _closeButton;
        private RoundedButton _minimizeButton;
        private Label _keySectionLabel;
        private RoundedButton _bindButton;
        private CardPanel _statusCard;
        private Panel _statusIndicator;
        private Label _statusLabel;
        private RoundedButton _toggleButton;
        private Label _footerLabel;

        public MainForm()
        {
            _keyboardHookProc = HookCallbackKeyboard;
            _mouseHookProc = HookCallbackMouse;

            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "Edit on Release";
            this.Size = new Size(380, 330);
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = false;
            this.BackColor = Theme.Background;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9);

            // Rounded window corners
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 16, 16));

            // App icon (embedded via ApplicationIcon in csproj)
            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { /* icon is cosmetic; ignore if extraction fails */ }

            // -------- Custom title bar --------
            _titleBar = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(Width, 44),
                BackColor = Theme.Background
            };
            _titleBar.MouseDown += TitleBar_MouseDown;
            this.Controls.Add(_titleBar);

            var iconBox = new PictureBox
            {
                Location = new Point(16, 12),
                Size = new Size(20, 20),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            if (this.Icon != null)
                iconBox.Image = this.Icon.ToBitmap();
            iconBox.MouseDown += TitleBar_MouseDown;
            _titleBar.Controls.Add(iconBox);

            _titleLabel = new Label
            {
                Text = "Edit on Release",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Theme.Text,
                Location = new Point(42, 12),
                AutoSize = true
            };
            _titleLabel.MouseDown += TitleBar_MouseDown;
            _titleBar.Controls.Add(_titleLabel);

            _closeButton = new RoundedButton
            {
                Text = "\u2715",
                Font = new Font("Segoe UI", 9),
                Size = new Size(30, 26),
                Location = new Point(Width - 42, 9),
                BackColor = Theme.Background,
                ForeColor = Theme.TextMuted,
                HoverColor = Theme.RedSoft,
                BorderColor = Theme.Background,
                CornerRadius = 6
            };
            _closeButton.Click += (s, e) => Close();
            _titleBar.Controls.Add(_closeButton);

            _minimizeButton = new RoundedButton
            {
                Text = "\u2013",
                Font = new Font("Segoe UI", 9),
                Size = new Size(30, 26),
                Location = new Point(Width - 76, 9),
                BackColor = Theme.Background,
                ForeColor = Theme.TextMuted,
                HoverColor = Theme.CardHover,
                BorderColor = Theme.Background,
                CornerRadius = 6
            };
            _minimizeButton.Click += (s, e) => WindowState = FormWindowState.Minimized;
            _titleBar.Controls.Add(_minimizeButton);

            // -------- Edit key section --------
            _keySectionLabel = new Label
            {
                Text = "EDIT KEY",
                Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold),
                ForeColor = Theme.TextMuted,
                Location = new Point(22, 58),
                AutoSize = true
            };
            this.Controls.Add(_keySectionLabel);

            _bindButton = new RoundedButton
            {
                Text = _editKey.ToString(),
                Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold),
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                HoverColor = Theme.CardHover,
                BorderColor = Theme.Border,
                CornerRadius = 10,
                Location = new Point(20, 80),
                Size = new Size(340, 56)
            };
            _bindButton.Click += BindButton_Click;
            this.Controls.Add(_bindButton);

            // -------- Status card --------
            _statusCard = new CardPanel
            {
                Location = new Point(20, 152),
                Size = new Size(340, 68),
                BackColor = Theme.Card,
                BorderColor = Theme.Border,
                CornerRadius = 10
            };
            this.Controls.Add(_statusCard);

            _statusIndicator = new Panel
            {
                Location = new Point(20, 28),
                Size = new Size(12, 12),
                BackColor = Theme.Green
            };
            _statusIndicator.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Theme.Card);
                using var brush = new SolidBrush(_statusIndicator.BackColor);
                e.Graphics.FillEllipse(brush, 0, 0, 11, 11);
            };
            _statusCard.Controls.Add(_statusIndicator);

            _statusLabel = new Label
            {
                Text = "Macro active",
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Theme.Green,
                Location = new Point(42, 24),
                AutoSize = true
            };
            _statusCard.Controls.Add(_statusLabel);

            _toggleButton = new RoundedButton
            {
                Text = "Disable",
                Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold),
                Size = new Size(88, 34),
                Location = new Point(236, 17),
                BackColor = Theme.GreenSoft,
                ForeColor = Theme.Green,
                HoverColor = Theme.CardHover,
                BorderColor = Theme.Border,
                CornerRadius = 8
            };
            _toggleButton.Click += (s, e) => ToggleActive();
            _statusCard.Controls.Add(_toggleButton);

            // -------- Footer hints --------
            _footerLabel = new Label
            {
                Text = "F10 — toggle globally    •    Esc — cancel binding",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Theme.TextMuted,
                Location = new Point(0, 240),
                Size = new Size(Width, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(_footerLabel);

            var howToLabel = new Label
            {
                Text = "Press edit key \u2192 drag LMB \u2192 release to confirm edit",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Theme.TextMuted,
                Location = new Point(0, 266),
                Size = new Size(Width, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            this.Controls.Add(howToLabel);
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeft, int nTop, int nRight, int nBottom, int nWidthEllipse, int nHeightEllipse);

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
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
            StartBinding();
        }

        private void StartBinding()
        {
            _isBinding = true;
            _bindButton.Text = "Press any key or mouse button\u2026";
            _bindButton.Font = new Font("Segoe UI", 10.5f);
            _bindButton.BackColor = Theme.AccentSoft;
            _bindButton.HoverColor = Theme.AccentSoft;
            _bindButton.ForeColor = Theme.Accent;
            _bindButton.BorderColor = Theme.Accent;
            _bindButton.Invalidate();
        }

        private void CancelBinding()
        {
            _isBinding = false;
            ResetBindButtonVisuals();
        }

        private void RegisterKey(BoundKey key)
        {
            _editKey = key;
            _isBinding = false;
            ResetBindButtonVisuals();
        }

        private void ResetBindButtonVisuals()
        {
            _bindButton.Text = _editKey.ToString();
            _bindButton.Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold);
            _bindButton.BackColor = Theme.Card;
            _bindButton.HoverColor = Theme.CardHover;
            _bindButton.ForeColor = Theme.Text;
            _bindButton.BorderColor = Theme.Border;
            _bindButton.Invalidate();
        }

        private void ToggleActive()
        {
            _isActive = !_isActive;
            if (_isActive)
            {
                _statusIndicator.BackColor = Theme.Green;
                _statusLabel.Text = "Macro active";
                _statusLabel.ForeColor = Theme.Green;
                _toggleButton.Text = "Disable";
                _toggleButton.BackColor = Theme.GreenSoft;
                _toggleButton.ForeColor = Theme.Green;
            }
            else
            {
                _statusIndicator.BackColor = Theme.Red;
                _statusLabel.Text = "Macro inactive";
                _statusLabel.ForeColor = Theme.Red;
                _toggleButton.Text = "Enable";
                _toggleButton.BackColor = Theme.RedSoft;
                _toggleButton.ForeColor = Theme.Red;
                _editKeyPressed = false;
                _holdingLMB = false;
            }
            _statusIndicator.Invalidate();
            _toggleButton.Invalidate();
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
                        if (key == Keys.Escape)
                        {
                            CancelBinding();
                            return (IntPtr)1;
                        }
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
