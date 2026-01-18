#nullable disable

using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
using Il2Cpp;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RetrieveBullets
{
    internal static class SerializationPatches
    {
        private static readonly MelonLogger.Instance logger = new MelonLogger.Instance("RetrieveBullets");
        private static readonly SaveDataManager sdm = new SaveDataManager();

        private static readonly object cacheLock = new object();
        private static Dictionary<string, BulletHitInfo> cache = new Dictionary<string, BulletHitInfo>();

        private const string MODDATA_KEY = "RetrieveBullets";
        private const float expirationHours = 8f * 24f; // 8 nap ingame

        // ================================================================
        // Thread-safe increment + instant ModData save
        // ================================================================
        public static void IncrementHit(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return;

            lock (cacheLock)
            {
                if (!cache.TryGetValue(guid, out BulletHitInfo? info))
                {
                    info = new BulletHitInfo();
                    cache[guid] = info;
                }
                info.Count++;
            }

            SaveToModData();
        }

        // ================================================================
        // Thread-safe TryGet / Remove
        // ================================================================
        public static bool TryGetInfo(string guid, out BulletHitInfo infoCopy)
        {
            infoCopy = null;
            if (string.IsNullOrEmpty(guid)) return false;

            lock (cacheLock)
            {
                BulletHitInfo info;
                if (!cache.TryGetValue(guid, out info))
                    return false;

                infoCopy = new BulletHitInfo
                {
                    Count = info.Count,
                    CreatedHours = info.CreatedHours
                };
                return true;
            }
        }

        public static bool RemoveEntry(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            lock (cacheLock) return cache.Remove(guid);
        }

        // ================================================================
        // LOAD (POSTFIX)
        // ================================================================
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadSaveGameSlot), new Type[] { typeof(string), typeof(int) })]
        private static class GameManagerPatches_LoadSaveGameSlot
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                logger.Msg("[RetrieveBullets] OnLoadGame -> attempting to load ModData");

                string json = null;
                try { json = sdm.Load(MODDATA_KEY); }
                catch (Exception ex)
                {
                    logger.Error("ModData load failed: " + ex);
                    lock (cacheLock) { cache.Clear(); }
                    return;
                }

                if (string.IsNullOrEmpty(json))
                {
                    lock (cacheLock) { cache.Clear(); }
                    return;
                }

                try
                {
                    Dictionary<string, BulletHitInfo> loaded = JsonConvert.DeserializeObject<Dictionary<string, BulletHitInfo>>(json);
                    lock (cacheLock)
                    {
                        cache = loaded ?? new Dictionary<string, BulletHitInfo>();
                    }
                    logger.Msg($"Loaded {cache.Count} bullet hit entries from ModData.");
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to parse ModData JSON: " + ex);
                    lock (cacheLock) { cache.Clear(); }
                }
            }
        }

        // ================================================================
        // SAVE + CLEANUP HELPER
        // ================================================================
        public static void SaveToModData()
        {
            try
            {
                Dictionary<string, BulletHitInfo> snapshot;
                lock (cacheLock)
                {
                    snapshot = new Dictionary<string, BulletHitInfo>(cache);
                }

                string json = JsonConvert.SerializeObject(snapshot);
                sdm.Save(json, MODDATA_KEY);
            }
            catch (Exception ex)
            {
                logger.Error("Failed to save mod data: " + ex);
            }
        }

        public static void CleanupExpired()
        {
            float nowHours = 0f;
            try
            {
                TimeOfDay tod = GameManager.GetTimeOfDayComponent();
                if (tod != null) nowHours = tod.GetHoursPlayedNotPaused();
            }
            catch { }

            int removed = 0;
            lock (cacheLock)
            {
                List<string> expired = new List<string>();
                foreach (KeyValuePair<string, BulletHitInfo> kv in cache)
                {
                    if (kv.Value != null && kv.Value.CreatedHours > 0f)
                    {
                        float age = nowHours - kv.Value.CreatedHours;
                        if (age > expirationHours) expired.Add(kv.Key);
                    }
                }

                foreach (string guid in expired)
                {
                    cache.Remove(guid);
                    removed++;
                }
            }

            if (removed > 0)
                logger.Msg($"[RetrieveBullets] Cleanup removed {removed} expired bullet entries.");
        }
    }
}