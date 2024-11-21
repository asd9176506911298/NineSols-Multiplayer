using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Collections.Generic;
using System.Threading;

namespace Multiplayer;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Multiplayer : BaseUnityPlugin {
    public static Multiplayer Instance { get; private set; }

    private Harmony harmony = null!;
    private NetManager? client;
    private NetDataWriter? dataWriter;
    private EventBasedNetListener? listener;
    private Dictionary<int, PlayerData> playerObjects = new Dictionary<int, PlayerData>();  // Store player data by their ID

    private int localPlayerId = -1;


    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);

        KeybindManager.Add(this, ConnectToServer, () => new KeyboardShortcut(KeyCode.S));
        KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.C));
        KeybindManager.Add(this, SendPosition, () => new KeyboardShortcut(KeyCode.V));

        Instance = this;
        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    void DisconnectFromServer() {
        var peer = client?.FirstPeer;
        if (peer != null) {
            peer.Disconnect();
            ToastManager.Toast($"Disconnected from server. Peer ID: {peer.Id}");
        } else {
            ToastManager.Toast("Not connected to any server.");
        }
    }

    private void ConnectToServer() {
        ToastManager.Toast("Attempting to connect to server...");
        listener = new EventBasedNetListener();
        client = new NetManager(listener) { AutoRecycle = true };
        client.Start();
        dataWriter = new NetDataWriter();
        client.Connect("localhost", 9050, "SomeConnectionKey");

        listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
            // Handle incoming messages from the server
            HandleReceivedData(fromPeer, dataReader);
            dataReader.Recycle();
        };

        listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
            if (playerObjects.ContainsKey(peer.Id)) {
                Destroy(playerObjects[peer.Id].PlayerObject);  // Remove player GameObject
                playerObjects.Remove(peer.Id);  // Remove player data
            }

            foreach(var x in playerObjects) {
                Destroy(x.Value.PlayerObject);
            }
            ToastManager.Toast($"Disconnected from server: Peer ID: {peer.Id}");
        };

    }

    void SendPosition() {
        if (client == null || client.FirstPeer == null) {
            ToastManager.Toast("No connection to the server!");
            return;
        }
        // Reset the writer to clear any previous data
        dataWriter.Reset();
        dataWriter.Put("Position");

        // Assuming player's position is stored in the GameObject's transform
        Vector3 position = Player.i.transform.position;  // Get player's position from the GameObject
        dataWriter.Put(Player.i.transform.position.x);
        dataWriter.Put(Player.i.transform.position.y);
        dataWriter.Put(Player.i.transform.position.z);

        // Send the data to the server
        client.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        ToastManager.Toast($"Position sent to server: ({position.x}, {position.y}, {position.z})");
    }

    void HandleReceivedData(NetPeer fromPeer, NetDataReader dataReader) {
        // Handle the received data (e.g., player position updates)
        string messageType = dataReader.GetString();
        if (messageType == "Position") {
            int playerId = dataReader.GetInt();
            float x = dataReader.GetFloat();
            float y = dataReader.GetFloat();
            float z = dataReader.GetFloat();
            ToastManager.Toast($"Player:{playerId} {x} {y} {z}");
            Vector3 pos = new Vector3(x, y, z);
            UpdatePlayerData(playerId, pos);
            ToastManager.Toast("UpdatePlayerData");
        }else if (messageType == "localPlayerId") {
            localPlayerId = dataReader.GetInt();
        } else {
            ToastManager.Toast(messageType);
        }
    }

    void UpdatePlayerData(int playerId, Vector3 position) {
        ToastManager.Toast($"UpdatePlayerData {playerId} {localPlayerId} {playerId != localPlayerId}");
        if (playerObjects.ContainsKey(playerId)) {
            // Update existing player data
            if (playerId == localPlayerId) return;
            playerObjects[playerId].Position = position;
            playerObjects[playerId].PlayerObject.transform.position = position;  // Update GameObject's position
        } else{
            // Add new player data
            GameObject playerObject = Instantiate(Player.i.transform.Find("RotateProxy").Find("SpriteHolder").gameObject, position, Quaternion.identity);  // Assuming you have a prefab
            playerObject.name = playerId.ToString();
            playerObjects.Add(playerId, new PlayerData(playerObject, position));
        }
    }

    void Update() {
        client?.PollEvents();  // Poll for incoming events
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
        client?.Stop();
    }
}
