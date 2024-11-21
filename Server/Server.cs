using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Server {
    internal class Server {
        static NetManager server = null;
        static NetDataWriter writer = null;

        // Dictionary to store player data by their NetPeer (you could also use PlayerId if needed)
        static Dictionary<int, PlayerData> players = new Dictionary<int, PlayerData>();

        static void Main(string[] args) {
            Console.WriteLine("Hello World");

            EventBasedNetListener listener = new EventBasedNetListener();
            server = new NetManager(listener);
            writer = new NetDataWriter();
            server.Start(9050 /* port */);

            listener.ConnectionRequestEvent += request => {
                if (server.ConnectedPeersCount < 10 /* max connections */)
                    request.AcceptIfKey("SomeConnectionKey");
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += fromPeer => {
                Console.WriteLine("We got connection: {0}", fromPeer);  // Show peer ip


                // Create and add player data
                PlayerData newPlayer = new PlayerData {
                    PlayerId = fromPeer.Id,
                    PlayerName = $"Player{fromPeer.Id}", // Or retrieve player name if applicable
                    Peer = fromPeer,
                    x = 0f,
                    y = 0f,
                    z = 0f
                };

                players[fromPeer.Id] = newPlayer;  // Save player data using their NetPeer ID as the key
                writer.Reset();
                writer.Put("localPlayerId");  // Put some string
                writer.Put(fromPeer.Id);  // Put some string
                fromPeer.Send(writer, DeliveryMethod.ReliableOrdered);  // Send with reliability

                writer.Reset();
                writer.Put($"Player {fromPeer.Id} Connect to Server");  // Put some string
                BoardCastToClients(fromPeer);
            };

            listener.PeerDisconnectedEvent += (fromPeer, DisconnectInfo) => {
                Console.WriteLine("We got Disconnect: {0}", fromPeer);  // Show peer ip
                writer.Reset();
                writer.Put($"Player {fromPeer.Id} DisConnected Server");  // Put some string

                // Remove player data when disconnected
                players.Remove(fromPeer.Id);

                BoardCastToClients(fromPeer);
            };

            listener.NetworkReceiveEvent += (fromPeer, dataReader, deliveryMethod, channel) => {
                string messageType = dataReader.GetString();
                switch (messageType) {
                    case "Position":
                        HandlePositionUpdate(fromPeer, dataReader);
                        break;
                    default:
                        Console.WriteLine($"Unknown message type: {messageType}");
                        break;
                }
                dataReader.Recycle();
            };

            while (!Console.KeyAvailable) {
                server.PollEvents();
                Thread.Sleep(15);
            }
            server.Stop();
        }

        static void BoardCastToClients(NetPeer fromPeer) {
            foreach (var peer in server.ConnectedPeerList) {
                if (peer != fromPeer)
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);  // Send with reliability
            }
        }

        // Broadcast player positions to all clients
        private static void BroadcastPlayerPositions() {
            foreach (var peer in server.ConnectedPeerList) {
                

                // Send all player positions to each client
                foreach (var player in players.Values) {
                    writer.Reset();
                    writer.Put("Position");  // Send message type
                    writer.Put(player.PlayerId);  // Send player ID
                    writer.Put(player.x);  // Send player X position
                    writer.Put(player.y);  // Send player Y position
                    writer.Put(player.z);  // Send player Z position
                    peer.Send(writer, DeliveryMethod.ReliableOrdered);  // Send to each peer
                }
            }
        }


        // Method to handle position updates
        private static void HandlePositionUpdate(NetPeer fromPeer, NetDataReader dataReader) {
            int playerId = fromPeer.Id;  // Get the player ID from the peer
            // Read the new position from the data
            float x = dataReader.GetFloat();
            float y = dataReader.GetFloat();
            float z = dataReader.GetFloat();
            // Update the player position in the dictionary
            if (!players.ContainsKey(playerId)) {
                PlayerData player = players[playerId]; 
            }
            players[playerId].x = x;
            players[playerId].y = y;
            players[playerId].z = z;
            players[playerId].PlayerId = playerId;

            BroadcastPlayerPositions();
            Console.WriteLine($"Player {playerId} position updated: {x}, {y}, {z}");
        }


    }
}
