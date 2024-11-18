using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System;

namespace Multiplayer {
    [BepInDependency(NineSolsAPICore.PluginGUID)]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Multiplayer : BaseUnityPlugin {
        private ConfigEntry<bool> enableServerConfig = null!;
        private ConfigEntry<int> serverPortConfig = null!;
        private ConfigEntry<KeyboardShortcut> startServerShortcut = null!;
        private ConfigEntry<KeyboardShortcut> joinServerShortcut = null!; // Add this shortcut for joining server

        private Harmony harmony = null!;

        private TcpListener? serverListener;
        private Thread? serverThread;
        private TcpClient? client;
        private NetworkStream? stream;
        private Thread? clientThread;

        private void Awake() {
            Log.Init(Logger);
            RCGLifeCycle.DontDestroyForever(gameObject);

            harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);

            enableServerConfig = Config.Bind("Server", "EnableServer", true, "Enable the server to host multiplayer.");
            serverPortConfig = Config.Bind("Server", "ServerPort", 7777, "Port number for the server to listen on.");
            startServerShortcut = Config.Bind("Server", "StartServerShortcut",
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to start the server");
            joinServerShortcut = Config.Bind("Client", "JoinServerShortcut",
                new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl), "Shortcut to join the server");

            KeybindManager.Add(this, StartServer, () => startServerShortcut.Value);
            KeybindManager.Add(this, StartClient, () => joinServerShortcut.Value);

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private void StartServer() {
            if (enableServerConfig.Value) {
                if (serverListener == null || !serverListener.Server.IsBound) {
                    StartHostingServer();
                    ToastManager.Toast("Server started.");
                } else {
                    ToastManager.Toast("Server is already running.");
                }
            } else {
                ToastManager.Toast("Server is disabled in the configuration.");
            }
        }

        private void StartHostingServer() {
            serverThread = new Thread(() => HostServer(serverPortConfig.Value));
            serverThread.Start();
        }

        private void HostServer(int port) {
            try {
                serverListener = new TcpListener(IPAddress.Any, port);
                serverListener.Start();
                Logger.LogInfo($"Server is hosting on port {port}.");

                while (true) {
                    TcpClient client = serverListener.AcceptTcpClient();
                    Logger.LogInfo($"New client connected: {client.Client.RemoteEndPoint}");
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
            } catch (Exception ex) {
                Logger.LogError($"Error hosting server: {ex.Message}");
            }
        }

        private void HandleClient(object obj) {
            TcpClient client = (TcpClient)obj;
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try {
                while (true) {
                    if (stream.DataAvailable) {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0) {
                            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            Logger.LogInfo($"Received message from client: {message}");

                            byte[] response = Encoding.UTF8.GetBytes("Message received");
                            stream.Write(response, 0, response.Length);
                        }
                    }
                    Thread.Sleep(50);
                }
            } catch (Exception ex) {
                Logger.LogError($"Error handling client: {ex.Message}");
            } finally {
                client.Close();
                Logger.LogInfo("Client disconnected.");
            }
        }

        private void StartClient() {
            try {
                if (client != null && client.Connected) {
                    Logger.LogInfo("Already connected to the server.");
                    return;
                }

                client = new TcpClient("127.0.0.1", serverPortConfig.Value); // Replace with the actual server IP
                stream = client.GetStream();
                Logger.LogInfo("Client connected to the server.");

                clientThread = new Thread(() => HandleServerCommunication(client));
                clientThread.Start();
            } catch (Exception ex) {
                Logger.LogError($"Error connecting to server: {ex.Message}");
            }
        }

        private void HandleServerCommunication(TcpClient client) {
            try {
                byte[] buffer = new byte[1024];

                while (client.Connected) {
                    if (stream!.DataAvailable) {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0) {
                            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            Logger.LogInfo($"Received message from server: {message}");
                        }
                    }

                    Thread.Sleep(50); // Prevent tight looping
                }
            } catch (Exception ex) {
                Logger.LogError($"Error in communication with server: {ex.Message}");
            } finally {
                client.Close();
                Logger.LogInfo("Client disconnected from the server.");
            }
        }

        private void OnDestroy() {
            serverListener?.Stop();
            serverThread?.Abort();
            client?.Close();
            clientThread?.Abort();
            harmony.UnpatchSelf();
        }
    }
}
