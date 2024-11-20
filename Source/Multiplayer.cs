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
using System.Reflection;

namespace Multiplayer;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Multiplayer : BaseUnityPlugin {
    public static Multiplayer Instance { get; private set; }
    private ConfigEntry<bool> isServer = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? netManager;
    private NetDataWriter dataWriter = new();
    GameObject tmpPlayer;

    private int localPlayerid;

    private EventBasedNetListener? listener;
    private NetPeer? serverPeer;

    private bool isConnected;
    public string localAnimationState;

    private Dictionary<int, PlayerData> activePlayers = new();



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

        Instance = this;
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

            foreach (var x in activePlayers) {
                ToastManager.Toast(x);
            }
            listener.ConnectionRequestEvent += request => {
                if (netManager.ConnectedPeersCount < 10) // Max players
                    request.AcceptIfKey("game_key");
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += peer => {
                int playerId = peer.Id;
                var playerObject = new GameObject($"Player_{playerId}");
                activePlayers[playerId] = new PlayerData(playerObject, "Idle");
                playerObject.transform.position = Vector3.zero; // Default position for new players
                ToastManager.Toast($"New player connected with ID {playerId}");

                dataWriter.Reset();
                dataWriter.Put(playerId);
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);

                // Send all active players to the new client
                foreach (var player in activePlayers) {
                    SendPlayerDataToClient(peer, player.Key);
                }

                // Notify all other players of the new player
                foreach (var otherPeer in netManager.ConnectedPeerList) {
                    if (otherPeer != peer) {
                        SendPlayerDataToClient(otherPeer, playerId);
                    }
                }
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                int playerId = peer.Id;

                if (activePlayers.ContainsKey(playerId)) {
                    Destroy(activePlayers[playerId].PlayerObject); // Destroy the player's GameObject
                    activePlayers.Remove(playerId); // Remove from the dictionary

                    Log.Info($"Player {playerId} disconnected and removed.");

                    // Notify all remaining connected peers about the disconnection
                    dataWriter.Reset();
                    dataWriter.Put(-1); // Use -1 as a signal for disconnect notification
                    dataWriter.Put(playerId); // Include the player ID to be removed
                    foreach (var connectedPeer in netManager.ConnectedPeerList) {
                        connectedPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
                    }
                }
            };




            listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
                // Read the player ID and position data
                int playerId = dataReader.GetInt();
                float x = dataReader.GetFloat();
                float y = dataReader.GetFloat();
                float z = dataReader.GetFloat();
                string animationState = dataReader.GetString();

                // Check if it's the host player (ID 0) and skip instantiation for the client
                if (!activePlayers.ContainsKey(playerId)) {
                    var playerObject = new GameObject($"Player_{playerId}");
                    playerObject.transform.position = new Vector3(x, y, z); // Set position
                    activePlayers[playerId] = new PlayerData(playerObject, animationState); // Add new player
                    ToastManager.Toast($"New player connected with ID {playerId}");

                } else {
                    // If the player already exists, just update their position
                    activePlayers[playerId].PlayerObject.transform.position = new Vector3(x, y, z);
                    activePlayers[playerId].AnimationState = animationState;
                }

                //Log.Info($"Player {playerId} position updated: ({x}, {y}, {z})");
            };


        } else {
            // Start client and connect to server (example: localhost)
            ToastManager.Toast("ConnectToServer");
            ConnectToServer();
        }
    }



    private void SendPlayerDataToClient(NetPeer clientPeer, int playerId) {
        if (activePlayers.ContainsKey(playerId)) {
            GameObject player = activePlayers[playerId].PlayerObject;
            dataWriter.Reset();
            dataWriter.Put(playerId);  // Send player ID
            dataWriter.Put(player.transform.position.x);  // Send player position
            dataWriter.Put(player.transform.position.y);  // Send player position
            dataWriter.Put(player.transform.position.z);  // Send player position
            dataWriter.Put(activePlayers[playerId].AnimationState);  // Send player position
            clientPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }


    void ConnectToServer() {
        if (isConnected)
            return;

        listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
            if (dataReader.AvailableBytes == sizeof(int)) {
                // Handle player ID assignment from server
                localPlayerid = dataReader.GetInt();
                Log.Info($"Assigned localPlayerid: {localPlayerid}");
            } else {
                // Handle other data (e.g., position updates)
                int playerId = dataReader.GetInt();
                float x = dataReader.GetFloat();
                float y = dataReader.GetFloat();
                float z = dataReader.GetFloat();
                string attack = dataReader.GetString();

                if (!activePlayers.ContainsKey(playerId) && playerId != localPlayerid) {
                    GameObject SpriteHolder = Instantiate(Player.i.transform.Find("RotateProxy").Find("SpriteHolder").gameObject);
                    SpriteHolder.name = $"Player_{playerId}";
                    SpriteHolder.transform.position = new Vector3(x, y, z);

                    // Add the new player to the activePlayers dictionary using the playerId
                    activePlayers[playerId] = new PlayerData(SpriteHolder, "Idle");
                } else if (playerId != localPlayerid) {
                    // Update the position and animation state for the existing player
                    activePlayers[playerId].PlayerObject.transform.position = new Vector3(x, y, z);
                    activePlayers[playerId].PlayerObject.GetComponent<Animator>().Play(attack, 0, 0f);
                }

            }
        };



        netManager.Start();
        netManager.Connect("localhost", 9050, "game_key");


        isConnected = true;
        // Register peer disconnection event
        listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
            int playerId = peer.Id;

            // Destroy all player objects on client disconnect
            DestroyAllPlayers();
            Log.Info($"Client disconnected. All player objects destroyed.");
            isConnected = false;
        };
    }

    private void SendPlayerDataToServer(int playerId) {
        // Assuming the server expects a message to register player information
        dataWriter.Reset();
        dataWriter.Put(playerId);
        netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
    }



    // Method to destroy all player objects
    private void DestroyAllPlayers() {
        foreach (var player in activePlayers.Values) {
            Destroy(player.PlayerObject);  // Destroy all player objects on client
        }
        activePlayers.Clear();  // Clear the dictionary after destroying all player objects
    }



    void TestMethod() {
        foreach (var xx in activePlayers) {
            ToastManager.Toast($"{xx.Key} {xx.Value}");
        }
        return;
        dataWriter.Reset();
        dataWriter.Put(1.2f);
        netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
    }

    void DisconnectFromServer() {
        if (netManager?.IsRunning == true) {
            var peer = netManager.FirstPeer;
            peer?.Disconnect();
            ToastManager.Toast($"Disconnected from server. {peer.Id}");
            isConnected = false;
        }
    }

    private void Update() {
        if (!isServer.Value) {
            foreach (var playerEntry in activePlayers) {
                if (Player.i != null) {
                    int playerId = playerEntry.Key;
                    if (playerId == localPlayerid) {
                        dataWriter.Reset();
                        dataWriter.Put(localPlayerid);  // Include player ID
                        dataWriter.Put(Player.i.transform.position.x);  // Include position
                        dataWriter.Put(Player.i.transform.position.y);
                        dataWriter.Put(Player.i.transform.position.z);
                        dataWriter.Put(localAnimationState);

                        foreach (var peer in netManager.ConnectedPeerList) {
                            peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
                        }
                    }   
                }
            }
        } else {
            foreach (var playerEntry in activePlayers) {
                int playerId = playerEntry.Key;
                var player = playerEntry.Value;
                dataWriter.Reset();
                dataWriter.Put(playerId);  // Include player ID
                dataWriter.Put(player.PlayerObject.transform.position.x);  // Include position
                dataWriter.Put(player.PlayerObject.transform.position.y+6.5f);
                dataWriter.Put(player.PlayerObject.transform.position.z);
                dataWriter.Put(player.AnimationState);

                foreach (var peer in netManager.ConnectedPeerList) {
                    peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
                }
            }
        }
        netManager?.PollEvents();
    }


    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
        DestroyAllPlayers();
        Log.Info("Networking stopped.");
    }
}

public class PlayerData {
    public GameObject PlayerObject { get; set; }
    public string AnimationState { get; set; }

    public PlayerData(GameObject playerObject, string animationState) {
        PlayerObject = playerObject;
        AnimationState = animationState;
    }
}
