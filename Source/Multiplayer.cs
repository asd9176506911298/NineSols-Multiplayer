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
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? client;
    private NetDataWriter dataWriter = new();


    private EventBasedNetListener? listener;

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(Multiplayer).Assembly);


        KeybindManager.Add(this, ConnectToServer, () => new KeyboardShortcut(KeyCode.X));
        KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.C));

        Instance = this;
        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    void DisconnectFromServer() {
        var peer = client.FirstPeer;
        peer?.Disconnect();
        ToastManager.Toast($"Disconnected from server. {peer.Id}");
    }

    private void ConnectToServer() {
        ToastManager.Toast("ConnectToServer");
        listener = new EventBasedNetListener();
        client = new NetManager(listener) { AutoRecycle = true };
        client.Start();
        client.Connect("localhost", 9050 , "SomeConnectionKey");
        listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) =>
        {
            ToastManager.Toast($"Recive From Sever {dataReader.GetString()}");
            dataReader.Recycle();
        };
         
        listener.PeerDisconnectedEvent += (peer,DisconnectInfo) => {
            ToastManager.Toast($"Connect {peer}");
        };
    } 

    void Update() {
        client.PollEvents();
        Thread.Sleep(15);
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
        client?.Stop();
    }
}

