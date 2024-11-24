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

        if (__instance.name == "SpriteHolder") {
            Multiplayer.Instance.localAnimationState = stateName;
        }

        // Return true to allow the original method to execute
        return true;
    }

    [HarmonyPatch(typeof(EffectReceiver), "OnHitEnter")]
    [HarmonyPrefix]
    public static bool OnHitEnter(EffectReceiver __instance, EffectHitData data) {


        if (__instance.transform.parent.parent.name.StartsWith("PlayerObject_")) {
            ToastManager.Toast($"{__instance.transform.parent.parent.name.StartsWith("PlayerObject_")} Owner:{__instance.Owner} dealer:{data.dealer.owner}");
            foreach (var x in Multiplayer.Instance.playerObjects.Values) {
                ToastManager.Toast($"id:{x.id} {x.PlayerObject == __instance.transform.parent.parent.gameObject}");
                if (x.PlayerObject == __instance.transform.parent.parent.gameObject)
                    Multiplayer.Instance.SendDecreaseHealth(x.id, data.dealer.FinalValue);
            }
        }
        // Return true to allow the original method to execute
        return true;
    }

}