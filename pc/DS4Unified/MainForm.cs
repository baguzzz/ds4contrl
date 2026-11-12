using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Vortice.DirectInput;

namespace DS4Unified;

public class MainForm : Form
{
    private TabControl tabControl;
    
    // --- UI Elements ---
    private Button btnStartLocal;
    private Label lblLocalStatus;
    private Button btnStartServer;
    private Label lblServerStatus;
    private TextBox txtIpAddress;
    
    // --- Local Gopher Variables ---
    private bool isLocalRunning = false;
    private IDirectInput8 _dinput;
    private IDirectInputDevice8 _joystick;
    private Thread _localThread;
    
    // --- Android Server Variables ---
    private bool isServerRunning = false;
    private TcpListener _tcpListener;
    private ViGEmClient _viGEmClient;
    private IDualShock4Controller _virtualDS4;
    
    public MainForm()
    {
        InitializeUI();
        InitDirectInput();
    }

    private void InitializeUI()
    {
        this.Text = "DS4 Unified - Gopher & Server";
        this.Size = new Size(800, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.ForeColor = Color.White;

        tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12F) };

        // ================= TAB 1: LOCAL CONTROLLER =================
        TabPage tabLocal = new TabPage("Local Controller (USB/BT)");
        tabLocal.BackColor = Color.FromArgb(45, 45, 45);

        lblLocalStatus = new Label { Text = "Status: Mati", ForeColor = Color.IndianRed, Location = new Point(20, 60), AutoSize = true, Font = new Font("Segoe UI", 14F) };
        btnStartLocal = new Button { Text = "Mulai Gopher Lokal", Location = new Point(20, 20), Size = new Size(200, 40), BackColor = Color.MediumSeaGreen, FlatStyle = FlatStyle.Flat };
        btnStartLocal.Click += (s, e) => ToggleLocalGopher();

        tabLocal.Controls.Add(btnStartLocal);
        tabLocal.Controls.Add(lblLocalStatus);

        // ================= TAB 2: ANDROID SERVER =================
        TabPage tabServer = new TabPage("Android Server (Wi-Fi)");
        tabServer.BackColor = Color.FromArgb(45, 45, 45);

        lblServerStatus = new Label { Text = "Status: Mati", ForeColor = Color.IndianRed, Location = new Point(20, 60), AutoSize = true, Font = new Font("Segoe UI", 14F) };
        btnStartServer = new Button { Text = "Mulai Server TCP", Location = new Point(20, 20), Size = new Size(200, 40), BackColor = Color.MediumSeaGreen, FlatStyle = FlatStyle.Flat };
        btnStartServer.Click += (s, e) => ToggleAndroidServer();
        
        Label lblIp = new Label { Text = "IP PC ini:", Location = new Point(250, 25), AutoSize = true, ForeColor = Color.LightGray };
        txtIpAddress = new TextBox { Location = new Point(250, 50), Width = 200, ReadOnly = true, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        txtIpAddress.Text = GetLocalIPAddress();

        tabServer.Controls.Add(btnStartServer);
        tabServer.Controls.Add(lblServerStatus);
        tabServer.Controls.Add(lblIp);
        tabServer.Controls.Add(txtIpAddress);

        // ================= TAB 3: CONFIG EDITOR =================
        TabPage tabConfig = new TabPage("Config Editor");
        tabConfig.BackColor = Color.FromArgb(45, 45, 45);
        
        WebView2 webView = new WebView2 { Dock = DockStyle.Fill };
        tabConfig.Controls.Add(webView);
        
        // Load HTML config editor saat tab dibuka
        this.Load += async (s, e) =>
        {
            await webView.EnsureCoreWebView2Async();
            // Anda bisa mengarahkan ini ke file config-editor.html lokal atau URL
            // webView.CoreWebView2.Navigate("file:///C:/path/to/config-editor.html");
            webView.NavigateToString(GetHtmlConfigEditor()); // Menggunakan string HTML bawaan
        };

        tabControl.TabPages.Add(tabLocal);
        tabControl.TabPages.Add(tabServer);
        tabControl.TabPages.Add(tabConfig);
        
        this.Controls.Add(tabControl);
    }

    // ================= LOGIKA LOCAL GOPHER (DirectInput) =================
    private void InitDirectInput()
    {
        try
        {
            _dinput = DInput.DirectInput8Create();
            var devices = _dinput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
            if (devices.Count > 0)
            {
                _joystick = _dinput.CreateDevice(devices[0].InstanceGuid);
                _joystick.SetDataFormat<JoystickState>();
                _joystick.Properties.BufferSize.Value = 128;
            }
        }
        catch { }
    }

    private void ToggleLocalGopher()
    {
        if (isLocalRunning)
        {
            isLocalRunning = false;
            lblLocalStatus.Text = "Status: Mati";
            lblLocalStatus.ForeColor = Color.IndianRed;
            btnStartLocal.Text = "Mulai Gopher Lokal";
            btnStartLocal.BackColor = Color.MediumSeaGreen;
        }
        else
        {
            if (_joystick == null)
            {
                MessageBox.Show("Controller DS4 tidak terdeteksi via USB/Bluetooth!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            isLocalRunning = true;
            lblLocalStatus.Text = "Status: Aktif (Membaca USB)";
            lblLocalStatus.ForeColor = Color.LimeGreen;
            btnStartLocal.Text = "Hentikan Gopher Lokal";
            btnStartLocal.BackColor = Color.IndianRed;
            
            _localThread = new Thread(LocalGopherLoop) { IsBackground = true };
            _localThread.Start();
        }
    }

    private void LocalGopherLoop()
    {
        _joystick.Acquire();
        while (isLocalRunning)
        {
            try
            {
                _joystick.Poll();
                var state = _joystick.GetCurrentJoystickState();

                // Baca Analog Kiri (Mouse) - Asumsi Rx/Ry atau Z/Rz
                float lx = NormalizeAxis(state.X);
                float ly = NormalizeAxis(state.Y);
                MoveMouse((int)(lx * Math.Abs(lx) * 15), (int)(ly * Math.Abs(ly) * 15));

                // Baca Tombol (Cross = Klik Kiri, dll) -- Sederhana
                bool cross = state.Buttons[1]; 
                if (cross) MouseClick(true); else MouseClick(false);
                
                Thread.Sleep(16); // ~60 FPS
            }
            catch { Thread.Sleep(100); _joystick.Acquire(); }
        }
        _joystick.Unacquire();
    }

    // ================= LOGIKA ANDROID SERVER (ViGEm & TCP) =================
    private void ToggleAndroidServer()
    {
        if (isServerRunning)
        {
            isServerRunning = false;
            _tcpListener?.Stop();
            _viGEmClient?.Dispose();
            lblServerStatus.Text = "Status: Mati";
            lblServerStatus.ForeColor = Color.IndianRed;
            btnStartServer.Text = "Mulai Server TCP";
            btnStartServer.BackColor = Color.MediumSeaGreen;
        }
        else
        {
            try
            {
                _viGEmClient = new ViGEmClient();
                _virtualDS4 = _viGEmClient.CreateDualShock4Controller();
                _virtualDS4.Connect();

                _tcpListener = new TcpListener(IPAddress.Any, 27020);
                _tcpListener.Start();
                
                isServerRunning = true;
                lblServerStatus.Text = "Status: Menunggu Android...";
                lblServerStatus.ForeColor = Color.Orange;
                btnStartServer.Text = "Hentikan Server TCP";
                btnStartServer.BackColor = Color.IndianRed;

                Task.Run(ServerLoop);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal start server. Pastikan ViGEmBus terinstal.\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async Task ServerLoop()
    {
        while (isServerRunning)
        {
            try
            {
                using var client = await _tcpListener.AcceptTcpClientAsync();
                this.Invoke(() => {
                    lblServerStatus.Text = "Status: Android Terhubung!";
                    lblServerStatus.ForeColor = Color.LimeGreen;
                });

                using var stream = client.GetStream();
                byte[] buffer = new byte[12];

                while (client.Connected && isServerRunning)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 12 && buffer[0] == 0xAA)
                    {
                        // Update Virtual DS4
                        ushort buttons = (ushort)(buffer[1] | (buffer[2] << 8));
                        _virtualDS4.SetButtonState(DualShock4Button.Cross, (buttons & 1) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.Circle, (buttons & 2) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.Square, (buttons & 4) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.Triangle, (buttons & 8) != 0);

                        _virtualDS4.SetAxisValue(DualShock4Axis.LeftThumbX, (byte)(buffer[3] - 128));
                        _virtualDS4.SetAxisValue(DualShock4Axis.LeftThumbY, (byte)(buffer[4] - 128));
                        _virtualDS4.SetAxisValue(DualShock4Axis.RightThumbX, (byte)(buffer[5] - 128));
                        _virtualDS4.SetAxisValue(DualShock4Axis.RightThumbY, (byte)(buffer[6] - 128));
                        _virtualDS4.SubmitReport();
                    }
                }
            }
            catch { break; }
        }
    }

    // ================= UTILS =================
    private static float NormalizeAxis(short val)
    {
        if (Math.Abs(val) < 8000) return 0f;
        return val > 0 ? (val - 8000) / 24767f : (val + 8000) / 24767f;
    }

    private static void MoveMouse(int dx, int dy)
    {
        INPUT input = new INPUT { type = 0 };
        input.mi.dx = dx; input.mi.dy = dy; input.mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE
        SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
    }

    private static void MouseClick(bool down)
    {
        INPUT input = new INPUT { type = 0 };
        input.mi.dwFlags = down ? 0x0002u : 0x0004u; // LEFTDOWN / LEFTUP
        SendInput(1, new[] { input }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(INPUT)));
    }

    private static string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        }
        return "127.0.0.1";
    }

    private string GetHtmlConfigEditor()
    {
        return @"
        <html>
        <body style='background:#1b2838; color:#c6d4df; font-family:sans-serif; padding:20px;'>
            <h2 style='color:#66c0f4;'>Config Editor Integrated</h2>
            <p>Di sini Anda bisa menaruh file <b>config-editor.html</b> yang kita buat sebelumnya.</p>
            <p>Ganti fungsi <code>GetHtmlConfigEditor()</code> di kode C# untuk memuat string HTML Anda.</p>
        </body>
        </html>";
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
