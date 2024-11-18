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

                // Log when client is connecting
                Log.Info($"Client {client.Client.RemoteEndPoint} connected.");

                // Check if the client already has an object; if not, instantiate one
                if (!clientObjects.ContainsKey(client)) {
                    GameObject newObject = InstantiatePlayerObject();
                    if (newObject == null) {
                        Log.Error("Failed to instantiate player object.");
                        return;
                    }
                    clientObjects[client] = newObject;
                    Log.Info($"Instantiated new object for client: {newObject.name}");
                }

                while (!stopServer) {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Client disconnected

                    if (bytesRead == 12) // Assuming Vector3 is sent as 12 bytes (3 floats)
                    {
                        try {
                            // Deserialize the position data
                            float x = BitConverter.ToSingle(buffer, 0);
                            float y = BitConverter.ToSingle(buffer, sizeof(float));
                            float z = BitConverter.ToSingle(buffer, 2 * sizeof(float));
                            Vector3 receivedVector = new Vector3(x, y, z);

                            // Log received position for debugging
                            Log.Info($"Received position: {receivedVector}");

                            // Ensure client object exists before updating position
                            if (clientObjects.TryGetValue(client, out var clientObject)) {
                                if (clientObject != null) {
                                    receivedVector.y += 6.5f; // Adjust for offset
                                    clientObject.transform.position = receivedVector;
                                    Log.Info($"Updated position of {clientObject.name} to: {receivedVector}");
                                } else {
                                    Log.Warning("Client object is null, cannot update position.");
                                }
                            } else {
                                Log.Warning("Client object not found in clientObjects dictionary.");
                            }

                            // Optionally, broadcast updated position to other clients
                            BroadcastPosition(client, receivedVector);
                        } catch (Exception ex) {
                            Log.Error($"Error while processing position update: {ex.Message}");
                        }
                    }
                }
            } catch (Exception ex) {
                Log.Warning($"Client disconnected or error occurred: {ex.Message}");
            } finally {
                // Clean up client objects
                if (clientObjects.TryGetValue(client, out var obj)) {
                    Destroy(obj);
                    clientObjects.Remove(client);
                }
                client.Close();
            }
        }



        private void BroadcastPosition(TcpClient sender, Vector3 position) {
            byte[] positionData = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(position.x), 0, positionData, 0, sizeof(float));
            Buffer.BlockCopy(BitConverter.GetBytes(position.y), 0, positionData, sizeof(float), sizeof(float));
            Buffer.BlockCopy(BitConverter.GetBytes(position.z), 0, positionData, 2 * sizeof(float), sizeof(float));

            foreach (var kvp in clientObjects) {
                if (kvp.Key != sender) { // Don't send to the sender
                    try {
                        var stream = kvp.Key.GetStream();
                        stream.Write(positionData, 0, positionData.Length);
                        stream.Flush();
                    } catch {
                        Log.Warning("Failed to broadcast to a client.");
                    }
                }
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
                        if (bytesRead == 12) { // Handle Vector3 (position update)
                            float x = BitConverter.ToSingle(buffer, 0);
                            float y = BitConverter.ToSingle(buffer, sizeof(float));
                            float z = BitConverter.ToSingle(buffer, 2 * sizeof(float));
                            Vector3 receivedVector = new(x, y, z);

                            // Update the corresponding object
                            UpdateClientObject(receivedVector);
                        } else if (bytesRead > 0) {
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

        private void UpdateClientObject(Vector3 position) {
            if (instantiatedObject != null) {
                instantiatedObject.transform.position = position;
                Log.Info($"Updated local object position to: {position}");
            } else {
                Log.Warning("No object instantiated to update.");
            }
        }

        public void UpdatePlayerPostion() {
            if (client != null && client.Connected && stream != null) {
                try {
                    // Capture the player's current position
                    Vector3 playerPosition = Player.i.transform.position;

                    // Prepare the position data for transmission
                    byte[] data = new byte[3 * sizeof(float)];
                    Buffer.BlockCopy(BitConverter.GetBytes(playerPosition.x), 0, data, 0, sizeof(float));
                    Buffer.BlockCopy(BitConverter.GetBytes(playerPosition.y), 0, data, sizeof(float), sizeof(float));
                    Buffer.BlockCopy(BitConverter.GetBytes(playerPosition.z), 0, data, 2 * sizeof(float), sizeof(float));

                    // Send the position data to the server
                    stream.Write(data, 0, data.Length);
                    stream.Flush(); // Ensure the data is sent immediately

                    Log.Info($"Position sent: {playerPosition}");
                } catch (Exception ex) {
                    Log.Error($"Error updating player position: {ex.Message}");
                }
            } else {
                // Handle cases where the client is not connected
                if (client == null || !client.Connected) {
                    Log.Warning("Client not connected. Attempting to reconnect...");
                    StartClient(); // Attempt to reconnect
                } else if (stream == null) {
                    Log.Warning("Stream is null. Check connection state.");
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
