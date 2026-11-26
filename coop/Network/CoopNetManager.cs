using System;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace RatopiaCoop.Network
{
    public class CoopNetManager : MonoBehaviour, INetEventListener
    {
        public static CoopNetManager Instance { get; private set; } = null!;

        public const int DEFAULT_PORT = 7777;
        public const string CONNECTION_KEY = "RatopiaCoop_v1";

        private LiteNetLib.NetManager? _netManager;
        private NetPeer? _serverPeer; // Set on Client
        private NetPeer? _clientPeer; // Set on Host

        public bool IsHost { get; private set; }
        public bool IsClient { get; private set; }
        public bool IsConnected => IsHost ? (_clientPeer != null && _clientPeer.ConnectionState == ConnectionState.Connected) 
                                          : (_serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected);

        public int Ping => IsHost ? (_clientPeer?.Ping ?? 0) : (_serverPeer?.Ping ?? 0);
        public string StatusMessage { get; set; } = "Offline";

        // Callbacks
        public event Action? OnConnectedEvent;
        public event Action? OnDisconnectedEvent;
        public event Action<PacketType, NetDataReader>? OnPacketReceivedEvent;

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

        private void Update()
        {
            _netManager?.PollEvents();
        }

        public bool StartHost(int port = DEFAULT_PORT)
        {
            Stop();

            _netManager = new LiteNetLib.NetManager(this)
            {
                AutoRecycle = true,
                BroadcastReceiveEnabled = true,
                UnconnectedMessagesEnabled = true,
                IPv6Enabled = false
            };

            bool success = _netManager.Start(port);
            if (success)
            {
                IsHost = true;
                IsClient = false;
                StatusMessage = $"Hosting on port {port}. Waiting for player...";
                Plugin.Log.LogInfo($"[CoopNetManager] Host started on port {port}");
            }
            else
            {
                StatusMessage = $"Failed to bind port {port}";
                Plugin.Log.LogError($"[CoopNetManager] Failed to start host on port {port}");
                Stop();
            }

            return success;
        }

        public bool Connect(string hostIp, int port = DEFAULT_PORT)
        {
            Stop();

            _netManager = new LiteNetLib.NetManager(this)
            {
                AutoRecycle = true,
                IPv6Enabled = false
            };

            if (!_netManager.Start())
            {
                StatusMessage = "Failed to start network client";
                Plugin.Log.LogError("[CoopNetManager] Failed to start network client");
                Stop();
                return false;
            }

            IsHost = false;
            IsClient = true;
            StatusMessage = $"Connecting to {hostIp}:{port}...";
            Plugin.Log.LogInfo($"[CoopNetManager] Connecting to {hostIp}:{port}...");

            _serverPeer = _netManager.Connect(hostIp, port, CONNECTION_KEY);
            return _serverPeer != null;
        }

        public void Stop()
        {
            if (_netManager != null)
            {
                _netManager.Stop();
                _netManager = null;
            }

            _serverPeer = null;
            _clientPeer = null;
            IsHost = false;
            IsClient = false;
            StatusMessage = "Offline";
            OnDisconnectedEvent?.Invoke();
        }

        public void SendPacket(PacketType type, NetDataWriter writer, DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered)
        {
            if (writer == null) return;
            try
            {
                NetDataWriter finalWriter = new NetDataWriter();
                finalWriter.Put((byte)type);
                finalWriter.Put(writer.Data, 0, writer.Length);

                // If non-reliable delivery method exceeds single-packet MTU limit, promote to ReliableOrdered so LiteNetLib fragments it safely
                if (deliveryMethod != DeliveryMethod.ReliableOrdered && deliveryMethod != DeliveryMethod.ReliableUnordered)
                {
                    if (finalWriter.Length > 900)
                    {
                        deliveryMethod = DeliveryMethod.ReliableOrdered;
                    }
                }

                if (IsHost)
                {
                    _clientPeer?.Send(finalWriter, deliveryMethod);
                }
                else if (IsClient)
                {
                    _serverPeer?.Send(finalWriter, deliveryMethod);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[CoopNetManager] Failed to send packet {type} ({writer.Length} bytes, {deliveryMethod}): {ex.Message}");
            }
        }

        // --- INetEventListener Callbacks ---

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (IsHost && _clientPeer == null)
            {
                request.AcceptIfKey(CONNECTION_KEY);
                Plugin.Log.LogInfo($"[CoopNetManager] Connection request accepted from {request.RemoteEndPoint}");
            }
            else
            {
                request.Reject();
                Plugin.Log.LogWarning($"[CoopNetManager] Connection request rejected from {request.RemoteEndPoint}");
            }
        }

        public void OnPeerConnected(NetPeer peer)
        {
            Plugin.Log.LogInfo($"[CoopNetManager] Peer connected: {peer}");
            if (IsHost)
            {
                _clientPeer = peer;
                StatusMessage = $"Player connected ({peer})";
            }
            else
            {
                _serverPeer = peer;
                StatusMessage = $"Connected to Host ({peer})";
            }

            OnConnectedEvent?.Invoke();
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Plugin.Log.LogInfo($"[CoopNetManager] Peer disconnected: {peer}, reason: {disconnectInfo.Reason}");
            if (IsHost && _clientPeer == peer)
            {
                _clientPeer = null;
                StatusMessage = "Client disconnected. Waiting for player...";
            }
            else if (IsClient && _serverPeer == peer)
            {
                _serverPeer = null;
                StatusMessage = $"Disconnected: {disconnectInfo.Reason}";
            }

            OnDisconnectedEvent?.Invoke();
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (reader.AvailableBytes < 1) return;

            byte typeByte = reader.GetByte();
            PacketType type = (PacketType)typeByte;

            OnPacketReceivedEvent?.Invoke(type, reader);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Plugin.Log.LogError($"[CoopNetManager] Network error from {endPoint}: {socketError}");
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // LAN Discovery responder
            if (IsHost && messageType == UnconnectedMessageType.Broadcast)
            {
                string msg = reader.GetString();
                if (msg == "RATOPIA_LAN_DISCOVER")
                {
                    NetDataWriter resp = new NetDataWriter();
                    resp.Put("RATOPIA_LAN_HOST");
                    resp.Put(Environment.MachineName);
                    _netManager?.SendUnconnectedMessage(resp, remoteEndPoint);
                }
            }
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
