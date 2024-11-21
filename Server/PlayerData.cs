using LiteNetLib;


public class PlayerData {
    public int PlayerId { get; set; }
    public string PlayerName { get; set; }
    public NetPeer Peer { get; set; }  // Store the NetPeer (connection) object for the player
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}
