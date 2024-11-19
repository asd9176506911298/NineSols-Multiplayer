using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Net;
using System.Collections.Generic;

namespace Multiplayer;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Multiplayer : BaseUnityPlugin {
    private ConfigEntry<bool> isServer = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? netManager;
    private NetDataWriter dataWriter = new();

    private Dictionary<NetPeer, PlayerData> serverPlayers = new Dictionary<NetPeer, PlayerData>();

    private EventBasedNetListener? listener;
    private NetPeer? serverPeer;

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);

        // Config for enabling server/client mode
        isServer = Config.Bind("Network", "IsServer", true, "Set to true to run as server, false for client");

        // Other configurations
        somethingKeyboardShortcut = Config.Bind("General.Something", "Shortcut",
            new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to execute");

        KeybindManager.Add(this, InitializeNetworking, () => somethingKeyboardShortcut.Value);
        KeybindManager.Add(this, TestMethod, () => new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));

        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    // Method to update player position on the server
    private void UpdatePlayerPosition(NetPeer peer, Vector3 position) {
        // Check if the player exists in the serverPlayers dictionary
        ToastManager.Toast(position);
        if (peer != null) {
            if (serverPlayers.ContainsKey(peer)) {
                // Update the player's position
                serverPlayers[peer].Position = position;
            } else {
                // If the player doesn't exist, create a new PlayerData object
                serverPlayers.Add(peer, new PlayerData(position));
            }
        }
    }

    private void InitializeNetworking() {
        listener = new EventBasedNetListener();
        netManager = new NetManager(listener) { AutoRecycle = true };
        ToastManager.Toast(isServer.Value);

        if (isServer.Value) {
            // Start server on port 9050
            netManager.Start(9050);
            Log.Info("Server started on port 9050.");

            // Handle connection requests (e.g., accept up to 10 clients)
            listener.ConnectionRequestEvent += request => {
                if (netManager.ConnectedPeersCount < 10) // Max players
                    request.AcceptIfKey("game_key");
                else
                    request.Reject();
            };

            // Handle incoming messages from clients
            listener.NetworkReceiveEvent += (peer, reader, channelNumber, deliveryMethod) => {
                // Read the position from the incoming packet
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                Vector3 newPosition = new Vector3(x, y, z);

                // Update the player position on the server
                UpdatePlayerPosition(peer, newPosition);

                // Optionally broadcast the updated position to all clients (if necessary)
                // BroadcastPlayerPosition(peer, newPosition);
            };
        } else {
            // Start client and connect to server (example: localhost)
            ConnectToServer();
        }
    }

    private void ConnectToServer() {
        // Client setup
        listener.NetworkReceiveEvent += (peer, reader, channelNumber, deliveryMethod) => {
            string message = reader.GetString();
            HandleClientMessage(message);
        };

        listener.ConnectionRequestEvent += request => {
            // Automatically accept connection requests if we are the client
            request.AcceptIfKey("game_key");
        };

        netManager.Start();  // Start the netManager (no port needed for client)
        Log.Info("Attempting to connect to server at 127.0.0.1:9050...");

        // Example of connecting to localhost at port 9050
        serverPeer = netManager.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9050), "game_key");
    }

    private void UpdatePlayerPositionOnClient(Vector3 newPosition) {
        // Update the player position on the client (Player.i.transform.position)
        Player.i.transform.position = newPosition;
    }

    private void BroadcastPlayerPosition(NetPeer excludePeer, Vector3 position) {
        if (netManager == null) return;

        dataWriter.Reset();
        dataWriter.Put(position.x);
        dataWriter.Put(position.y);
        dataWriter.Put(position.z);

        foreach (var peer in netManager.ConnectedPeerList) {
            if (peer != excludePeer) {
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private void BroadcastToClients(string message, NetPeer excludePeer = null) {
        if (netManager == null) return;

        dataWriter.Reset();
        dataWriter.Put(message);

        foreach (var peer in netManager.ConnectedPeerList) {
            if (peer != excludePeer) {
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private void HandleClientMessage(string message) {
        // Process messages received on the client
        ToastManager.Toast($"Message from server: {message}");
    }

    private void SendPlayerPositionToServer(Vector3 position) {
        if (netManager != null && netManager.FirstPeer != null) {
            // Create a new data writer and reset it
            dataWriter.Reset();
            dataWriter.Put(position.x); // Send X position
            dataWriter.Put(position.y); // Send Y position
            dataWriter.Put(position.z); // Send Z position

            // Send the position to the server
            netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }

    // Send a single float to test
    private void SendTestDataToServer() {
        if (netManager != null && netManager.FirstPeer != null) {
            dataWriter.Reset();
            dataWriter.Put(53.58f); // Send a single float
            netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }

    private void OnTestDataReceived(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod) {
        float receivedValue = reader.GetFloat(); // Read the single float value
        ToastManager.Toast($"Received float: {receivedValue}");
    }

    private void TestMethod() {
        // Example: Broadcast a message
        if (netManager != null) {
            SendPlayerPositionToServer(Player.i.transform.position) ;
        }
    }

    private void Update() {
        netManager?.PollEvents();
    }

    private void OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod) {
        // Handle incoming data from clients
        string message = reader.GetString();
        Log.Info($"Received: {message}");
    }

    private void OnConnectionRequest(ConnectionRequest request) {
        // Accept a connection request
        request.AcceptIfKey("game_key");
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
        Log.Info("Networking stopped.");
    }

    public class PlayerData {
        public Vector3 Position { get; set; }

        public PlayerData(Vector3 position) {
            Position = position;
        }
    }
}
