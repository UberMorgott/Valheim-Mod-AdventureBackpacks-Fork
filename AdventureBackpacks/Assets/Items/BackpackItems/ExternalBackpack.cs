using System;
using System.Collections.Generic;
using AdventureBackpacks.API;
using AdventureBackpacks.Assets.Effects;
using AdventureBackpacks.Assets.Factories;
using UnityEngine;

namespace AdventureBackpacks.Assets.Items.BackpackItems;

internal class ExternalBackpack : BackpackItem
{
    private readonly ABAPI.BackpackDefinition _backpackDefinition;
    private static EffectsBase _setEffectsBase;

    public ExternalBackpack(ABAPI.BackpackDefinition backpackDefinition, GameObject goItem) : base(backpackDefinition, goItem)
    {
        _backpackDefinition = backpackDefinition;
        SetupBackpack();
    }
    public ExternalBackpack(ABAPI.BackpackDefinition backpackDefinition) : base(backpackDefinition)
    {
        _backpackDefinition = backpackDefinition;
        SetupBackpack();
    }

    public void SetupBackpack()
    {
        RegisterConfigSettings();

        Recipe.Configurable = true;

        AssignCraftingTable(_backpackDefinition.CraftingTable, _backpackDefinition.StationLevel);

        Recipe.MaximumRequiredStationLevel = _backpackDefinition.MaxRequiredStationLevel;

        foreach (var ingredient in _backpackDefinition.RecipeIngredients)
        {
            AddRecipeIngredient(ingredient.ItemPrefabName, ingredient.Quantity);
        }

        foreach (var ingredient in _backpackDefinition.UpgradeIngredients)
        {
            AddUpgradeIngredient(ingredient.ItemPrefabName, ingredient.Quantity);
        }

        foreach (var target in _backpackDefinition.DropsFrom)
        {
            Recipe.AddDrop(target.Creature, target.Chance, target.Min, target.Max);
        }

        AdventureBackpacks.Waiter.StatusChanged += (_, _) => RegisterEffects();
    }

    private void RegisterEffects()
    {
        foreach (var applyEffect in _backpackDefinition.EffectsToApply)
        {
            if ((BackpackBiome.Value & applyEffect.Key) == BackpackBiomes.None) continue;

            foreach (var effectsBase in EffectsFactory.AllEffects)
            {
                if (effectsBase.GetStatusEffect() == null)
                    effectsBase.LoadStatusEffect();

                if (effectsBase.GetStatusEffect().m_name.Equals(applyEffect.Value.Key.m_name, StringComparison.Ordinal))
                {
                    if (_backpackDefinition.ItemSetStatusEffect != null &&
                        _backpackDefinition.ItemSetStatusEffect.m_name.Equals(effectsBase.GetStatusEffect().m_name, StringComparison.Ordinal))
                        _setEffectsBase = effectsBase;

                    effectsBase.RegisterEffectBiomeQuality(applyEffect.Key, applyEffect.Value.Value);
                }
            }
        }
    }

    internal sealed override void RegisterConfigSettings()
    {
        RegisterBackpackBiome(_backpackDefinition.BackpackBiome);
        foreach (var sizeConfig in _backpackDefinition.BackpackSizeByQuality)
        {
            RegisterBackpackSize(sizeConfig.Key, (int)sizeConfig.Value.x, (int)sizeConfig.Value.y);
        }
        RegisterStatusEffectInfo();
        RegisterWeightMultiplier(_backpackDefinition.WeightMultiplier);
        RegisterCarryBonus(_backpackDefinition.CarryBonus);
        RegisterSpeedMod(_backpackDefinition.SpeedMod);
        RegisterShaderSwap();
    }

    internal override void UpdateStatusEffects(int quality, SE_Stats statusEffect, List<HitData.DamageModPair> modifierList, ItemDrop.ItemData itemData)
    {
        itemData.m_shared.m_movementModifier = SpeedMod.Value / quality;

        if (_setEffectsBase != null)
        {
            if (_setEffectsBase.HasActiveStatusEffect(itemData, out var necroSetEffect))
            {
                itemData.m_shared.m_setStatusEffect = necroSetEffect;
            }
            else
            {
                itemData.m_shared.m_setStatusEffect = null;
            }
        }

        statusEffect.m_addMaxCarryWeight = CarryBonus.Value * quality;
    }
}
