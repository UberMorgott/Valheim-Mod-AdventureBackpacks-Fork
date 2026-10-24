using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

//Ashen cape base, heat resistance on top.
internal class BackpackAshlands : BackpackItem
{
    public BackpackAshlands(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;

        AssignCraftingTable(CraftingTable.BlackForge, 3);

        Recipe.MaximumRequiredStationLevel = 4;

        AddRecipeIngredient("CapeAsh", 1);
        AddRecipeIngredient("AskHide", 6);
        AddRecipeIngredient("FlametalNew", 5);
        AddRecipeIngredient("MorgenSinew", 2);

        AddUpgradeIngredient("AskHide", 2);
        AddUpgradeIngredient("FlametalNew", 3);
        AddUpgradeIngredient("CharredBone", 5);
        AddUpgraderIngredient("Upgrader6Armor", 1);

        Recipe.AddDrop("Charred_Melee", 0.002f, 1);
        Recipe.AddDrop("Charred_Archer", 0.002f, 1);
        Recipe.AddDrop("Charred_Mage", 0.002f, 1);
        Recipe.AddDrop("Charred_Twitcher", 0.001f, 1);
        Recipe.AddDrop("Morgen", 0.005f, 1);
        Recipe.AddDrop("Fader", 0.08f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.Ashlands);
        RegisterBackpackSize(1, 6, 4);
        RegisterBackpackSize(2, 7, 4);
        RegisterBackpackSize(3, 8, 4);
        RegisterBackpackSize(4, 9, 4);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(35);
        RegisterArmorPerLevel();
        RegisterSpeedMod();
        RegisterHeatResistance();
        if ((BackpackBiome.Value & BackpackBiomes.Ashlands) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.FrostResistance].RegisterEffectBiomeQuality(BackpackBiomes.Ashlands, 1);
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.Ashlands, 1);
        }
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        var shared = itemData.m_shared;

        shared.m_movementModifier = GetSpeedModifier(quality);

        //Ashen cape stats
        shared.m_attackStaminaModifier = -0.10f;
        shared.m_blockStaminaModifier = -0.20f;

        //Stops at the Flametal set value; upgrader levels above 4 do not add more
        shared.m_heatResistanceModifier = HeatResistance.Value * System.Math.Min(quality, 4);

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;

        //Keep it below Barley Wine (Resistant)
        if (quality >= 3)
        {
            modifierList.Add(new HitData.DamageModPair
            {
                m_type = HitData.DamageType.Fire,
                m_modifier = HitData.DamageModifier.SlightlyResistant
            });
        }
    }
}
