using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using NineSolsAPI.Menu;
using NineSolsAPI.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multiplayer {
    [BepInDependency(NineSolsAPICore.PluginGUID)]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Multiplayer : BaseUnityPlugin {
        public static Multiplayer Instance { get; private set; }

        private Harmony _harmony;
        private NetManager _client;
        private NetDataWriter _dataWriter;
        private EventBasedNetListener _listener;

        private ConfigEntry<string> ip;
        private ConfigEntry<int> port;
        private ConfigEntry<bool> join;
        private ConfigEntry<bool> leave;
        private ConfigEntry<string> pvp;
        private ConfigEntry<string> playerName;
        public bool isPVP;

        //private TitlescreenModifications titlescreenModifications = new();

        public readonly Dictionary<int, PlayerData> _playerObjects = new();
        private int _localPlayerId = -1;
        private const float SendInterval = 0.02f;
        private float _sendTimer;

        private string? currentAnimationState = string.Empty;
        public string? localAnimationState = "";

        private void Awake() {
            Instance = this;
            Log.Init(Logger);
            try {
                RCGLifeCycle.DontDestroyForever(gameObject);

                _harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);
                InitializeNetworking();

                //titlescreenModifications.Load();

                ip = Config.Bind("", "Server Ip", "yukikaco.ddns.net", "");
                port = Config.Bind("", "Server Port", 9050, "");
                join = Config.Bind("", "Join Server Button", false, "");
                leave = Config.Bind("", "Leave Server Button", false, "");
                pvp = Config.Bind("", "Server PVP State", "", "");
                playerName = Config.Bind("", "Your Player Name", "", "");

#if DEBUG
                KeybindManager.Add(this, ConnectToServer, () => new KeyboardShortcut(KeyCode.S,KeyCode.LeftControl));
                KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.Q,KeyCode.LeftControl));
                ip.Value = "127.0.0.1";
#endif

                join.SettingChanged += (_, _) => { if (join.Value) ConnectToServer(); join.Value = false; };
                leave.SettingChanged += (_, _) => { if (leave.Value) DisconnectFromServer(); leave.Value = false; };

                SceneManager.sceneLoaded += OnSceneLoaded;
            } catch (Exception e) {
                Log.Error($"Failed to initialized modding API: {e}");
            }

            Log.Info("Multiplayer plugin initialized.");
        }

        private void InitializeNetworking() {
            _listener = new EventBasedNetListener();
            _client = new NetManager(_listener) { AutoRecycle = true };
            _dataWriter = new NetDataWriter();

            _listener.NetworkReceiveEvent += OnNetworkReceiveEvent;
            _listener.PeerDisconnectedEvent += OnPeerDisconnectedEvent;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (_client.FirstPeer != null) {
                ClearPlayerObjects();
            }

            //titlescreenModifications.MaybeExtendMainMenu(scene);
        }

        private void ConnectToServer() {
            if (_client.IsRunning) return;

            ToastManager.Toast("Connecting to server...");
            _client.Start();
            _client.Connect(ip.Value, port.Value, "SomeConnectionKey");
            _localPlayerId = -1;
            ClearPlayerObjects();
        }

        private async void DisconnectFromServer() {
            if (!_client.IsRunning) return;

            _dataWriter.Reset();
            _dataWriter.Put("Leave");
            _dataWriter.Put(playerName.Value);
            _client.FirstPeer.Send(_dataWriter, DeliveryMethod.Unreliable);

            // Wait briefly to ensure the message is sent
            await Task.Delay(100); // Adjust the delay as needed (100 ms is usually enough)

            // Proceed with disconnection
            _client.DisconnectAll();
            _client.Stop();
            _localPlayerId = -1;
            ClearPlayerObjects();
            ToastManager.Toast("Disconnected from server.");
        }


        private void ClearPlayerObjects() {
            foreach (var playerData in _playerObjects.Values) {
                Destroy(playerData.PlayerObject);
            }
            _playerObjects.Clear();
        }

        private void Update() {
            if (_client.IsRunning && _client.FirstPeer?.ConnectionState == ConnectionState.Connected) {
                _sendTimer += Time.deltaTime;
                if (_sendTimer >= SendInterval) {
                    SendPosition();
                    _sendTimer = 0;
                }
            }
            _client.PollEvents();
        }

        public void SendDecreaseHealth(int playerId, float value) {
            if (_client.FirstPeer == null) {
                ToastManager.Toast("Not connected to server.");
                return;
            }

            _dataWriter.Reset();
            _dataWriter.Put("DecreaseHealth");
            _dataWriter.Put(playerId);
            _dataWriter.Put(value);
            _client.FirstPeer.Send(_dataWriter, DeliveryMethod.Unreliable);
        }

        private void SendPosition() {
            if (_localPlayerId == -1 || Player.i == null) return;

            _dataWriter.Reset();
            _dataWriter.Put("Position");
            var position = Player.i.transform.position;
            _dataWriter.Put(position.x);
            _dataWriter.Put(position.y + 6.5f);
            _dataWriter.Put(position.z);
            _dataWriter.Put(localAnimationState);
            _dataWriter.Put(Player.i.Facing.ToString() == "Right");

            _client.FirstPeer.Send(_dataWriter, DeliveryMethod.Unreliable);
        }

        private void OnNetworkReceiveEvent(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod) {
            HandleReceivedData(reader);
            reader.Recycle();
        }

        private void OnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo) {
            ClearPlayerObjects();
        }

        private void HandleReceivedData(NetDataReader reader) {
            var messageType = reader.GetString();

            switch (messageType) {
                case "Position":
                    HandlePositionMessage(reader);
                    break;
                case "localPlayerId":
                    _localPlayerId = reader.GetInt();
                    _dataWriter.Reset();
                    _dataWriter.Put("Join");
                    _dataWriter.Put(playerName.Value);
                    _client.FirstPeer.Send(_dataWriter, DeliveryMethod.Unreliable);
                    //ToastManager.Toast($"Assigned Player ID: {_localPlayerId}");
                    break;
                case "DecreaseHealth":
                    HandleDecreaseHealth(reader);
                    break;
                case "DestroyDisconnectObject":
                    HandleDisconnectObject(reader);
                    break;
                case "PvPEnabled":
                    enablePVP(reader);
                    break;
                default:
                    ToastManager.Toast(messageType);
                    break;
            }
        }

        private void enablePVP(NetDataReader reader) {
            var enable = reader.GetBool();
            isPVP = enable;
            pvp.Value = enable ? "PVP Enable" : "PVP Disable";
            ToastManager.Toast($"PvP {(enable ? "Enabled" : "Disabled")}");
        }

        private void HandlePositionMessage(NetDataReader reader) {
            var playerId = reader.GetInt();
            var position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var animationState = reader.GetString();
            var isFacingRight = reader.GetBool();

            if (_localPlayerId == playerId) return;

            if (!_playerObjects.TryGetValue(playerId, out var playerData)) {
                playerData = CreatePlayerObject(playerId, position);
                _playerObjects[playerId] = playerData;
            }

            UpdatePlayerObject(playerData, position, animationState, isFacingRight);
        }

        private void HandleDecreaseHealth(NetDataReader reader) {
            var playerId = reader.GetInt();
            var damage = reader.GetFloat();

            if (playerId == _localPlayerId && Player.i != null) {
                Player.i.health.ReceiveDOT_Damage(damage);
                Player.i.ChangeState(PlayerStateType.Hurt, true);
            }
        }

        private void HandleDisconnectObject(NetDataReader reader) {
            var playerId = reader.GetInt();
            if (_playerObjects.TryGetValue(playerId, out var playerData)) {
                Destroy(playerData.PlayerObject);
                _playerObjects.Remove(playerId);
            }
        }

        private PlayerData CreatePlayerObject(int playerId, Vector3 position) {
            // Instantiate the player object
            var playerObject = Instantiate(
                Player.i.transform.Find("RotateProxy/SpriteHolder").gameObject,
                position,
                Quaternion.identity
            );

            // Update effect type on the EffectReceiver component
            var effectReceiver = playerObject.transform
                .Find("Health(Don'tKey)/DamageReceiver")
                .GetComponent<EffectReceiver>();
            if (effectReceiver != null) {
                effectReceiver.effectType = EffectType.EnemyAttack |
                                            EffectType.BreakableBreaker |
                                            EffectType.ShieldBreak |
                                            EffectType.PostureDecreaseEffect;
            }

            // Disable all AbilityActivateChecker components
            foreach (var abilityChecker in playerObject.GetComponentsInChildren<AbilityActivateChecker>(true)) {
                abilityChecker.enabled = false;
            }

            // Set player object name
            playerObject.name = $"PlayerObject_{playerId}";

            // Return the player data
            return new PlayerData(playerObject, position, playerId);
        }


        private void UpdatePlayerObject(PlayerData playerData, Vector3 position, string animationState, bool isFacingRight) {
            var playerObject = playerData.PlayerObject;
            playerObject.transform.position = Vector3.Lerp(playerObject.transform.position, position, Time.deltaTime * 100f);

            var animator = playerObject.GetComponent<Animator>();
            if (animator == null) {
                Log.Error("Animator not found on player object!");
                return;
            }

            if (animationState != currentAnimationState) {
                currentAnimationState = animationState;
                animator.CrossFade(animationState, 0.1f);
            }

            if (animator.GetCurrentAnimatorStateInfo(0).IsName(animationState) &&
                animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f) {
                animator.Play(animationState, 0, 0f);
            }

            var scale = playerObject.transform.localScale;
            scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1 : -1);
            playerObject.transform.localScale = scale;
        }

        private void OnDestroy() {
            _harmony.UnpatchSelf();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            //titlescreenModifications.Unload();
            DisconnectFromServer();
        }
    }
}
