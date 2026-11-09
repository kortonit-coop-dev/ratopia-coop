using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using RatopiaCoop.Sync;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RatopiaCoop.UI
{
    public class CoopGUI : MonoBehaviour
    {
        public static CoopGUI Instance { get; private set; } = null!;

        // Win32 API for kernel-level hardware keyboard query (bypasses any Unity input interception)
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static readonly int[] Win32Hotkeys = new int[]
        {
            0x79, // VK_F10
            0x7A, // VK_F11
            0x78, // VK_F9
            0x71, // VK_F2
            0x72, // VK_F3
            0xC0, // VK_OEM_3 (~ / ` / Ё)
            0x2D, // VK_INSERT
            0x24  // VK_HOME
        };

        private static readonly string[] Win32KeyLabels = new string[]
        {
            "F10", "F11", "F9", "F2", "F3", "` (Tilde / Ё)", "Insert", "Home"
        };

        private static readonly bool[] Win32KeyState = new bool[256];

        // Menu visibility (starts closed so Singleplayer Title Screen is unobstructed; top banner & F9 open it)
        private bool _showGUI = false;
        public bool IsMenuVisible => _showGUI;

        // Window drag state
        private bool _isDragging = false;
        private Vector2 _dragOffset = Vector2.zero;

        // Debounce to prevent rapid double-toggling
        private float _lastToggleTime = 0f;
        private const float DEBOUNCE_COOLDOWN = 0.35f;

        private string _targetIp = "127.0.0.1";
        private string _targetPort = "7777";
        private Rect _windowRect = new Rect(25, 52, 400, 560);
        private Vector2 _scrollPosition = Vector2.zero;

        // Solid textures for guaranteed visibility over any Unity canvas
        private static Texture2D? _solidDarkBg;
        private static Texture2D? _solidHeaderBg;
        private static Texture2D? _solidBoxBg;
        private static Texture2D? _solidInputBg;
        private static Texture2D? _solidButtonBg;
        private static Texture2D? _solidButtonHoverBg;
        private static Texture2D? _solidGreenButtonBg;
        private static Texture2D? _solidRedButtonBg;
        private static Texture2D? _solidBorder;
        private static Texture2D? _solidTopBannerBg;

        // Custom styles
        private static GUIStyle? _whiteTextStyle;
        private static GUIStyle? _titleStyle;
        private static GUIStyle? _buttonStyle;
        private static GUIStyle? _greenButtonStyle;
        private static GUIStyle? _redButtonStyle;
        private static GUIStyle? _closeBtnStyle;
        private static GUIStyle? _inputStyle;
        private static GUIStyle? _boxStyle;

        // LAN Discovery
        private LiteNetLib.NetManager? _discoveryClient;
        private readonly List<LanHostInfo> _discoveredHosts = new List<LanHostInfo>();
        private bool _isScanning;
        private float _scanTimeout;
        private bool _firstOnGUILogged;

        private struct LanHostInfo
        {
            public string Name;
            public IPEndPoint EndPoint;
            public float DiscoveredTime;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this && Instance.gameObject != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Plugin.Print($"[CoopGUI] Awake() initialized on GameObject '{gameObject.name}'!");
        }

        private void OnEnable()
        {
            Plugin.Print($"[CoopGUI] OnEnable() called on '{gameObject.name}'! Active menu: {_showGUI}");
        }

        private void OnDisable()
        {
            Plugin.Print($"[CoopGUI] [WARN] OnDisable() called on '{gameObject.name}'!");
        }

        private void Start()
        {
            Plugin.Print($"[CoopGUI] Start() executed on '{gameObject.name}'! Initial menu visibility: {_showGUI}");
        }

        /// <summary>
        /// Centralized menu toggle with strict debounce cooldown to eliminate double-firing.
        /// </summary>
        public void ToggleMenu(string triggerSource = "Manual")
        {
            float now = Time.unscaledTime;
            if (now - _lastToggleTime < DEBOUNCE_COOLDOWN)
            {
                return;
            }

            _lastToggleTime = now;
            _showGUI = !_showGUI;
            Plugin.Print($"[CoopGUI] >>> MENU TOGGLED by [{triggerSource}] -> NOW {(_showGUI ? "VISIBLE (OPEN)" : "HIDDEN (CLOSED)")} <<<");
        }

        private void Update()
        {
            PollInputTriggers();

            // Update LAN discovery if scanning
            if (_isScanning)
            {
                _discoveryClient?.PollEvents();
                if (Time.time > _scanTimeout)
                {
                    StopLanScan();
                }
            }
        }

        public void PollHardwareHotkeysDirect()
        {
            PollInputTriggers();
        }

        /// <summary>
        /// Multi-source input polling: Hardware Win32 -> Unity New Input System -> Unity Legacy Input.
        /// </summary>
        public void PollInputTriggers()
        {
            // Only process hotkeys in the currently focused window (prevents toggling both Host & Client simultaneously on 1 PC)
            if (!Application.isFocused)
            {
                return;
            }

            // 1. Hardware Win32 GetAsyncKeyState
            for (int i = 0; i < Win32Hotkeys.Length; i++)
            {
                int vk = Win32Hotkeys[i];
                bool isDown = (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (isDown && !Win32KeyState[vk])
                {
                    Win32KeyState[vk] = true;
                    ToggleMenu($"Win32 Hardware Key: {Win32KeyLabels[i]}");
                    return;
                }
                else if (!isDown)
                {
                    Win32KeyState[vk] = false;
                }
            }

            // 2. Unity New Input System (Keyboard.current)
            try
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (kb.f10Key.wasPressedThisFrame) { ToggleMenu("Keyboard.current: F10"); return; }
                    if (kb.f11Key.wasPressedThisFrame) { ToggleMenu("Keyboard.current: F11"); return; }
                    if (kb.f9Key.wasPressedThisFrame) { ToggleMenu("Keyboard.current: F9"); return; }
                    if (kb.f2Key.wasPressedThisFrame) { ToggleMenu("Keyboard.current: F2"); return; }
                    if (kb.backquoteKey.wasPressedThisFrame) { ToggleMenu("Keyboard.current: Backquote (~)"); return; }
                    if (kb.insertKey.wasPressedThisFrame) { ToggleMenu("Keyboard.current: Insert"); return; }
                    if (kb.homeKey.wasPressedThisFrame) { ToggleMenu("Keyboard.current: Home"); return; }
                }
            }
            catch { }

            // 3. Unity Legacy Input (in case project setting is Both)
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.F10)) { ToggleMenu("Input.GetKeyDown: F10"); return; }
                if (UnityEngine.Input.GetKeyDown(KeyCode.F11)) { ToggleMenu("Input.GetKeyDown: F11"); return; }
                if (UnityEngine.Input.GetKeyDown(KeyCode.F9)) { ToggleMenu("Input.GetKeyDown: F9"); return; }
                if (UnityEngine.Input.GetKeyDown(KeyCode.BackQuote)) { ToggleMenu("Input.GetKeyDown: BackQuote (~)"); return; }
                if (UnityEngine.Input.GetKeyDown(KeyCode.Insert)) { ToggleMenu("Input.GetKeyDown: Insert"); return; }
                if (UnityEngine.Input.GetKeyDown(KeyCode.F2)) { ToggleMenu("Input.GetKeyDown: F2"); return; }
            }
            catch { }
        }

        private static Texture2D CreateSolidColorTexture(Color color)
        {
            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private void InitStyles()
        {
            if (_solidDarkBg == null) _solidDarkBg = CreateSolidColorTexture(new Color(0.08f, 0.11f, 0.16f, 0.98f));
            if (_solidHeaderBg == null) _solidHeaderBg = CreateSolidColorTexture(new Color(0.12f, 0.18f, 0.28f, 1.0f));
            if (_solidBoxBg == null) _solidBoxBg = CreateSolidColorTexture(new Color(0.05f, 0.07f, 0.11f, 0.95f));
            if (_solidInputBg == null) _solidInputBg = CreateSolidColorTexture(new Color(0.04f, 0.05f, 0.08f, 1.0f));
            if (_solidButtonBg == null) _solidButtonBg = CreateSolidColorTexture(new Color(0.18f, 0.45f, 0.90f, 1.0f));
            if (_solidButtonHoverBg == null) _solidButtonHoverBg = CreateSolidColorTexture(new Color(0.28f, 0.58f, 1.0f, 1.0f));
            if (_solidGreenButtonBg == null) _solidGreenButtonBg = CreateSolidColorTexture(new Color(0.12f, 0.65f, 0.32f, 1.0f));
            if (_solidRedButtonBg == null) _solidRedButtonBg = CreateSolidColorTexture(new Color(0.78f, 0.18f, 0.18f, 1.0f));
            if (_solidBorder == null) _solidBorder = CreateSolidColorTexture(new Color(0.0f, 0.85f, 1.0f, 1.0f));
            if (_solidTopBannerBg == null) _solidTopBannerBg = CreateSolidColorTexture(new Color(0.06f, 0.09f, 0.15f, 0.95f));

            if (_whiteTextStyle == null)
            {
                _whiteTextStyle = new GUIStyle
                {
                    normal = { textColor = Color.white },
                    fontSize = 12,
                    richText = true,
                    wordWrap = true,
                    margin = new RectOffset(2, 2, 2, 2),
                    padding = new RectOffset(2, 2, 2, 2)
                };
            }

            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle
                {
                    normal = { textColor = new Color(0.0f, 0.95f, 1.0f) },
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    richText = true,
                    margin = new RectOffset(2, 2, 4, 4)
                };
            }

            if (_buttonStyle == null)
            {
                _buttonStyle = new GUIStyle
                {
                    normal = { background = _solidButtonBg, textColor = Color.white },
                    hover = { background = _solidButtonHoverBg, textColor = Color.cyan },
                    active = { background = _solidBorder, textColor = Color.white },
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                    margin = new RectOffset(2, 2, 3, 3),
                    padding = new RectOffset(6, 6, 4, 4)
                };
            }

            if (_greenButtonStyle == null)
            {
                _greenButtonStyle = new GUIStyle
                {
                    normal = { background = _solidGreenButtonBg, textColor = Color.white },
                    hover = { background = _solidButtonHoverBg, textColor = Color.yellow },
                    active = { background = _solidBorder, textColor = Color.white },
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                    margin = new RectOffset(2, 2, 3, 3),
                    padding = new RectOffset(6, 6, 4, 4)
                };
            }

            if (_redButtonStyle == null)
            {
                _redButtonStyle = new GUIStyle
                {
                    normal = { background = _solidRedButtonBg, textColor = Color.white },
                    hover = { background = _solidButtonHoverBg, textColor = Color.white },
                    active = { background = _solidBorder, textColor = Color.yellow },
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                    margin = new RectOffset(2, 2, 3, 3),
                    padding = new RectOffset(6, 6, 4, 4)
                };
            }

            if (_closeBtnStyle == null)
            {
                _closeBtnStyle = new GUIStyle
                {
                    normal = { background = _solidRedButtonBg, textColor = Color.white },
                    hover = { background = _solidButtonHoverBg, textColor = Color.yellow },
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
            }

            if (_inputStyle == null)
            {
                _inputStyle = new GUIStyle(GUI.skin.textField)
                {
                    normal = { background = _solidInputBg, textColor = Color.cyan },
                    focused = { background = _solidInputBg, textColor = Color.white },
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 2, 2)
                };
            }

            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle
                {
                    normal = { background = _solidBoxBg },
                    padding = new RectOffset(8, 8, 8, 8),
                    margin = new RectOffset(0, 0, 4, 4)
                };
            }
        }

        private static void DrawRectOutline(Rect rect, int thickness, Color color)
        {
            if (_solidBorder == null) _solidBorder = CreateSolidColorTexture(color);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), _solidBorder);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), _solidBorder);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), _solidBorder);
            GUI.DrawTexture(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), _solidBorder);
        }

        private void OnGUI()
        {
            if (Screen.width < 100 || Screen.height < 100) return;

            // Highest possible rendering priority over all 2D/3D and UI canvases
            GUI.depth = -9999;
            InitStyles();

            if (!_firstOnGUILogged)
            {
                _firstOnGUILogged = true;
                Plugin.Print($"[CoopGUI] OnGUI is rendering normally! Resolution: {Screen.width}x{Screen.height}");
            }

            // 0. IMGUI Event KeyDown Check
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.F10 || e.keyCode == KeyCode.F11 || e.keyCode == KeyCode.F9 ||
                    e.keyCode == KeyCode.BackQuote || e.keyCode == KeyCode.Insert || e.keyCode == KeyCode.F2)
                {
                    ToggleMenu($"IMGUI KeyDown: {e.keyCode}");
                    e.Use();
                }
            }

            // 1. Persistent Top Floating Header / Button Bar (ALWAYS VISIBLE)
            float barWidth = Mathf.Min(Screen.width - 20, 560f);
            Rect headerRect = new Rect(10, 8, barWidth, 34);

            GUI.DrawTexture(headerRect, _solidTopBannerBg!);
            DrawRectOutline(headerRect, 1, new Color(0f, 0.85f, 1f, 0.8f));

            GUI.Label(new Rect(headerRect.x + 8, headerRect.y + 7, 360, 22),
                "<b><color=#00e5ff>[RATOPIA CO-OP]</color></b> Hotkeys: <b>F10 | F11 | ~ | F9</b>", _whiteTextStyle);

            // Clickable Toggle Button in header
            Rect btnRect = new Rect(headerRect.x + barWidth - 170, headerRect.y + 4, 162, 26);
            string btnText = _showGUI ? "<b><color=#ff6666>[ ❌ Close Menu ]</color></b>" : "<b><color=#55ff88>[ 🌐 Open Co-op Menu ]</color></b>";
            if (GUI.Button(btnRect, btnText, _buttonStyle))
            {
                ToggleMenu("Header Bar Button Click");
            }

            if (!_showGUI) return;

            // 2. Dragging Logic for Window Header
            Rect titleBarRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width - 40, 32);
            if (e != null)
            {
                if (e.type == EventType.MouseDown && titleBarRect.Contains(e.mousePosition))
                {
                    _isDragging = true;
                    _dragOffset = e.mousePosition - new Vector2(_windowRect.x, _windowRect.y);
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDragging = false;
                }
                if (_isDragging && e.type == EventType.MouseDrag)
                {
                    _windowRect.x = e.mousePosition.x - _dragOffset.x;
                    _windowRect.y = e.mousePosition.y - _dragOffset.y;
                }
            }

            // Clamp window within viewport
            _windowRect.x = Mathf.Clamp(_windowRect.x, 10, Mathf.Max(10, Screen.width - _windowRect.width - 10));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 48, Mathf.Max(48, Screen.height - _windowRect.height - 10));

            // 3. Draw Window Frame & Background Directly (Guaranteed solid, no window-layering occlusion)
            Rect borderRect = new Rect(_windowRect.x - 2, _windowRect.y - 2, _windowRect.width + 4, _windowRect.height + 4);
            GUI.DrawTexture(borderRect, _solidBorder!);
            GUI.DrawTexture(_windowRect, _solidDarkBg!);

            // Window Header Bar
            Rect winHeaderRect = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, 32);
            GUI.DrawTexture(winHeaderRect, _solidHeaderBg!);
            DrawRectOutline(winHeaderRect, 1, new Color(0f, 0.85f, 1f, 0.4f));

            GUI.Label(new Rect(winHeaderRect.x + 10, winHeaderRect.y + 6, winHeaderRect.width - 50, 22),
                "<b><color=#00e5ff>Ratopia LAN Co-op Mod</color></b> (F10 / F11 / ~ / F9)", _titleStyle);

            // Close [X] button in window header
            Rect winCloseRect = new Rect(winHeaderRect.x + winHeaderRect.width - 32, winHeaderRect.y + 4, 26, 24);
            if (GUI.Button(winCloseRect, "<b>✕</b>", _closeBtnStyle))
            {
                ToggleMenu("Window Header [X]");
            }

            // 4. Draw Window Contents via Direct GUILayout.BeginArea
            Rect contentArea = new Rect(_windowRect.x + 12, _windowRect.y + 38, _windowRect.width - 24, _windowRect.height - 48);
            GUILayout.BeginArea(contentArea);
            DrawMenuContents();
            GUILayout.EndArea();
        }

        private void DrawMenuContents()
        {
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            var net = CoopNetManager.Instance;
            if (net == null)
            {
                GUILayout.Space(15);
                GUILayout.Label("<color=#ffaa00><b>Co-op Network Subsystems are Initializing...</b></color>", _whiteTextStyle);
                GUILayout.Space(10);
                if (GUILayout.Button("<b>Initialize Co-op Network Core</b>", _buttonStyle, GUILayout.Height(34)))
                {
                    Plugin.EnsureCoreRoot();
                }
                GUILayout.EndScrollView();
                return;
            }

            // Status Card
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label($"<b>Status:</b> <color={(net.IsConnected ? "#00ff88" : "#ffaa00")}>{net.StatusMessage}</color>", _whiteTextStyle);

            var saveMgr = SaveTransferManager.Instance;
            if (saveMgr != null && saveMgr.IsTransferring)
            {
                GUILayout.Label($"<b>World Sync:</b> <color=#00e5ff>{saveMgr.StatusMessage}</color>", _whiteTextStyle);
            }

            if (net.IsConnected)
            {
                GUILayout.Label($"<b>Ping:</b> {net.Ping} ms", _whiteTextStyle);
                GUILayout.Label($"<b>Role:</b> <color=#00e5ff>{(net.IsHost ? "HOST (Player 1)" : "CLIENT (Player 2)")}</color>", _whiteTextStyle);

                var queenMgr = QueenSyncManager.Instance;
                if (queenMgr != null)
                {
                    string localName = net.IsHost ? "Queen (Host)" : "Worker Rat (Player 2)";
                    string remoteName = net.IsHost ? "Worker Rat (Player 2)" : "Queen (Host)";
                    GUILayout.Label($"<b>Your Unit:</b> {(queenMgr.LocalCharacter != null ? $"<color=#00ff88>{localName} (Active)</color>" : "<color=#ffaa00>Waiting...</color>")}", _whiteTextStyle);
                    GUILayout.Label($"<b>Partner Unit:</b> {(queenMgr.RemoteCharacter != null ? $"<color=#00e5ff>{remoteName} (In Sync)</color>" : "<color=#888888>Not connected</color>")}", _whiteTextStyle);
                }
                GUILayout.Label("<i>Chat: Press [Enter] to talk in-game</i>", _whiteTextStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);

            if (!net.IsConnected && !net.IsHost)
            {
                // Host button (Green)
                if (GUILayout.Button("<b>[ Create LAN Server (Port 7777) ]</b>", _greenButtonStyle, GUILayout.Height(36)))
                {
                    int port = int.Parse(_targetPort);
                    Plugin.Print($"[CoopGUI] Action: Starting LAN Host on port {port}...");
                    net.StartHost(port);
                }

                GUILayout.Space(10);
                GUILayout.Label("<b>Direct Connect by IP:</b>", _titleStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", _whiteTextStyle, GUILayout.Width(35));
                _targetIp = GUILayout.TextField(_targetIp, _inputStyle, GUILayout.Width(170), GUILayout.Height(24));
                GUILayout.Label("Port:", _whiteTextStyle, GUILayout.Width(40));
                _targetPort = GUILayout.TextField(_targetPort, _inputStyle, GUILayout.Width(65), GUILayout.Height(24));
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                if (GUILayout.Button("<b>[ Connect to IP ]</b>", _buttonStyle, GUILayout.Height(32)))
                {
                    if (int.TryParse(_targetPort, out int port))
                    {
                        Plugin.Print($"[CoopGUI] Action: Connecting to {_targetIp}:{port}...");
                        net.Connect(_targetIp, port);
                    }
                }

                GUILayout.Space(10);

                // LAN Broadcast Discovery
                if (!_isScanning)
                {
                    if (GUILayout.Button("<b>[ Scan Local Network (LAN Discovery) ]</b>", _buttonStyle, GUILayout.Height(30)))
                    {
                        Plugin.Print("[CoopGUI] Action: Scanning LAN for servers...");
                        StartLanScan();
                    }
                }
                else
                {
                    GUILayout.Label("<color=#00e5ff><b>Scanning local subnet for hosts...</b></color>", _whiteTextStyle);
                }

                // Show discovered servers
                if (_discoveredHosts.Count > 0)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("<b>Discovered LAN Hosts:</b>", _titleStyle);
                    for (int i = 0; i < _discoveredHosts.Count; i++)
                    {
                        var host = _discoveredHosts[i];
                        if (GUILayout.Button($"<b>▶ Connect: {host.Name} ({host.EndPoint.Address})</b>", _greenButtonStyle, GUILayout.Height(28)))
                        {
                            StopLanScan();
                            _targetIp = host.EndPoint.Address.ToString();
                            _targetPort = host.EndPoint.Port.ToString();
                            Plugin.Print($"[CoopGUI] Action: Connecting to discovered host {host.Name} ({_targetIp}:{_targetPort})...");
                            net.Connect(_targetIp, host.EndPoint.Port);
                        }
                    }
                }
            }
            else
            {
                // Connected / Hosting state
                if (GUILayout.Button("<b>[ Disconnect / Stop Server ]</b>", _redButtonStyle, GUILayout.Height(34)))
                {
                    Plugin.Print("[CoopGUI] Action: Disconnecting / Stopping server...");
                    net.Stop();
                }

                if (net.IsHost)
                {
                    GUILayout.Space(6);
                    if (GUILayout.Button("<b>[ Resync World with Client ]</b>", _buttonStyle, GUILayout.Height(30)))
                    {
                        Plugin.Print("[CoopGUI] Action: Triggering manual world resync...");
                        SaveTransferManager.Instance?.StartHostSaveTransfer();
                    }
                }
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Close Menu [F10 / F11 / ~ / F9]", _buttonStyle, GUILayout.Height(26)))
            {
                ToggleMenu("Bottom Close Button");
            }

            GUILayout.EndScrollView();
        }

        private void StartLanScan()
        {
            _discoveredHosts.Clear();
            _isScanning = true;
            _scanTimeout = Time.time + 3.0f; // 3 seconds timeout

            var listener = new EventBasedNetListener();
            listener.NetworkReceiveUnconnectedEvent += (point, reader, messageType) =>
            {
                try
                {
                    if (reader.AvailableBytes > 0)
                    {
                        string header = reader.GetString();
                        if (header == "RATOPIA_LAN_HOST")
                        {
                            string hostName = reader.GetString();
                            if (!_discoveredHosts.Exists(h => h.EndPoint.Address.Equals(point.Address)))
                            {
                                Plugin.Print($"[CoopGUI] Found LAN Host: {hostName} at {point.Address}:{point.Port}");
                                _discoveredHosts.Add(new LanHostInfo
                                {
                                    Name = hostName,
                                    EndPoint = point,
                                    DiscoveredTime = Time.time
                                });
                            }
                        }
                    }
                }
                catch { }
            };

            _discoveryClient = new LiteNetLib.NetManager(listener)
            {
                BroadcastReceiveEnabled = true,
                UnconnectedMessagesEnabled = true,
                IPv6Enabled = false
            };

            _discoveryClient.Start();
            NetDataWriter writer = new NetDataWriter();
            writer.Put("RATOPIA_LAN_DISCOVER");
            _discoveryClient.SendBroadcast(writer, CoopNetManager.DEFAULT_PORT);
        }

        private void StopLanScan()
        {
            _isScanning = false;
            _discoveryClient?.Stop();
            _discoveryClient = null;
        }

        private void OnDestroy()
        {
            StopLanScan();
        }
    }
}
