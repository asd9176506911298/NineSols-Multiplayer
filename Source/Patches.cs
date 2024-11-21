using HarmonyLib;
using NineSolsAPI;
using System;
using UnityEngine;

namespace Multiplayer;

[HarmonyPatch]
public class Patches {
    //[HarmonyPatch(typeof(Player), nameof(Player.SetStoryWalk))]
    //[HarmonyPrefix]
    //private static bool PatchStoryWalk(ref float walkModifier) {
    //    walkModifier = 1.0f;

    //    return true; // the original method should be executed
    //}

    [HarmonyPatch(typeof(Animator), "Play", new[] { typeof(string), typeof(int), typeof(float) })]
    [HarmonyPrefix]
    public static bool Prefix(Animator __instance, string stateName, int layer, float normalizedTime) {
        // Your custom logic before the original method is called
        //Log.Info($"{__instance.name} {stateName} {layer} {normalizedTime}");
        
        //if(__instance.name == "SpriteHolder") {
        //    Multiplayer.Instance.localAnimationState = stateName;
        //}

        // Return true to allow the original method to execute
        return true;
    }



}