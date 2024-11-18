using System;
using System.Net.Sockets;
using System.Text;

public class Client {
    private TcpClient tcpClient;
    private NetworkStream networkStream;
    private string serverIp = "127.0.0.1"; // Replace with the server's IP if not local
    private int serverPort = 7777; // Server port

    public void ConnectToServer() {
        try {
            tcpClient = new TcpClient(serverIp, serverPort);
            networkStream = tcpClient.GetStream();
            Console.WriteLine($"Connected to server at {serverIp}:{serverPort}");

            // Send message to the server
            SendMessage("Hello Server!");

            // Listen for server responses
            ListenForServerResponse();
        } catch (Exception ex) {
            Console.WriteLine($"Error connecting to server: {ex.Message}");
        }
    }

    private void SendMessage(string message) {
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        networkStream.Write(buffer, 0, buffer.Length);
        Console.WriteLine($"Sent: {message}");
    }

    private void ListenForServerResponse() {
        byte[] buffer = new byte[1024];
        while (true) {
            int bytesRead = networkStream.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0) {
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {response}");
            }
        }
    }


    public void CloseConnection() {
        networkStream?.Close();
        tcpClient?.Close();
    }
}
