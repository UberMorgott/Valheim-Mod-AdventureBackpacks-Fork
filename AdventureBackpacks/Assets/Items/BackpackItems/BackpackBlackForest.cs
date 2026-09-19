using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class BackpackBlackForest : BackpackItem
{
    public BackpackBlackForest(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;

        AssignCraftingTable(CraftingTable.Forge, 1);

        Recipe.MaximumRequiredStationLevel = 3;

        AddRecipeIngredient("CapeTrollHide", 1);
        AddRecipeIngredient("Copper", 5);

        AddUpgradeIngredient("TrollHide", 3);
        AddUpgradeIngredient("Bronze", 3);

        Recipe.AddDrop("Greydwarf", 0.002f, 1);
        Recipe.AddDrop("Greydwarf_Elite", 0.004f, 1);
        Recipe.AddDrop("Greydwarf_Shaman", 0.004f, 1);
        Recipe.AddDrop("Troll", 0.01f, 1);
        Recipe.AddDrop("Bjorn", 0.04f, 1);
        Recipe.AddDrop("gd_king", 0.08f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.BlackForest);
        RegisterBackpackSize(1, 3, 2);
        RegisterBackpackSize(2, 4, 2);
        RegisterBackpackSize(3, 5, 2);
        RegisterBackpackSize(4, 6, 2);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(10);
        RegisterSpeedMod();
        if ((BackpackBiome.Value & BackpackBiomes.BlackForest) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.BlackForest, 1);
            EffectsFactory.EffectList[BackpackEffect.TrollArmor].RegisterEffectBiomeQuality(BackpackBiomes.BlackForest, 2);
        }
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
