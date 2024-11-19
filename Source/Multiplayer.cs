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

namespace Multiplayer;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class Multiplayer : BaseUnityPlugin {
    private ConfigEntry<bool> isServer = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;
    private NetManager? netManager;
    private NetDataWriter dataWriter = new();

    private EventBasedNetListener? listener;
    private NetPeer? serverPeer;

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
        KeybindManager.Add(this, DisconnectFromServer, () => new KeyboardShortcut(KeyCode.D, KeyCode.LeftControl));
        KeybindManager.Add(this, TestMethod, () => new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));

        Log.Info($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void InitializeNetworking() {
        listener = new EventBasedNetListener();
        netManager = new NetManager(listener) { AutoRecycle = true };

        if (isServer.Value) {
            ToastManager.Toast("Start Server");
            // Start server on port 9050
            netManager.Start(9050);
            Log.Info("Server started on port 9050.");

            // Handle connection requests (e.g., accept up to 10 clients)
            listener.ConnectionRequestEvent += request => {
                if (netManager.ConnectedPeersCount < 10) // Max players
                    request.AcceptIfKey("game_key");
                else
                    request.Reject();
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                ToastManager.Toast($"{peer} {disconnectInfo}");
                peer?.Disconnect();
            };

            listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
                ToastManager.Toast($"{fromPeer} {dataReader.GetFloat()}");
            };

            // Handle incoming messages from clients
            listener.PeerConnectedEvent += peer => {
                ToastManager.Toast($"Kirito Connected {peer}");
                dataWriter.Reset();
                dataWriter.Put("Welcome Kirito Enter Server");
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
                dataWriter.Reset();
                dataWriter.Put(1.2f);
                peer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
            };
        } else {
            // Start client and connect to server (example: localhost)
            ToastManager.Toast("ConnectToServer");
            ConnectToServer();
        }
    }

    void ConnectToServer() {
        listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) =>
        {
            //ToastManager.Toast($"Receive From Server:{dataReader.GetString()}");
            ToastManager.Toast($"Receive From Server:{dataReader.GetFloat()}");
        };
        netManager.Start();
        netManager.Connect("localhost", 9050, "game_key");

    }

    void TestMethod() {
        dataWriter.Reset();
        dataWriter.Put(1.2f);
        netManager.FirstPeer.Send(dataWriter, DeliveryMethod.ReliableOrdered);
    }

    void DisconnectFromServer() {
        if (netManager?.IsRunning == true) {
            var peer = netManager.FirstPeer;
            peer?.Disconnect();
            Log.Info("Disconnected from server.");
            ToastManager.Toast("Disconnected from server.");
        }
    }

    private void Update() {
        netManager?.PollEvents();
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
        netManager?.Stop();
        Log.Info("Networking stopped.");
    }
}