using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;
using AdventureBackpacks.Configuration;
using BepInEx.Configuration;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

//Moose Hide cape base. Level 4 fires an effect when adrenaline fills.
internal class BackpackDeepNorth : BackpackItem
{
    internal ConfigEntry<string> SecondWindEffect;

    public BackpackDeepNorth(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;

        AssignCraftingTable(CraftingTable.BlackForge, 4);

        Recipe.MaximumRequiredStationLevel = 4;

        AddRecipeIngredient("CapeDeepNorth", 1);
        AddRecipeIngredient("MooseHide", 6);
        AddRecipeIngredient("MooseSinew", 2);
        AddRecipeIngredient("FrostCore", 3);

        AddUpgradeIngredient("MooseHide", 3);
        AddUpgradeIngredient("MooseSinew", 1);
        AddUpgradeIngredient("Ice", 10);
        AddUpgraderIngredient("Upgrader7Armor", 1);

        Recipe.AddDrop("Moose", 0.002f, 1);
        Recipe.AddDrop("JotunWarrior", 0.002f, 1);
        Recipe.AddDrop("JotunWitch", 0.002f, 1);
        Recipe.AddDrop("TrollFrost", 0.01f, 1);
        Recipe.AddDrop("FrozenKing", 0.08f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.DeepNorth);
        RegisterBackpackSize(1, 6, 4);
        RegisterBackpackSize(2, 7, 4);
        RegisterBackpackSize(3, 8, 4);
        RegisterBackpackSize(4, 9, 4);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(40);
        RegisterArmorPerLevel();
        RegisterSpeedMod();
        RegisterSecondWind();
        if ((BackpackBiome.Value & BackpackBiomes.DeepNorth) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.FrostResistance].RegisterEffectBiomeQuality(BackpackBiomes.DeepNorth, 1);
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.DeepNorth, 1);
        }
    }

    private void RegisterSecondWind()
    {
        ConfigRegistry.BindSynced(EnglishSection, "Second Wind Effect", "Potion_stamina_medium",
            new ConfigDescription("Status effect prefab applied when the adrenaline bar fills while wearing a level 4 pack. Empty disables it. Potion_stamina_medium is 160 stamina over 2 seconds.",
                null,
                new ConfigurationManagerAttributes { Category = LocalizedCategory, Order = 12 }), ref SecondWindEffect);

        SecondWindEffect!.SettingChanged += Backpacks.UpdateItemDataConfigValues;
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        var shared = itemData.m_shared;

        shared.m_movementModifier = GetSpeedModifier(quality);

        //Moose Hide cape stats, attack from level 2
        shared.m_runStaminaModifier = -0.20f;
        shared.m_attackStaminaModifier = quality >= 2 ? -0.20f : 0f;

        //Shovel, hammer, hoe
        shared.m_homeItemsStaminaModifier = quality >= 3 ? -0.20f : 0f;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;

        //Own bar so it works without a trinket, adds to one if worn
        shared.m_maxAdrenaline = quality >= 4 ? 50f : 0f;

        //Fires when adrenaline fills, bar resets after
        shared.m_fullAdrenalineSE = null;
        if (quality >= 4 && !string.IsNullOrEmpty(SecondWindEffect.Value) && ObjectDB.instance != null)
        {
            var effect = ObjectDB.instance.GetStatusEffect(SecondWindEffect.Value.GetStableHashCode());
            if (effect != null)
                shared.m_fullAdrenalineSE = effect;
            else
                Log.Warning($"Second Wind: status effect '{SecondWindEffect.Value}' not found in ObjectDB.");
        }
    }
}
