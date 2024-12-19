
using Auto.Utils;
using BepInEx;
using BepInEx.Configuration;
using Dialogue;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using NineSolsAPI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UnityEngine.UIElements.StylePropertyAnimationSystem;

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

        private float timeStarted = 0f;  // Time when Enter key was pressed
        private float timeLimit = 0.1f;    // Time after which the input field should be hidden
        private bool isTimerRunning = false; // Flag to track whether the timer is running

        public bool isTexting = false;

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
        private GameObject scrollView;

        private Coroutine disableScrollCoroutine;



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

            //CreateChatCanvas();

            //// Create Chat Log (Scroll View)
            //CreateChatLog();

            //// Create Input Field
            //CreateInputField();

            // Make chat window initially hidden
            //chatCanvas.SetActive(false);
        }

        void CreateChatCanvas() {
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

            RCGLifeCycle.DontDestroyForever(chatCanvas);
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

        GameObject v = null;

        Camera cameraToUse = null;

        void CaptureScreenshot() {
            // Find the camera dynamically if not set in inspector
            if (cameraToUse == null) {
                cameraToUse = GameObject.Find("CameraCore/DockObj/OffsetObj/ShakeObj/SceneCamera").GetComponent<Camera>();
            }

            // Set the desired resolution (3840x2160)
            int width = 15360;
            int height = 8640;

            // Create a RenderTexture with the desired resolution
            RenderTexture rt = new RenderTexture(width, height, 24);
            cameraToUse.targetTexture = rt;

            // Set the background to transparent
            cameraToUse.clearFlags = CameraClearFlags.SolidColor;
            cameraToUse.backgroundColor = new Color(0, 0, 0, 0); // Fully transparent

            // Render the object to the RenderTexture
            RenderTexture.active = rt;
            cameraToUse.Render();

            // Create a Texture2D to read pixels from the RenderTexture
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGBA32, false);
            screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            screenshot.Apply();

            // Convert the texture to PNG
            byte[] bytes = screenshot.EncodeToPNG();

            // Save the PNG to file
            string path = Path.Combine(Application.dataPath, "Screenshot.png");
            File.WriteAllBytes(path, bytes);

            // Clean up
            cameraToUse.targetTexture = null;
            RenderTexture.active = null;
            Destroy(rt);

            Debug.Log("Screenshot saved to: " + path);
        }

        void test2() {
            // Array of player object names
            ToastManager.Toast(":goodtimefrog: Za Warudo");
            CaptureScreenshot();
            //ToastManager.Toast(MonsterManager.Instance.FindClosestMonster().GetComponentsInChildren<SpriteRenderer>());
            //ToastManager.Toast(MonsterManager.Instance.FindClosestMonster().transform.Find("MonsterCore/Animator(Proxy)/Animator/StealthMonster_GiantBlade"));
            //v = Instantiate(MonsterManager.Instance.FindClosestMonster().transform.Find("MonsterCore/Animator(Proxy)/Animator/StealthMonster_GiantBlade")).gameObject;

            //foreach (var x in MonsterManager.Instance.FindClosestMonster().GetComponentsInChildren<SpriteRenderer>()) {
            //    // Check if the component SpriteFlasher exists
            //    SpriteFlasher flasher;
            //    if (x.TryGetComponent<SpriteFlasher>(out flasher)) {
            //        continue;
            //    }

            //    // Skip the "Animator" named object
            //    if (x.name == "Animator")
            //        continue;

            //    // Instantiate the object at the position with an offset
            //    Vector3 offset = new Vector3(100, 0, 0);  // Example offset: 100 units on the x-axis
            //    var i = Instantiate(x.gameObject, x.transform.position + offset, Quaternion.identity);
            //    instantiatedMonsters.Add(i);  // Store reference to the instantiated object

            //    // Modify the SpriteRenderer's alpha transparency
            //    SpriteRenderer spriteRenderer = i.GetComponent<SpriteRenderer>();
            //    Color currentColor = spriteRenderer.color;
            //    currentColor.a = 0.4f;
            //    spriteRenderer.color = currentColor;
            //}

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

            // Check if the chatCanvas already exists, if not, create it
            if (chatCanvas == null) {
                foreach (var chat in Resources.FindObjectsOfTypeAll<Canvas>()) {
                    if (chat.name == "ChatCanvas (RCGLifeCycle)") {
                        chatCanvas = chat.gameObject;
                        //ToastManager.Toast(chat.name); // Log the found canvas
                    }
                }
                // Create Chat Canvas if it doesn't exist
                if (chatCanvas == null) {
                    CreateChatCanvas();
                }
            }

            // Check if the scrollView already exists, if not, create it
            if (scrollView == null) {
                foreach (var log in Resources.FindObjectsOfTypeAll<ScrollRect>()) {
                    if (log.transform.parent.name == "ChatCanvas (RCGLifeCycle)") {
                        scrollView = log.gameObject;
                        ToastManager.Toast(log.name); // Log the found ScrollView
                    }
                }
                // Create Chat Log (Scroll View) if it doesn't exist
                if (scrollView == null) {
                    CreateChatLog();
                }
            }

            // Check if the inputField already exists, if not, create it
            if (inputField == null) {
                foreach (var input in Resources.FindObjectsOfTypeAll<InputField>()) {
                    if (input.transform.parent.name == "ChatCanvas (RCGLifeCycle)") {
                        inputField = input.gameObject;
                        ToastManager.Toast(input.name); // Log the found InputField
                    }
                }
                // Create Input Field if it doesn't exist
                if (inputField == null) {
                    CreateInputField();
                }
            }

        }

        private void OnConnectionStatusChecked(bool success) {
            if (!success) {
                ToastManager.Toast("Connection failed.");
                return;
            }

            if (Player.i == null) return;

            //ConfigurePlayerEffectReceiver();
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

            _client.PollEvents();

            if (v) {
                // Find the target transform
                Transform targetParent = GameObject.Find("A1_S2_GameLevel/Room/Prefab/Gameplay5/EventBinder/LootProvider/General Boss Fight FSM ObjectA1_S2_大劍兵/FSM Animator/LogicRoot/---Boss---/BossShowHealthArea/StealthGameMonster_Samurai_General_Boss Variant/MonsterCore/Animator(Proxy)/Animator/StealthMonster_GiantBlade").transform;

                if (targetParent != null) {
                    // Cache the reference to the main object to avoid redundant calls
                    Transform referenceTransform = GameObject.Find("A1_S2_GameLevel/Room/Prefab/Gameplay5/EventBinder/LootProvider/General Boss Fight FSM ObjectA1_S2_大劍兵/FSM Animator/LogicRoot/---Boss---/BossShowHealthArea/StealthGameMonster_Samurai_General_Boss Variant").transform;

                    if (referenceTransform != null) {
                        // Negate the localScale.x of `referenceTransform` and apply it to `v`
                        Vector3 newScale = v.transform.localScale;
                        newScale.x = -referenceTransform.localScale.x; // Negate the x-axis scale
                        v.transform.localScale = newScale;

                        // Update `v`'s position to match `targetParent`
                        v.transform.position = targetParent.position;

                        

                        // You can proceed to update child objects if needed
                    } else {
                        Log.Warning("Reference transform not found!");
                    }

                    var childSprites = targetParent.GetComponentsInChildren<SpriteRenderer>();
                    var vt = v.GetComponentsInChildren<SpriteRenderer>();


                    foreach (var child in childSprites) {
                        ToastManager.Toast(child.name);
                        //if (child.name.Contains("StealthMonster_GiantBlade"))
                        //    continue;

                        //foreach(var g in vt) {
                        //    g.rotation = child.rotation;
                        //}
                    }
                } else {
                    Log.Warning("Target parent transform not found!");
                }
            }






            // Ensure inputField is not null before accessing
            if (inputField != null) {
                var input = inputField.GetComponent<InputField>(); // Fetch InputField once

                // Handling the Enter key press
                if (Input.GetKeyDown(KeyCode.Return)) {
                    isTexting = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;

                    // Stop coroutine if it exists
                    if (disableScrollCoroutine != null) {
                        StopCoroutine(disableScrollCoroutine);
                        disableScrollCoroutine = null;
                    }

                    // Ensure scrollView and inputField are not null
                    if (scrollView != null && inputField != null && !input.isFocused) {
                        scrollView.SetActive(true);  // Ensure the scroll view is shown
                        inputField.SetActive(true);  // Ensure the input field is visible
                        input.ActivateInputField();  // Focus the input field
                    }

                    // Start counting time when Enter is pressed
                    if (!isTimerRunning) {
                        timeStarted = Time.time;  // Record the time when Enter was pressed
                        isTimerRunning = true;    // Start the timer
                    }

                    // Process message sending if input text is valid
                    string message = input.text.Trim(); // Remove leading/trailing spaces

                    if (!string.IsNullOrWhiteSpace(message)) {
                        // If the message is valid, send it to the chat
                        SendMessageToChat(message); // Call SendMessageToChat with the message
                        input.text = string.Empty;  // Clear the input field after sending
                        if (inputField != null) {
                            inputField.SetActive(false); // Hide the input field after sending the message
                        }
                        if (disableScrollCoroutine != null) {
                            StopCoroutine(disableScrollCoroutine);
                        }
                        disableScrollCoroutine = StartCoroutine(DisableScrollViewAfterDelay(3f)); // Optionally hide the scroll view after a delay
                        isTexting = false;
                    }

                    // Keep the input field focused after processing
                    if (inputField != null) {
                        input.ActivateInputField();
                    }
                }

                // Check for time elapsed after Enter key is pressed
                if (isTimerRunning) {
                    float timeElapsed = Time.time - timeStarted;  // Calculate elapsed time

                    if (timeElapsed >= timeLimit && Input.GetKeyDown(KeyCode.Return)) {
                        // If the specified time has passed, hide the input field
                        if (inputField != null) {
                            inputField.SetActive(false);  // Hide the input field after time limit
                        }
                        isTexting = false;

                        // Stop coroutine if it exists
                        if (disableScrollCoroutine != null) {
                            StopCoroutine(disableScrollCoroutine);
                            disableScrollCoroutine = null;
                        }
                        disableScrollCoroutine = StartCoroutine(DisableScrollViewAfterDelay(3f)); // Optionally hide the scroll view after a delay
                        isTimerRunning = false;  // Stop the timer
                    }
                }

                // Handling the Escape key
                if (Input.GetKeyDown(KeyCode.Escape)) {
                    if (inputField != null) {
                        input.text = string.Empty; // Clear the input field
                        inputField.SetActive(false); // Hide the input field
                    }
                    isTexting = false;

                    // Stop coroutine if it exists
                    if (disableScrollCoroutine != null) {
                        StopCoroutine(disableScrollCoroutine);
                        disableScrollCoroutine = null;
                    }
                    disableScrollCoroutine = StartCoroutine(DisableScrollViewAfterDelay(3f)); // Optionally start the coroutine for scroll view
                }
            }
        }


        // Coroutine to disable the scrollView after a delay
        private IEnumerator DisableScrollViewAfterDelay(float delay) {
            yield return new WaitForSeconds(delay);
            scrollView.SetActive(false);
            disableScrollCoroutine = null; // Reset the coroutine reference
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
            text.fontSize = 12;
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
            placeholder.fontSize = 12;
            placeholder.color = new Color(0.7f, 0.7f, 0.7f, 1); // Gray placeholder color
            placeholder.text = "Enter message...";
            placeholder.alignment = TextAnchor.MiddleLeft;

            input.placeholder = placeholder; // Assign the placeholder component

            inputField.SetActive(false);
        }


        private void CreateChatLog() {
            scrollView = new GameObject("ChatLogScrollView");
            scrollView.transform.localPosition = Vector3.zero;
            scrollView.transform.SetParent(chatCanvas.transform);

            var scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.vertical = true; // Enable vertical scrolling
            scrollRect.horizontal = false; // Disable horizontal scrolling

            var rect = scrollView.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(380, 130); // Adjust size as needed
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(20, 50);
            rect.pivot = new Vector2(0, 0);

            var mask = scrollView.AddComponent<Mask>();
            var image = scrollView.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.3f);

            var content = new GameObject("Content");
            content.transform.SetParent(scrollView.transform);

            var contentRect = content.AddComponent<RectTransform>();
            contentRect.sizeDelta = new Vector2(360, 0);
            contentRect.anchoredPosition = new Vector2(10, 0);

            var verticalLayout = content.AddComponent<VerticalLayoutGroup>();
            verticalLayout.childAlignment = TextAnchor.UpperLeft;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;  // Let messages grow vertically as needed
            verticalLayout.spacing = 5; // Space between messages

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRect;

            chatLog = content;

            scrollView.SetActive(false);
        }


        private void ReceiveMessageToChat(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Ensure the scroll view is active
            scrollView.SetActive(true);

            // Stop any active coroutine to disable the scroll view
            if (disableScrollCoroutine != null) {
                StopCoroutine(disableScrollCoroutine);
                disableScrollCoroutine = null;
            }

            // Create a new GameObject for the chat message
            var messageObj = new GameObject("ChatMessage");
            messageObj.transform.SetParent(chatLog.transform, false);

            // Add RectTransform for layout
            var messageRect = messageObj.AddComponent<RectTransform>();
            messageRect.sizeDelta = new Vector2(0, 0);  // Allow size to adjust dynamically

            // Add Text component for displaying the message
            var text = messageObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 12;
            text.color = Color.white;  // Set the text color
            text.text = message;

            // Enable word wrapping and multi-line behavior
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;

            // Add LayoutElement to allow dynamic height adjustment
            var layoutElement = messageObj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 380;  // Maximum width before wrapping
            layoutElement.flexibleHeight = 0;   // Let the height adjust dynamically
            layoutElement.minHeight = 20;       // Minimum height for single-line messages

            // Force the layout to update after adding the message
            Canvas.ForceUpdateCanvases();

            // Scroll to the bottom of the chat log
            var scrollRect = chatLog.GetComponentInParent<ScrollRect>();
            if (scrollRect != null) {
                Canvas.ForceUpdateCanvases();  // Ensure layout updates are applied
                scrollRect.verticalNormalizedPosition = 0f;  // Scroll to the bottom
            }

            if(!isTexting)
                disableScrollCoroutine = StartCoroutine(DisableScrollViewAfterDelay(3f));
        }


        private void SendMessageToChat(string message) {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Create the GameObject for the chat message
            var messageObj = new GameObject("ChatMessage");
            messageObj.transform.SetParent(chatLog.transform, false);  // Attach to the chatLog container

            // Add RectTransform
            var messageRect = messageObj.AddComponent<RectTransform>();
            messageRect.sizeDelta = new Vector2(0, 0);  // Allow dynamic adjustment based on content

            // Add Text component
            var text = messageObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 12;
            text.color = Color.white;  // Set text color
            text.text = playerName.Value + ": " + message;

            // Enable wrapping and multi-line behavior
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;

            // Add LayoutElement for dynamic layout adjustments
            var layoutElement = messageObj.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 380;  // Max width of the message before wrapping
            layoutElement.flexibleHeight = 0;   // Let height adjust dynamically
            layoutElement.minHeight = 20;       // Minimum height for single-line messages

            // Force the layout to update to account for the new message
            Canvas.ForceUpdateCanvases();

            // Scroll to the bottom to display the latest message
            var scrollRect = chatLog.GetComponentInParent<ScrollRect>();
            if (scrollRect != null) {
                Canvas.ForceUpdateCanvases();  // Ensure layout updates are applied
                scrollRect.verticalNormalizedPosition = 0f;  // Scroll to the bottom
            }

            // Networking logic
            if (_client.FirstPeer == null) {
                ToastManager.Toast("Not connected to server.");
                return;
            }

            _dataWriter.Reset();
            _dataWriter.Put("Chat");
            _dataWriter.Put($"{playerName.Value}: {message}");
            _client.FirstPeer.Send(_dataWriter, DeliveryMethod.Unreliable);
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
                case "Chat":
                    //ToastManager.Toast("Chat");
                    var msg = reader.GetString();

                    ReceiveMessageToChat(msg);
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
