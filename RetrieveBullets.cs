#nullable disable

using HarmonyLib;
using Il2Cpp;
using Il2CppTLD.IntBackedUnit;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RetrieveBullets
{
    public class Main : MelonMod
    {
        private static GearItem cachedBulletPrefab = null;
        private static Dictionary<string, int> bulletHarvestedData = new Dictionary<string, int>();

        public override void OnInitializeMelon()
        {
            MelonCoroutines.Start(PreloadDamagedBulletCoroutine());
            MelonLogger.Msg("[RetrieveBulletsMod] Initialized and waiting for game events.");
        }

        // -------- Prefab preload --------
        private static bool hasLoggedMissingGearItem = false;
        private static bool hasLoggedLoadFailure = false;

        private static IEnumerator PreloadDamagedBulletCoroutine()
        {
            if (cachedBulletPrefab != null)
                yield break;

            MelonLogger.Msg("[RetrieveBulletsMod] Waiting for GEAR_DamagedBullet prefab to become available...");

            while (cachedBulletPrefab == null)
            {
                AsyncOperationHandle<GameObject> handle = Addressables.LoadAssetAsync<GameObject>("GEAR_DamagedBullet");
                yield return handle;

                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                {
                    GameObject prefab = handle.Result;
                    GearItem gearItem = prefab.GetComponent<GearItem>();

                    if (gearItem != null)
                    {
                        cachedBulletPrefab = gearItem;
                        MelonLogger.Msg("[RetrieveBulletsMod] Cached damaged bullet prefab successfully.");
                        yield break;
                    }
                    else
                    {
                        if (!hasLoggedMissingGearItem)
                        {
                            MelonLogger.Warning("[RetrieveBulletsMod] Loaded prefab has no GearItem component. Retrying...");
                            hasLoggedMissingGearItem = true;
                        }
                    }
                }
                else
                {
                    if (!hasLoggedLoadFailure)
                    {
                        MelonLogger.Msg("[RetrieveBulletsMod] Failed to load GEAR_DamagedBullet. Retrying...");
                        hasLoggedLoadFailure = true;
                    }
                }

                yield return new WaitForSeconds(1f);
            }
        }

        // -------- Runtime bullet hit --------
        private static void RegisterBulletHit(BaseAi aiInstance)
        {
            try
            {
                if (aiInstance == null) return;

                PlayerManager pm = GameManager.GetPlayerManagerComponent();
                if (pm == null || pm.m_ItemInHands == null) return;

                string held = pm.m_ItemInHands.name?.ToLowerInvariant() ?? string.Empty;
                if (!held.Contains("rifle") && !held.Contains("revolver")) return;

                string guid = ObjectGuid.GetGuidFromGameObject(aiInstance.gameObject);
                if (string.IsNullOrEmpty(guid)) return;

                // increment using shared cache
                SerializationPatches.IncrementHit(guid);

                MelonLogger.Msg($"[RetrieveBulletsMod] Hit {aiInstance.name} GUID={guid}");

                SerializationPatches.SaveToModData();
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[RetrieveBulletsMod] RegisterBulletHit error: " + ex);
            }
        }

        // -------- Harmony patches --------
        [HarmonyPatch(typeof(BaseAi), nameof(BaseAi.ApplyDamage),
            new Type[] { typeof(float), typeof(float), typeof(DamageSource), typeof(string) })]
        private static class Patch_ApplyDamage_4
        {
            [HarmonyPostfix]
            private static void Postfix(BaseAi __instance, float damage, float bleedOutMintues, DamageSource damageSource, string collider)
            {
                if (damageSource == DamageSource.Player)
                    RegisterBulletHit(__instance);
            }
        }

        [HarmonyPatch(typeof(Panel_BodyHarvest), nameof(Panel_BodyHarvest.HarvestSuccessful))]
        private static class Patch_HarvestSuccessful
        {
            [HarmonyPostfix]
            private static void Postfix(Panel_BodyHarvest __instance)
            {
                if (__instance?.m_BodyHarvest == null) return;
                string guid = ObjectGuid.GetGuidFromGameObject(__instance.m_BodyHarvest.gameObject);
                if (string.IsNullOrEmpty(guid)) return;

                if (!bulletHarvestedData.ContainsKey(guid))
                    bulletHarvestedData[guid] = 0;

                SerializationPatches.CleanupExpired();
                SerializationPatches.SaveToModData();
            }
        }

        [HarmonyPatch(typeof(Panel_BodyHarvest), nameof(Panel_BodyHarvest.TransferGearFromCarcassToInventoryByWeight),
            new Type[] { typeof(GameObject), typeof(ItemWeight) })]
        private static class Patch_TransferGear
        {
            [HarmonyPostfix]
            private static void Postfix(Panel_BodyHarvest __instance, GameObject prefab, ItemWeight weightToAdd)
            {
                try
                {
                    if (__instance?.m_BodyHarvest == null || prefab == null) return;

                    GearItem gear = prefab.GetComponent<GearItem>();
                    if (gear == null || !gear.name.ToLowerInvariant().Contains("meat")) return;

                    string guid = ObjectGuid.GetGuidFromGameObject(__instance.m_BodyHarvest.gameObject);
                    if (string.IsNullOrEmpty(guid)) return;

                    BulletHitInfo info;
                    if (!SerializationPatches.TryGetInfo(guid, out info) || info.Count <= 0)
                        return;

                    int harvestedSoFar = bulletHarvestedData.TryGetValue(guid, out int harvested) ? harvested : 0;
                    int newlyHarvestedKg = (int)Math.Floor(weightToAdd.ToQuantity(1f));

                    int bulletsLeft = info.Count - harvestedSoFar;
                    int bulletsToGive = Math.Min(newlyHarvestedKg, bulletsLeft);
                    if (bulletsToGive <= 0) return;

                    if (cachedBulletPrefab == null)
                    {
                        MelonLogger.Warning("[RetrieveBulletsMod] Damaged bullet prefab not ready; skipping.");
                        return;
                    }

                    PlayerManager pm = GameManager.GetPlayerManagerComponent();
                    for (int i = 0; i < bulletsToGive; i++)
                        pm.InstantiateItemInPlayerInventory(cachedBulletPrefab, 1, 1f, PlayerManager.InventoryInstantiateFlags.None);

                    harvestedSoFar += bulletsToGive;
                    bulletHarvestedData[guid] = harvestedSoFar;

                    if (harvestedSoFar >= info.Count)
                    {
                        SerializationPatches.RemoveEntry(guid);
                        bulletHarvestedData.Remove(guid);
                        MelonLogger.Msg($"[RetrieveBulletsMod] All bullets retrieved for GUID={guid}, entry removed.");
                    }

                    HUDMessage.AddMessage($"{bulletsToGive} Damaged Bullets harvested");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error("[RetrieveBulletsMod] TransferGear_Postfix error: " + ex);
                }
            }
        }
    }
}