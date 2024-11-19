using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Net;
using System.Collections.Generic;
using System;

namespace Multiplayer;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Multiplayer : BaseUnityPlugin {
    private ConfigEntry<bool> isServer = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? netManager;
    private NetDataWriter dataWriter = new();
    GameObject tmpPlayer;

    private EventBasedNetListener? listener;
    private NetPeer? serverPeer;

    private Dictionary<int, GameObject> activePlayers = new();  // Keeps track of all connected players by their IDs


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
        KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.D, KeyCode.LeftControl));
        KeybindManager.Add(this, TestMethod, () => new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));

        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void InitializeNetworking() {
        listener = new EventBasedNetListener();
        netManager = new NetManager(listener) { AutoRecycle = true };

        if (isServer.Value) {
            ToastManager.Toast("Start Server");
            // Start server on port 9050
            netManager.Start(9050);
            Log.Info("Server started on port 9050.");

            // Add the host player (itself) to the activePlayers list
            int hostPlayerId = 0;  // Assign an ID for the host player
            activePlayers[hostPlayerId] = new GameObject("HostPlayer");  // Create host player object
            Log.Info("Host player created and added to active players.");

            listener.ConnectionRequestEvent += request => {
                if (netManager.ConnectedPeersCount < 10) // Max players
                    request.AcceptIfKey("game_key");
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += peer => {
                int playerId = peer.Id;  // Assign a unique ID to each player
                activePlayers[playerId] = new GameObject($"Player_{playerId}");  // Create new player object
                Log.Info($"New player connected with ID {playerId}");

                // Send all existing players' data (including host) to the new client
                foreach (var player in activePlayers) {
                    SendPlayerDataToClient(peer, player.Key);
                }
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                int playerId = peer.Id;
                if (activePlayers.ContainsKey(playerId)) {
                    Destroy(activePlayers[playerId]);  // Cleanup the player object
                    activePlayers.Remove(playerId);
                    Log.Info($"Player {playerId} disconnected.");
                }
            };

            listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
                // Handle data from clients (e.g., player actions, updates, etc.)
            };
        } else {
            // Start client and connect to server (example: localhost)
            ToastManager.Toast("ConnectToServer");
            ConnectToServer();
        }
    }


    private void SendPlayerDataToClient(NetPeer clientPeer, int playerId) {
        if (activePlayers.ContainsKey(playerId)) {
            NetDataWriter dataWriter = new();
            GameObject player = activePlayers[playerId];
            dataWriter.Reset();
            dataWriter.Put(0.1f);  // Send player position
            clientPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }

    void ConnectToServer() {
        listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) =>
        {
            int playerId = fromPeer.Id;  // Player ID is sent along with the data
            //Vector3 playerPosition = dataReader.GetVector3();  // Receiving player position
            Vector3 playerPosition = Vector3.zeroVector;

            // Instantiate or update the player object
            if (!activePlayers.ContainsKey(playerId)) {
                GameObject newPlayer = new GameObject($"Player_{playerId}");
                newPlayer.transform.position = playerPosition;
                activePlayers[playerId] = newPlayer;
            } else {
                activePlayers[playerId].transform.position = playerPosition;  // Update position if already exists
            }
        };

        netManager.Start();
        netManager.Connect("localhost", 9050, "game_key");
    }


    void TestMethod() {
        dataWriter.Reset();
        dataWriter.Put(1.2f);
        netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
    }

    void DisconnectFromServer() {
        if (netManager?.IsRunning == true) {
            var peer = netManager.FirstPeer;
            peer?.Disconnect();
            ToastManager.Toast("Disconnected from server.");
        }
    }

    private void Update() {
        netManager?.PollEvents();
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
        Log.Info("Networking stopped.");
    }
}