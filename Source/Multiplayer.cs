﻿using BepInEx;
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
    private ConfigEntry<bool> isServer = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? netManager;
    private NetDataWriter dataWriter = new();
    GameObject tmpPlayer;

    private int localPlayerid;

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
                var SpriteHolder = new GameObject($"Player_{playerId}");
                activePlayers[playerId] = SpriteHolder;
                activePlayers[playerId].transform.position = Vector3.zero;
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
                foreach(var xx in activePlayers) {
                    ToastManager.Toast($"{xx.Key} {xx.Value}"); 
                }
                if (activePlayers.ContainsKey(playerId)) {
                    Destroy(activePlayers[playerId]); // Destroy the player's GameObject
                    activePlayers.Remove(playerId); // Remove from the dictionary

                    Log.Info($"Player {playerId} disconnected and removed.");

                    // Notify all remaining connected peers about the disconnection
                    dataWriter.Reset();
                    dataWriter.Put(playerId); // Notify about the removed player ID
                    foreach (var connectedPeer in netManager.ConnectedPeerList) {
                        if (connectedPeer != peer) { // Don't send to the disconnected peer
                            connectedPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
                        }
                    }
                }
                foreach (var xx in activePlayers) {
                    ToastManager.Toast($"{xx.Key} {xx.Value}");
                }
            };



            listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
                // Read the player ID and position data
                int playerId = dataReader.GetInt();
                float x = dataReader.GetFloat();
                float y = dataReader.GetFloat();
                float z = dataReader.GetFloat();

                // Check if it's the host player (ID 0) and skip instantiation for the client
                if (!activePlayers.ContainsKey(playerId)) {
                    var SpriteHolder = new GameObject($"Player_{playerId}");
                    SpriteHolder.transform.position = new Vector3(x, y, z);  // Set the player's position
                    activePlayers[playerId] = SpriteHolder;  // Add to active players
                    Log.Info($"New player with ID {playerId} instantiated on client.");

                } else {
                    // If the player already exists, just update their position
                    activePlayers[playerId].transform.position = new Vector3(x, y, z);
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
            GameObject player = activePlayers[playerId];
            dataWriter.Reset();
            dataWriter.Put(playerId);  // Send player ID
            dataWriter.Put(player.transform.position.x);  // Send player position
            dataWriter.Put(player.transform.position.y);  // Send player position
            dataWriter.Put(player.transform.position.z);  // Send player position
            clientPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }


    void ConnectToServer() {
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

                if (!activePlayers.ContainsKey(localPlayerid) && playerId != localPlayerid) {
                    // Instantiate a new SpriteHolder for other players
                    GameObject SpriteHolder;
                    Log.Info($"playerId != localPlayerid:{playerId != localPlayerid}");
                    SpriteHolder = Instantiate(Player.i.transform.Find("RotateProxy").Find("SpriteHolder").gameObject);
                    SpriteHolder.name = $"Player_{playerId}";
                    SpriteHolder.transform.position = new Vector3(x, y, z);
                    activePlayers[localPlayerid] = SpriteHolder;
                    
                } else
                    if (playerId != localPlayerid)
                        activePlayers[localPlayerid].transform.position = new Vector3(x, y, z);
            }
        };


        netManager.Start();
        netManager.Connect("localhost", 9050, "game_key");

        // Register peer disconnection event
        listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
            int playerId = peer.Id;

            // Destroy all player objects on client disconnect
            DestroyAllPlayers();
            Log.Info($"Client disconnected. All player objects destroyed.");
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
            Destroy(player);  // Destroy all player objects on client
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
        }
    }

    private void Update() {
        // Handle disconnections
        var disconnectedPlayers = new List<int>();

        foreach (var playerEntry in activePlayers) {
            int playerId = playerEntry.Key;

            // Check if player is still connected
            if (!netManager.ConnectedPeerList.Exists(peer => peer.Id == playerId)) {
                disconnectedPlayers.Add(playerId);
            }
        }

        // Remove disconnected players outside the loop
        foreach (int playerId in disconnectedPlayers) {
            HandlePlayerDisconnection(playerId);
        }

        // Synchronize player positions
        SynchronizePlayerPositions();

        // Poll network events
        netManager?.PollEvents();
    }

    private void HandlePlayerDisconnection(int playerId) {
        if (activePlayers.ContainsKey(playerId)) {
            // Remove the player and log it
            var player = activePlayers[playerId];
            Destroy(player); // Destroy the player GameObject
            activePlayers.Remove(playerId);
            Log.Info($"Player {playerId} disconnected and removed.");
        } else {
            // Log only if disconnection logic is triggered unnecessarily
            Log.Warning($"Attempted to remove Player {playerId}, but they were already removed.");
        }
    }

    private void SynchronizePlayerPositions() {
        foreach (var playerEntry in activePlayers) {
            int playerId = playerEntry.Key;
            var player = playerEntry.Value;

            dataWriter.Reset();

            // Server-specific adjustment
            if (isServer.Value) {
                dataWriter.Put(playerId);
                dataWriter.Put(player.transform.position.x);
                dataWriter.Put(player.transform.position.y + 6.5f);
                dataWriter.Put(player.transform.position.z);
            } else if (playerId == localPlayerid && Player.i != null) {
                // If not server, send local player position
                dataWriter.Put(localPlayerid);
                dataWriter.Put(Player.i.transform.position.x);
                dataWriter.Put(Player.i.transform.position.y);
                dataWriter.Put(Player.i.transform.position.z);
            }

            // Send data to all connected peers
            foreach (var peer in netManager.ConnectedPeerList) {
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
            }
        }
    }





    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
        DestroyAllPlayers();
        Log.Info("Networking stopped.");
    }
}