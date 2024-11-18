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

        private void Awake() {
            harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            Logger.LogInfo("Multiplayer plugin loaded.");

            // Bind shortcuts from the config file
            startServerShortcut = Config.Bind("Server", "StartServerShortcut",
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to start the server");
            joinServerShortcut = Config.Bind("Client", "JoinServerShortcut",
                new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl), "Shortcut to join the server");

            // Add keybindings using KeybindManager
            KeybindManager.Add(this, StartServer, () => startServerShortcut.Value);
            KeybindManager.Add(this, StartClient, () => joinServerShortcut.Value);
        }

        private void Update() {
            // Listen for key presses based on the configured shortcuts
            if (startServerShortcut.Value.IsPressed()) {
                StartServer();
            }
            if (joinServerShortcut.Value.IsPressed()) {
                StartClient();
            }
        }

        public void StartServer() {
            if (serverListener == null) {
                serverThread = new Thread(() => {
                    serverListener = new TcpListener(IPAddress.Any, ServerPort);
                    serverListener.Start();
                    Logger.LogInfo($"Server started on port {ServerPort}");

                    while (true) {
                        var client = serverListener.AcceptTcpClient();
                        Logger.LogInfo($"Client connected: {client.Client.RemoteEndPoint}");
                        ThreadPool.QueueUserWorkItem(HandleClient, client);
                    }
                });
                serverThread.Start();
            } else {
                Logger.LogInfo("Server is already running.");
            }
        }

        public void StartClient() {
            if (client == null) {
                try {
                    client = new TcpClient("127.0.0.1", ServerPort);
                    stream = client.GetStream();
                    Logger.LogInfo("Connected to server.");

                    string message = "Hello from client!";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    stream.Write(data, 0, data.Length);

                    clientThread = new Thread(() => HandleServerResponse(client));
                    clientThread.Start();
                } catch (Exception ex) {
                    Logger.LogError($"Error connecting to server: {ex.Message}");
                }
            } else {
                Logger.LogInfo("Client already connected.");
            }
        }

        private void HandleClient(object obj) {
            var client = (TcpClient)obj;
            var stream = client.GetStream();

            try {
                byte[] buffer = new byte[1024];
                while (true) {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Logger.LogInfo($"Received from client: {message}");

                    byte[] response = Encoding.UTF8.GetBytes($"Server response: {message}");
                    stream.Write(response, 0, response.Length);
                }
            } catch {
                Logger.LogWarning("Client disconnected.");
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
                        Logger.LogInfo($"Received from server: {message}");
                    }
                }
            } catch {
                Logger.LogWarning("Disconnected from server.");
            } finally {
                client.Close();
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
