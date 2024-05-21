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
            private static void Postfix(SapCollector __instance, ref string __result)
            {
                if (!Configuration.Current.SapCollector.IsEnabled || !Configuration.Current.SapCollector.showDuration || __instance.GetLevel() == __instance.m_maxLevel) return;
                                
                int duration = (int)(__instance.m_secPerUnit - __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_product));
                var info = duration >= 120 ? duration / 60 + " minutes" : duration + " seconds";
                __result = __result.Replace(" )", " )\n(" + info + ")");
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
