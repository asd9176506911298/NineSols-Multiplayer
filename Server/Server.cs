using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Threading;

namespace Server {
    internal class Server {
        static NetManager server;
        static NetDataWriter writer;

        // Dictionary to store player data by their ID
        static Dictionary<int, PlayerData> players = new Dictionary<int, PlayerData>();

        static void Main(string[] args) {
            Console.WriteLine("Starting Server...");

            EventBasedNetListener listener = new EventBasedNetListener();
            server = new NetManager(listener);
            writer = new NetDataWriter();

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

                writer.Reset();
                writer.Put("DestoryDisconnectObject");
                writer.Put(peer.Id);

                foreach (var p in server.ConnectedPeerList) {
                    if (p != peer)
                        p.Send(writer, DeliveryMethod.ReliableOrdered);
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
        }

        static void RemovePlayer(int playerId) {
            players.Remove(playerId);
        }

        static void SendLocalPlayerId(NetPeer peer) {
            writer.Reset();
            writer.Put("localPlayerId");
            writer.Put(peer.Id);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        static void BroadcastMessage(string message, NetPeer excludePeer) {
            writer.Reset();
            writer.Put(message);

            foreach (var peer in server.ConnectedPeerList) {
                if (peer != excludePeer)
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);
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
                    writer.Reset();
                    writer.Put("Position");
                    writer.Put(player.PlayerId);
                    writer.Put(player.x);
                    writer.Put(player.y);
                    writer.Put(player.z);
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);
                }
            }
        }
    }


}
