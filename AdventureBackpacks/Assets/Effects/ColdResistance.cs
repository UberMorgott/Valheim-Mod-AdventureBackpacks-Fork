using Jotunn.Entities;
using UnityEngine;

namespace AdventureBackpacks.Assets.Effects;

public class ColdResistance : EffectsBase
{
    private StatusEffect _externalStatusEffect;
    public ColdResistance(string effectName, string effectDesc) : base(effectName, effectDesc)
    {
    }

    private void LoadExternalStatusEffect()
    {
        if (_externalStatusEffect == null)
        {
            var cold = ObjectDB.instance.GetStatusEffect("Cold".GetStableHashCode());
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = "SE_adventurebackpacks_cold_immunity";
            se.m_name = "$adventurebackpacks_se_cold_immunity";
            se.m_icon = cold.m_icon;
            _externalStatusEffect = se;
            // Fixed template: Jotunn adds it to every ObjectDB. Per-backpack effects are built at runtime instead.
            Jotunn.Managers.ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, fixReference: false));
            SetStatusEffect(_externalStatusEffect);
        }
    }

    public override void LoadStatusEffect()
    {
        LoadExternalStatusEffect();
    }

    public override bool HasActiveStatusEffect(Humanoid human, out StatusEffect statusEffect)
    {
        LoadExternalStatusEffect();
        SetStatusEffect(_externalStatusEffect);
        return base.HasActiveStatusEffect(human, out statusEffect);
    }

    public override bool HasActiveStatusEffect(ItemDrop.ItemData item, out StatusEffect statusEffect)
    {
        LoadExternalStatusEffect();
        SetStatusEffect(_externalStatusEffect);
        return base.HasActiveStatusEffect(item, out statusEffect);
    }
}