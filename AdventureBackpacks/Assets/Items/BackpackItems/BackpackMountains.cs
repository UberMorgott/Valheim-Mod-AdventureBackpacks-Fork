using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Factories;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class BackpackMountains : BackpackItem
{
    public BackpackMountains(string assetName, string prefabName, string itemName) : base(assetName, prefabName, itemName)
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;
        AssignCraftingTable(CraftingTable.Forge, 3);

        Recipe.MaximumRequiredStationLevel = 7;

        AddRecipeIngredient("CapeWolf", 1);
        AddRecipeIngredient("WolfHairBundle", 10);

        AddUpgradeIngredient("WolfPelt", 5);
        AddUpgradeIngredient("Silver", 5);

        Recipe.AddDrop("Fenring_Cultist", 0.002f, 1);
        Recipe.AddDrop("Ulv", 0.001f, 1);
        Recipe.AddDrop("Fenring", 0.008f, 1);
        Recipe.AddDrop("Dragon", 0.04f, 1);
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(BackpackBiomes.Mountains);
        RegisterBackpackSize(1, 3, 3);
        RegisterBackpackSize(2, 4, 3);
        RegisterBackpackSize(3, 5, 3);
        RegisterBackpackSize(4, 6, 3);
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier();
        RegisterCarryBonus(20);
        RegisterSpeedMod();
        if ((BackpackBiome.Value & BackpackBiomes.Mountains) != BackpackBiomes.None)
        {
            EffectsFactory.EffectList[BackpackEffect.FeatherFall].RegisterEffectBiomeQuality(BackpackBiomes.Mountains, 4);
            EffectsFactory.EffectList[BackpackEffect.FrostResistance].RegisterEffectBiomeQuality(BackpackBiomes.Mountains, 1);
            EffectsFactory.EffectList[BackpackEffect.ColdResistance].RegisterEffectBiomeQuality(BackpackBiomes.Mountains, 1);
        }

    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
