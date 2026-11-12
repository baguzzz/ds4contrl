using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using SharpDX.DirectInput;

namespace DS4Unified;

public class MainForm : Form
{
    private TabControl tabControl = null!;
    
    // --- UI Elements ---
    private Button btnStartLocal = null!;
    private Label lblLocalStatus = null!;
    private Button btnStartServer = null!;
    private Label lblServerStatus = null!;
    private TextBox txtIpAddress = null!;
    
    // --- Local Gopher Variables ---
    private bool isLocalRunning = false;
    private DirectInput _directInput = null!;
    private Joystick? _joystick;
    private Thread? _localThread;
    
    // --- Android Server Variables ---
    private bool isServerRunning = false;
    private TcpListener? _tcpListener;
    private ViGEmClient? _viGEmClient;
    private IDualShock4Controller? _virtualDS4;
    
    public MainForm()
    {
        InitializeUI();
        InitDirectInput();
    }

    private void InitializeUI()
    {
        this.Text = "DS4 Unified - Gopher & Server";
        this.Size = new Size(850, 650);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.ForeColor = Color.White;

        tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12F) };

        // ================= TAB 1: LOCAL CONTROLLER =================
        TabPage tabLocal = new TabPage("Local Controller (USB/BT)");
        tabLocal.BackColor = Color.FromArgb(45, 45, 45);

        lblLocalStatus = new Label { Text = "Status: Mati", ForeColor = Color.IndianRed, Location = new Point(20, 60), AutoSize = true, Font = new Font("Segoe UI", 14F) };
        btnStartLocal = new Button { Text = "Mulai Gopher Lokal", Location = new Point(20, 20), Size = new Size(200, 40), BackColor = Color.MediumSeaGreen, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        btnStartLocal.Click += (s, e) => ToggleLocalGopher();

        tabLocal.Controls.Add(btnStartLocal);
        tabLocal.Controls.Add(lblLocalStatus);

        // ================= TAB 2: ANDROID SERVER =================
        TabPage tabServer = new TabPage("Android Server (Wi-Fi)");
        tabServer.BackColor = Color.FromArgb(45, 45, 45);

        lblServerStatus = new Label { Text = "Status: Mati", ForeColor = Color.IndianRed, Location = new Point(20, 60), AutoSize = true, Font = new Font("Segoe UI", 14F) };
        btnStartServer = new Button { Text = "Mulai Server TCP", Location = new Point(20, 20), Size = new Size(200, 40), BackColor = Color.MediumSeaGreen, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        btnStartServer.Click += (s, e) => ToggleAndroidServer();
        
        Label lblIp = new Label { Text = "IP PC ini (Masukkan ke Android):", Location = new Point(250, 25), AutoSize = true, ForeColor = Color.LightGray };
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
        
        this.Load += async (s, e) =>
        {
            try
            {
                // Inisialisasi WebView2
                await webView.EnsureCoreWebView2Async();
                
                // Coba cari file config-editor.html di folder aplikasi
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-editor.html");
                if (File.Exists(htmlPath))
                {
                    webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                }
                else
                {
                    // Jika file tidak ada, tampilkan pesan HTML sederhana
                    webView.NavigateToString(@"
                        <html>
                        <body style='background:#1b2838; color:#c6d4df; font-family:sans-serif; padding:20px;'>
                            <h2 style='color:#f44336;'>config-editor.html Tidak Ditemukan!</h2>
                            <p>Pastikan Anda meletakkan file <b>config-editor.html</b> di folder yang sama dengan DS4Unified.exe ini.</p>
                        </body>
                        </html>");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal memuat WebView2. Pastikan WebView2 Runtime sudah terinstal di PC ini.\n" + ex.Message, "Error UI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };

        tabControl.TabPages.Add(tabLocal);
        tabControl.TabPages.Add(tabServer);
        tabControl.TabPages.Add(tabConfig);
        
        this.Controls.Add(tabControl);
    }

    // ================= LOGIKA LOCAL GOPHER =================
    private void InitDirectInput()
    {
        try
        {
            _directInput = new DirectInput();
            Guid joystickGuid = Guid.Empty;

            // 1. Coba deteksi sebagai Gamepad
            var devices = _directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly);
            if (devices.Count > 0) joystickGuid = devices[0].InstanceGuid;

            // 2. Jika gagal, coba sebagai Joystick (DS4 sering terdeteksi sebagai Joystick)
            if (joystickGuid == Guid.Empty)
            {
                devices = _directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly);
                if (devices.Count > 0) joystickGuid = devices[0].InstanceGuid;
            }

            // 3. Jika gagal, coba sebagai FirstPerson (beberapa driver memakai ini)
            if (joystickGuid == Guid.Empty)
            {
                devices = _directInput.GetDevices(DeviceType.FirstPerson, DeviceEnumerationFlags.AttachedOnly);
                if (devices.Count > 0) joystickGuid = devices[0].InstanceGuid;
            }

            // 4. Fallback terakhir: Ambil semua GameControl
            if (joystickGuid == Guid.Empty)
            {
                devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                if (devices.Count > 0) joystickGuid = devices[0].InstanceGuid;
            }

            if (joystickGuid != Guid.Empty)
            {
                _joystick = new Joystick(_directInput, joystickGuid);
                _joystick.Properties.BufferSize = 128;
                _joystick.Acquire();
                lblLocalStatus.Text = "Status: Controller Terdeteksi!";
            }
            else
            {
                lblLocalStatus.Text = "Status: Controller Tidak Ditemukan";
                MessageBox.Show("Controller DS4 tidak terdeteksi.\n\nTips:\n1. Tutup DS4Windows / Steam.\n2. Colok kabel USB yang mendukung data.\n3. Buka Control Panel > Devices and Printers untuk memastikan Windows mendeteksinya.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal inisialisasi DirectInput: " + ex.Message);
        }
    }

    private void ToggleLocalGopher()
    {
        if (_joystick == null)
        {
            MessageBox.Show("Controller tidak terdeteksi. Tidak bisa memulai.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

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
            isLocalRunning = true;
            lblLocalStatus.Text = "Status: Aktif (Mengontrol Mouse)";
            lblLocalStatus.ForeColor = Color.LimeGreen;
            btnStartLocal.Text = "Hentikan Gopher Lokal";
            btnStartLocal.BackColor = Color.IndianRed;
            
            _localThread = new Thread(LocalGopherLoop) { IsBackground = true };
            _localThread.Start();
        }
    }

    private void LocalGopherLoop()
    {
        try
        {
            _joystick.Acquire();
            while (isLocalRunning)
            {
                try
                {
                    _joystick.Poll();
                    var state = _joystick.GetCurrentState();

                    // Baca Analog Kiri (Mouse Move)
                    float lx = NormalizeAxis(state.X);
                    float ly = NormalizeAxis(state.Y);
                    MoveMouse((int)(lx * Math.Abs(lx) * 15), (int)(ly * Math.Abs(ly) * 15));

                    // Baca Tombol (Index 1 biasanya Cross/Cross, untuk klik kiri)
                    bool isClicking = false;
                    if (state.Buttons.Length > 1)
                    {
                        isClicking = state.Buttons[1];
                    }
                    
                    MouseClickAction(isClicking);
                    
                    Thread.Sleep(16); // ~60 FPS
                }
                catch 
                { 
                    Thread.Sleep(100); 
                    _joystick.Acquire(); 
                }
            }
        }
        catch { }
        _joystick.Unacquire();
    }

    // ================= LOGIKA ANDROID SERVER =================
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
                MessageBox.Show("Gagal start server. Pastikan ViGEmBus terinstal (https://github.com/nefarius/ViGEmBus/releases).\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async Task ServerLoop()
    {
        while (isServerRunning && _tcpListener != null)
        {
            try
            {
                using var client = await _tcpListener.AcceptTcpClientAsync();
                
                // Update UI dari thread lain
                this.Invoke(() => {
                    lblServerStatus.Text = "Status: Android Terhubung!";
                    lblServerStatus.ForeColor = Color.LimeGreen;
                });

                using var stream = client.GetStream();
                byte[] buffer = new byte[12];

                while (client.Connected && isServerRunning)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 12 && buffer[0] == 0xAA && _virtualDS4 != null)
                    {
                        // Parsing data dari Android
                        ushort buttons = (ushort)(buffer[1] | (buffer[2] << 8));
                        
                        _virtualDS4.SetButtonState(DualShock4Button.Cross, (buttons & 1) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.Circle, (buttons & 2) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.Square, (buttons & 4) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.Triangle, (buttons & 8) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.ShoulderLeft, (buttons & 16) != 0);
                        _virtualDS4.SetButtonState(DualShock4Button.ShoulderRight, (buttons & 32) != 0);

                        _virtualDS4.SetAxisValue(DualShock4Axis.LeftThumbX, (byte)(buffer[3] - 128));
                        _virtualDS4.SetAxisValue(DualShock4Axis.LeftThumbY, (byte)(buffer[4] - 128));
                        _virtualDS4.SetAxisValue(DualShock4Axis.RightThumbX, (byte)(buffer[5] - 128));
                        _virtualDS4.SetAxisValue(DualShock4Axis.RightThumbY, (byte)(buffer[6] - 128));
                        
                        _virtualDS4.SubmitReport();
                    }
                }
            }
            catch { 
                break; // Keluar dari loop jika koneksi terputus
            }
        }
        
        this.Invoke(() => {
            if(isServerRunning) {
                lblServerStatus.Text = "Status: Koneksi Terputus, Menunggu...";
                lblServerStatus.ForeColor = Color.Orange;
            }
        });
    }

    // ================= UTILS =================
    private static float NormalizeAxis(int val)
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

    private static void MouseClickAction(bool down)
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