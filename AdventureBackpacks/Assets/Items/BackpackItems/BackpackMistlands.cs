using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;


namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class BackpackMistlands : BackpackItem
{
    public BackpackMistlands(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;

        AssignCraftingTable(CraftingTable.BlackForge, 1);

        Recipe.MaximumRequiredStationLevel = 2;

        AddRecipeIngredient("CapeFeather", 1);
        AddRecipeIngredient("ScaleHide", 5);
        AddRecipeIngredient("Eitr", 10);

        AddUpgradeIngredient("ScaleHide", 4);
        AddUpgradeIngredient("Eitr", 2);
        AddUpgradeIngredient("Softtissue", 5);

        Recipe.AddDrop("Dverger", 0.002f, 1);
        Recipe.AddDrop("DvergerMage", 0.002f, 1);
        Recipe.AddDrop("DvergerMageFire", 0.002f, 1);
        Recipe.AddDrop("DvergerMageIce", 0.002f, 1);
        Recipe.AddDrop("DvergerMageSupport", 0.002f, 1);
        Recipe.AddDrop("SeekerQueen", 0.08f, 1);
    }
    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.Mistlands);
        RegisterBackpackSize(1, 8, 2);
        RegisterBackpackSize(2, 5, 4);
        RegisterBackpackSize(3, 6, 4);
        RegisterBackpackSize(4, 7, 4);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(30);
        RegisterSpeedMod();
        if ((BackpackBiome.Value & BackpackBiomes.Mistlands) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.FeatherFall].RegisterEffectBiomeQuality(BackpackBiomes.Mistlands, 3);
            EffectsFactory.EffectList[BackpackEffect.Demister].RegisterEffectBiomeQuality(BackpackBiomes.Mistlands, 4);
            EffectsFactory.EffectList[BackpackEffect.FrostResistance].RegisterEffectBiomeQuality(BackpackBiomes.Mistlands, 2);
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.Mistlands, 1);
        }
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
