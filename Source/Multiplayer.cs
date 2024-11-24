using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using NineSolsAPI.Utils;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multiplayer {
    [BepInDependency(NineSolsAPICore.PluginGUID)]
    [BepInPlugin("com.example.multiplayer", "Multiplayer Plugin", "1.0.0")]
    public class Multiplayer : BaseUnityPlugin {
        public static Multiplayer Instance { get; set; }
        private Harmony harmony;
        private NetManager client;
        private NetDataWriter dataWriter;
        private EventBasedNetListener listener;

        private float sendInterval = 0.2f; // 50ms
        private float sendTimer = 0;
        public string? localAnimationState;
        // Dictionary to store other players' data
        public Dictionary<int, PlayerData> playerObjects = new Dictionary<int, PlayerData>();
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
            KeybindManager.Add(this, test, () => new KeyboardShortcut(KeyCode.V));


            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.Info("Multiplayer plugin initialized.");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (client.FirstPeer != null) {
                DestroyAllPlayerObjects();
            }
        }

        public void SendDecreaseHealth(int playerId, float value) {
            if (client.FirstPeer == null) {
                ToastManager.Toast("Not connected to server.");
                return;
            }

            dataWriter.Reset();
            dataWriter.Put("DecreaseHealth");
            dataWriter.Put(playerId);
            dataWriter.Put(value);
            client.FirstPeer.Send(dataWriter, DeliveryMethod.Unreliable);
        }

        void test() {
            SendDecreaseHealth(0, 50f);
            //CreatePlayerObject(Player.i.transform.position,1337);
            return;
            //ToastManager.Toast(Resources.Load<GameObject>("Global Prefabs/GameCore").GetComponent<GameCore>().player.gameObject);
            var x = Instantiate(Resources.Load<GameObject>("Global Prefabs/GameCore").GetComponent<GameCore>().transform.Find("RCG LifeCycle"));
            x.transform.Find("PPlayer").position = Player.i.transform.position;
            x.transform.Find("PPlayer").position = new Vector3(x.transform.Find("PPlayer").position.x + 20f, x.transform.Find("PPlayer").position.y, x.transform.Find("PPlayer").position.z);
        }

        private void ConnectToServer() {
            ToastManager.Toast("Connecting to server...");
            client.Start();
            client.Connect("localhost", 9050, "SomeConnectionKey");
            localPlayerId = -1;
            DestroyAllPlayerObjects();

            listener.NetworkReceiveEvent += OnNetworkReceiveEvent;
            listener.PeerDisconnectedEvent += OnPeerDisconnectedEvent;

            //listener.NetworkReceiveEvent += (peer, reader, deliveryMethod, channel) => {
            //    HandleReceivedData(peer, reader);
            //    reader.Recycle();
            //};

            //listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
            //    // Clear player objects on disconnection
            //    DestroyAllPlayerObjects();
            //};
        }

        void OnNetworkReceiveEvent(NetPeer peer,NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
            HandleReceivedData(peer, reader);
            reader.Recycle();
        }

        void OnPeerDisconnectedEvent(NetPeer peer,DisconnectInfo disconnectInfo) {
            DestroyAllPlayerObjects();
        }

        private void DisconnectFromServer() {
            localPlayerId = -1;
            client?.DisconnectAll();
            listener.NetworkReceiveEvent -= OnNetworkReceiveEvent;
            listener.PeerDisconnectedEvent -= OnPeerDisconnectedEvent;
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
            dataWriter.Put(localAnimationState);
            dataWriter.Put(Player.i.Facing.ToString().Equals("Right") ? true : false);
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
                string animState = reader.GetString();
                bool isFacingRight = reader.GetBool();

                // Only update other players' positions if we have received our localPlayerId
                if (localPlayerId != -1 && playerId != localPlayerId) {
                    UpdatePlayerData(playerId, new Vector3(x, y, z), animState, isFacingRight);
                }
            } else if (messageType == "localPlayerId") {
                localPlayerId = reader.GetInt();
                ToastManager.Toast($"Local Player ID set to {localPlayerId}");
            } else if (messageType == "DestoryDisconnectObject") {
                int playerId = reader.GetInt();
                Destroy(playerObjects[playerId].PlayerObject);
                playerObjects.Remove(playerId);
            }else if (messageType == "DecreaseHealth") {
                ToastManager.Toast("DecreaseHealth");
                int playerId = reader.GetInt();
                float value = reader.GetFloat();
                if(playerId == localPlayerId) {
                    Player.i.health.ReceiveDOT_Damage(value);
                    Player.i.ChangeState(PlayerStateType.Hurt, true);
                }
            }
        }


        private void UpdatePlayerData(int playerId, Vector3 newPosition, string animationState, bool isFacingRight) {
            if (playerObjects.TryGetValue(playerId, out var playerData)) {
                playerData.PlayerObject.transform.position = newPosition;
            
                playerData.PlayerObject.transform.localScale = new Vector3(Mathf.Abs(transform.localScale.x) * (isFacingRight ? 1 : -1),
                        transform.localScale.y,
                        transform.localScale.z
                    );

                playerData.id = playerId;
                //playerData.PlayerObject.GetComponent<Animator>().PlayInFixedTime(animationState, 0, 0f);
            } else {
                // Instantiate a new player object if not found
                GameObject playerObject;
                if (Player.i != null)
                    playerObject = CreatePlayerObject(newPosition, playerId);
                else
                    playerObject = new GameObject($"Player{playerId}");
                playerObjects[playerId] = new PlayerData(playerObject, newPosition);
            }
        }

        GameObject CreatePlayerObject(Vector3 pos, int playerid) {
            var x = Instantiate(Player.i.transform.Find("RotateProxy/SpriteHolder").gameObject, pos, Quaternion.identity);
            x.transform.Find("Health(Don'tKey)").Find("DamageReceiver").GetComponent<EffectReceiver>().effectType = EffectType.EnemyAttack | EffectType.BreakableBreaker;
            x.name = $"PlayerObject_{playerid}";
            return x;
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
            listener.NetworkReceiveEvent -= OnNetworkReceiveEvent;
            listener.PeerDisconnectedEvent -= OnPeerDisconnectedEvent;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            client?.Stop();
        }


    }
}
