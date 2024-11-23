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

        private float sendInterval = 0.05f; // 50ms
        private float sendTimer = 0;

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
            localPlayerId = -1;
            DestroyAllPlayerObjects();

            listener.NetworkReceiveEvent += (peer, reader, deliveryMethod, channel) => {
                HandleReceivedData(peer, reader);
                reader.Recycle();
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                // Clear player objects on disconnection
                DestroyAllPlayerObjects();
            };
        }

        private void DisconnectFromServer() {
            localPlayerId = -1;
            client?.DisconnectAll();
            DestroyAllPlayerObjects();

            ToastManager.Toast("Disconnected from server.");
        }

        private void DestroyAllPlayerObjects() {
            foreach (var playerData in playerObjects.Values) {
                if (playerData.PlayerObject != null) {
                    Destroy(playerData.PlayerObject);
                }
            }
            playerObjects.Clear();
        }


        private void SendPosition() {
            if (client.FirstPeer == null) {
                ToastManager.Toast("Not connected to server.");
                return;
            }

            dataWriter.Reset();
            dataWriter.Put("Position");
            Vector3 position;
            if (Player.i != null)
                position = Player.i.transform.position; // Use your player's position
            else
                position = Vector3.zero;
            dataWriter.Put(position.x);
            dataWriter.Put(position.y+6.5f);
            dataWriter.Put(position.z);
            client.FirstPeer.Send(dataWriter, DeliveryMethod.Unreliable);
        }

        private void HandleReceivedData(NetPeer peer, NetDataReader reader) {
            if (peer.ConnectionState != ConnectionState.Connected) return; // Ensure only active peers are processed

            string messageType = reader.GetString();

            if (messageType == "Position") {
                int playerId = reader.GetInt();
                float x = reader.GetFloat();
                float y = reader.GetFloat();
                float z = reader.GetFloat();

                // Only update other players' positions if we have received our localPlayerId
                if (localPlayerId != -1 && playerId != localPlayerId) {
                    UpdatePlayerData(playerId, new Vector3(x, y, z));
                }
            } else if (messageType == "localPlayerId") {
                localPlayerId = reader.GetInt();
                ToastManager.Toast($"Local Player ID set to {localPlayerId}");
            } else if (messageType == "DestoryDisconnectObject") {
                int playerId = reader.GetInt();
                Destroy(playerObjects[playerId].PlayerObject);
                playerObjects.Remove(playerId);
            }
        }


        private void UpdatePlayerData(int playerId, Vector3 newPosition) {
            if (playerObjects.TryGetValue(playerId, out var playerData)) {
                playerData.PlayerObject.transform.position = Vector3.Lerp(
                    playerData.PlayerObject.transform.position,
                    newPosition,
                    Time.deltaTime * 10 // Adjust smoothing factor as needed
                );
            } else {
                // Instantiate a new player object if not found
                GameObject playerObject;
                if (Player.i != null)
                    playerObject = Instantiate(Player.i.transform.Find("RotateProxy/SpriteHolder").gameObject, newPosition, Quaternion.identity);
                else
                    playerObject = new GameObject($"Player{playerId}");
                playerObjects[playerId] = new PlayerData(playerObject, newPosition);
            }
        }


        private void Update() {
            if (client?.FirstPeer != null && client.FirstPeer.ConnectionState == ConnectionState.Connected) {
                sendTimer += Time.deltaTime;
                if (sendTimer >= sendInterval) {
                    SendPosition();
                    sendTimer = 0;
                }
            }

            client?.PollEvents();
        }



        private void OnDestroy() {
            harmony.UnpatchSelf();
            client?.Stop();
        }


    }
}
