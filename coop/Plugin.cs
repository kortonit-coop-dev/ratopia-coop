using System;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RatopiaCoop
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    [BepInDependency("com.ratopiacoop.unitbrain")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.ratopia.coop";
        public const string PLUGIN_NAME = "Ratopia LAN Co-op Mod";
        public const string PLUGIN_VERSION = "1.0.0";

        public static Plugin Instance { get; private set; } = null!;
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;
        private Harmony? _harmony;

        // Windows Win32 API for Console initialization
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;
        private int _pluginFrameCount;

        private void Awake()
        {
            Instance = this;

            // 1. Force Open Windows Terminal Debug Console
            InitConsole();

            Print($"{PLUGIN_NAME} v{PLUGIN_VERSION} initializing...");

            // Protect Plugin GameObject from scene unloads
            try
            {
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(gameObject);
            }
            catch { }

            // 2. Attach All Co-op Subsystems to persistent core GameObject with HideAndDontSave
            EnsureCoreRoot();

            // 3. Register SceneLoaded listener
            SceneManager.sceneLoaded += (scene, mode) =>
            {
                Print($"[Plugin] SceneLoaded: '{scene.name}' (mode: {mode})");
            };

            // 4. Apply Harmony Patches with Safe Error Isolation
            try
            {
                _harmony = new Harmony(PLUGIN_GUID);
                _harmony.PatchAll();
                Print("[Plugin] All Harmony patches applied successfully!");
            }
            catch (Exception ex)
            {
                Print($"[Plugin] [ERROR] Harmony PatchAll encountered an error: {ex}");
            }

            Print("================================================================================");
            Print($"[Plugin] {PLUGIN_NAME} READY!");
            Print("  Menu is OPEN by default on screen.");
            Print("  Hotkeys: [F10], [F11], [F9], [~ Tilde / Ё], [Insert], [F2]");
            Print("  Or click the [Open / Close Co-op Menu] button at the top of the screen.");
            Print("================================================================================");
        }

        private static GameObject? _coreRoot;

        public static void EnsureCoreRoot()
        {
            if (_coreRoot != null) return;

            try
            {
                _coreRoot = new GameObject("RatopiaCoop_Core");
                _coreRoot.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(_coreRoot);

                _coreRoot.AddComponent<Network.CoopNetManager>();
                _coreRoot.AddComponent<Sync.SaveTransferManager>();
                _coreRoot.AddComponent<Sync.QueenSyncManager>();
                _coreRoot.AddComponent<Sync.WorldSyncManager>();
                _coreRoot.AddComponent<Sync.OrderSyncManager>();
                _coreRoot.AddComponent<Sync.CitizenSyncManager>();
                _coreRoot.AddComponent<Sync.CombatSyncManager>();
                _coreRoot.AddComponent<Sync.EconomySyncManager>();
                _coreRoot.AddComponent<Sync.ChatManager>();
                _coreRoot.AddComponent<UI.CoopGUI>();

                Print("[Plugin] All 11 co-op subsystems instantiated on immortal HideAndDontSave GameObject!");
            }
            catch (Exception ex)
            {
                Print($"[Plugin] [ERROR] Failed to attach subsystems: {ex}");
            }
        }

        private void OnEnable()
        {
            Print("[Plugin] OnEnable() called!");
        }

        private void OnDisable()
        {
            Print("[Plugin] [WARN] OnDisable() called!");
        }

        private void Start()
        {
            Print("[Plugin] Start() reached! Unity engine main loop is actively executing.");
        }

        private void Update()
        {
            _pluginFrameCount++;
            if (_pluginFrameCount % 300 == 0) // Heartbeat every ~5 seconds
            {
                string sceneName = SceneManager.GetActiveScene().name;
                bool isVisible = UI.CoopGUI.Instance != null && UI.CoopGUI.Instance.IsMenuVisible;
                Print($"[Plugin] Heartbeat: frame {_pluginFrameCount} | Scene: '{sceneName}' | Menu Visible: {isVisible}");
            }
        }

        private void InitConsole()
        {
            try
            {
                if (AllocConsole())
                {
                    var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                    Console.SetOut(stdOut);
                    Console.SetError(stdOut);

                    IntPtr handle = GetConsoleWindow();
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SW_SHOW);
                    }
                }
            }
            catch { }

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("================================================================================");
                Console.WriteLine("  RATOPIA LAN CO-OP MOD v1.0.0 - DEDICATED DEBUG CONSOLE");
                Console.WriteLine("  Hotkeys: [F10]  [F11]  [F9]  [` Tilde / Ё]  [Insert]  [F2]");
                Console.WriteLine("  Persistent Header: Click '[Open / Close Co-op Menu]' on screen anytime");
                Console.WriteLine("================================================================================");
                Console.ResetColor();
            }
            catch { }
        }

        public static void Print(string message)
        {
            Log.LogInfo(message);
            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
            catch { }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
