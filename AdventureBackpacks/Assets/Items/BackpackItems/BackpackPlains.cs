using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class BackpackPlains : BackpackItem
{
    public BackpackPlains(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();
        Recipe.Configurable = true;
        AssignCraftingTable(CraftingTable.Forge, 3);

        Recipe.MaximumRequiredStationLevel = 7;

        AddRecipeIngredient("CapeLox", 1);
        AddRecipeIngredient("Tar", 5);
        AddRecipeIngredient("BlackMetal", 5);

        AddUpgradeIngredient("LoxPelt", 2);
        AddUpgradeIngredient("BlackMetal", 5);

        Recipe.AddDrop("Goblin", 0.002f, 1);
        Recipe.AddDrop("GoblinArcher", 0.002f, 1);
        Recipe.AddDrop("GoblinBrute", 0.002f, 1);
        Recipe.AddDrop("GoblinShaman", 0.002f, 1);
        Recipe.AddDrop("Unbjorn", 0.02f, 1);
        Recipe.AddDrop("GoblinKing", 0.04f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.Plains);
        RegisterBackpackSize(1, 3, 4);
        RegisterBackpackSize(2, 4, 4);
        RegisterBackpackSize(3, 5, 4);
        RegisterBackpackSize(4, 6, 4);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(25);
        RegisterSpeedMod();
        if ((BackpackBiome.Value & BackpackBiomes.Plains) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.FrostResistance].RegisterEffectBiomeQuality(BackpackBiomes.Plains, 3);
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.Plains, 1);
        }

    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
