using UnityEngine;

public class PlayerData {
    public GameObject PlayerObject;
    public Vector3 Position;

    public PlayerData(GameObject obj, Vector3 pos) {
        PlayerObject = obj;
        Position = pos;
    }
}