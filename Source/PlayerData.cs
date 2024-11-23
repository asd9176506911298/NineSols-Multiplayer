using UnityEngine;

public class PlayerData {
    public GameObject PlayerObject { get; set; }
    public Vector3 Position { get; set; }
    public string AnimationState { get; set; }
    public bool isFacingRight { get; set; }

    public PlayerData(GameObject obj, Vector3 pos) {
        PlayerObject = obj;
        Position = pos;
    }
}