using UnityEngine;

public class PlayerData {
    public GameObject PlayerObject { get; set; }
    public Vector3 Position { get; set; }
    public string AnimationState { get; set; }
    public bool isFacingRight { get; set; }
    public int id { get; set; }
    public string name { get; set; }
    public GameObject nameObject;

    public PlayerData(GameObject obj, Vector3 pos, int id, GameObject nameObject) {
        PlayerObject = obj;
        Position = pos;
        this.id = id;
        this.nameObject = nameObject;
    }

    public PlayerData(int id,string name) {
        this.id = id;
        this.name = name;
    }

    public PlayerData() { }
}