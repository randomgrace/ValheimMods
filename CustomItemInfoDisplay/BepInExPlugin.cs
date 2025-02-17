﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CustomItemInfoDisplay
{
    [BepInPlugin("aedenthorn.CustomItemInfoDisplay", "Custom Item Info Display", "0.2.0")]
    public partial class BepInExPlugin : BaseUnityPlugin
    {
        private static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<int> nexusID;

        private static string assetPath;
        private static string[] baseTemplate;
        private static Dictionary<ItemDrop.ItemData.ItemType, string[]> typeTemplates = new Dictionary<ItemDrop.ItemData.ItemType, string[]>();

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + " " : "") + str);
        }
        private void Awake()
        {

            context = this;
            modEnabled = Config.Bind<bool>("Config", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("Config", "IsDebug", true, "Enable debug logs");
            nexusID = Config.Bind<int>("Config", "NexusID", 1254, "Nexus mod ID for updates");

            assetPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), typeof(BepInExPlugin).Namespace);

            GetTooltipTemplates();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private static void GetTooltipTemplates()
        {
            CheckModFiles();
            baseTemplate = File.ReadAllLines(Path.Combine(assetPath, "template.txt"));
            typeTemplates.Clear();
            foreach (ItemDrop.ItemData.ItemType type in Enum.GetValues(typeof(ItemDrop.ItemData.ItemType)))
            {
                typeTemplates.Add(type, File.ReadAllLines(Path.Combine(assetPath, "ItemTypes", type + ".txt")));
            }
        }

        private static bool CheckModFiles()
        {
            if (!Directory.Exists(assetPath))
            {
                Directory.CreateDirectory(assetPath);
                Directory.CreateDirectory(Path.Combine(assetPath, "ItemTypes"));
                File.WriteAllText(Path.Combine(assetPath, "template.txt"), DefaultTemplates.GetTemplate());
                foreach (ItemDrop.ItemData.ItemType type in Enum.GetValues(typeof(ItemDrop.ItemData.ItemType)))
                {
                    File.WriteAllText(Path.Combine(assetPath, "ItemTypes", type + ".txt"), DefaultTemplates.GetTemplate(type));
                }
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip", new Type[]{typeof(ItemDrop.ItemData), typeof(int), typeof(bool) })]
        static class GetTooltip_Patch
        {
            static bool Prefix(ItemDrop.ItemData item, int qualityLevel, bool crafting, ref string __result)
            {
                if (!modEnabled.Value || baseTemplate == null)
                    return true;

                __result = CheckReplaceTemplate(baseTemplate, item, qualityLevel, crafting);
                __result = __result.Replace("{itemTypeInfo}", CheckReplaceTemplate(typeTemplates[item.m_shared.m_itemType], item, qualityLevel, crafting));

                return false;
			}

        }

        private static string CheckReplaceTemplate(string[] template, ItemDrop.ItemData item, int qualityLevel, bool crafting)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < template.Length; i++)
            {
                if(template[i] == "{itemSpawnName}")
                {
                    string name = GetSpawnName(item);
                    if (name != "")
                        lines.Add(name);
                }
                else if (template[i].StartsWith("[") && template[i].Contains("]"))
                {
                    string[] parts = template[i].Substring(1, template[i].IndexOf(']') - 1).Split(',');
                    string line = template[i].Substring(template[i].IndexOf(']') + 1);
                    if (!CheckToggles(item, qualityLevel, crafting, parts, ref line))
                        continue;
                    line = ReplaceLine(item, qualityLevel, crafting, line);
                    if (line != null)
                        lines.Add(line);
                }
                else
                {
                    string line = ReplaceLine(item, qualityLevel, crafting, template[i]);
                    if (line != null)
                        lines.Add(line);
                }
            }

            return string.Join("\n", lines);
        }


        private static bool CheckToggles(ItemDrop.ItemData item, int qualityLevel, bool crafting, string[] checks, ref string replace)
        {
            foreach (string check in checks) 
            {
                if (!CheckToggle(item, qualityLevel, crafting, check, ref replace))
                    return false;
            }
            return true;
        }
        private static bool CheckToggle(ItemDrop.ItemData item, int qualityLevel, bool crafting, string check, ref string replace)
        {

            switch (check)
            {
                case "dlc":
                    return item.m_shared.m_dlc.Length > 0;
                case "!dlc":
                    return item.m_shared.m_dlc.Length == 0;
                case "handed":
                    string handed = GetHanded(item);
                    if (handed != null)
                    {
                        replace = replace.Replace("{itemHanded}", handed);
                        return true;
                    }
                    return false;
                case "!handed":
                    return GetHanded(item) == null;
                case "crafting":
                    return crafting;
                case "!crafting":
                    return !crafting;
                case "crafted":
                    return item.m_crafterID != 0;
                case "!crafted":
                    return item.m_crafterID == 0;
                case "teleport":
                    return item.m_shared.m_teleportable;
                case "!teleport":
                    return !item.m_shared.m_teleportable;
                case "value":
                    return item.m_shared.m_value > 0;
                case "!value":
                    return item.m_shared.m_value == 0;
                case "quality":
                    return item.m_shared.m_maxQuality > 1;
                case "!quality":
                    return item.m_shared.m_maxQuality == 1;
                case "durability":
                    return item.m_shared.m_useDurability;
                case "!durability":
                    return !item.m_shared.m_useDurability;
                case "repairable":
                    return item.m_shared.m_useDurability && item.m_shared.m_canBeReparied && ObjectDB.instance.GetRecipe(item) != null;
                case "!repairable":
                    return !item.m_shared.m_useDurability || !item.m_shared.m_canBeReparied || ObjectDB.instance.GetRecipe(item) == null;
                case "movement":
                    return item.m_shared.m_movementModifier != 0 && Player.m_localPlayer != null;
                case "!movement":
                    return item.m_shared.m_movementModifier == 0;
                case "setStatus":
                    if(item.m_shared.m_setStatusEffect != null)
                    {
                        string setStatus =  item.m_shared.m_setStatusEffect.GetTooltipString();
                        replace = replace.Replace("{itemSetStatusInfo}", setStatus);
                        return true;
                    }
                    return false;
                case "!setStatus":
                    return item.m_shared.m_setStatusEffect == null;
                case "timedBlock":
                    return item.m_shared.m_timedBlockBonus > 1;
                case "!timedBlock":
                    return item.m_shared.m_timedBlockBonus > 1;
                case "projectile":
                    string projectile = Traverse.Create(item).Method("GetProjectileTooltip", new object[] { qualityLevel }).GetValue<string>();
                    if (projectile.Length > 0)
                    {
                        replace = replace.Replace("{itemProjectileInfo}", projectile);
                        return true;
                    }
                    return false;
                case "!projectile":
                    return Traverse.Create(item).Method("GetProjectileTooltip", new object[] { qualityLevel }).GetValue<string>().Length == 0;
                case "damageMod":
                    string damageMod = SE_Stats.GetDamageModifiersTooltipString(item.m_shared.m_damageModifiers);
                    if (damageMod.Length > 0)
                    {
                        replace = replace.Replace("{itemDamageModInfo}", damageMod);
                        return true;
                    }
                    return false;
                case "!damageMod":
                    return SE_Stats.GetDamageModifiersTooltipString(item.m_shared.m_damageModifiers).Length == 0;
                case "food":
                    return item.m_shared.m_food > 0;
                case "!food":
                    return item.m_shared.m_food == 0;
                case "status":
                    if (item.m_shared.m_attackStatusEffect)
                    {
                        replace = replace.Replace("{itemStatusInfo}", item.m_shared.m_attackStatusEffect.GetTooltipString());
                        return true;
                    }
                    if (item.m_shared.m_consumeStatusEffect)
                    {
                        replace = replace.Replace("{itemStatusInfo}", item.m_shared.m_consumeStatusEffect.GetTooltipString());
                        return true;
                    }
                    return false;
                case "!status":
                    return !item.m_shared.m_attackStatusEffect && !item.m_shared.m_consumeStatusEffect;
            }
            if (check == "itemType" + item.m_shared.m_itemType)
                return true;
            if (check.StartsWith("!itemType") && check != "!itemType" + item.m_shared.m_itemType)
                return true;
            return false;
        }

        private static string GetHanded(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Torch:
                    return "$item_onehanded";
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.Tool:
                    return "$item_twohanded";
                default:
                    return null;
            }
        }

        private static string ReplaceLine(ItemDrop.ItemData item, int qualityLevel, bool crafting, string line)
        {
            return line
                .Replace("{itemDescription}", item.m_shared.m_description)
                .Replace("{itemSpawnName}", GetSpawnName(item))
                .Replace("{itemCrafterName}", item.m_crafterName.ToString())
                .Replace("{itemValue}",item.GetValue().ToString())
                .Replace("{itemBaseValue}", item.m_shared.m_value.ToString())
                .Replace("{itemWeight}",item.GetWeight().ToString())
                .Replace("{itemQuality}",qualityLevel.ToString())
                .Replace("{itemMaxDurability}", item.GetMaxDurability(qualityLevel).ToString())
                .Replace("{itemPercentDurability}", (item.GetDurabilityPercentage() * 100f).ToString("0"))
                .Replace("{itemDurability}", item.m_durability.ToString("0"))
                .Replace("{itemStationLevel}", ObjectDB.instance.GetRecipe(item)?.m_minStationLevel.ToString())
                .Replace("{itemMovementMod}", (item.m_shared.m_movementModifier * 100f).ToString("+0;-0"))
                .Replace("{totalMovementMod}",(Player.m_localPlayer.GetEquipmentMovementModifier() * 100).ToString("+0;-0"))
                .Replace("{itemSetSize}", item.m_shared.m_setSize.ToString())
                .Replace("{itemDamage}", GetDamageString(item, qualityLevel))
                .Replace("{itemBaseBlock}", item.GetBaseBlockPower(qualityLevel).ToString())
                .Replace("{itemBlock}", item.GetBlockPowerTooltip(qualityLevel).ToString("0"))
                .Replace("{itemDeflection}", item.GetDeflectionForce(qualityLevel).ToString())
                .Replace("{itemBlockBonus}", item.m_shared.m_timedBlockBonus.ToString())
                .Replace("{itemAttackForce}", item.m_shared.m_attackForce.ToString())
                .Replace("{itemBackstab}", item.m_shared.m_backstabBonus.ToString())
                .Replace("{itemArmor}", item.GetArmor(qualityLevel).ToString())
                .Replace("{itemFoodHealth}", item.m_shared.m_food.ToString())
                .Replace("{itemSetSize}", item.m_shared.m_setSize.ToString())
                .Replace("{itemFoodStamina}", item.m_shared.m_foodStamina.ToString())
                .Replace("{itemFoodDuration}", item.m_shared.m_foodBurnTime.ToString())
                .Replace("{itemFoodRegen}", item.m_shared.m_foodRegen.ToString())
                .Replace("\\n", "\n");
        }

        private static string GetDamageString(ItemDrop.ItemData item, int qualityLevel)
        {
            string str = item.GetDamage(qualityLevel).GetTooltipString(item.m_shared.m_skillType);
            if (str.StartsWith("\n"))
                return str.Substring(1);
            return str;
        }

        private static string GetSpawnName(ItemDrop.ItemData item)
        {
            if (item.m_dropPrefab == null)
                return "";
            string name = Utils.GetPrefabName(item.m_dropPrefab) ?? "";
            return name;
        }

        [HarmonyPatch(typeof(Terminal), "InputText")]
        static class InputText_Patch
        {
            static bool Prefix(Terminal __instance)
            {
                if (!modEnabled.Value)
                    return true;
                string text = __instance.m_input.text;
                if (text.ToLower().Equals($"{typeof(BepInExPlugin).Namespace.ToLower()} reset"))
                {
                    context.Config.Reload();
                    context.Config.Save();

                    __instance.AddString(text);
                    __instance.AddString($"{context.Info.Metadata.Name} config reloaded");
                    return false;
                }
                return true;
            }
        }
    }
}
