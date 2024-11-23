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
                AddNewPlayer(peer);  // Add the new player to the server's dictionary

                // Send the local player's ID to the new player (so they know their own ID)
                SendLocalPlayerId(peer);

                // Broadcast to all other clients that a new player has connected
                BroadcastNewPlayerToOthers(peer);
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

        // Send the new player’s data to all other clients
        static void BroadcastNewPlayerToOthers(NetPeer newPeer) {
            writer.Reset();
            writer.Put("NewPlayer");
            writer.Put(newPeer.Id);
            writer.Put(0f); // Initial position x
            writer.Put(0f); // Initial position y
            writer.Put(0f); // Initial position z

            foreach (var peer in server.ConnectedPeerList) {
                if (peer != newPeer) {
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);
                }
            }
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
                BroadcastPlayerPositions();
                Console.WriteLine($"Player {playerId} position updated: {x}, {y}, {z}");
            }
        }

        static void BroadcastPlayerPositions() {
            foreach (var peer in server.ConnectedPeerList) {
                writer.Reset();
                writer.Put("Position");
                writer.Put(peer.Id);

                // Check if the peer.Id exists in the players dictionary
                if (players.TryGetValue(peer.Id, out var player)) {
                    writer.Put(player.x);
                    writer.Put(player.y);
                    writer.Put(player.z);
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);
                } else {
                    // Handle the case where the player data does not exist
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
