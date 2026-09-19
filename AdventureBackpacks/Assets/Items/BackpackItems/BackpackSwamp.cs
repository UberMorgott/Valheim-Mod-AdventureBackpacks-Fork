using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class BackpackSwamp : BackpackItem
{
    public BackpackSwamp(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;
        AssignCraftingTable(CraftingTable.Workbench, 2);

        Recipe.MaximumRequiredStationLevel = 5;

        AddRecipeIngredient("Bloodbag", 10);
        AddRecipeIngredient("Root", 4);
        AddRecipeIngredient("Guck", 4);

        AddUpgradeIngredient("Bloodbag", 2);
        AddUpgradeIngredient("Iron", 5);

        Recipe.AddDrop("Draugr", 0.002f, 1);
        Recipe.AddDrop("Draugr_Ranged", 0.004f, 1);
        Recipe.AddDrop("Draugr_Elite", 0.004f, 1);
        Recipe.AddDrop("Abomination", 0.008f, 1);
        Recipe.AddDrop("Bonemass", 0.04f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.Swamp);
        RegisterBackpackSize(1, 2, 3);
        RegisterBackpackSize(2, 3, 3);
        RegisterBackpackSize(3, 4, 3);
        RegisterBackpackSize(4, 5, 3);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(15);
        RegisterSpeedMod();
        if ((BackpackBiome.Value & BackpackBiomes.Swamp) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.WaterResistance].RegisterEffectBiomeQuality(BackpackBiomes.Swamp, 2);
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.Swamp, 1);
        }
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
