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

    private GameObject? instantiatedObject;

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);

        isServer = Config.Bind("Network", "IsServer", true, "Set to true to run as server, false for client");

        somethingKeyboardShortcut = Config.Bind("General.Something", "Shortcut",
            new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to execute");

        KeybindManager.Add(this, InitializeNetworking, () => somethingKeyboardShortcut.Value);
        KeybindManager.Add(this, TestMethod, () => new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));

        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void InitializeNetworking() {
        listener = new EventBasedNetListener();
        netManager = new NetManager(listener) { AutoRecycle = true };

        if (isServer.Value) {
            ToastManager.Toast("StartServer");
            netManager.Start(9050);

            listener.ConnectionRequestEvent += request => {
                if (netManager.ConnectedPeersCount < 10)
                    request.AcceptIfKey("game_key");
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += peer => {
                ToastManager.Toast($"Client connected: {peer.RemoteId}");

                // Send existing players to the new client
                foreach (var existingPlayer in serverPlayers) {
                    NetPeer existingPeer = existingPlayer.Key;
                    Vector3 position = existingPlayer.Value.Position;

                    dataWriter.Reset();
                    dataWriter.Put("ExistingPlayer");
                    dataWriter.Put(existingPeer.Id); // Unique ID of the existing player
                    dataWriter.Put(position.x);
                    dataWriter.Put(position.y);
                    dataWriter.Put(position.z);

                    peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
                }

                // Add the new client to the player dictionary and broadcast its position
                Vector3 spawnPosition = Player.i.transform.position;
                UpdatePlayerPosition(peer, spawnPosition);
                BroadcastNewPlayer(peer, spawnPosition);
            };



            listener.NetworkReceiveEvent += (peer, reader, channelNumber, deliveryMethod) => {
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                Vector3 newPosition = new Vector3(x, y, z);
                UpdatePlayerPosition(peer, newPosition);
            };
        } else {
            ToastManager.Toast("Connecting to server");
            ConnectToServer();
        }
    }


    private void UpdatePlayerPosition(NetPeer peer, Vector3 position) {
        if (!serverPlayers.ContainsKey(peer)) {
            // Create a new player object if it doesn't exist
            //GameObject newPlayerObject = Instantiate(Player.i.transform.Find("RotateProxy").Find("SpriteHolder").gameObject);
            GameObject newPlayerObject = new GameObject("Player");
            serverPlayers[peer] = new PlayerData(position, newPlayerObject);
        }

        serverPlayers[peer].Position = position; // Update the position for this player

        // Broadcast the updated position to all clients (except the sender)
        BroadcastPlayerPosition(peer, position);
    }


    private void BroadcastNewPlayer(NetPeer newPeer, Vector3 position) {
        if (netManager == null) return;

        dataWriter.Reset();
        dataWriter.Put("NewPlayer"); // Identifier for a new player
        dataWriter.Put(position.x);
        dataWriter.Put(position.y);
        dataWriter.Put(position.z);

        foreach (var peer in netManager.ConnectedPeerList) {
            if (peer != newPeer) { // Exclude the new client
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private void BroadcastPlayerPosition(NetPeer excludePeer, Vector3 position) {
        if (netManager == null) return;

        dataWriter.Reset();
        dataWriter.Put("PlayerPosition"); // Identifier for player position
        dataWriter.Put(position.x);
        dataWriter.Put(position.y);
        dataWriter.Put(position.z);

        // Broadcast the updated position to all peers except the one sending the update
        foreach (var peer in netManager.ConnectedPeerList) {
            if (peer != excludePeer) { // Exclude the sender
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
            }
        }
    }

    private void ConnectToServer() {
        listener.NetworkReceiveEvent += (peer, reader, channelNumber, deliveryMethod) => {
            string messageType = reader.GetString();

            if (messageType == "NewPlayer") {
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                Vector3 newPlayerPosition = new Vector3(x, y, z);
                ToastManager.Toast($"New player joined at position {newPlayerPosition}");
                UpdatePlayerPositionOnClient(newPlayerPosition);
            } else if (messageType == "PlayerPosition") {
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                Vector3 updatedPosition = new Vector3(x, y, z);
                UpdatePlayerPositionOnClient(updatedPosition);
            } else if (messageType == "ExistingPlayer") {
                int playerId = reader.GetInt();
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                Vector3 existingPlayerPosition = new Vector3(x, y, z);
                ToastManager.Toast($"Existing player {playerId} at position {existingPlayerPosition}");
                UpdatePlayerPositionOnClient(existingPlayerPosition);
            }
        };

        netManager.Start();
        serverPeer = netManager.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9050), "game_key");
    }


    //private void CreatePlayer() {
    //    ToastManager.Toast("Connected to server");
    //    if (instantiatedObject == null) {
    //        GameObject spriteHolder = Player.i.transform.Find("RotateProxy").Find("SpriteHolder").gameObject;
    //        instantiatedObject = Instantiate(spriteHolder);
    //    }
    //}

    private void UpdatePlayerPositionOnClient(Vector3 newPosition) {
        if (instantiatedObject != null) {
            instantiatedObject.transform.position = newPosition;
        }
    }

    private void TestMethod() {
        if (netManager != null) {
            ToastManager.Toast("Sending position");
            SendPlayerPositionToServer(new Vector3(1f, 2f, 3f));
        }
    }

    private void SendPlayerPositionToServer(Vector3 position) {
        if (netManager != null && netManager.FirstPeer != null) {
            dataWriter.Reset();
            dataWriter.Put("PlayerPosition");
            dataWriter.Put(position.x);
            dataWriter.Put(position.y);
            dataWriter.Put(position.z);

            netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }

    private void Update() {
        //if (Player.i != null) {
        //    Vector3 newPosition = Player.i.transform.position;
        //    // Send the updated position to the server if it's different from the current one
        //    if (HasPositionChanged(newPosition)) {
        //        SendPlayerPositionToServer(newPosition);
        //    }
        //}

        netManager?.PollEvents();
    }

    private bool HasPositionChanged(Vector3 newPosition) {
        // Compare with the last position stored for the player
        return Player.i.transform.position != newPosition;
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
        Log.Info("Networking stopped.");
    }

    public class PlayerData {
        public Vector3 Position { get; set; }
        public GameObject PlayerObject { get; set; }

        public PlayerData(Vector3 position, GameObject playerObject) {
            Position = position;
            PlayerObject = playerObject;
        }
    }
}
