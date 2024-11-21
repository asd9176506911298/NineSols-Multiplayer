using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Multiplayer {
    [BepInDependency(NineSolsAPICore.PluginGUID)]
    [BepInPlugin("com.example.multiplayer", "Multiplayer Plugin", "1.0.0")]
    public class Multiplayer : BaseUnityPlugin {
        private Harmony harmony;
        private NetManager client;
        private NetDataWriter dataWriter;
        private EventBasedNetListener listener;

        // Dictionary to store other players' data
        private Dictionary<int, PlayerData> playerObjects = new Dictionary<int, PlayerData>();
        private int localPlayerId = -1;

        private void Awake() {
            Log.Init(Logger);
            RCGLifeCycle.DontDestroyForever(gameObject);

            harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);

            listener = new EventBasedNetListener();
            client = new NetManager(listener) { AutoRecycle = true };
            dataWriter = new NetDataWriter();

            KeybindManager.Add(this, ConnectToServer, () => new KeyboardShortcut(KeyCode.S));
            KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.C));
            KeybindManager.Add(this, SendPosition, () => new KeyboardShortcut(KeyCode.V));

            Log.Info("Multiplayer plugin initialized.");
        }

        private void ConnectToServer() {
            ToastManager.Toast("Connecting to server...");
            client.Start();
            client.Connect("localhost", 9050, "SomeConnectionKey");

            listener.NetworkReceiveEvent += (peer, reader, deliveryMethod, channel) => {
                HandleReceivedData(peer, reader);
                reader.Recycle();
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                playerObjects.Remove(peer.Id);
                ToastManager.Toast($"Disconnected: Peer ID: {peer.Id}");
            };
        }

        private void DisconnectFromServer() {
            client?.DisconnectAll();
            ToastManager.Toast("Disconnected from server.");
        }

        private void SendPosition() {
            if (client.FirstPeer == null) {
                ToastManager.Toast("Not connected to server.");
                return;
            }

            dataWriter.Reset();
            dataWriter.Put("Position");
            Vector3 position = Player.i.transform.position; // Use your player's position
            dataWriter.Put(position.x);
            dataWriter.Put(position.y);
            dataWriter.Put(position.z);
            client.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
        }

        private void HandleReceivedData(NetPeer peer, NetDataReader reader) {
            string messageType = reader.GetString();

            if (messageType == "Position") {
                int playerId = reader.GetInt();
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();

                // Only instantiate/update other players' objects
                if (playerId != localPlayerId) {
                    UpdatePlayerData(playerId, new Vector3(x, y, z));
                }
            } else if (messageType == "localPlayerId") {
                localPlayerId = reader.GetInt();
            }
        }

        private void UpdatePlayerData(int playerId, Vector3 position) {
            if (!playerObjects.TryGetValue(playerId, out var playerData)) {
                // Instantiate a new player object for other players
                GameObject playerObject = Instantiate(Player.i.transform.Find("RotateProxy/SpriteHolder").gameObject, position, Quaternion.identity);
                playerData = new PlayerData(playerObject, position);
                playerObjects[playerId] = playerData;
            } else {
                // Update position for existing player object
                playerData.PlayerObject.transform.position = position;
            }
        }

        private void Update() {
            client?.PollEvents();
        }

        private void OnDestroy() {
            harmony.UnpatchSelf();
            client?.Stop();
        }

        private class PlayerData {
            public GameObject PlayerObject;
            public Vector3 Position;

            public PlayerData(GameObject obj, Vector3 pos) {
                PlayerObject = obj;
                Position = pos;
            }
        }
    }
}
