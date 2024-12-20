using UnityEngine;

public class EnemyData {
    public GameObject EnemyObject { get; set; } // Holds the GameObject representing the enemy
    public string guid { get; set; }           // Unique identifier for the enemy

    public EnemyData(GameObject obj, string guid) {
        EnemyObject = obj;
        this.guid = guid;
    }
}
