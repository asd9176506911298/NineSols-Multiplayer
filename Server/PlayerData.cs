using LiteNetLib;

class PlayerData {
    public int PlayerId { get; set; }
    public NetPeer Peer { get; set; }
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
    public string AnimationState { get; set; }
    public bool isFacingRight { get; set; }
    public string name { get; set; }
    public string scene { get; set; }
}