﻿using UnityEngine;

public class PlayerData {
    public GameObject PlayerObject { get; set; }
    public Vector3 Position { get; set; }
    public string AnimationState { get; set; }
    public bool isFacingRight { get; set; }
    public int id { get; set; }
    public string name { get; set; }

    public PlayerData(GameObject obj, Vector3 pos, int id) {
        PlayerObject = obj;
        Position = pos;
        this.id = id;
    }

    public PlayerData(int id,string name) {
        this.id = id;
        this.name = name;
    }

    public PlayerData() { }
}