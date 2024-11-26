using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Server {
    internal class Server {
        private static NetManager _server;
        private static NetDataWriter _writer;
        private static readonly ConcurrentDictionary<int, PlayerData> _players = new ();

        private const int Port = 9050;
        private const string ConnectionKey = "SomeConnectionKey";
        private const int MaxConnections = 50;

        static void Main(string[] args) {
            Console.WriteLine("Starting Server...");

            var listener = new EventBasedNetListener();
            _server = new NetManager(listener);

            _server.Start(Port);

            listener.ConnectionRequestEvent += request => {
                if (_server.ConnectedPeersCount < MaxConnections)
                    request.AcceptIfKey(ConnectionKey);
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += peer => HandlePeerConnected(peer);
            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => HandlePeerDisconnected(peer);
            listener.NetworkReceiveEvent += (peer, reader, deliveryMethod, channel) => HandleNetworkReceive(peer, reader);

            RunServerLoop();
        }

        private static void RunServerLoop() {
            while (true) {
                _server.PollEvents();
                Thread.Sleep(15); // Reduce CPU usage
            }

            _server.Stop();
            Console.WriteLine("Server stopped.");
        }

        private static void HandlePeerConnected(NetPeer peer) {
            Console.WriteLine($"Player {peer.Id} connected.");
            AddNewPlayer(peer);
            SendLocalPlayerId(peer);
            BroadcastSystemMessage($"{peer.Id} connected to the server.", peer);
        }

        private static void HandlePeerDisconnected(NetPeer peer) {
            Console.WriteLine($"Player {peer.Id} disconnected.");
            RemovePlayer(peer.Id);
            BroadcastSystemMessage($"{peer.Id} disconnected from the server.", peer);

            NotifyPlayersAboutDisconnection(peer.Id);
        }

        private static void NotifyPlayersAboutDisconnection(int playerId) {
            _writer = new NetDataWriter();
            _writer.Put("DestroyDisconnectObject");
            _writer.Put(playerId);

            foreach (var peer in _server.ConnectedPeerList) {
                peer.Send(_writer, DeliveryMethod.Unreliable);
            }
        }

        private static void HandleNetworkReceive(NetPeer peer, NetPacketReader reader) {
            var messageType = reader.GetString();
            switch (messageType) {
                case "Position":
                    UpdatePlayerPosition(peer, reader);
                    break;
                case "DecreaseHealth":
                    HandleDecreaseHealth(peer, reader);
                    break;
                default:
                    Console.WriteLine($"Unknown message type: {messageType}");
                    break;
            }
            reader.Recycle();
        }

        private static void HandleDecreaseHealth(NetPeer peer, NetPacketReader reader) {
            var playerId = reader.GetInt();
            var damageValue = reader.GetFloat();

            Console.WriteLine($"DecreaseHealth - Player: {playerId}, Damage: {damageValue}");

            if (_players.ContainsKey(playerId)) {
                BroadcastHealthUpdate(playerId, damageValue);
            }
        }

        private static void BroadcastHealthUpdate(int playerId, float damageValue) {
            _writer = new NetDataWriter();
            _writer.Put("DecreaseHealth");
            _writer.Put(playerId);
            _writer.Put(damageValue);

            foreach (var peer in _server.ConnectedPeerList) {
                peer.Send(_writer, DeliveryMethod.Unreliable);
            }
        }

        private static void AddNewPlayer(NetPeer peer) {
            var newPlayer = new PlayerData {
                PlayerId = peer.Id,
                Peer = peer
            };

            _players[peer.Id] = newPlayer;
            Console.WriteLine($"Added new player: {peer.Id}");
        }

        private static void RemovePlayer(int playerId) {
            _players.TryRemove(playerId, out _);
        }

        private static void SendLocalPlayerId(NetPeer peer) {
            _writer = new NetDataWriter();
            _writer.Put("localPlayerId");
            _writer.Put(peer.Id);
            peer.Send(_writer, DeliveryMethod.Unreliable);
        }

        private static void BroadcastSystemMessage(string message, NetPeer excludePeer) {
            _writer = new NetDataWriter();
            _writer.Put(message);

            foreach (var peer in _server.ConnectedPeerList) {
                if (peer != excludePeer) {
                    peer.Send(_writer, DeliveryMethod.Unreliable);
                }
            }
        }

        private static void UpdatePlayerPosition(NetPeer peer, NetPacketReader reader) {
            var playerId = peer.Id;
            var x = reader.GetFloat();
            var y = reader.GetFloat();
            var z = reader.GetFloat();
            var animationState = reader.GetString();
            var _isFacingRight = reader.GetBool();

            if (_players.TryGetValue(playerId, out var player)) {
                player.x = x;
                player.y = y;
                player.z = z;
                player.AnimationState = animationState;
                player.isFacingRight = _isFacingRight;

                BroadcastPlayerPositions(peer);
            }
        }

        private static void BroadcastPlayerPositions(NetPeer excludePeer) {
            foreach (var peer in _server.ConnectedPeerList) {
                if (peer == excludePeer) continue;

                foreach (var player in _players.Values) {
                    _writer = new NetDataWriter();
                    _writer.Put("Position");
                    _writer.Put(player.PlayerId);
                    _writer.Put(player.x);
                    _writer.Put(player.y);
                    _writer.Put(player.z);
                    _writer.Put(player.AnimationState);
                    _writer.Put(player.isFacingRight);
                    peer.Send(_writer, DeliveryMethod.Unreliable);
                }
            }
        }
    }
}
