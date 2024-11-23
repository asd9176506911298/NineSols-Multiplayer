using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Server {
    internal class Server {
        static NetManager server;
        static NetDataWriter writer;

        // Use ConcurrentDictionary for thread-safety
        static ConcurrentDictionary<int, PlayerData> players = new ConcurrentDictionary<int, PlayerData>();

        static void Main(string[] args) {
            Console.WriteLine("Starting Server...");

            EventBasedNetListener listener = new EventBasedNetListener();
            server = new NetManager(listener);

            server.Start(9050); // Start server on port 9050

            listener.ConnectionRequestEvent += request => {
                if (server.ConnectedPeersCount < 10) // Max connections
                    request.AcceptIfKey("SomeConnectionKey");
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += peer => {
                Console.WriteLine($"Player {peer.Id} connected.");
                AddNewPlayer(peer);
                SendLocalPlayerId(peer);
                BroadcastMessage($"{peer.Id} connected to the server.", peer);
            };

            listener.PeerDisconnectedEvent += (peer, disconnectInfo) => {
                Console.WriteLine($"Player {peer.Id} disconnected.");
                RemovePlayer(peer.Id);
                BroadcastMessage($"{peer.Id} disconnected from the server.", peer);

                // Notify others about the disconnection
                writer = new NetDataWriter(); // Use a fresh writer each time
                writer.Put("DestoryDisconnectObject");
                writer.Put(peer.Id);

                foreach (var p in server.ConnectedPeerList) {
                    if (p != peer)
                        p.Send(writer, DeliveryMethod.Unreliable);
                }
            };

            listener.NetworkReceiveEvent += (peer, dataReader, deliveryMethod, channel) => {
                string messageType = dataReader.GetString();
                if (messageType == "Position") {
                    UpdatePlayerPosition(peer, dataReader);
                } else {
                    Console.WriteLine($"Unknown message type: {messageType}");
                }
                dataReader.Recycle();
            };

            while (!Console.KeyAvailable) {
                server.PollEvents();
                Thread.Sleep(15); // Reduce CPU usage
            }

            server.Stop();
            Console.WriteLine("Server stopped.");
        }

        static void AddNewPlayer(NetPeer peer) {
            players[peer.Id] = new PlayerData {
                PlayerId = peer.Id,
                Peer = peer,
                x = 0,
                y = 0,
                z = 0
            };
            Console.WriteLine($"New player added with PlayerId: {peer.Id}");
        }

        static void RemovePlayer(int playerId) {
            players.TryRemove(playerId, out _);
        }

        static void SendLocalPlayerId(NetPeer peer) {
            writer = new NetDataWriter(); // Fresh writer instance for each message
            writer.Put("localPlayerId");
            writer.Put(peer.Id);
            peer.Send(writer, DeliveryMethod.Unreliable);
        }

        static void BroadcastMessage(string message, NetPeer excludePeer) {
            writer = new NetDataWriter(); // Fresh writer instance for each broadcast
            writer.Put(message);

            foreach (var peer in server.ConnectedPeerList) {
                if (peer != excludePeer)
                    peer.Send(writer, DeliveryMethod.Unreliable);
            }
        }

        static void UpdatePlayerPosition(NetPeer peer, NetDataReader dataReader) {
            int playerId = peer.Id;
            float x = dataReader.GetFloat();
            float y = dataReader.GetFloat();
            float z = dataReader.GetFloat();

            if (players.TryGetValue(playerId, out var player)) {
                player.x = x;
                player.y = y;
                player.z = z;
                BroadcastPlayerPositions(peer);
                Console.WriteLine($"Player {playerId} position updated: {x}, {y}, {z}");
            }
        }

        static void BroadcastPlayerPositions(NetPeer fromPeer) {
            foreach (var peer in server.ConnectedPeerList) {
                foreach (var player in players.Values) {
                    if (player.PlayerId != fromPeer.Id) {
                        writer = new NetDataWriter(); // Fresh writer instance for each broadcast
                        writer.Put("Position");
                        writer.Put(player.PlayerId);
                        writer.Put(player.x);
                        writer.Put(player.y);
                        writer.Put(player.z);
                        peer.Send(writer, DeliveryMethod.Unreliable);
                    }
                }
            }
        }
    }

    class PlayerData {
        public int PlayerId { get; set; }
        public NetPeer Peer { get; set; }
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
    }
}
