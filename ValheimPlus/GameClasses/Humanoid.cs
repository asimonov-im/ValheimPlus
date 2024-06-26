﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using ValheimPlus.Configurations;

namespace ValheimPlus.GameClasses
{
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetCurrentWeapon))]
    public static class ModifyCurrentWeapon
    {
        [UsedImplicitly]
        private static void Postfix(ref ItemDrop.ItemData __result, ref Humanoid __instance)
        {
            if (__instance is not Player playerInstance
                || !Configuration.Current.Player.IsEnabled
                || __result?.m_shared?.m_name != "Unarmed")
            {
                return;
            }

            float unarmedSkillFactor = playerInstance.GetSkillFactor(Skills.SkillType.Unarmed);
            float newDamage = unarmedSkillFactor * Configuration.Current.Player.baseUnarmedDamage;
            __result.m_shared.m_damages.m_blunt = Math.Max(2f, newDamage);
        }
    }

    /// <summary>
    /// When equipping a one-handed weapon, also equip best shield from inventory.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    public static class Humanoid_EquipItem_Patch
    {
        private static bool Postfix(bool __result, Humanoid __instance, ItemDrop.ItemData item)
        {
            if (Configuration.Current.Player.IsEnabled &&
                Configuration.Current.Player.autoEquipShield &&
                __result && 
                __instance.IsPlayer() && 
                __instance.m_rightItem?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon &&
                item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield)
            {
                List<ItemDrop.ItemData> inventoryItems = __instance.m_inventory.GetAllItems();

                ItemDrop.ItemData bestShield = null;
                foreach (ItemDrop.ItemData inventoryItem in inventoryItems)
                {
                    if (inventoryItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                    {
                        if (bestShield == null)
                        {
                            bestShield = inventoryItem;

                            continue;
                        }

                        if (bestShield.m_shared.m_blockPower < inventoryItem.m_shared.m_blockPower)
                        {
                            bestShield = inventoryItem;

                            continue;
                        }
                    }
                }

                if (bestShield != null)
                {
                    __instance.EquipItem(bestShield, false);
                }
            }

            return __result;
        }
    }


    /// <summary>
    /// When unequipping a one-handed weapon also unequip shield from inventory.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "UnequipItem")]
    public static class Humanoid_UnequipItem_Patch
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (Configuration.Current.Player.IsEnabled &&
                Configuration.Current.Player.autoUnequipShield &&
                item?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon &&
                __instance.IsPlayer())
            {
                List<ItemDrop.ItemData> inventoryItems = __instance.m_inventory.GetAllItems();

                foreach (ItemDrop.ItemData inventoryItem in inventoryItems)
                {
                    if (inventoryItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                    {
                        if(inventoryItem.m_equipped)
                            __instance.UnequipItem(inventoryItem, false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Re-equip items when leaving the water.
    /// </summary>
    public static class UpdateEquipmentState
    {
        public static bool shouldReequipItemsAfterSwimming = false;
    }
    [HarmonyPatch(typeof(Humanoid), "UpdateEquipment")]
    public static class Humanoid_UpdateEquipment_Patch
    {
        private static bool Prefix(Humanoid __instance)
        {
            if (!Configuration.Current.Player.IsEnabled || !Configuration.Current.Player.reequipItemsAfterSwimming || Configuration.Current.Player.dontUnequipItemsWhenSwimming)
                return true;

            if (__instance.IsPlayer() && __instance.IsSwimming() && !__instance.IsOnGround())
            {
                // The above is only enough to know we will eventually exit swimming, but we still don't know if the items were visible prior or not.
                // We only want to re-show them if they were shown to begin with, so we need to check.
                // This is also why this must be a prefix patch; in a postfix patch, the items are already hidden, and we don't know
                // if they were hidden by UpdateEquipment or by the user far earlier.

                if (__instance.m_leftItem != null || __instance.m_rightItem != null)
                    UpdateEquipmentState.shouldReequipItemsAfterSwimming = true;
            }
            else if (__instance.IsPlayer() && !__instance.IsSwimming() && __instance.IsOnGround() && UpdateEquipmentState.shouldReequipItemsAfterSwimming)
            {
                __instance.ShowHandItems();
                UpdateEquipmentState.shouldReequipItemsAfterSwimming = false;
            }

            return true;
        }
    }


    /// <summary>
    /// Removes the forced un-equip of items in your main and off-hand when entering water.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UpdateEquipment))]
    public static class Player_Humanoid_UpdateEquipment
    {
        private static MethodInfo method_Humanoid_HideHandItems = AccessTools.Method(typeof(Humanoid), nameof(Humanoid.HideHandItems));
        private static MethodInfo method_HideHandItems = AccessTools.Method(typeof(Player_Humanoid_UpdateEquipment), nameof(Player_Humanoid_UpdateEquipment.HideHandItems));

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (!Configuration.Current.Player.IsEnabled || !Configuration.Current.Player.dontUnequipItemsWhenSwimming) return instructions;

            List<CodeInstruction> il = instructions.ToList();

            for (int i = 0; i < il.Count; ++i)
            {
                if (il[i].Calls(method_Humanoid_HideHandItems))
                {
                    il[i - 1].opcode = OpCodes.Nop; // required to remove the this. index(0) stack value [ldarg.0]
                    il[i].operand = method_HideHandItems;
                    break;
                }
            }

            return il.AsEnumerable();
        }

        public static void HideHandItems()
        {
        }
    }


}
