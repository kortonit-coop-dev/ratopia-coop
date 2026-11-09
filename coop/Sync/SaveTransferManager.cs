using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using UnityEngine;
using UnityEngine.SceneManagement;
using Utility.IO;

namespace RatopiaCoop.Sync
{
    public class SaveTransferManager : MonoBehaviour
    {
        public static SaveTransferManager Instance { get; private set; } = null!;

        public const int CHUNK_SIZE = 16384; // 16 KB chunks

        public bool IsTransferring { get; private set; }
        public bool IsSuppressingPauseSync { get; private set; }
        public float TransferProgress { get; private set; } // 0.0 - 1.0
        public string StatusMessage { get; private set; } = string.Empty;

        private byte[]? _clientReceiveBuffer;
        private int _totalBytesExpected;
        private int _totalChunksExpected;
        private int _chunksReceived;

        /// <summary>
        /// Universal guard for all world/tile/blueprint/research hooks:
        /// returns true whenever save transfer, map generation, or save loading is in progress.
        /// </summary>
        public static bool IsWorldLoading
        {
            get
            {
                if (Instance != null && Instance.IsTransferring)
                    return true;
                if (PlayDataMgr.Instance != null && PlayDataMgr.Instance.IsLoadGame)
                    return true;
                var tileMgr = GameMgr.Instance?._TileMgr;
                if (tileMgr != null && (tileMgr.m_MapLoading || tileMgr.m_GameLoading || tileMgr.m_MapInit))
                    return true;
                return false;
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (Instance != this)
            {
                Destroy(this);
            }
        }

        private void Start()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent += HandlePacket;
                CoopNetManager.Instance.OnConnectedEvent += HandleConnected;
                CoopNetManager.Instance.OnDisconnectedEvent += HandleDisconnected;
            }
        }

        private void HandleConnected()
        {
            if (CoopNetManager.Instance.IsHost)
            {
                Plugin.Log.LogInfo("[SaveTransferManager] Client connected. Pausing world and preparing in-memory save transfer...");
                StartHostSaveTransfer();
            }
        }

        private void HandleDisconnected()
        {
            IsTransferring = false;
            IsSuppressingPauseSync = false;
            // Ensure world is unpaused on Host if client disconnects during transfer
            if (GameMgr.Instance != null && GameMgr.Instance._SysMgr != null)
            {
                GameMgr.Instance._SysMgr.SystemPause(false);
            }
            Time.timeScale = 1f;
        }

        public void StartHostSaveTransfer()
        {
            if (PlayDataMgr.Instance == null || PlayDataMgr.Instance.m_GameData == null)
            {
                Plugin.Log.LogWarning("[SaveTransferManager] Cannot transfer save: PlayDataMgr game data is null.");
                return;
            }

            try
            {
                IsTransferring = true;
                StatusMessage = "Pausing world and generating snapshot...";

                // 1. Immediately pause the game world on Host (suppress SystemPatches duplicate packet)
                IsSuppressingPauseSync = true;
                try
                {
                    if (GameMgr.Instance != null && GameMgr.Instance._SysMgr != null)
                    {
                        GameMgr.Instance._SysMgr.SystemPause(true);
                    }
                    Time.timeScale = 0f;
                }
                finally
                {
                    IsSuppressingPauseSync = false;
                }

                // Send single authoritative Pause packet to Client
                NetDataWriter pauseWriter = new NetDataWriter();
                pauseWriter.Put(true);
                pauseWriter.Put(0f);
                CoopNetManager.Instance.SendPacket(PacketType.GamePause, pauseWriter, DeliveryMethod.ReliableOrdered);

                Plugin.Log.LogInfo("[SaveTransferManager] World paused. Capturing current D_Data state in memory...");
                PlayDataMgr.Instance.Save();

                // 2. Serialize D_Data into an in-memory .zip archive without touching SaveFile/PlayFile or user AutoSaves!
                byte[]? zipBytes = SerializeGameDataToZipBytes(PlayDataMgr.Instance.m_GameData);
                if (zipBytes != null && zipBytes.Length > 0)
                {
                    SendSaveDataChunks(zipBytes);
                }
                else
                {
                    Plugin.Log.LogError("[SaveTransferManager] Failed to serialize D_Data into memory zip.");
                    IsTransferring = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SaveTransferManager] Error during save transfer: {ex}");
                IsTransferring = false;
            }
        }

        private static byte[]? SerializeGameDataToZipBytes(D_Data gameData)
        {
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        string cityName = string.IsNullOrEmpty(gameData.FileName) ? "CoopKingdom" : gameData.FileName;
                        var entry = archive.CreateEntry($"{cityName}_0_CoopSync.dat", System.IO.Compression.CompressionLevel.Optimal);
                        using (var entryStream = entry.Open())
                        {
                            var formatter = new BinaryFormatter();
                            formatter.Serialize(entryStream, gameData);
                        }
                    }
                    return memoryStream.ToArray();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SaveTransferManager] Exception in SerializeGameDataToZipBytes: {ex}");
                return null;
            }
        }

        private void SendSaveDataChunks(byte[] data)
        {
            int totalBytes = data.Length;
            int totalChunks = Mathf.CeilToInt((float)totalBytes / CHUNK_SIZE);

            Plugin.Log.LogInfo($"[SaveTransferManager] Sending world snapshot ({totalBytes} bytes in {totalChunks} chunks)...");

            // 1. Send Start packet
            NetDataWriter startWriter = new NetDataWriter();
            startWriter.Put(totalBytes);
            startWriter.Put(totalChunks);
            CoopNetManager.Instance.SendPacket(PacketType.SaveTransferStart, startWriter, DeliveryMethod.ReliableOrdered);

            // 2. Send Chunks
            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * CHUNK_SIZE;
                int count = Mathf.Min(CHUNK_SIZE, totalBytes - offset);

                NetDataWriter chunkWriter = new NetDataWriter();
                chunkWriter.Put(i);
                chunkWriter.Put(count);
                chunkWriter.Put(data, offset, count);

                CoopNetManager.Instance.SendPacket(PacketType.SaveTransferChunk, chunkWriter, DeliveryMethod.ReliableOrdered);
            }

            // 3. Send Complete packet
            NetDataWriter completeWriter = new NetDataWriter();
            CoopNetManager.Instance.SendPacket(PacketType.SaveTransferComplete, completeWriter, DeliveryMethod.ReliableOrdered);

            StatusMessage = "World snapshot sent. Waiting for Client to load...";
            Plugin.Log.LogInfo("[SaveTransferManager] World snapshot transfer completed. Waiting for ClientReady.");
        }

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            switch (type)
            {
                case PacketType.SaveTransferStart:
                {
                    _totalBytesExpected = reader.GetInt();
                    _totalChunksExpected = reader.GetInt();
                    _clientReceiveBuffer = new byte[_totalBytesExpected];
                    _chunksReceived = 0;
                    IsTransferring = true;
                    TransferProgress = 0f;
                    StatusMessage = $"Downloading world snapshot ({_totalBytesExpected / 1024} KB)...";
                    Plugin.Log.LogInfo($"[SaveTransferManager] Receiving save: {_totalBytesExpected} bytes in {_totalChunksExpected} chunks.");
                    break;
                }
                case PacketType.SaveTransferChunk:
                {
                    if (_clientReceiveBuffer == null) return;

                    int chunkIndex = reader.GetInt();
                    int count = reader.GetInt();
                    byte[] chunkBytes = new byte[count];
                    reader.GetBytes(chunkBytes, count);

                    int offset = chunkIndex * CHUNK_SIZE;
                    if (offset + count <= _clientReceiveBuffer.Length)
                    {
                        Buffer.BlockCopy(chunkBytes, 0, _clientReceiveBuffer, offset, count);
                        _chunksReceived++;
                        TransferProgress = (float)_chunksReceived / _totalChunksExpected;
                        StatusMessage = $"Downloading world: {(int)(TransferProgress * 100)}%";
                    }
                    break;
                }
                case PacketType.SaveTransferComplete:
                {
                    if (_clientReceiveBuffer == null) return;

                    Plugin.Log.LogInfo("[SaveTransferManager] World snapshot received in full! Unpacking and loading...");
                    StatusMessage = "Loading world into game scene...";
                    ApplyDownloadedSave(_clientReceiveBuffer);
                    _clientReceiveBuffer = null;
                    // IsTransferring remains true until GameScene is loaded and WaitForSceneLoadedAndNotify finishes
                    break;
                }
                case PacketType.ClientReady:
                {
                    Plugin.Log.LogInfo("[SaveTransferManager] Client is fully loaded in game! Resuming co-op gameplay!");
                    IsTransferring = false;
                    QueenSyncManager.Instance?.SetupQueens();
                    WorldSyncManager.Instance?.SendWorldObjectBatch();

                    // Unpause Host (suppress duplicate SystemPatches broadcast)
                    IsSuppressingPauseSync = true;
                    try
                    {
                        if (GameMgr.Instance != null && GameMgr.Instance._SysMgr != null)
                        {
                            GameMgr.Instance._SysMgr.SystemPause(false);
                        }
                        Time.timeScale = 1f;
                    }
                    finally
                    {
                        IsSuppressingPauseSync = false;
                    }

                    // Send single authoritative unpause packet to Client
                    NetDataWriter unpauseWriter = new NetDataWriter();
                    unpauseWriter.Put(false);
                    unpauseWriter.Put(1f);
                    CoopNetManager.Instance.SendPacket(PacketType.GamePause, unpauseWriter, DeliveryMethod.ReliableOrdered);

                    ChatManager.Instance?.AddMessage("SYSTEM", "Player 2 joined! World resumed.");
                    break;
                }
            }
        }

        private void ApplyDownloadedSave(byte[] saveBytes)
        {
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"Ratopia_Coop_Sync_{Process.GetCurrentProcess().Id}.zip");
            try
            {
                D_Data? loadedData = ExtractDataFromZipBytes(saveBytes);
                if (loadedData != null)
                {
                    File.WriteAllBytes(tempZipPath, saveBytes);
                    Plugin.Log.LogInfo("[SaveTransferManager] D_Data deserialized from memory! Loading world into PlayDataMgr...");

                    SaveLoadAsyncData.Data = loadedData;
                    SaveLoadAsyncData.FilePath = tempZipPath;
                    SaveLoadAsyncData.Message = SaveLoadConfigure.TypeSaveLoadResult.Success;
                    SaveLoadAsyncData.IsFileLoading = false;

                    PlayDataMgr.Instance.LoadData(loadedData);
                    PlayDataMgr.Instance.IsLoadGame = true;

                    // Trigger loading screen into GameScene
                    LoadingSceneMgr.LoadScene(SceneConfigure.SCENE_GAME);

                    // Wait until scene is loaded and GameMgr + LoadMgr.LoadSettingC are completely ready, then notify host
                    StartCoroutine(WaitForSceneLoadedAndNotify(tempZipPath));
                }
                else
                {
                    Plugin.Log.LogError("[SaveTransferManager] Failed to deserialize .dat from downloaded save bytes.");
                    IsTransferring = false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SaveTransferManager] Error applying downloaded save: {ex}");
                IsTransferring = false;
            }
        }

        private static D_Data? ExtractDataFromZipBytes(byte[] zipBytes)
        {
            try
            {
                using (var memoryStream = new MemoryStream(zipBytes))
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Log.LogInfo($"[SaveTransferManager] Found save entry: '{entry.FullName}' ({entry.Length} bytes). Deserializing...");
                            using (var stream = entry.Open())
                            {
                                var formatter = new BinaryFormatter();
                                return (D_Data)formatter.Deserialize(stream);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SaveTransferManager] Exception in ExtractDataFromZipBytes: {ex}");
            }
            return null;
        }

        private IEnumerator WaitForSceneLoadedAndNotify(string tempZipPath)
        {
            // Wait using unscaled realtime (since Time.timeScale may be 0f during pause)
            // until GameScene is active AND MakeMapC + LoadMgr.LoadSettingC (PlayDataMgr.Instance.IsLoadGame) have 100% finished
            while (SceneManager.GetActiveScene().name != SceneConfigure.SCENE_GAME ||
                   GameMgr.Instance == null ||
                   GameMgr.Instance._T_UnitMgr == null ||
                   GameMgr.Instance._T_UnitMgr.m_Queen == null ||
                   GameMgr.Instance._TileMgr == null ||
                   GameMgr.Instance._TileMgr.m_MapLoading ||
                   GameMgr.Instance._TileMgr.m_GameLoading ||
                   (PlayDataMgr.Instance != null && PlayDataMgr.Instance.IsLoadGame))
            {
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // Extra 0.3s realtime settle buffer after LoadSettingC finishes
            yield return new WaitForSecondsRealtime(0.3f);

            try
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }
            catch
            {
                // Ignore temp file cleanup errors
            }

            Plugin.Log.LogInfo("[SaveTransferManager] Game scene & LoadSettingC 100% finished! Setting up co-op characters and notifying host...");
            QueenSyncManager.Instance?.SetupQueens();
            CitizenSyncManager.Instance?.MarkExistingUnitsAsPuppets();

            IsTransferring = false; // Loading fully complete on client
            NetDataWriter readyWriter = new NetDataWriter();
            CoopNetManager.Instance.SendPacket(PacketType.ClientReady, readyWriter, DeliveryMethod.ReliableOrdered);
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
                CoopNetManager.Instance.OnConnectedEvent -= HandleConnected;
                CoopNetManager.Instance.OnDisconnectedEvent -= HandleDisconnected;
            }
        }
    }
}
