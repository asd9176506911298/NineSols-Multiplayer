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

            listener.PeerConnectedEvent += peer =>
            {
                Console.WriteLine("We got connection: {0}", peer);  // Show peer ip
                NetDataWriter writer = new NetDataWriter();         // Create writer class
                writer.Put("Hello client!");                        // Put some string
                peer.Send(writer, DeliveryMethod.ReliableOrdered);  // Send with reliability
            };

            while (!Console.KeyAvailable) {
                server.PollEvents();
                Thread.Sleep(15);
            }
            server.Stop();
        }
    }
}
