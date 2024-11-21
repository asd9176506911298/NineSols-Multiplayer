using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System.Net;
using System.Collections.Generic;
using System;
using System.Reflection;
using System.Threading;

namespace Multiplayer;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Multiplayer : BaseUnityPlugin {
    public static Multiplayer Instance { get; private set; }
    private ConfigEntry<bool> isServer = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? netManager;
    private NetDataWriter dataWriter = new();
    GameObject tmpPlayer;

    private int localPlayerid;

    private EventBasedNetListener? listener;
    private NetPeer? serverPeer;

    private bool isConnected;
    public string localAnimationState = "";

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);

        // Config for enabling server/client mode
        isServer = Config.Bind("Network", "IsServer", true, "Set to true to run as server, false for client");

        // Other configurations
        somethingKeyboardShortcut = Config.Bind("General.Something", "Shortcut",
            new KeyboardShortcut(KeyCode.H, KeyCode.LeftControl), "Shortcut to execute");

        KeybindManager.Add(this, InitializeNetworking, () => somethingKeyboardShortcut.Value);

        Instance = this;
        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    void Start() {
        InitializeNetworking();
    }

    private void InitializeNetworking() {
        EventBasedNetListener listener = new EventBasedNetListener();
        NetManager client = new NetManager(listener);
        client.Start();
        client.Connect("localhost" /* host ip or name */, 9050 /* port */, "SomeConnectionKey" /* text key or NetDataWriter */);
        listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) =>
        {
            ToastManager.Toast("123");
            dataReader.Recycle();
        };

        while (!Console.KeyAvailable) {
            client.PollEvents();
            Thread.Sleep(15);
        }

        client.Stop();
    } 

    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
    }
}

