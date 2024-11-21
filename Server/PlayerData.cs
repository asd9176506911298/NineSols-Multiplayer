using LiteNetLib;
using UnityEngine;

public class PlayerData {
    public int PlayerId { get; set; }
    public string PlayerName { get; set; }
    public NetPeer Peer { get; set; }  // Store the NetPeer (connection) object for the player
    public Vector3 pos { get; set; }
}
