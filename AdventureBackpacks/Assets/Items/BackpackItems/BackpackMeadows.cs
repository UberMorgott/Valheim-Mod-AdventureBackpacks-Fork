using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class BackpackMeadows : BackpackItem
{
    public BackpackMeadows(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;

        AssignCraftingTable(CraftingTable.Workbench, 2);

        Recipe.MaximumRequiredStationLevel = 3;

        AddRecipeIngredient("CapeDeerHide", 1);
        AddRecipeIngredient("DeerHide", 8);
        AddRecipeIngredient("BoneFragments", 2);

        AddUpgradeIngredient("LeatherScraps", 5);
        AddUpgradeIngredient("DeerHide", 3);

        Recipe.AddDrop("Greyling", 0.002f, 1);
        Recipe.AddDrop("Eikthyr", 0.04f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.Meadows);
        RegisterBackpackSize(1, 3, 1);
        RegisterBackpackSize(2, 4, 1);
        RegisterBackpackSize(3, 5, 1);
        RegisterBackpackSize(4, 6, 1);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(5);
        RegisterSpeedMod();
        if ((BackpackBiome.Value & BackpackBiomes.Meadows) != BackpackBiomes.None)
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.Meadows, 3);
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
