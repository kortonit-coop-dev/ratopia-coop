using System;
using BepInEx;
using HarmonyLib;
using RatopiaUnitBrain.Puppeteer;
using RatopiaUnitBrain.Registry;
using RatopiaUnitBrain.Sensor;
using UnityEngine;

namespace RatopiaUnitBrain
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class UnitBrainPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.ratopiacoop.unitbrain";
        public const string PLUGIN_NAME = "Ratopia Unit Brain API";
        public const string PLUGIN_VERSION = "1.0.0";

        public static UnitBrainPlugin Instance { get; private set; } = null!;
        public static BepInEx.Logging.ManualLogSource Log => Instance.Logger;
        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            try
            {
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(gameObject);
            }
            catch { }

            Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} initializing...");

            // Pre-initialize singletons
            _ = UnitRegistry.Instance;
            _ = BrainSensor.Instance;
            _ = BrainPuppeteer.Instance;

            try
            {
                _harmony = new Harmony(PLUGIN_GUID);
                _harmony.PatchAll();
                Logger.LogInfo($"[{PLUGIN_NAME}] All Harmony patches applied successfully!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[{PLUGIN_NAME}] Error applying Harmony patches: {ex}");
            }
        }

        private void Update()
        {
            Puppeteer.BrainPuppeteer.Instance.UpdatePuppets(Time.deltaTime);
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch { }
        }
    }
}
