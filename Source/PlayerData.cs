using UnityEngine;

public class PlayerData {
    public GameObject PlayerObject { get; set; }
    public Vector3 Position { get; set; }

    // Constructor to initialize player data
    public PlayerData(GameObject playerObject, Vector3 position) {
        PlayerObject = playerObject;
        Position = position;
    }
}
