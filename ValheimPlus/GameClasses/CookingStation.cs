using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using ValheimPlus.Configurations;

namespace ValheimPlus.GameClasses
{
    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.FindCookableItem))]
    public static class CookingStation_FindCookableItem_Transpiler
    {
        private static List<Container> nearbyChests = null;

        private static MethodInfo method_PullCookableItemFromNearbyChests = AccessTools.Method(typeof(CookingStation_FindCookableItem_Transpiler), nameof(CookingStation_FindCookableItem_Transpiler.PullCookableItemFromNearbyChests));

        /// <summary>
        /// Patches out the code that looks for cookable items in player inventory.
        /// When not cookables items have been found in the player inventory, check inside nearby chests.
        /// If found, remove the item from the chests it was in, instantiate a game object and returns it so it can be placed on the CookingStation.
        /// </summary>
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!Configuration.Current.CraftFromChest.IsEnabled || Configuration.Current.CraftFromChest.disableCookingStation) return instructions;

            List<CodeInstruction> il = instructions.ToList();
            int endIdx = -1;
            for (int i = 0; i < il.Count; i++)
            {
                if (il[i].opcode == OpCodes.Ldnull)
                {
                    il[i] = new CodeInstruction(OpCodes.Ldarg_0)
                    {
                        labels = il[i].labels
                    };
                    il.Insert(++i, new CodeInstruction(OpCodes.Call, method_PullCookableItemFromNearbyChests));
                    il.Insert(++i, new CodeInstruction(OpCodes.Stloc_3));
                    il.Insert(++i, new CodeInstruction(OpCodes.Ldloc_3));
                    endIdx = i;
                    break;
                }
            }
            if (endIdx == -1)
            {
                ValheimPlusPlugin.Logger.LogError("Failed to apply CookingStation_FindCookableItem_Transpiler");
                return instructions;
            }

            return il.AsEnumerable();
        }

        private static ItemDrop.ItemData PullCookableItemFromNearbyChests(CookingStation station)
        {
            if (station.GetFreeSlot() == -1) return null;

            Stopwatch delta = GameObjectAssistant.GetStopwatch(station.gameObject);

            int lookupInterval = Helper.Clamp(Configuration.Current.CraftFromChest.lookupInterval, 1, 10) * 1000;
            if (!delta.IsRunning || delta.ElapsedMilliseconds > lookupInterval)
            {
                nearbyChests = InventoryAssistant.GetNearbyChests(station.gameObject, Helper.Clamp(Configuration.Current.CraftFromChest.range, 1, 50), !Configuration.Current.CraftFromChest.ignorePrivateAreaCheck);
                delta.Restart();
            }

            foreach (CookingStation.ItemConversion itemConversion in station.m_conversion)
            {
                ItemDrop.ItemData itemData = itemConversion.m_from.m_itemData;

                foreach (Container c in nearbyChests)
                {
                    if (c.GetInventory().HaveItem(itemData.m_shared.m_name))
                    {
                        // Remove one item from chest
                        InventoryAssistant.RemoveItemFromChest(c, itemData);
                        // Instantiate cookabled GameObject
                        GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(itemConversion.m_from.gameObject.name);

                        ZNetView.m_forceDisableInit = true;
                        GameObject cookabledItem = UnityEngine.Object.Instantiate<GameObject>(itemPrefab);
                        ZNetView.m_forceDisableInit = false;

                        return cookabledItem.GetComponent<ItemDrop>().m_itemData;
                    }
                }
            }
            return null;
        }
    }

    public static class CookingStationFuel
    {
        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Awake))]
        public static class CookingStation_Awake_Patch
        {
            /// <summary>
            /// When fire source is loaded in view, check for configurations and set its fuel to max fuel
            /// </summary>
            private static void Postfix(CookingStation __instance)
            {
                if (!__instance.m_useFuel || !Configuration.Current.Oven.IsEnabled || !__instance.m_nview.IsValid()) return;
                if (Configuration.Current.Oven.infiniteFuel) __instance.SetFuel(__instance.m_maxFuel);
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.UpdateFuel))]
        public static class CookingStation_UpdateFuel_Patch
        {
            // If stay-at-max-fuel mode, fuel is set to max in Awake,
            // so simply cancel this call so that fuel level is never decremented.
            private static bool Prefix(CookingStation __instance) =>
                !__instance.m_useFuel || !Configuration.Current.Oven.IsEnabled ||
                !Configuration.Current.Oven.infiniteFuel;
        }
        
        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.UpdateCooking))]
        public static class CookingStation_UpdateCooking_Patch
        {
            // If the oven isn't lit, UpdateFuel will never be called,
            // so we prefix UpdateCooking to autoFuel if necessary.
            private static void Prefix(CookingStation __instance)
            {
                if (!__instance.m_useFuel || !Configuration.Current.Oven.IsEnabled ||
                    !Configuration.Current.Oven.autoFuel || !__instance.m_nview.IsValid()) return;

                // Only check every second:
                Stopwatch delta = GameObjectAssistant.GetStopwatch(__instance.gameObject);
                if (delta.IsRunning && delta.ElapsedMilliseconds < 1000) return;
                delta.Restart();

                AddFuelFromNearbyChests(__instance);
            }
        }
        
        private static void AddFuelFromNearbyChests(CookingStation __instance)
        {
            // Find the integer of fuels to take us back to exactly m_maxFuel and no further.
            int toMaxFuel = __instance.m_maxFuel - (int)Math.Ceiling(__instance.GetFuel());
            if (toMaxFuel < 1) return;

            ItemDrop.ItemData fuelItemData = __instance.m_fuelItem.m_itemData;
            int addedFuel = InventoryAssistant.RemoveItemInAmountFromAllNearbyChests(__instance.gameObject,
                Helper.Clamp(Configuration.Current.Oven.autoRange, 1, 50), fuelItemData, toMaxFuel,
                !Configuration.Current.Oven.ignorePrivateAreaCheck);
            if (addedFuel < 1) return;

            for (int i = 0; i < addedFuel; i++) __instance.m_nview.InvokeRPC("RPC_AddFuel");
            ValheimPlusPlugin.Logger.LogInfo("Added " + addedFuel + " fuel(" + fuelItemData.m_shared.m_name +
                ") in " + __instance.m_name);
        }
    }
}
