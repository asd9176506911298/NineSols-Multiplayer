using NineSolsAPI;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System;

public class MultiplayerServer {
    private TcpListener? server;
    private List<TcpClient> clients = new List<TcpClient>();

    public void StartServer(string ip, int port) {
        server = new TcpListener(IPAddress.Parse(ip), port);
        server.Start();
        ToastManager.Toast("Server started on IP: " + ip + " Port: " + port);  // Using ToastManager.Toast

        Thread serverThread = new Thread(ListenForClients);
        serverThread.Start();
    }

    private void ListenForClients() {
        while (true) {
            if (server == null) {
                ToastManager.Toast("Server is not initialized.");
                return;  // Exit the loop if the server is null
            }

            TcpClient client = server.AcceptTcpClient();  // Safe to call now
            clients.Add(client);

            ToastManager.Toast("Client connected: " + client.Client.RemoteEndPoint);  // Using ToastManager.Toast

            Thread clientThread = new Thread(() => HandleClient(client));
            clientThread.Start();
        }
    }


    private void HandleClient(TcpClient client) {
        NetworkStream stream = client.GetStream();

        while (true) {
            try {
                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                ToastManager.Toast("Received: " + message);  // Using ToastManager.Toast

                byte[] response = Encoding.UTF8.GetBytes("Server response: " + message);
                stream.Write(response, 0, response.Length);
            } catch (Exception ex) {
                ToastManager.Toast("Error handling client: " + ex.Message);  // Using ToastManager.Toast
                break;
            }
        }

        client.Close();
    }
}
