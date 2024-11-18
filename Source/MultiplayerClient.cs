using UnityEngine;
using System.Net.Sockets;
using System.Text;
using System;

public class MultiplayerClient : MonoBehaviour {
    private TcpClient? tcpClient;
    private NetworkStream? stream;

    private string serverIp = "127.0.0.1";  // Server IP (localhost for now, change if hosting on a different machine)
    private int serverPort = 7777;          // Server port (same as the one used by the server)

    private void Start() {
        ConnectToServer();
    }

    private void ConnectToServer() {
        try {
            // Create a new TcpClient and connect to the server
            tcpClient = new TcpClient(serverIp, serverPort);
            stream = tcpClient.GetStream();
            Debug.Log("Connected to server!");

            // Optionally send a message to the server after connecting
            SendMessageToServer("Hello, I'm a client!");

            // Start reading data from the server
            ReadMessagesFromServer();
        } catch (Exception ex) {
            Debug.LogError($"Failed to connect to server: {ex.Message}");
        }
    }

    private void SendMessageToServer(string message) {
        if (tcpClient != null && stream != null) {
            byte[] data = Encoding.UTF8.GetBytes(message);
            stream.Write(data, 0, data.Length);
            Debug.Log($"Sent message: {message}");
        }
    }

    private void ReadMessagesFromServer() {
        if (stream == null || tcpClient == null) return;

        byte[] buffer = new byte[1024];
        int bytesRead;

        try {
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0) {
                string receivedMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Debug.Log($"Received from server: {receivedMessage}");
            }
        } catch (Exception ex) {
            Debug.LogError($"Error reading from server: {ex.Message}");
        }
    }


    private void OnApplicationQuit() {
        // Clean up and close the connection when the application quits
        if (tcpClient != null) {
            tcpClient.Close();
            Debug.Log("Disconnected from server.");
        }
    }
}
