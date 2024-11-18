using BepInEx;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Multiplayer {
    [BepInDependency(NineSolsAPICore.PluginGUID)]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Multiplayer : BaseUnityPlugin {
        private Harmony? harmony;
        private TcpListener? serverListener;
        private TcpClient? client;
        private NetworkStream? stream;
        private Thread? serverThread;
        private Thread? clientThread;

        private const int ServerPort = 7777;

        // Config Bindings for Key Shortcuts
        private ConfigEntry<KeyboardShortcut> startServerShortcut;
        private ConfigEntry<KeyboardShortcut> joinServerShortcut;
        private ConfigEntry<KeyboardShortcut> sendFloatShortcut;

        private void Awake() {
            Log.Init(Logger);
            harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            Log.Info("Multiplayer plugin loaded.");

            // Bind shortcuts from the config file
            startServerShortcut = Config.Bind("Server", "StartServerShortcut",
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to start the server");
            joinServerShortcut = Config.Bind("Client", "JoinServerShortcut",
                new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl), "Shortcut to join the server");
            sendFloatShortcut = Config.Bind("Client", "SendFloatShortcut",
                new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl), "Shortcut to send float to server");

            // Add keybindings using KeybindManager
            KeybindManager.Add(this, StartServer, () => startServerShortcut.Value);
            KeybindManager.Add(this, StartClient, () => joinServerShortcut.Value);
            KeybindManager.Add(this, SendVector3, () => sendFloatShortcut.Value);
        }

        void Update() {
            if (client != null && client.Connected) {
                SendVector3();
            } else {
                Log.Warning("Client is not connected, skipping sending Vector3.");
            }
        }

        public void StartServer() {
            if (serverListener == null) {
                serverThread = new Thread(() => {
                    serverListener = new TcpListener(IPAddress.Any, ServerPort);
                    serverListener.Start();
                    Log.Info($"Server started on port {ServerPort}");

                    while (true) {
                        var client = serverListener.AcceptTcpClient();
                        Log.Info($"Client connected: {client.Client.RemoteEndPoint}");
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                });
                serverThread.Start();
            } else {
                Log.Info("Server is already running.");
            }
        }

        public void StartClient() {
            if (client == null) {
                try {
                    client = new TcpClient("127.0.0.1", ServerPort);
                    stream = client.GetStream();
                    Log.Info("Connected to server.");

                    string message = "Hello from client!";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    stream.Write(data, 0, data.Length);

                    clientThread = new Thread(() => HandleServerResponse(client));
                    clientThread.Start();
                } catch (Exception ex) {
                    Log.Error($"Error connecting to server: {ex.Message}");
                }
            } else {
                Log.Info("Client already connected.");
            }
        }

        private void HandleClient(object obj) {
            var client = (TcpClient)obj;
            var stream = client.GetStream();

            try {
                byte[] buffer = new byte[1024];
                while (true) {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;  // No more data from client

                    // If we read exactly 12 bytes, treat it as a Vector3 (3 floats)
                    if (bytesRead == 12) {
                        float x = BitConverter.ToSingle(buffer, 0);
                        float y = BitConverter.ToSingle(buffer, sizeof(float));
                        float z = BitConverter.ToSingle(buffer, 2 * sizeof(float));
                        Vector3 receivedVector = new Vector3(x, y, z);
                        Player.i.transform.position = receivedVector;
                        Log.Info($"Received Vector3 from client: {receivedVector}");
                    } else {
                        // Otherwise, treat the data as a string or some other message
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Log.Info($"Received message from client: {message}");
                    }

                    // Send a response back to the client
                    byte[] response = Encoding.UTF8.GetBytes("Message received");
                    stream.Write(response, 0, response.Length);
                    Log.Info("Sent response to client: Message received");
                }
            } catch (Exception ex) {
                Log.Warning($"Client disconnected or error occurred: {ex.Message}");
            } finally {
                client.Close();
            }
        }


        private void HandleServerResponse(TcpClient client) {
            var stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try {
                while (true) {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0) {
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Log.Info($"Received from server: {message}");
                    }
                }
            } catch {
                Log.Warning("Disconnected from server.");
            } finally {
                client.Close();
            }
        }

        public void SendVector3() {
            if (client != null && client.Connected && stream != null) {
                try {
                    Vector3 valueToSend = Player.i.transform.position; // Example Vector3 value to send
                    byte[] data = new byte[3 * sizeof(float)]; // Each float is 4 bytes, so for 3 floats, it's 12 bytes

                    // Copy the components of Vector3 into the byte array
                    Buffer.BlockCopy(BitConverter.GetBytes(valueToSend.x), 0, data, 0, sizeof(float));
                    Buffer.BlockCopy(BitConverter.GetBytes(valueToSend.y), 0, data, sizeof(float), sizeof(float));
                    Buffer.BlockCopy(BitConverter.GetBytes(valueToSend.z), 0, data, 2 * sizeof(float), sizeof(float));

                    stream.Write(data, 0, data.Length);
                    Log.Info($"Sent Vector3: {valueToSend}");
                } catch (Exception ex) {
                    Log.Error($"Error sending Vector3: {ex.Message}");
                }
            } else {
                if (client == null || !client.Connected) {
                    Log.Warning("Client not connected.");
                } else {
                    Log.Warning("Stream is null.");
                }
            }
        }


        private void OnDestroy() {
            serverListener?.Stop();
            serverThread?.Abort();
            client?.Close();
            clientThread?.Abort();
            harmony?.UnpatchSelf();
        }
    }
}
