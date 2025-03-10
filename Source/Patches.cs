﻿using Auto.Utils;
using ChartUtil;
using HarmonyLib;
using InControl;
using InputExtension;
using LiteNetLib;
using NineSolsAPI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Multiplayer {
    [HarmonyPatch]
    public class Patches {
        [HarmonyPatch(typeof(Animator), "Play", new[] { typeof(string), typeof(int), typeof(float) })]
        [HarmonyPrefix]
        public static bool Prefix(Animator __instance, string stateName, int layer, float normalizedTime) {
            
            if (__instance.name == "SpriteHolder" && Multiplayer.Instance.localAnimationState != stateName) {
                Multiplayer.Instance.localAnimationState = stateName;
            }

            if (MonsterManager.Instance?.FindClosestMonster() != null) {
                var closestMonster = MonsterManager.Instance.FindClosestMonster();
                if (closestMonster != null &&
                    __instance?.transform?.parent?.parent?.parent?.name == closestMonster.name) {

                    Multiplayer.Instance.enemyAnimationState = stateName;

                    // Uncomment if needed
                    // ToastManager.Toast(normalizedTime);
                    // ToastManager.Toast($"same {stateName}");
                    // Multiplayer.Instance.SendEnemy(__instance.transform.parent.parent.parent.name, stateName, __instance.transform.position);
                }
            }



            return true; // Allow the original method to execute
        }

        [HarmonyPatch(typeof(InControlManager), "Update")]
        [HarmonyPrefix]
        public static bool HookUpdate(InControlManager __instance) {
            if (Multiplayer.Instance.isTexting) {
                return false;
            }

            return true; // Allow the original method to execute
        }

        [HarmonyPatch(typeof(RCGInput), "SetCursorVisible")]
        [HarmonyPrefix]
        public static bool HookSetCursorVisible(ref bool visible) {
            if (Multiplayer.Instance.isTexting)
                return false;

            return true; // Allow the original method to execute
        }

        //[HarmonyPatch(typeof(EffectReceiver), "OnHitEnter")]
        //[HarmonyPrefix]
        //public static bool OnHitEnter(EffectReceiver __instance, EffectHitData data) {
        //    if (__instance.transform.parent.parent.name.StartsWith("PlayerObject_")) {
        //        foreach (var playerData in Multiplayer.Instance._playerObjects.Values) {
        //            if (playerData.PlayerObject == __instance.transform.parent.parent.gameObject && Multiplayer.Instance.isPVP) {
        //                Multiplayer.Instance.SendDecreaseHealth(playerData.id, data.dealer.FinalValue);
        //            }
        //        }
        //    }

        //    return true; // Allow the original method to execute
        //}

        //[HarmonyPatch(typeof(ParriableAttackEffect), "EffectCountered")]
        //[HarmonyPostfix]
        //public static void hookEffectCountered(ParriableAttackEffect __instance, PlayerParryState parryState, EffectHitData hitData) {
        //    ToastManager.Toast(hitData.dealer.transform.root.gameObject);

        //    GameObject rootObject = hitData.dealer.transform.root.gameObject;

        //    // Find the PlayerData whose PlayerObject matches the rootObject
        //    PlayerData matchingPlayerData = null;

        //    foreach (var playerDataEntry in Multiplayer.Instance._playerObjects.Values) {
        //        if (playerDataEntry.PlayerObject == rootObject) {
        //            matchingPlayerData = playerDataEntry;
        //            break;
        //        }
        //    }

        //    if (matchingPlayerData != null) {
        //        ToastManager.Toast($"Found PlayerData: ID = {matchingPlayerData.id}, Name = {matchingPlayerData.name}");
        //    } else {
        //        ToastManager.Toast("PlayerData not found for the given GameObject.");
        //    }
        //    //ToastManager.Toast($"result:{__result}");
        //    Multiplayer.Instance.SendRecoverableDamage(matchingPlayerData.id, hitData.dealer.FinalValue);
        //}

        //[HarmonyPatch(typeof(ParryCounterDefenseState), "Parried")]
        //[HarmonyPostfix]
        //public static void HookParried(ParryCounterDefenseState __instance, EffectHitData hitData, ParryParam param, DamageDealer bindDamage, ref bool __result) {

        //    if (__result) {
        //        ToastManager.Toast($"instance:{__instance.transform.parent.root.gameObject}");
        //        ToastManager.Toast(bindDamage.transform.parent.root.gameObject);

        //        GameObject rootObject = bindDamage.transform.parent.root.gameObject;

        //        // Find the PlayerData whose PlayerObject matches the rootObject
        //        PlayerData matchingPlayerData = null;

        //        foreach (var playerDataEntry in Multiplayer.Instance._playerObjects.Values) {
        //            if (playerDataEntry.PlayerObject == rootObject) {
        //                matchingPlayerData = playerDataEntry;
        //                break;
        //            }
        //        }

        //        if (matchingPlayerData != null) {
        //            ToastManager.Toast($"Found PlayerData: ID = {matchingPlayerData.id}, Name = {matchingPlayerData.name}");
        //        } else {
        //            ToastManager.Toast("PlayerData not found for the given GameObject.");
        //        }
        //        //ToastManager.Toast($"result:{__result}");
        //        Multiplayer.Instance.SendRecoverableDamage(matchingPlayerData.id, hitData.dealer.FinalValue);
        //    }
        //}
    }
}
