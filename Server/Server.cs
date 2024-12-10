using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;

namespace Server {
    internal class Server {
        private static NetManager _server;
        private static NetDataWriter _writer;
        private static readonly ConcurrentDictionary<int, PlayerData> _players = new ();

        private const int Port = 9050;
        private const string ConnectionKey = "SomeConnectionKey";
        private const int MaxConnections = 50;

        private static bool _isPvPEnabled = false; // PvP state

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

            Task.Run(() => CommandLoop());

            RunServerLoop();
        }

        private static void CommandLoop() {
            while (true) {
                var command = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(command)) continue;

                // Handle "shutdown" command
                if (command.Equals("stop", StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine("Shutting down server...");

                    _writer = new NetDataWriter();
                    _writer.Put("stop");
                    foreach (var peer in _server.ConnectedPeerList) {
                        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
                    }

                    Task.Delay(1000).Wait(); // Blocks the current thread for 5 
                    _server.Stop();
                    Task.Delay(1000).Wait(); // Blocks the current thread for 5 
                    Environment.Exit(0);
                    break; // Exit the loop
                }
                // Handle "pvp" commands
                else if (command.Equals("pvp 1", StringComparison.OrdinalIgnoreCase)) {
                    EnablePvP(true);
                } else if (command.Equals("pvp 0", StringComparison.OrdinalIgnoreCase)) {
                    EnablePvP(false);
                }
                // Handle "tp" command
                else if (command.StartsWith("tp ", StringComparison.OrdinalIgnoreCase)) {
                    var sceneName = command.Substring(3).Trim();
                    if (!string.IsNullOrEmpty(sceneName)) {
                        _writer = new NetDataWriter();
                        _writer.Put("tp");
                        _writer.Put(sceneName);
                        foreach (var peer in _server.ConnectedPeerList) {
                            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
                        }
                    } else {
                        Console.WriteLine("Invalid scene name. Usage: tp <SceneName>");
                    }
                }
                // Handle "say" command
                else if (command.StartsWith("say ", StringComparison.OrdinalIgnoreCase)) {
                    var message = "Server Owner:" + command.Substring(4).Trim();
                    if (!string.IsNullOrEmpty(message)) {
                        _writer = new NetDataWriter();
                        _writer.Put(message);
                        foreach (var peer in _server.ConnectedPeerList) {
                            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
                        }
                        Console.WriteLine($"Message sent: {message}");
                    } else {
                        Console.WriteLine("Invalid message. Usage: say <message>");
                    }
                }
                // Handle unknown commands
                else {
                    Console.WriteLine($"Unknown command: {command}");
                }
            }
        }




        private static void EnablePvP(bool enable) {
            _isPvPEnabled = enable;
            Console.WriteLine($"PvP has been {(_isPvPEnabled ? "enabled" : "disabled")}!");

            // Notify all players about the PvP state
            _writer = new NetDataWriter();
            _writer.Put("PvPEnabled");
            _writer.Put(enable);

            foreach (var peer in _server.ConnectedPeerList) {
                peer.Send(_writer, DeliveryMethod.ReliableOrdered);
            }
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

            _writer = new NetDataWriter();
            _writer.Put("PvPEnabled");
            _writer.Put(_isPvPEnabled);
            peer.Send(_writer, DeliveryMethod.ReliableOrdered);

            _writer = new NetDataWriter();
            _writer.Put($"Server Player Count:{_server.ConnectedPeersCount}");
            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
        }

        private static void HandlePeerDisconnected(NetPeer peer) {
            Console.WriteLine($"Player {peer.Id} disconnected.");
            RemovePlayer(peer.Id);

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
                case "RecoverableDamage":
                    HandleRecoverableDamage(peer, reader);
                    break;
                case "Join":
                    var name = reader.GetString();
                    _players[peer.Id].name = name;
                    var playersName = "";
                    foreach (var x in _players.Values) {
                        playersName += ", "+ x.name;
                    }
                    Console.WriteLine(playersName);
                    BroadcastSystemMessage($"{name} connected. Player Count:{_server.ConnectedPeersCount}\n{playersName}", peer);
                    break;
                case "Leave":
                    var namee = reader.GetString();
                    BroadcastSystemMessage($"{namee} disconnected. Player Count:{_server.ConnectedPeersCount}", peer);
                    break;
                case "Scene":
                    var scene = reader.GetString();
                    _players[peer.Id].scene = scene;
                    Console.WriteLine($"{_players[peer.Id].name} on {scene}");
                    break;
                case "GetName":
                    var playerId = reader.GetInt();
                    Console.WriteLine(_players[playerId].name);
                    _writer = new NetDataWriter();
                    _writer.Put("GetName");
                    _writer.Put(playerId);
                    _writer.Put(_players[playerId].name);
                    peer.Send(_writer, DeliveryMethod.Unreliable);
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
                BroadcastHealthUpdate("DecreaseHealth", playerId, damageValue);
            }
        }

        private static void HandleRecoverableDamage(NetPeer peer, NetPacketReader reader) {
            var playerId = reader.GetInt();
            var damageValue = reader.GetFloat();

            Console.WriteLine($"RecoverableDamage - Player: {playerId}, InternalDamage: {damageValue}");

            foreach(var x in _players.Values) {
                if(x.PlayerId == playerId) {
                    //BroadcastHealthUpdate("RecoverableDamage", playerId, damageValue);
                    _writer = new NetDataWriter();
                    _writer.Put("RecoverableDamage"); // Specify the update type
                    _writer.Put(playerId);
                    _writer.Put(damageValue);

                    x.Peer.Send(_writer, DeliveryMethod.Unreliable);
                }
            }

               
            
        }

        private static void BroadcastHealthUpdate(string updateType, int playerId, float damageValue) {
            _writer = new NetDataWriter();
            _writer.Put(updateType); // Specify the update type
            _writer.Put(playerId);
            _writer.Put(damageValue);

            foreach (var peer in _server.ConnectedPeerList) {
                peer.Send(_writer, DeliveryMethod.Unreliable);
            }
        }

        private static void AddNewPlayer(NetPeer peer) {
            var newPlayer = new PlayerData {
                PlayerId = peer.Id,
                Peer = peer,
                name = ""
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
                    peer.Send(_writer, DeliveryMethod.ReliableOrdered);
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
                    _writer.Put(player.scene);
                    peer.Send(_writer, DeliveryMethod.Unreliable);
                }
            }
        }
    }
}
