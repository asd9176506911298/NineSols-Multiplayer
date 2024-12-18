
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private ConfigEntry<bool> displayPlayerName;
        private ConfigEntry<int> playerNameSize;
        public bool isPVP;

        bool testbool = false;

        GameObject minionPrefab = null;

        public readonly Dictionary<int, PlayerData> _playerObjects = new();
        private int _localPlayerId = -1;
        private const float SendInterval = 0.02f;
        private float _sendTimer;

        private string? currentAnimationState = string.Empty;
        public string? localAnimationState = "";

        private GameObject chatCanvas;
        private GameObject inputField;
        private GameObject chatLog;


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
                playerName = Config.Bind("Name", "Your Player Name", "", "");
                displayPlayerName = Config.Bind("Name", "Is Display Player Name", true, "");
                playerNameSize = Config.Bind("Name", "Player Name Size", 200, "");

#if DEBUG
                KeybindManager.Add(this, ConnectToServer, () => new KeyboardShortcut(KeyCode.S,KeyCode.LeftControl));
                KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.Q,KeyCode.LeftControl));
                KeybindManager.Add(this, StartMemoryChallenge, () => new KeyboardShortcut(KeyCode.Z));
                KeybindManager.Add(this, test, () => new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl));
                KeybindManager.Add(this, test2, () => new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));
                KeybindManager.Add(this, test3, () => new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl));
                ip.Value = "127.0.0.1";
#endif

                join.SettingChanged += (_, _) => { if (join.Value) ConnectToServer(); join.Value = false; };
                leave.SettingChanged += (_, _) => { if (leave.Value) DisconnectFromServer(); leave.Value = false; };
                displayPlayerName.SettingChanged += (_, _) =>  SetPlayerNameVisible();
                playerNameSize.SettingChanged += (_, _) => SetPlayerNameSize();

                SceneManager.sceneLoaded += OnSceneLoaded;
            } catch (Exception e) {
                Log.Error($"Failed to initialized modding API: {e}");
            }

            Log.Info("Multiplayer plugin initialized.");

            chatCanvas = new GameObject("ChatCanvas");
            var canvas = chatCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Add CanvasScaler and GraphicRaycaster for proper UI functionality
            chatCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            chatCanvas.AddComponent<GraphicRaycaster>();

            var rect = chatCanvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400, 300); // Set your desired size
            rect.anchorMin = new Vector2(0, 0); // Anchor to bottom-left
            rect.anchorMax = new Vector2(0, 0); // Anchor to bottom-left
            rect.pivot = new Vector2(0, 0); // Set pivot to bottom-left corner
            rect.anchoredPosition = new Vector2(0, 0); // Set position to (0, 0) relativ

            // Create Chat Log (Scroll View)
            CreateChatLog();

            // Create Input Field
            CreateInputField();

            // Make chat window initially hidden
            chatCanvas.SetActive(false);
        }

        void SetPlayerNameSize() {
            // Cache the player name size value
            float fontSize = playerNameSize.Value;

            // Iterate over the values directly if keys are not needed
            foreach (var player in _playerObjects.Values) {
                // Attempt to find the "PlayerName" GameObject
                var playerNameTransform = player.PlayerObject.transform.Find("PlayerName");

                if (playerNameTransform != null) {
                    // Get the TextMeshPro component
                    var textMeshPro = playerNameTransform.GetComponent<TextMeshPro>();

                    if (textMeshPro != null) {
                        // Set the font size
                        textMeshPro.fontSize = fontSize;
                    } else {
                        // Optional: Log a warning if TextMeshPro is missing
                        Log.Warning($"TextMeshPro component not found in {player.PlayerObject.name}'s PlayerName.");
                    }
                } else {
                    // Optional: Log a warning if "PlayerName" GameObject is missing
                    Log.Warning($"PlayerName GameObject not found in {player.PlayerObject.name}");
                }
            }
        }


        private void SetPlayerNameVisible() {
            bool isVisible = displayPlayerName.Value;

            foreach (var player in _playerObjects.Values) {
                var playerNameObject = player.PlayerObject.transform.Find("PlayerName")?.gameObject;
                if (playerNameObject != null) {
                    playerNameObject.SetActive(isVisible);
                }
            }
        }

#if DEBUG
        void test3() {
            testbool = !testbool;
        }

        private IEnumerator PreloadSceneObjects(string sceneName, string objectPath) {
            // Load the scene asynchronously in the background.
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            // Wait until the scene is fully loaded.
            while (!asyncLoad.isDone) {
                yield return null;
            }

            // Scene is now loaded, find the target object.
            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            GameObject targetObject = null;

            if (loadedScene.IsValid()) {
                // Temporarily activate the loaded scene.
                SceneManager.SetActiveScene(loadedScene);

                // Find the target GameObject.
                targetObject = GameObject.Find(objectPath);

                if (targetObject != null) {
                    // Detach the object to make it a root GameObject.
                    //targetObject.SetActive(true);
                    //targetObject.transform.SetParent(null);

                    // Ensure it's now a root object before making it persistent.
                    if (targetObject.transform.parent == null) {
                        //horse
                        //Vector3 v = new Vector3(targetObject.transform.position.x, targetObject.transform.position.y + 100f, targetObject.transform.position.z);
                        //Vector3 v = new Vector3(targetObject.transform.position.x, targetObject.transform.position.y - 500f, targetObject.transform.position.z);
                        //Vector3 v = new Vector3(targetObject.transform.position.x + 100f, targetObject.transform.position.y + 95f, targetObject.transform.position.z);
                        Vector3 v = new Vector3(targetObject.transform.position.x + 100f, targetObject.transform.position.y - 500f, targetObject.transform.position.z);
                        targetObject.transform.position = v;
                        ToastManager.Toast(GetGameObjectPath(targetObject.gameObject));
                        RCGLifeCycle.DontDestroyForever(targetObject);
                        //var levelAwakeList = targetObject.GetComponentsInChildren<ILevelAwake>(true);
                        //for (var i = levelAwakeList.Length - 1; i >= 0; i--) {
                        //    var context = levelAwakeList[i];
                        //    try { context.EnterLevelAwake(); } catch (Exception ex) { Log.Error(ex.StackTrace); }
                        //}
                        Log.Info($"Found and persisted GameObject: {targetObject.name}");
                    } else {
                        Log.Warning($"Failed to detach GameObject: {targetObject.name}");
                    }
                } else {
                    Log.Warning($"GameObject with path '{objectPath}' not found in scene '{sceneName}'.");
                }
            } else {
                Log.Warning($"Scene '{sceneName}' is not valid or failed to load.");
            }

            // Unload the scene.
            SceneManager.UnloadSceneAsync(sceneName);

            Log.Info("Scene unloaded and active scene reverted.");

            // Reload the current scene to reset the state if needed.
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

       

        void test2() {
            // Array of player object names
            ToastManager.Toast(":goodtimefrog: Za Warudo");

            //var sp = GameObject.Find("GameCore(Clone)/RCG LifeCycle/PPlayer/RotateProxy/SpriteHolder/customObject").GetComponent<SpriteRenderer>().sprite;
            ////ToastManager.Toast(sp);

            //foreach (var x in GameObject.Find("A1_S2_GameLevel").GetComponentsInChildren<SpriteRenderer>()) {
            //    foreach (var a in x.GetComponentsInChildren<Animator>())
            //        a.enabled = false;

            //    foreach (var s in x.GetComponentsInChildren<SpriteRenderer>()) {
            //        s.sprite = sp;

            //        // Scale up the sprite if necessary
            //        float scaleFactor = 3.0f; // Adjust this value as needed
            //        s.transform.localScale = new Vector3(scaleFactor, scaleFactor, 1);
            //    }
            //}

            //foreach (var x in GameObject.FindObjectsOfType<MonsterBase>()) {
            //    foreach (var a in x.GetComponentsInChildren<Animator>())
            //        a.enabled = false;

            //    foreach (var s in x.GetComponentsInChildren<SpriteRenderer>()) {
            //        s.sprite = sp;

            //        // Scale up the sprite if necessary
            //        float scaleFactor = 5.0f; // Adjust this value as needed
            //        s.transform.localScale = new Vector3(scaleFactor, scaleFactor, 1);
            //    }
            //}

            //foreach(Transform x in GameObject.Find("GameCore(Clone)/RCG LifeCycle/PPlayer/RotateProxy/SpriteHolder/HitBoxManager").transform) {
            //    foreach(var z in x.GetComponentsInChildren<EffectDealer>()) {
            //        ToastManager.Toast(z.name);
            //    }
            //}
            //GameCore.Instance.GoToSceneWithSavePoint("VR_Challenge_Boss_SpearHorseman");
            //foreach(var x in _playerObjects) {
            //    x.Value.PlayerObject.transform.Find("PlayerName").gameObject.SetActive(false);
            //}
            //SceneManager.LoadScene("VR_Challenge_Hub");
            //if(minionPrefab == null && Player.i != null)
            //    StartCoroutine(Test2Coroutine());
            //else {
            //    ToastManager.Toast("ddddddddnull");
            //}
            //foreach (var obj in Resources.FindObjectsOfTypeAll<MonsterBase>()) {
            //    if (obj.name == "StealthGameMonster_Minion_prefab")
            //        ToastManager.Toast("StealthGameMonster_Minion_prefab");
            //}
            //// Find the object in memory
            //GameObject minionPrefab = null;
            //var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            //foreach (var obj in allObjects) {
            //    if (obj.name == "StealthGameMonster_Minion_prefab") {
            //        minionPrefab = obj;
            //        AutoAttributeManager.AutoReference(obj);
            //        AutoAttributeManager.AutoReferenceAllChildren(obj);
            //        break;
            //    }
            //}

            //if (minionPrefab != null) {
            //    ToastManager.Toast("Found object: " + minionPrefab.name);
            //    // Do something with the object
            //} else {
            //    ToastManager.Toast("Object not found.");
            //}

            //var copy = Instantiate(minionPrefab);
            //AutoAttributeManager.AutoReference(copy);
            //AutoAttributeManager.AutoReferenceAllChildren(copy);

            //var levelAwakeList = copy.GetComponentsInChildren<ILevelAwake>(true);
            //for (var i = levelAwakeList.Length - 1; i >= 0; i--) {
            //    var context = levelAwakeList[i];
            //    try { context.EnterLevelAwake(); } catch (Exception ex) { Log.Error(ex.StackTrace); }
            //}

            //copy.transform.position = Player.i.transform.position;


            //StartCoroutine(PreloadSceneObjects("A2_S5_BossHorseman_Final", "A2_S5_ BossHorseman_GameLevel"));
            //StartCoroutine(PreloadSceneObjects("A3_S5_BossGouMang_Final", "A3_S5_BossGouMang_GameLevel"));
            //StartCoroutine(PreloadSceneObjects("A11_S0_Boss_YiGung", "GameLevel"));
            //ToastManager.Toast(GameObject.Find("Room/StealthGameMonster_SpearHorseMan"));
            //var copy = GameObject.Find("Room");
            //AutoAttributeManager.AutoReference(copy);
            //AutoAttributeManager.AutoReferenceAllChildren(copy);

            //var levelAwakeList = copy.GetComponentsInChildren<ILevelAwake>(true);
            //for (var i = levelAwakeList.Length - 1; i >= 0; i--) {
            //    var context = levelAwakeList[i];
            //    try { context.EnterLevelAwake(); } catch (Exception ex) { Log.Error(ex.StackTrace); }
            //}

            //foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>()) {
            //    var monsterBases = gameObject.GetComponentsInChildren<DamageDealer>(true);
            //    foreach (var monster in monsterBases) {
            //        ToastManager.Toast(GetGameObjectPath(monster.gameObject));
            //    }
            //}


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
            

            // Wait until the game state is 'Playing'
            while (GameCore.Instance.currentCoreState != GameCore.GameCoreState.Playing) {
                //ToastManager.Toast(GameCore.Instance.currentCoreState);
                yield return new WaitForSeconds(0.3f); // Wait for the next frame before rechecking
                //ToastManager.Toast("123");
            }

            while (Player.i.playerInput.currentStateType != PlayerInputStateType.Action) {
                //ToastManager.Toast(GameCore.Instance.currentCoreState);
                yield return new WaitForSeconds(0.3f); // Wait for the next frame before rechecking
                //ToastManager.Toast("456");
            }

            //yield return new WaitForSeconds(2f);

            // Execute the logic once the condition is met
            ClearPlayerObjects();
        }


        private void ConnectToServer() {
            if (_client.IsRunning) return;

            if (Player.i == null) {
                ToastManager.Toast("Yi haven't create. Enter game try join server again");
                return;
            }

            ToastManager.Toast("Connecting to server...");
            _client.Start();
            _client.Connect(ip.Value, port.Value, "SomeConnectionKey");
            _localPlayerId = -1;
            ClearPlayerObjects();

            // Start a coroutine to check the connection status
            StartCoroutine(CheckConnectionStatus(OnConnectionStatusChecked));
        }

        private void OnConnectionStatusChecked(bool success) {
            if (!success) {
                ToastManager.Toast("Connection failed.");
                return;
            }

            if (Player.i == null) return;

            ConfigurePlayerEffectReceiver();
            LoadMinionPrefab();
        }

        private void ConfigurePlayerEffectReceiver() {
            var effectReceiver = Player.i.transform
                .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
                ?.GetComponent<EffectReceiver>();

            if (effectReceiver != null) {
                effectReceiver.effectType = EffectType.EnemyAttack |
                                            EffectType.BreakableBreaker |
                                            EffectType.ShieldBreak |
                                            EffectType.PostureDecreaseEffect;
            }
        }

        private void LoadMinionPrefab() {
            var allObjects = Resources.FindObjectsOfTypeAll<MonsterBase>();
            foreach (var obj in allObjects) {
                if (obj.name == "StealthGameMonster_Minion_prefab") {
                    minionPrefab = obj.gameObject;
                    AutoAttributeManager.AutoReference(minionPrefab);
                    AutoAttributeManager.AutoReferenceAllChildren(minionPrefab);

                    var levelAwakeList = minionPrefab.GetComponentsInChildren<ILevelAwake>(true);
                    for (var i = levelAwakeList.Length - 1; i >= 0; i--) {
                        var context = levelAwakeList[i];
                        try { context.EnterLevelAwake(); } catch (Exception ex) { Log.Error(ex.StackTrace); }
                    }
                    return;
                }
            }

            if (minionPrefab == null) {
                StartCoroutine(PreloadMinionPrefab());
            }
        }

        // Coroutine to check connection status
        private IEnumerator CheckConnectionStatus(Action<bool> callback) {
            float timeout = 2f; // Time to wait for a successful connection
            float elapsedTime = 0f;

            while (!(_client.FirstPeer?.ConnectionState == ConnectionState.Connected) && elapsedTime < timeout) {
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            bool isConnected = _client.FirstPeer?.ConnectionState == ConnectionState.Connected;
            callback(isConnected);

            if (!isConnected) {
                _client.Stop(); // Clean up client on failure
            }
        }



        private IEnumerator PreloadMinionPrefab() {
            // Execute the scene loading/unloading coroutine
            yield return StartCoroutine(LoadUnloadScene());

            // Once the scene is unloaded, search for the object
            var allObjects = Resources.FindObjectsOfTypeAll<MonsterBase>();

            foreach (var obj in allObjects) {
                if (obj.name == "StealthGameMonster_Minion_prefab") {
                    minionPrefab = obj.gameObject;
                    AutoAttributeManager.AutoReference(minionPrefab);
                    AutoAttributeManager.AutoReferenceAllChildren(minionPrefab);
                    break;
                }
            }

            // Log the result of the search
            if (minionPrefab != null) {
                Log.Info($"Object '{minionPrefab.name}' found.");
            } else {
                Log.Warning("Object 'StealthGameMonster_Minion_prefab' not found.");
            }
        }

        private IEnumerator LoadUnloadScene() {
            string sceneName = "A1_S2_ConnectionToElevator_Final";

            // Load the scene additively
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (!asyncLoad.isDone) {
                yield return null; // Wait for the scene to load
            }

            Log.Info($"Scene {sceneName} loaded.");

            // Access the loaded scene
            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            if (!loadedScene.IsValid()) {
                Log.Error($"Scene {sceneName} is not valid after loading.");
                yield break;
            }

            // Unload the scene
            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);
            while (!asyncUnload.isDone) {
                yield return null; // Wait for the scene to unload
            }

            Log.Info($"Scene {sceneName} unloaded.");

            // Wait for 3 seconds
            Log.Info("Waiting for 3 seconds before reloading the active scene...");
            yield return WaitForPrefabAndGameCoreState("StealthGameMonster_Minion_prefab");

            // Reload the active scene
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            Log.Info("Active scene reloaded.");
        }



        private IEnumerator WaitForPrefabAndGameCoreState(string prefabName, float timeoutSeconds = 2f) {

            float elapsedTime = 0f;

            // Continuously check for the prefab and game state within the timeout duration
            while (minionPrefab == null && elapsedTime < timeoutSeconds) {
                // Check all objects for the target prefab
                foreach (var obj in Resources.FindObjectsOfTypeAll<MonsterBase>()) {
                    if (obj.name == prefabName) {
                        minionPrefab = obj.gameObject;
                        break;
                    }
                }

                // Log if the object hasn't been found yet (optional)
                if (minionPrefab == null) {
                    Log.Info($"Waiting for '{prefabName}' to be found...");
                }

                // Wait for the next frame and increment elapsed time
                yield return null;
                elapsedTime += Time.deltaTime;
            }

            if (minionPrefab == null) {
                Log.Warning($"Timeout: Prefab '{prefabName}' not found within {timeoutSeconds} seconds.");
                yield break;
            }

            Log.Info($"Prefab '{minionPrefab.name}' found within {elapsedTime:F2} seconds.");
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
            //ToastManager.Toast($"{GameCore.Instance.currentCoreState} {Player.i.playerInput.currentStateType}");
            _client.PollEvents();
#if DEBUG
            if (testbool) {
                Player.i.ChangeState(PlayerStateType.ParryCounterDefense, true);
            }

            if (Input.GetKeyDown(KeyCode.T)) {
                chatCanvas.SetActive(!chatCanvas.activeSelf);
            }

            // Send message when Enter is pressed
            if (chatCanvas.activeSelf && Input.GetKeyDown(KeyCode.Return)) {
                SendMessageToChat(inputField.GetComponent<InputField>().text);
                inputField.GetComponent<InputField>().text = string.Empty; // Clear input field
            }
#endif
        }

        private void CreateInputField() {
            inputField = new GameObject("InputField");
            inputField.transform.SetParent(chatCanvas.transform, false);  // Keep local position unaffected

            var rect = inputField.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(380, 30);  // Adjust width and height of the input field
            rect.anchorMin = new Vector2(0, 0);  // Anchor it to the bottom-left corner
            rect.anchorMax = new Vector2(0, 0);  // Anchor it to the bottom-left corner
            rect.pivot = new Vector2(0, 0);  // Set pivot at the bottom-left corner
            rect.anchoredPosition = new Vector2(20, 20);  // Position with a little padding from the bottom-left corner

            var image = inputField.AddComponent<Image>();
            image.color = Color.gray;

            var input = inputField.AddComponent<InputField>();

            // Add a child GameObject for input text
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(inputField.transform, false);

            var textRect = textObj.AddComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(360, 25);  // Adjust size of the text input
            textRect.anchoredPosition = Vector2.zero;

            var text = textObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.color = Color.black;  // Input text color
            text.alignment = TextAnchor.MiddleLeft;

            input.textComponent = text; // Assign the input text component

            // Add a placeholder text
            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(inputField.transform, false);

            var placeholderRect = placeholderObj.AddComponent<RectTransform>();
            placeholderRect.sizeDelta = new Vector2(360, 25);
            placeholderRect.anchoredPosition = Vector2.zero;

            var placeholder = placeholderObj.AddComponent<Text>();
            placeholder.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            placeholder.fontSize = 14;
            placeholder.color = new Color(0.7f, 0.7f, 0.7f, 1); // Gray placeholder color
            placeholder.text = "Enter message...";
            placeholder.alignment = TextAnchor.MiddleLeft;

            input.placeholder = placeholder; // Assign the placeholder component
        }
        private void SendMessageToChat(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Create a new message object
            var messageObj = new GameObject("ChatMessage");
            messageObj.transform.SetParent(chatLog.transform, false);  // Keep local position unaffected

            // Add a RectTransform to the message object for layout control
            var messageRect = messageObj.AddComponent<RectTransform>();
            messageRect.sizeDelta = new Vector2(380, 20);  // Set width and height (adjust as needed)

            // Add a Text component to display the message
            var text = messageObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 14;
            text.color = Color.white;  // Input text color
            text.text = message;

            // Optionally, add LayoutElement to manage the message size
            var layoutElement = messageObj.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 20;  // Control the height of each message box

            // Ensure the vertical layout group forces proper alignment and spacing
            var verticalLayout = chatLog.GetComponent<VerticalLayoutGroup>();
            if (verticalLayout != null) {
                verticalLayout.childForceExpandWidth = true;
                verticalLayout.childForceExpandHeight = false;
            }

            // Ensure the content height is recalculated every time a new message is added
            var contentRect = chatLog.GetComponent<RectTransform>();
            contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, contentRect.sizeDelta.y + 20);  // Increase the height by 20

            // Force layout to update immediately
            Canvas.ForceUpdateCanvases();  // Forces the layout to update

            // Force the layout rebuild for chat log
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatLog.GetComponent<RectTransform>());

            // If you have a ScrollRect, make sure to scroll to the bottom immediately
            var scrollRect = chatLog.GetComponentInParent<ScrollRect>();
            if (scrollRect != null) {
                scrollRect.verticalNormalizedPosition = 0f;  // Scroll to the bottom immediately
            }

            ToastManager.Toast($"Message sent: {message}");
        }



        private void CreateChatLog() {
            var scrollView = new GameObject("ChatLog");
            scrollView.transform.SetParent(chatCanvas.transform, false);  // Keep local position unaffected

            var rect = scrollView.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400, 200);  // Set a fixed size for the scroll view (adjust as needed)

            // Anchor the scroll view to the bottom-left
            rect.anchorMin = new Vector2(0, 0);  // Bottom-left corner
            rect.anchorMax = new Vector2(0, 0);  // Bottom-left corner
            rect.pivot = new Vector2(0, 0);  // Pivot at the bottom-left corner
            rect.anchoredPosition = new Vector2(20, 20);  // Position relative to the bottom-left corner (adjust as needed)

            // Add ScrollRect component to make the content scrollable
            var scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.vertical = true;  // Enable vertical scrolling
            scrollRect.horizontal = false;  // Disable horizontal scrolling

            // Create content container for messages
            var content = new GameObject("Content");
            content.transform.SetParent(scrollView.transform, false);

            var contentRect = content.AddComponent<RectTransform>();
            contentRect.sizeDelta = new Vector2(400, 0);  // Start with 0 height, it will adjust dynamically

            // Add VerticalLayoutGroup for automatic message layout
            var verticalLayout = content.AddComponent<VerticalLayoutGroup>();
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.spacing = 5;  // Optional spacing between messages

            // Add ContentSizeFitter to automatically adjust the content's height
            var contentSizeFitter = content.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Set the Content of ScrollRect to the content container
            scrollRect.content = contentRect;

            // Add a background image to the chat log
            var image = scrollView.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.5f);

            chatLog = content;  // Store the content for adding messages later
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

        public void SendRecoverableDamage(int playerId, float value) {
            if (_client.FirstPeer == null) {
                ToastManager.Toast("Not connected to server.");
                return;
            }

            _dataWriter.Reset();
            _dataWriter.Put("RecoverableDamage");
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
            HandleReceivedDataAsync(reader);
            reader.Recycle();
        }

        private void OnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo) {
            ClearPlayerObjects();
        }

        private async Task HandleReceivedDataAsync(NetDataReader reader) {
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
                case "RecoverableDamage":
                    HandleRecoverableDamage(reader);
                    break;
                case "DestroyDisconnectObject":
                    HandleDisconnectObject(reader);
                    break;
                case "PvPEnabled":
                    EnablePVP(reader);
                    break;
                case "GetName":
                    var playerId = reader.GetInt();
                    var name = reader.GetString();
                    //ToastManager.Toast($"11111111111 {playerId} {name}");
                    _playerObjects[playerId].name = name;
                    //ToastManager.Toast(_playerObjects[playerId].PlayerObject);
                    _playerObjects[playerId].PlayerObject.transform.Find("PlayerName").GetComponent<TextMeshPro>().text = name;
                    break;
                case "tp":
                    var tpSceneName = reader.GetString();
                    // Notify players about the teleport
                    ToastManager.Toast($"Server Teleported All Players to {tpSceneName}");
                    if (SceneManager.GetActiveScene().name == tpSceneName) break;
                    // Go to the target scene
                    GameCore.Instance?.GoToScene(tpSceneName);

                    // Wait until the game is ready for playing
                    await WaitPlaying();

                    // Set the revive save point after the game is ready
                    var teleportData = new TeleportPointData {
                        sceneName = tpSceneName,
                        TeleportPosition = Player.i.transform.position
                    };
                    GameCore.Instance?.SetReviveSavePoint(teleportData);
                    break;
                case "stop":
                    ToastManager.Toast("Server Owner Stop Server");
                    DisconnectFromServer();
                    break;
                default:
                    ToastManager.Toast(messageType);
                    break;
            }
        }

        private async Task WaitPlaying() {
            while (GameCore.Instance.currentCoreState != GameCore.GameCoreState.Playing ||
                   Player.i.playerInput.currentStateType == PlayerInputStateType.Cutscene) {
                // Wait for 300 milliseconds before rechecking
                await Task.Delay(300);
            }
        }


        private void EnablePVP(NetDataReader reader) {
            isPVP = reader.GetBool();
            pvp.Value = isPVP ? "PVP Enabled" : "PVP Disabled";
            ToastManager.Toast($"PvP {(isPVP ? "Enabled" : "Disabled")}");

            var effectReceiver = Player.i.transform
                .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
                ?.GetComponent<EffectReceiver>();

            if (effectReceiver != null) {
                if (isPVP) {
                    effectReceiver.effectType |= EffectType.EnemyAttack |
                                                  EffectType.BreakableBreaker |
                                                  EffectType.ShieldBreak |
                                                  EffectType.PostureDecreaseEffect;

                    foreach (var x in _playerObjects) {
                        var e = x.Value.PlayerObject.transform.Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")?.GetComponent<EffectReceiver>();
                        e.effectType |= EffectType.EnemyAttack |
                                                  EffectType.BreakableBreaker |
                                                  EffectType.ShieldBreak |
                                                  EffectType.PostureDecreaseEffect;
                    }
                } else {
                    effectReceiver.effectType &= ~(
                        EffectType.BreakableBreaker |
                        EffectType.ShieldBreak |
                        EffectType.PostureDecreaseEffect);

                    foreach (var x in _playerObjects) {
                        var e = x.Value.PlayerObject.transform.Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")?.GetComponent<EffectReceiver>();
                        e.effectType &= ~(
                        EffectType.BreakableBreaker |
                        EffectType.ShieldBreak |
                        EffectType.PostureDecreaseEffect);
                    }
                }
            }
        }


        private void HandlePositionMessage(NetDataReader reader) {
            var playerId = reader.GetInt();
            var position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            var animationState = reader.GetString();
            var isFacingRight = reader.GetBool();
            var scene = reader.GetString();

            // Ignore updates for the local player
            if (_localPlayerId == playerId) return;

            // Check if the player is in a different scene
            if (scene != SceneManager.GetActiveScene().name) {
                // If the player exists in the current scene, destroy their object and remove them
                if (_playerObjects.TryGetValue(playerId, out var p)) {
                    Destroy(p.PlayerObject);
                    _playerObjects.Remove(playerId);
                }
                return;
            }

            // If the player object doesn't exist, create it
            if (!_playerObjects.TryGetValue(playerId, out var playerData)) {
                if (minionPrefab == null) return;
                _dataWriter.Reset();
                _dataWriter.Put("GetName");
                _dataWriter.Put(playerId);
                _client.FirstPeer.Send(_dataWriter, DeliveryMethod.ReliableOrdered);
                ToastManager.Toast(playerId); // Notify that a new player object is being created
                playerData = CreatePlayerObject(playerId, position);
                _playerObjects[playerId] = playerData;
            }

            // Update the player's object properties
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

        private void HandleRecoverableDamage(NetDataReader reader) {
            //ToastManager.Toast("RecoverableDamage");
            var playerId = reader.GetInt();
            var internalDamage = reader.GetFloat();

            ToastManager.Toast($"HandleRecoverableDamage: {playerId} {_localPlayerId}");

            if (playerId == _localPlayerId && Player.i != null) {
                Player.i.health.ReceiveRecoverableDamage(internalDamage);
                Player.i.ChangeState(PlayerStateType.LieDown, true);
                Player.i.velocityModifierManager.attackKnockbackModifier.ApplyVelocity(400f * -Player.i.towardDir.x, 0f);
            }
        }

        private void HandleDisconnectObject(NetDataReader reader) {
            var playerId = reader.GetInt();
            if (_playerObjects.TryGetValue(playerId, out var playerData)) {
                Destroy(playerData.PlayerObject);
                _playerObjects.Remove(playerId);
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

        private void MakeDamage(GameObject playerObject, Player dp) {
            // Locate the HitBoxManager
            var hitBoxManager = playerObject.transform.Find("RotateProxy/SpriteHolder/HitBoxManager")?.transform;

            if (hitBoxManager == null) {
                // HitBoxManager not found
                return;
            }

            // Get monster and binding parry references
            var monster = minionPrefab.GetComponent<MonsterBase>();
            var bindingParry = minionPrefab.transform
                .Find("MonsterCore/Animator(Proxy)/Animator/LogicRoot/SwordSlashEffect/DamageArea")
                ?.GetComponent<DamageDealer>()?.bindingParry;

            if (monster == null || bindingParry == null) {
                // Monster or binding parry not found
                return;
            }

            // Iterate through children of HitBoxManager
            foreach (Transform child in hitBoxManager) {
                // Get all EffectDealer components within this child's hierarchy
                var effectDealers = child.GetComponentsInChildren<EffectDealer>();
                
                foreach (var effectDealer in effectDealers) {
                    // Get the path of the game object
                    var hitBoxPath = GetGameObjectPath(effectDealer.gameObject);

                    // Add DamageDealer to the game object
                    var damageDealer = AddDamageDealer(playerObject, hitBoxPath, bindingParry, monster, effectDealer.FinalValue);
                    if (damageDealer != null) {
                        ConfigureEffectDealer(effectDealer, dp, damageDealer);
                        ConfigureEffectReceiver(playerObject, dp);
                    }
                }
            }
        }

        // Adds and configures a DamageDealer to a given path
        private DamageDealer AddDamageDealer(GameObject playerObject, string hitBoxPath, ParriableAttackEffect bindingParry, MonsterBase monster, float damageAmount) {
            var damageDealer = GameObject.Find(hitBoxPath).AddComponent<DamageDealer>();
            if (damageDealer != null) {
                damageDealer.type = DamageType.MonsterAttack;
                damageDealer.bindingParry = bindingParry;
                damageDealer.attacker = new Health(); // Add actual monster health if applicable
                damageDealer.damageAmount = damageAmount;

                Traverse.Create(damageDealer).Field("_parriableOwner").SetValue(monster);
                Traverse.Create(damageDealer).Field("owner").SetValue(monster);
            }

            return damageDealer;
        }

        // Configures an EffectDealer with references to the DamageDealer
        private void ConfigureEffectDealer(EffectDealer effectDealer, Player dp, DamageDealer damageDealer) {
            Traverse.Create(effectDealer).Field("valueProvider").SetValue(damageDealer);
            Traverse.Create(effectDealer).Field("fxTimingOverrider").SetValue(damageDealer);
            effectDealer.owner = dp;
            effectDealer.DealerEffectOwner = dp;

            var customDealers = new List<DamageDealer> { damageDealer };
            Traverse.Create(effectDealer).Field("customDealers").SetValue(customDealers.ToArray());
        }

        // Configures the EffectReceiver based on PvP status
        private void ConfigureEffectReceiver(GameObject playerObject, Player dp) {
            var effectReceiver = playerObject.transform
                .Find("RotateProxy/SpriteHolder/Health(Don'tKey)/DamageReceiver")
                ?.GetComponent<EffectReceiver>();

            if (effectReceiver == null) return;

            effectReceiver.Owner = dp;

            if (isPVP) {
                effectReceiver.effectType = EffectType.BreakableBreaker |
                                            EffectType.ShieldBreak |
                                            EffectType.PostureDecreaseEffect;
            } else {
                effectReceiver.effectType &= ~(EffectType.BreakableBreaker |
                                               EffectType.ShieldBreak |
                                               EffectType.PostureDecreaseEffect);
            }
        }

        void DestroyChildObjects(GameObject parent, params string[] paths) {
            foreach (var path in paths) {
                var child = parent.transform.Find(path);
                if (child != null) {
                    Destroy(child.gameObject);
                }
            }
        }

        private PlayerData CreatePlayerObject(int playerId, Vector3 position) {
            // Instantiate the player object
            var playerObject = Instantiate(
                Player.i.gameObject,
                position,
                Quaternion.identity
            );

            var name = new GameObject("PlayerName");

            if (!displayPlayerName.Value)
                name.SetActive(false);
            var text = name.AddComponent<TextMeshPro>();
            text.text = "Yuki";
            text.fontSize = playerNameSize.Value;
            // Optionally, enable auto sizing if needed
            // text.autoSizeTextContainer = true;

            text.alignment = TextAlignmentOptions.Center;

            Vector3 playerPosition = playerObject.transform.position;

            // Adjust the text's position by adding an offset to the y-axis
            text.transform.position = new Vector3(playerPosition.x, playerPosition.y + 50f, playerPosition.z);

            // Set the parent of the text to the player object
            name.transform.SetParent(playerObject.transform);

            // Ensure no rotation is applied to the text object (make it horizontal)
            text.transform.rotation = Quaternion.identity;  // Reset any rotations

            // Optional: If the text container is too constrained, make sure it's wide enough
            // You can adjust the container's size or enable auto-sizing for the text
            text.rectTransform.sizeDelta = new Vector2(2000f, 50f);  // Adjust width and height based on needs

            Destroy(playerObject.GetComponent<Player>());
            var dp = playerObject.AddComponent<Player>();


            DestroyChildObjects(playerObject,
        "RotateProxy/SpriteHolder/HitBoxManager/Foo",
        "RotateProxy/SpriteHolder/HitBoxManager/FooInit",
        "RotateProxy/SpriteHolder/HitBoxManager/FooExplode");

            AutoAttributeManager.AutoReference(playerObject);
            AutoAttributeManager.AutoReferenceAllChildren(playerObject);

            playerObject.name = $"PlayerObject_{playerId}";

            MakeDamage(playerObject, dp);

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
            

            // Return the player data
            return new PlayerData(playerObject, position, playerId,name);
        }


        private void UpdatePlayerObject(PlayerData playerData, Vector3 position, string animationState, bool isFacingRight) {
            // Update the position of the player object
            var playerObject = playerData.PlayerObject.transform.Find("RotateProxy/SpriteHolder");
            if (playerObject == null) {
                Log.Error("Player object not found!");
                return;
            }

            playerObject.transform.position = position;

            // Update the position of nameObject
            Vector3 nameObjectPosition = position;
            nameObjectPosition.y += 50f;
            playerData.nameObject.transform.position = nameObjectPosition;

            // Update animation state
            var animator = playerObject.GetComponent<Animator>();
            if (animator == null) {
                Log.Error("Animator not found on player object!");
                return;
            }

            if (animationState != currentAnimationState) {
                currentAnimationState = animationState;
                animator.CrossFade(animationState, 0.1f);
            } else if (animator.GetCurrentAnimatorStateInfo(0).IsName(animationState) &&
                       animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f) {
                animator.PlayInFixedTime(animationState, 0, 0f);
            }

            // Update facing direction
            var scale = playerObject.transform.localScale;
            scale.x = Mathf.Abs(scale.x) * (isFacingRight ? 1 : -1);
            playerObject.transform.localScale = scale;
        }


        private void OnDestroy() {
            _harmony.UnpatchSelf();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DisconnectFromServer();
        }
    }
}
