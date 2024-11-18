using BepInEx;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
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
        private GameObject? instantiatedObject;

        private ConfigEntry<KeyboardShortcut> startServerShortcut = null!;
        private ConfigEntry<KeyboardShortcut> joinServerShortcut = null!;

        private const int ServerPort = 7777;

        // Store client-to-object mapping
        private Dictionary<TcpClient, GameObject> clientObjects = new();

        private volatile bool stopServer = false;
        private volatile bool stopClient = false;

        private void Awake() {
            Log.Init(Logger);
            harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            startServerShortcut = Config.Bind("Server", "StartServerShortcut",
                new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to start the server");
            joinServerShortcut = Config.Bind("Client", "JoinServerShortcut",
                new KeyboardShortcut(KeyCode.J, KeyCode.LeftControl), "Shortcut to join the server");

            KeybindManager.Add(this, StartServer, () => startServerShortcut.Value);
            KeybindManager.Add(this, StartClient, () => joinServerShortcut.Value);
            KeybindManager.Add(this, UpdatePlayerPostion, () => new KeyboardShortcut(KeyCode.X));
            Log.Info("Multiplayer plugin loaded.");
        }

        public void StartServer() {
            if (serverListener == null) {
                stopServer = false; // Ensure the flag is reset
                serverThread = new Thread(() => {
                    serverListener = new TcpListener(IPAddress.Any, ServerPort);
                    serverListener.Start();
                    Log.Info($"Server started on port {ServerPort}");

                    while (!stopServer) {
                        if (serverListener.Pending()) {
                            var newClient = serverListener.AcceptTcpClient();
                            Log.Info($"Client connected: {newClient.Client.RemoteEndPoint}");
                            ThreadPool.QueueUserWorkItem(HandleClient, newClient);
                        }
                    }
                });
                serverThread.Start();
            } else {
                Log.Info("Server is already running.");
            }
        }

        private void HandleClient(object clientObj) {
            var client = (TcpClient)clientObj;
            var stream = client.GetStream();

            try {
                byte[] buffer = new byte[1024];

                // Instantiate a new object for this client
                if (!clientObjects.ContainsKey(client)) {
                    GameObject newObject = InstantiatePlayerObject();
                    clientObjects[client] = newObject;
                }

                while (!stopServer) {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    if (bytesRead == 12) { // Handle Vector3 (position)
                        float x = BitConverter.ToSingle(buffer, 0);
                        float y = BitConverter.ToSingle(buffer, sizeof(float));
                        float z = BitConverter.ToSingle(buffer, 2 * sizeof(float));
                        Vector3 receivedVector = new(x, y, z);

                        if (clientObjects.TryGetValue(client, out var clientObject)) {
                            receivedVector.y += 6.5f;
                            clientObject.transform.position = receivedVector;
                            Log.Info($"Updated {clientObject.name} position to: {receivedVector}");
                        }
                    } else { // Handle messages
                        string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Log.Info($"Received message from client: {message}");
                    }

                    byte[] response = Encoding.UTF8.GetBytes("Message received");
                    stream.Write(response, 0, response.Length);
                    Log.Info("Sent response to client: Message received");
                }
            } catch (Exception ex) {
                Log.Warning($"Client disconnected or error occurred: {ex.Message}");
            } finally {
                if (clientObjects.TryGetValue(client, out var obj)) {
                    Destroy(obj);
                    clientObjects.Remove(client);
                }
                client.Close();
            }
        }

        private GameObject InstantiatePlayerObject() {
            var rotateProxy = Player.i.transform.Find("RotateProxy");
            if (rotateProxy == null) {
                Log.Error("RotateProxy not found!");
                throw new InvalidOperationException("RotateProxy not found!");
            }

            var spriteHolder = rotateProxy.Find("SpriteHolder");
            if (spriteHolder == null) {
                Log.Error("SpriteHolder not found!");
                throw new InvalidOperationException("SpriteHolder not found!");
            }

            GameObject newObject = Instantiate(spriteHolder.gameObject);
            newObject.transform.position = Vector3.zero;
            newObject.name = $"ClientObject_{clientObjects.Count + 1}";
            Log.Info($"Instantiated new object for client: {newObject.name}");
            return newObject;
        }

        public void StartClient() {
            if (client == null) {
                try {
                    client = new TcpClient("127.0.0.1", ServerPort);
                    stream = client.GetStream();
                    Log.Info("Connected to server.");

                    clientThread = new Thread(() => HandleServerResponse(client));
                    clientThread.Start();
                } catch (Exception ex) {
                    Log.Error($"Error connecting to server: {ex.Message}");
                }
            } else {
                Log.Info("Client already connected.");
            }
        }

        private void HandleServerResponse(TcpClient client) {
            var stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try {
                while (!stopClient) {
                    if (stream.DataAvailable) {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0) {
                            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            Log.Info($"Received from server: {message}");
                        }
                    }
                }
            } catch {
                Log.Warning("Disconnected from server.");
            } finally {
                client.Close();
            }
        }

        public void UpdatePlayerPostion() {
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
            stopServer = true;
            stopClient = true;

            serverListener?.Stop();
            client?.Close();

            serverThread?.Join();
            clientThread?.Join();

            foreach (var obj in clientObjects.Values) {
                Destroy(obj);
            }
            clientObjects.Clear();

            harmony?.UnpatchSelf();
        }
    }
}