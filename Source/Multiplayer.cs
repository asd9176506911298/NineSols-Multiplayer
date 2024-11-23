using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Analytics;

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


            listener.NetworkReceiveEvent += OnNetworkReceive;
            listener.PeerDisconnectedEvent += PeerDisconnectedEvent;
            //listener.NetworkReceiveEvent += (peer, reader, deliveryMethod, channel) => {
            //    ToastManager.Toast("NetworkReceiveEvent");
            //    //HandleReceivedData(peer, reader);
            //    //reader.Recycle();
            //};

            //listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
            //    ToastManager.Toast("PeerDisconnectedEvent");
            //    // Clear player objects on disconnection
            //    //DestroyAllPlayerObjects();
            //};
        }

        void OnNetworkReceive(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod deliveryMethod) {
            ToastManager.Toast($"NetworkReceiveEvent peer:{peer} reader:{reader} channel:{channel} deliveryMethod:{deliveryMethod}");
        }

        void PeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo) {
            ToastManager.Toast($"PeerDisconnectedEvent peer:{peer} disconnectInfo:{disconnectInfo}");
        }

        private void DisconnectFromServer() {
            localPlayerId = -1;
            client?.DisconnectAll();
            DestroyAllPlayerObjects();
            listener.NetworkReceiveEvent -= OnNetworkReceive;
            listener.PeerDisconnectedEvent -= PeerDisconnectedEvent;
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
            dataWriter.Put(position.y + 6.5f); // Adjust Y if necessary
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
            } else if (messageType == "NewPlayer") {
                // Handle the new player joining
                int newPlayerId = reader.GetInt();
                float newX = reader.GetFloat();
                float newY = reader.GetFloat();
                float newZ = reader.GetFloat();
                AddNewPlayerObject(newPlayerId, newX, newY, newZ);
            } else if (messageType == "localPlayerId") {
                localPlayerId = reader.GetInt();
                ToastManager.Toast($"Local Player ID set to {localPlayerId}");
            } else if (messageType == "DestoryDisconnectObject") {
                int playerId = reader.GetInt();
                if (playerObjects.ContainsKey(playerId)) {
                    Destroy(playerObjects[playerId].PlayerObject);
                    playerObjects.Remove(playerId);
                }
            }
        }

        void AddNewPlayerObject(int playerId, float x, float y, float z) {
            // Check if the player already exists
            if (!playerObjects.ContainsKey(playerId)) {
                var newPlayerObject = InstantiatePlayerModel(playerId);  // Instantiate the new player model
                playerObjects[playerId] = new PlayerData(newPlayerObject, new Vector3(x, y, z));
                SetPlayerPosition(playerId, x, y, z);  // Set initial position
                Debug.Log($"New player {playerId} added at position: {x}, {y}, {z}");
            } else {
                // If the player already exists, just update the position
                SetPlayerPosition(playerId, x, y, z);
            }
        }

        // Example: Instantiate a new player model (you can customize this)
        private GameObject InstantiatePlayerModel(int playerId) {
            GameObject playerPrefab = Resources.Load<GameObject>("PlayerPrefab"); // Ensure you have a prefab
            if (playerPrefab == null) {
                Debug.LogError("Player prefab not found!");
                return new GameObject($"Player{playerId}"); // Fallback if prefab not found
            }
            GameObject newPlayerObject = Instantiate(playerPrefab);
            newPlayerObject.name = $"Player {playerId}";
            return newPlayerObject;
        }

        // Set the player's position on the client
        void SetPlayerPosition(int playerId, float x, float y, float z) {
            if (playerObjects.ContainsKey(playerId)) {
                var playerObject = playerObjects[playerId];
                playerObject.SetPosition(x, y, z);  // Assuming `SetPosition` is a valid method
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

        void UpdatePlayerPositionOnClient(int playerId, float x, float y, float z) {
            if (!playerObjects.ContainsKey(playerId)) {
                // The player object might not exist yet; handle accordingly
                AddNewPlayerObject(playerId, x, y, z);  // Create the player if necessary
            } else {
                SetPlayerPosition(playerId, x, y, z);  // Update position of the existing player object
            }
        }

        private void Update() {
            //if (client?.FirstPeer != null && client.FirstPeer.ConnectionState == ConnectionState.Connected) {
            //    sendTimer += Time.deltaTime;
            //    if (sendTimer >= sendInterval) {
            //        SendPosition();
            //        sendTimer = 0;
            //    }
            //}

            client?.PollEvents();
        }

        private void OnDestroy() {
            harmony.UnpatchSelf();
            listener.NetworkReceiveEvent -= OnNetworkReceive;
            listener.PeerDisconnectedEvent -= PeerDisconnectedEvent;
            client?.Stop();
        }
    }

    public class PlayerData {
        public GameObject PlayerObject;
        public Vector3 Position;

        public PlayerData(GameObject playerObject, Vector3 position) {
            PlayerObject = playerObject;
            Position = position;
        }

        public void SetPosition(float x, float y, float z) {
            PlayerObject.transform.position = new Vector3(x, y, z);
        }
    }
}
