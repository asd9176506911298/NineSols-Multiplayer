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

    [SerializeField]
    private GameObject PlayerPrefab = null!; // Assign in Unity Editor or load dynamically

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

    private void UpdatePlayerPosition(NetPeer peer, Vector3 position) {
        if (!serverPlayers.ContainsKey(peer)) {
            // Create a new player object
            GameObject newPlayerObject = Instantiate(PlayerPrefab); // Make sure this is assigned
            serverPlayers[peer] = new PlayerData(position, newPlayerObject);
        }

        serverPlayers[peer].Position = position;
        BroadcastPlayerPosition(peer, position);
    }

    private void OnConnectedToServer() {
        ToastManager.Toast("OnConnectedToServer");
        if(instantiatedObject == null) {
            GameObject spriteHolder = Player.i.transform.Find("RotateProxy").Find("SpriteHolder").gameObject;
            instantiatedObject = Instantiate(spriteHolder);
        }   
    }

    private void UpdatePlayerPositionOnClient(Vector3 newPosition) {
        if (instantiatedObject != null) {
            instantiatedObject.transform.position = newPosition;
        }
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

            listener.NetworkReceiveEvent += (peer, reader, channelNumber, deliveryMethod) => {
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();
                Vector3 newPosition = new Vector3(x, y, z);
                ToastManager.Toast(newPosition);
                UpdatePlayerPosition(peer, newPosition);
            };
        } else {
            ToastManager.Toast("Coeenct");
            ConnectToServer();
        }
    }

    private void HandleClientMessage(string message) {
        ToastManager.Toast($"Message from server: {message}");
    }


    private void ConnectToServer() {
        listener.NetworkReceiveEvent += (peer, reader, channelNumber, deliveryMethod) => {
            //string message = reader.GetString();
            //HandleClientMessage(message);
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            float z = reader.GetFloat();
            Vector3 newPosition = new Vector3(x, y, z);
            ToastManager.Toast(newPosition);
        };

        netManager.Start();
        serverPeer = netManager.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 9050), "game_key");
        OnConnectedToServer();
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

    private void SendPlayerPositionToServer(Vector3 position) {
        if (netManager != null && netManager.FirstPeer != null) {
            dataWriter.Reset();
            dataWriter.Put(position.x);
            dataWriter.Put(position.y);
            dataWriter.Put(position.z);
            netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }
    }

    private void TestMethod() {
        if (netManager != null) {
            ToastManager.Toast("123");
            SendPlayerPositionToServer(new Vector3(1f,2f,3f));
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

    public class PlayerData {
        public Vector3 Position { get; set; }
        public GameObject PlayerObject { get; set; }

        public PlayerData(Vector3 position, GameObject playerObject) {
            Position = position;
            PlayerObject = playerObject;
        }
    }
}
