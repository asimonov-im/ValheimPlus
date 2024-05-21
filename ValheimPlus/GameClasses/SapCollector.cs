using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using ValheimPlus.Configurations;

namespace ValheimPlus.GameClasses
{
    public static class SapCollectorDeposit
    {
        /// <summary>
        /// Apply SapCollector class changes
        /// </summary>
        [HarmonyPatch(typeof(SapCollector), "Awake")]
        public static class SapCollector_Awake_Patch
        {
            private static bool Prefix(ref float ___m_secPerUnit, ref int ___m_maxLevel)
            {
                if (Configuration.Current.SapCollector.IsEnabled)
                {
                    ___m_secPerUnit = Configuration.Current.SapCollector.sapProductionSpeed;
                    ___m_maxLevel = Configuration.Current.SapCollector.maximumSapPerCollector;
                }
                return true;
            }
        }
    
        /// <summary>
        /// Altering the hover text to display the time until the next sap is produced
        /// </summary>
        [HarmonyPatch(typeof(SapCollector), "GetHoverText")]
        public static class SapCollector_GetHoverText_Patch
        {
            private static bool Prefix(SapCollector __instance, ref string __result)
            {
                if (!Configuration.Current.SapCollector.IsEnabled || !Configuration.Current.SapCollector.showDuration)
                    return true;
    
                if (!PrivateArea.CheckAccess(__instance.transform.position, 0f, false))
                {
                    __result = Localization.instance.Localize(__instance.m_name + "\n$piece_noaccess");
                    return false;
                }
                int level = __instance.GetLevel();
                if (level > 0)
                {
                    __result = Localization.instance.Localize(string.Concat(new object[]
                    {
                    __instance.m_name,
                    " ( ",
                    __instance.m_spawnItem.m_itemData.m_shared.m_name,
                    " x ",
                    level,
                    " ) " + calculateTimeLeft(__instance) + "\n[<color=yellow><b>$KEY_Use</b></color>] $piece___instance_extract"
                    }));
                    return false;
                }
                __result = Localization.instance.Localize(__instance.m_name + " ( $piece_container_empty ) " + calculateTimeLeft(__instance) + "\n[<color=yellow><b>$KEY_Use</b></color>]");
                return false;
    
            }
    
            private static string calculateTimeLeft(SapCollector SapCollectorInstance)
            {
                string info = "";
    
                if (SapCollectorInstance.GetLevel() == SapCollectorInstance.m_maxLevel)
                    return info;
    
                float num = SapCollectorInstance.m_nview.GetZDO().GetFloat("product");
    
                float durationUntilDone = SapCollectorInstance.m_secPerUnit - num;
                int minutes = (int)durationUntilDone / 60;
    
                if (((int)durationUntilDone) >= 120)
                    info = minutes + " minutes";
                else
                    info = (int)durationUntilDone + " seconds";
    
                return " (" + info + ")";
            }
    
        }
    
        /// <summary>
        /// Auto Deposit for SapCollectors
        /// </summary>
        [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.RPC_Extract))]
        public static class SapCollector_RPC_Extract_Patch
        {
            private static bool Prefix(long caller, SapCollector __instance)
            {
                if (__instance.GetLevel() <= 0) return true;
                return Deposit(__instance);
            }
        }
    
        [HarmonyPatch(typeof(SapCollector), nameof(SapCollector.UpdateTick))]
        public static class SapCollector_UpdateTick_Patch
        {
            private static void Postfix(SapCollector __instance)
            {
                if (__instance.GetLevel() != __instance.m_maxLevel) return;
                Deposit(__instance);
            }
        }

        private static bool Deposit(SapCollector __instance)
        {
            if (!Configuration.Current.SapCollector.IsEnabled || !Configuration.Current.SapCollector.autoDeposit || !__instance.m_nview.IsOwner()) 
                return true;
            
            // find nearby chests
            List<Container> nearbyChests = InventoryAssistant.GetNearbyChests(__instance.gameObject, Helper.Clamp(Configuration.Current.SapCollector.autoDepositRange, 1, 50));
            if (nearbyChests.Count == 0)
                return true;

            while (__instance.GetLevel() > 0)
            {
                GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(__instance.m_spawnItem.gameObject.name);

                ZNetView.m_forceDisableInit = true;
                GameObject obj = Object.Instantiate<GameObject>(itemPrefab);
                ZNetView.m_forceDisableInit = false;

                ItemDrop comp = obj.GetComponent<ItemDrop>();

                bool result = spawnNearbyChest(comp, true);
                Object.Destroy(obj);

                if (!result)
                {
                    // Couldn't drop in chest, letting original code handle things
                    return true;
                }
            }

            if (__instance.GetLevel() == 0)
                __instance.m_spawnEffect.Create(__instance.m_spawnPoint.position, Quaternion.identity);

            bool spawnNearbyChest(ItemDrop item, bool mustHaveItem)
            {
                foreach (Container chest in nearbyChests)
                {
                    Inventory cInventory = chest.GetInventory();
                    if (mustHaveItem && !cInventory.HaveItem(item.m_itemData.m_shared.m_name))
                        continue;

                    if (!cInventory.AddItem(item.m_itemData))
                    {
                        //Chest full, move to the next
                        continue;
                    }
                    __instance.m_nview.GetZDO().Set("level", __instance.GetLevel() - 1);
                    InventoryAssistant.ConveyContainerToNetwork(chest);
                    return true;
                }

                if (mustHaveItem)
                    return spawnNearbyChest(item, false);

                return false;
            }

            return true;
        }
    }
    
}
