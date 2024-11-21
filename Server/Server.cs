using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Threading;

namespace Server
{
    internal class Server
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World");

            EventBasedNetListener listener = new EventBasedNetListener();
            NetManager server = new NetManager(listener);
            server.Start(9050 /* port */);

            listener.ConnectionRequestEvent += request =>
            {
                if (server.ConnectedPeersCount < 10 /* max connections */)
                    request.AcceptIfKey("SomeConnectionKey");
                else
                    request.Reject();
            };

            listener.PeerConnectedEvent += fromPeer =>
            {
                Console.WriteLine("We got connection: {0}", fromPeer);  // Show peer ip
                NetDataWriter writer = new NetDataWriter();         // Create writer class
                writer.Put($"Player  Connect to Server");                        // Put some string

                foreach(var peer in server.ConnectedPeerList) {
                    if(peer != fromPeer)
                        peer.Send(writer, DeliveryMethod.ReliableOrdered);  // Send with reliability
                }
            };

            listener.PeerDisconnectedEvent += (peer, DisconnectInfo) => {
                Console.WriteLine($"Disconnect{peer}");  // Show peer ip
            };


            while (!Console.KeyAvailable) {
                server.PollEvents();
                Thread.Sleep(15);
            }
            server.Stop();
        }
    }
}
