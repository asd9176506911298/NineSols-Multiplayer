﻿using Auto.Utils;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using NineSolsAPI.Menu;
using NineSolsAPI.Preload;
using NineSolsAPI.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
                KeybindManager.Add(this, StartMemoryChallenge, () => new KeyboardShortcut(KeyCode.Z));
                KeybindManager.Add(this, test, () => new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl));
                KeybindManager.Add(this, test2, () => new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));
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
#if DEBUG
        public static T CopyComponent<T>(T source, GameObject target) where T : Component {
            if (source == null || target == null)
                return null;

            // Add the same type of component to the target
            T targetComponent = target.AddComponent<T>();

            // Copy all fields from the source to the target
            System.Type type = typeof(T);
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            foreach (var field in fields) {
                field.SetValue(targetComponent, field.GetValue(source));
            }

            // Copy all properties that are not read-only
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var property in properties) {
                if (property.CanWrite && property.GetSetMethod(true) != null) {
                    try {
                        property.SetValue(targetComponent, property.GetValue(source));
                    } catch {
                        // Handle exceptions if needed (e.g., some properties are not accessible or valid)
                    }
                }
            }

            return targetComponent;
        }

        public GameObject preloadedObject; // This will hold the preloaded GameObject after loading

        void test2() {
            // Array of player object names
            ToastManager.Toast("test");

            //ToastManager.Toast(Player.i.transform.Find("RotateProxy/SpriteHolder/HitBoxManager"));
            //var x = Player.i.transform.Find("RotateProxy/SpriteHolder/HitBoxManager").transform;
            //for(int i = 0; i < x.childCount; i++) {
            //    ToastManager.Toast(x.GetChild(i).name);
            //}
            //GameObject.Find("PlayerObject_0/RotateProxy/SpriteHolder").transform.SetParent(null);
            //Destroy(GameObject.Find("PlayerObject_0"));
            //string[] playerObjectNames = { "PlayerObject_0", "PlayerObject_1", "PlayerObject_2", "PlayerObject_3" };

            //// Loop through each player object
            //foreach (string playerName in playerObjectNames) {
            //    GameObject playerObject = GameObject.Find(playerName);
            //    if (playerObject != null) {
            //        Animator animator = playerObject.GetComponent<Animator>();
            //        if (animator != null) {
            //            animator.PlayInFixedTime("Attack1", 0, 0f);
            //        }
            //    }
            //}

        }
        void test() {
            ToastManager.Toast("test");

            var effectReceiver = Player.i.transform
                        .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
                        .GetComponent<EffectReceiver>();

            ToastManager.Toast(effectReceiver);
            if (effectReceiver != null) {
                effectReceiver.effectType = EffectType.EnemyAttack |
                                            EffectType.BreakableBreaker |
                                            EffectType.ShieldBreak |
                                            EffectType.PostureDecreaseEffect;
            }

            var p = Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.AddComponent<DamageDealer>();
            p.type = DamageType.MonsterAttack;
            Traverse.Create(p).Field("_parriableOwner").SetValue(MonsterManager.Instance.monsterDict.First().Value);
            p.bindingParry = MonsterManager.Instance.monsterDict.First().Value.GetComponentInChildren<ParriableAttackEffect>();
            p.attacker = Player.i.health;
            p.damageAmount = Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().FinalValue;

            Traverse.Create(Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("valueProvider").SetValue(p);
            Traverse.Create(Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("fxTimingOverrider").SetValue(p);

            var c = Traverse.Create(Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("customDealers");
            var n = new List<DamageDealer> { p };
            // Set the new array back to the customDealers field.
            c.SetValue(n.ToArray());

            var dummy = Instantiate(
                Player.i.transform.Find("RotateProxy/SpriteHolder").gameObject,
                Player.i.transform.position,
                Quaternion.identity
            );

            var pp = dummy.transform.Find("HitBoxManager/AttackFront").gameObject.AddComponent<DamageDealer>();
            pp.type = DamageType.MonsterAttack;
            Traverse.Create(pp).Field("_parriableOwner").SetValue(MonsterManager.Instance.monsterDict.First().Value);
            pp.bindingParry = MonsterManager.Instance.monsterDict.First().Value.GetComponentInChildren<ParriableAttackEffect>();
            pp.attacker = MonsterManager.Instance.monsterDict.First().Value.health;
            pp.damageAmount = dummy.transform.Find("HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().FinalValue;

            Traverse.Create(dummy.transform.Find("HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("valueProvider").SetValue(p);
            Traverse.Create(dummy.transform.Find("HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("fxTimingOverrider").SetValue(p);

            var cc = Traverse.Create(dummy.transform.Find("HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("customDealers");
            var nn = new List<DamageDealer> { pp };
            cc.SetValue(nn.ToArray());

            var e = dummy.transform
                        .Find("Health(Don'tKey)/DamageReceiver")
                        .GetComponent<EffectReceiver>();

            ToastManager.Toast(e);
            if (e != null) {
                e.effectType = EffectType.EnemyAttack |
                                            EffectType.BreakableBreaker |
                                            EffectType.ShieldBreak |
                                            EffectType.PostureDecreaseEffect;
            }

            AutoAttributeManager.AutoReference(dummy);
            AutoAttributeManager.AutoReferenceAllChildren(dummy);
            //Player.i.Suicide();
            //var x = Instantiate(Resources.Load<GameObject>("Global Prefabs/GameCore").transform.Find("RCG LifeCycle"));
            //x.transform.Find("PPlayer").transform.position = Player.i.transform.position;

            //var effectReceiver = Player.i.transform
            //            .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
            //            .GetComponent<EffectReceiver>();

            //ToastManager.Toast(effectReceiver);
            //if (effectReceiver != null) {
            //    effectReceiver.effectType = EffectType.EnemyAttack |
            //                                EffectType.BreakableBreaker |
            //                                EffectType.ShieldBreak |
            //                                EffectType.PostureDecreaseEffect;
            //}

            //AutoAttributeManager.AutoReference(x.gameObject);
            //AutoAttributeManager.AutoReferenceAllChildren(x.gameObject);

            //var d = x.transform.Find("PPlayer/RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.AddComponent<DamageDealer>();
            //Traverse.Create(d).Field("_parriableOwner").SetValue(MonsterManager.Instance.monsterDict.First().Value);
            //d.type = DamageType.MonsterAttack;
            //Traverse.Create(d).Field("_parriableOwner").SetValue(MonsterManager.Instance.monsterDict.First().Value);
            //d.bindingParry = MonsterManager.Instance.monsterDict.First().Value.GetComponentInChildren<ParriableAttackEffect>();
            ////d.attacker = x.transform.Find("PPlayer").GetComponent<Player>().health;
            //d.attacker = MonsterManager.Instance.monsterDict.First().Value.GetComponentInChildren<Health>();
            //d.damageAmount = x.transform.Find("PPlayer/RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().FinalValue;

            //Traverse.Create(x.transform.Find("PPlayer/RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("valueProvider").SetValue(d);
            //Traverse.Create(x.transform.Find("PPlayer/RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("fxTimingOverrider").SetValue(d);

            //var customDealersField = Traverse.Create(x.transform.Find("PPlayer/RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("customDealers");
            //var newDealersArray = new List<DamageDealer> { d };
            //// Set the new array back to the customDealers field.
            //customDealersField.SetValue(newDealersArray.ToArray());

            //effectReceiver = x.transform
            //            .Find("PPlayer/RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
            //            .GetComponent<EffectReceiver>();

            //if (effectReceiver != null) {
            //    effectReceiver.effectType = EffectType.EnemyAttack |
            //                                EffectType.BreakableBreaker |
            //                                EffectType.ShieldBreak |
            //                                EffectType.PostureDecreaseEffect;
            //}

            //var p = Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.AddComponent<DamageDealer>();
            //Traverse.Create(p).Field("_parriableOwner").SetValue(MonsterManager.Instance.monsterDict.First().Value);
            //p.type = DamageType.MonsterAttack;
            //Traverse.Create(p).Field("_parriableOwner").SetValue(MonsterManager.Instance.monsterDict.First().Value);
            //p.bindingParry = MonsterManager.Instance.monsterDict.First().Value.GetComponentInChildren<ParriableAttackEffect>();
            //p.attacker = Player.i.health;
            //p.damageAmount = Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().FinalValue;

            //Traverse.Create(Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("valueProvider").SetValue(p);
            //Traverse.Create(Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("fxTimingOverrider").SetValue(p);

            //var c = Traverse.Create(Player.i.gameObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("customDealers");
            //var n = new List<DamageDealer> { p };
            //// Set the new array back to the customDealers field.
            //c.SetValue(n.ToArray());

            //if (SceneManager.GetActiveScene().name == "TitleScreenMenu" && StartMenuLogic.Instance != null) {

            //    var StartMemoryChallenge = typeof(StartMenuLogic).GetMethod("StartMemoryChallenge");
            //    if (StartMemoryChallenge != null)
            //        StartMemoryChallenge.Invoke(StartMenuLogic.Instance, new object[] { });
            //}

            //foreach(var x in Resources.FindObjectsOfTypeAll<ParriableAttackEffect>()) {
            //    ToastManager.Toast(GetGameObjectPath(x.gameObject));
            //}
            //var x = Instantiate(Resources.Load<GameObject>("Global Prefabs/GameCore").GetComponent<GameCore>().transform.Find("RCG LifeCycle").gameObject, Player.i.transform.position, Quaternion.identity);
            //Player.i.ChangeState(PlayerStateType.Parry);
        }

        void StartMemoryChallenge() {
            if (SceneManager.GetActiveScene().name == "TitleScreenMenu" && StartMenuLogic.Instance != null) {
                ToastManager.Toast("StartMemoryChallenge");
                var StartMemoryChallenge = typeof(StartMenuLogic).GetMethod("StartMemoryChallenge");
                if (StartMemoryChallenge != null)
                    StartMemoryChallenge.Invoke(StartMenuLogic.Instance, new object[] { });
            }
        }

        string GetGameObjectPath(GameObject obj) {
            string path = obj.name;
            Transform current = obj.transform;

            while (current.parent != null) {
                current = current.parent;
                path = current.name + "/" + path;
            }

            return path;
        }
#endif

        private void InitializeNetworking() {
            _listener = new EventBasedNetListener();
            _client = new NetManager(_listener) { AutoRecycle = true };
            _dataWriter = new NetDataWriter();

            _listener.NetworkReceiveEvent += OnNetworkReceiveEvent;
            _listener.PeerDisconnectedEvent += OnPeerDisconnectedEvent;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (_client.FirstPeer != null) {
                // Start a coroutine to wait for 3 seconds before clearing player objects
                StartCoroutine(WaitAndClearPlayerObjects(scene));
            }

            _dataWriter.Reset();
            _dataWriter.Put("Scene");
            _dataWriter.Put(SceneManager.GetActiveScene().name);
            _client.FirstPeer.Send(_dataWriter, DeliveryMethod.ReliableOrdered);
        }

        private IEnumerator WaitAndClearPlayerObjects(Scene scene) {
            // Wait for 3 seconds first
            yield return new WaitForSeconds(0.1f);

            // Wait until the game state is 'Playing'
            while (GameCore.Instance.currentCoreState != GameCore.GameCoreState.Playing) {
                yield return null; // Wait for the next frame before rechecking
            }

            // Execute the logic once the condition is met
            ClearPlayerObjects();
        }


        private void ConnectToServer() {
            if (_client.IsRunning) return;

            ToastManager.Toast("Connecting to server...");
            _client.Start();
            _client.Connect(ip.Value, port.Value, "SomeConnectionKey");
            _localPlayerId = -1;
            ClearPlayerObjects();

            var effectReceiver = Player.i.transform
                        .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
                        .GetComponent<EffectReceiver>();
            if (effectReceiver != null) {
                effectReceiver.effectType = EffectType.EnemyAttack |
                                            EffectType.BreakableBreaker |
                                            EffectType.ShieldBreak |
                                            EffectType.PostureDecreaseEffect;
            }
        }

        private async void DisconnectFromServer() {
            if (!_client.IsRunning) return;

            // Send the "Leave" message reliably
            _dataWriter.Reset();
            _dataWriter.Put("Leave");
            _dataWriter.Put(playerName.Value);
            _client.FirstPeer.Send(_dataWriter, DeliveryMethod.ReliableOrdered);

            // Wait for the message to be processed
            const int MaxWaitTime = 500; // Maximum wait time in milliseconds
            int elapsedTime = 0;
            while (_client.FirstPeer.ConnectionState != ConnectionState.Disconnected && elapsedTime < MaxWaitTime) {
                await Task.Delay(50); // Check every 50 ms
                elapsedTime += 50;
            }

            // Disconnect the client
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

                    _dataWriter.Reset();
                    _dataWriter.Put("Scene");
                    _dataWriter.Put(SceneManager.GetActiveScene().name);
                    _client.FirstPeer.Send(_dataWriter, DeliveryMethod.ReliableOrdered);
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
            var scene = reader.GetString();

            ToastManager.Toast(scene);

            if (_localPlayerId == playerId) return;

            if (!_playerObjects.TryGetValue(playerId, out var playerData)) {
                ToastManager.Toast(playerId);
                playerData = CreatePlayerObject(playerId, position);
                _playerObjects[playerId] = playerData;
            }

            UpdatePlayerObject(playerData, position, animationState, isFacingRight);
        }

        private void HandleDecreaseHealth(NetDataReader reader) {
            ToastManager.Toast("HandleDecreaseHealth");
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

        void makeDamage(GameObject playerObject, Player dp) {
            var hitBoxManager = Player.i.transform.Find("RotateProxy/SpriteHolder/HitBoxManager")?.transform;

            if (hitBoxManager == null) {
                ToastManager.Toast("HitBoxManager not found.");
                return;
            }

            var monsterPath = "A1_S2_GameLevel/Room/Prefab/Gameplay5/[自然巡邏框架]/[MonsterBehaviorProvider] LevelDesign_CullingAndResetGroup/[MonsterBehaviorProvider] LevelDesign_Init_Scenario (看守的人)/StealthGameMonster_Spearman (1)";
            var monsterCorePath = $"{monsterPath}/MonsterCore/Animator(Proxy)/Animator/LogicRoot/SwordSlashEffect/DamageArea";

            var monster = GameObject.Find(monsterPath)?.GetComponent<StealthGameMonster>();
            var bindingParry = GameObject.Find(monsterCorePath)?.GetComponent<DamageDealer>()?.bindingParry;

            if (monster == null || bindingParry == null) {
                ToastManager.Toast("Monster or binding parry not found.");
                return;
            }

            for (int i = 0; i < hitBoxManager.childCount; i++) {
                var child = hitBoxManager.GetChild(i);
                var name = child.name;
                ToastManager.Toast(name);

                var hitBoxPath = $"RotateProxy/SpriteHolder/HitBoxManager/{name}";
                var effectDealer = playerObject.transform.Find(hitBoxPath)?.GetComponent<EffectDealer>();

                if (effectDealer == null) {
                    ToastManager.Toast($"EffectDealer not found for {name}");
                    continue;
                }

                // Add and configure DamageDealer
                var damageDealer = playerObject.transform.Find(hitBoxPath)?.gameObject.AddComponent<DamageDealer>();
                if (damageDealer != null) {
                    damageDealer.type = DamageType.MonsterAttack;
                    damageDealer.bindingParry = bindingParry;
                    damageDealer.attacker = monster.health;
                    damageDealer.damageAmount = effectDealer.FinalValue;

                    Traverse.Create(damageDealer).Field("_parriableOwner").SetValue(monster);
                    Traverse.Create(damageDealer).Field("owner").SetValue(monster);
                }

                // Update EffectDealer
                Traverse.Create(effectDealer).Field("valueProvider").SetValue(damageDealer);
                Traverse.Create(effectDealer).Field("fxTimingOverrider").SetValue(damageDealer);
                effectDealer.owner = dp;
                effectDealer.DealerEffectOwner = dp;

                // Update EffectReceiver
                var effectReceiver = playerObject.transform
                    .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
                    ?.GetComponent<EffectReceiver>();

                if (effectReceiver != null) {
                    effectReceiver.Owner = dp;
                    effectReceiver.effectType = EffectType.EnemyAttack |
                                                  EffectType.BreakableBreaker |
                                                  EffectType.ShieldBreak |
                                                  EffectType.PostureDecreaseEffect;
                } else {
                    ToastManager.Toast("EffectReceiver not found.");
                }

                // Set customDealers field
                var customDealers = new List<DamageDealer> { damageDealer };
                Traverse.Create(effectDealer).Field("customDealers").SetValue(customDealers.ToArray());
            }
        }


        private PlayerData CreatePlayerObject(int playerId, Vector3 position) {
            // Instantiate the player object
            var playerObject = Instantiate(
                Player.i.gameObject,
                position,
                Quaternion.identity
            );

            Destroy(playerObject.GetComponent<Player>());
            var dp = playerObject.AddComponent<Player>();

            AutoAttributeManager.AutoReference(playerObject);
            AutoAttributeManager.AutoReferenceAllChildren(playerObject);

            //makeDamage(playerObject, dp);

            //var pp = playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.AddComponent<DamageDealer>();
            //pp.type = DamageType.MonsterAttack;
            //Traverse.Create(pp).Field("_parriableOwner").SetValue(GameObject.Find("A1_S2_GameLevel/Room/Prefab/Gameplay5/[自然巡邏框架]/[MonsterBehaviorProvider] LevelDesign_CullingAndResetGroup/[MonsterBehaviorProvider] LevelDesign_Init_Scenario (看守的人)/StealthGameMonster_Spearman (1)").GetComponent<StealthGameMonster>());
            //Traverse.Create(pp).Field("owner").SetValue(GameObject.Find("A1_S2_GameLevel/Room/Prefab/Gameplay5/[自然巡邏框架]/[MonsterBehaviorProvider] LevelDesign_CullingAndResetGroup/[MonsterBehaviorProvider] LevelDesign_Init_Scenario (看守的人)/StealthGameMonster_Spearman (1)").GetComponent<StealthGameMonster>());
            //pp.bindingParry = GameObject.Find("A1_S2_GameLevel/Room/Prefab/Gameplay5/[自然巡邏框架]/[MonsterBehaviorProvider] LevelDesign_CullingAndResetGroup/[MonsterBehaviorProvider] LevelDesign_Init_Scenario (看守的人)/StealthGameMonster_Spearman (1)/MonsterCore/Animator(Proxy)/Animator/LogicRoot/SwordSlashEffect/DamageArea").GetComponent<DamageDealer>().bindingParry;
            //pp.attacker = GameObject.Find("A1_S2_GameLevel/Room/Prefab/Gameplay5/[自然巡邏框架]/[MonsterBehaviorProvider] LevelDesign_CullingAndResetGroup/[MonsterBehaviorProvider] LevelDesign_Init_Scenario (看守的人)/StealthGameMonster_Spearman (1)").GetComponent<StealthGameMonster>().health;
            //pp.damageAmount = playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().FinalValue;

            //Traverse.Create(playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("valueProvider").SetValue(pp);
            //Traverse.Create(playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("fxTimingOverrider").SetValue(pp);
            //playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().owner = dp;
            //playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>().DealerEffectOwner = dp;

            //playerObject.transform.Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver").gameObject.GetComponent<EffectReceiver>().Owner = dp;
            //ToastManager.Toast(playerObject.transform.Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver"));

            //var cc = Traverse.Create(playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager/AttackFront").gameObject.GetComponent<EffectDealer>()).Field("customDealers");
            //var nn = new List<DamageDealer> { pp };
            //cc.SetValue(nn.ToArray());

            //var e = playerObject.transform
            //            .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
            //            .GetComponent<EffectReceiver>();

            //ToastManager.Toast(e);
            //if (e != null) {
            //    e.effectType &= EffectType.EnemyAttack |
            //                                EffectType.BreakableBreaker |
            //                                EffectType.ShieldBreak |
            //                                EffectType.PostureDecreaseEffect;
            //}




            //var effectReceiver = Player.i.transform
            //    .Find("Health(Don'tKey)/DamageReceiver")
            //    .GetComponent<EffectReceiver>();
            //if (effectReceiver != null) {
            //    effectReceiver.effectType = EffectType.EnemyAttack |
            //                                EffectType.BreakableBreaker |
            //                                EffectType.ShieldBreak |
            //                                EffectType.PostureDecreaseEffect;
            //}

            // Update effect type on the EffectReceiver component
            //var effectReceiver = playerObject.transform
            //    .Find("Health(Don'tKey)/DamageReceiver")
            //    .GetComponent<EffectReceiver>();
            //if (effectReceiver != null) {
            //    effectReceiver.effectType &= ~(EffectType.EnemyAttack |
            //                                EffectType.BreakableBreaker |
            //                                EffectType.ShieldBreak |
            //                                EffectType.PostureDecreaseEffect);
            //}

            //// Disable all AbilityActivateChecker components
            //foreach (var abilityChecker in playerObject.GetComponentsInChildren<AbilityActivateChecker>(true)) {
            //    abilityChecker.enabled = false;
            //}

            // Set player object name
            playerObject.name = $"PlayerObject_{playerId}";

            // Return the player data
            return new PlayerData(playerObject, position, playerId);
        }


        private void UpdatePlayerObject(PlayerData playerData, Vector3 position, string animationState, bool isFacingRight) {
            var playerObject = playerData.PlayerObject.transform.Find("RotateProxy/SpriteHolder");
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
                animator.PlayInFixedTime(animationState, 0, 0f);
            }

            //playerData.PlayerObject.GetComponent<Player>().Facing = isFacingRight ? Facings.Right : Facings.Left;
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
