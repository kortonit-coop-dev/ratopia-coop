using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using RatopiaCoop.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RatopiaCoop.Sync
{
    public class ChatManager : MonoBehaviour
    {
        public static ChatManager Instance { get; private set; } = null!;

        public struct ChatMsg
        {
            public string Sender;
            public string Message;
            public float Time;
        }

        public readonly List<ChatMsg> Messages = new List<ChatMsg>();
        public bool IsChatOpen { get; private set; }
        private string _inputBuffer = string.Empty;
        private const int MAX_MESSAGES = 50;

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
            }
        }

        private void Update()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected)
            {
                IsChatOpen = false;
                return;
            }

            if (IsEnterPressed())
            {
                if (!IsChatOpen)
                {
                    IsChatOpen = true;
                    _inputBuffer = string.Empty;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_inputBuffer))
                    {
                        SendChatMessage(_inputBuffer.Trim());
                    }
                    IsChatOpen = false;
                    _inputBuffer = string.Empty;
                }
            }
            else if (IsChatOpen && IsEscapePressed())
            {
                IsChatOpen = false;
                _inputBuffer = string.Empty;
            }
        }

        private bool IsEnterPressed()
        {
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
                    return true;
            }
            catch { }

            try
            {
                if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                    return true;
            }
            catch { }

            return false;
        }

        private bool IsEscapePressed()
        {
            try
            {
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                    return true;
            }
            catch { }

            try
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    return true;
            }
            catch { }

            return false;
        }

        public void SendChatMessage(string text)
        {
            string sender = CoopNetManager.Instance != null && CoopNetManager.Instance.IsHost ? "Player 1 (Host)" : "Player 2";
            AddMessage(sender, text);

            if (CoopNetManager.Instance != null && CoopNetManager.Instance.IsConnected)
            {
                NetDataWriter writer = new NetDataWriter();
                writer.Put(sender);
                writer.Put(text);
                CoopNetManager.Instance.SendPacket(PacketType.ChatMessage, writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public void AddMessage(string sender, string text)
        {
            Messages.Add(new ChatMsg
            {
                Sender = sender,
                Message = text,
                Time = UnityEngine.Time.time
            });

            if (Messages.Count > MAX_MESSAGES)
            {
                Messages.RemoveAt(0);
            }
        }

        private void HandlePacket(PacketType type, NetDataReader reader)
        {
            if (type == PacketType.ChatMessage)
            {
                string sender = reader.GetString();
                string text = reader.GetString();
                AddMessage(sender, text);
            }
        }

        private void OnGUI()
        {
            if (CoopNetManager.Instance == null || !CoopNetManager.Instance.IsConnected)
                return;

            GUI.depth = -9998;

            // Always display recent chat messages in bottom-left
            float startY = Screen.height - 240;
            float width = 380;
            float height = 180;

            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.75f);
            GUILayout.BeginArea(new Rect(15, startY, width, height), GUI.skin.box);

            GUILayout.Label("<b>[Kingdom Chat]</b> <i>(Press Enter to speak)</i>");
            
            int startIdx = Mathf.Max(0, Messages.Count - 6);
            for (int i = startIdx; i < Messages.Count; i++)
            {
                var msg = Messages[i];
                string colorHex = msg.Sender.Contains("Host") ? "#66CCFF" : (msg.Sender == "SYSTEM" ? "#FFCC00" : "#CC99FF");
                GUILayout.Label($"<color={colorHex}>{msg.Sender}:</color> {msg.Message}");
            }

            // Input field when active
            if (IsChatOpen)
            {
                GUI.SetNextControlName("ChatInputField");
                _inputBuffer = GUILayout.TextField(_inputBuffer, GUILayout.Height(25));
                GUI.FocusControl("ChatInputField");
            }

            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (CoopNetManager.Instance != null)
            {
                CoopNetManager.Instance.OnPacketReceivedEvent -= HandlePacket;
            }
        }
    }
}
