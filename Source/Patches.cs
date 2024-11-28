using HarmonyLib;
using NineSolsAPI;
using UnityEngine;

namespace Multiplayer {
    [HarmonyPatch]
    public class Patches {
        [HarmonyPatch(typeof(Animator), "Play", new[] { typeof(string), typeof(int), typeof(float) })]
        [HarmonyPrefix]
        public static bool Prefix(Animator __instance, string stateName, int layer, float normalizedTime) {
            if (__instance.name == "SpriteHolder" && Multiplayer.Instance.localAnimationState != stateName) {
                Multiplayer.Instance.localAnimationState = stateName;
            }

            return true; // Allow the original method to execute
        }

        [HarmonyPatch(typeof(EffectReceiver), "OnHitEnter")]
        [HarmonyPrefix]
        public static bool OnHitEnter(EffectReceiver __instance, EffectHitData data) {
            if (__instance.transform.parent.parent.name.StartsWith("PlayerObject_")) {
                foreach (var playerData in Multiplayer.Instance._playerObjects.Values) {
                    if (playerData.PlayerObject == __instance.transform.parent.parent.gameObject && Multiplayer.Instance.isPVP) {
                        Multiplayer.Instance.SendDecreaseHealth(playerData.id, data.dealer.FinalValue);
                    }
                }
            }

            return true; // Allow the original method to execute
        }
    }
}
