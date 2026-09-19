using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;
using BepInEx.Configuration;
using Jotunn.Configs;
using UnityEngine;

namespace AdventureBackpacks.Assets.Effects;

public class Demister : EffectsBase
{
    private static Heightmap.Biome _previousBiome = Heightmap.Biome.None;
    private static ConfigEntry<KeyCode> WisplightKeyToggle;
    private static ConfigEntry<bool> WisplightBiomeLogic;
    private static ButtonConfig _wisplightButton;


    public Demister(string effectName, string effectDesc) : base(effectName, effectDesc)
    {
    }

    public override bool RequiresPerFrameToggle => true;

    public override void ToggleEffect()
    {
        if (!Player.m_localPlayer || !ZNetScene.instance)
            return;

        var player = Player.m_localPlayer;
        if ((player.m_currentBiome.Equals(Heightmap.Biome.Mistlands) && WisplightBiomeLogic.Value) || !WisplightBiomeLogic.Value)
        {
            if (Player.m_localPlayer.IsBackpackEquipped())
            {
                if (_previousBiome != Heightmap.Biome.Mistlands && WisplightBiomeLogic.Value)
                {
                    SetEffectSwitch(true);
                    player.UpdateEquipmentStatusEffects();
                }
                if (ZInput.GetButtonDown(_wisplightButton.Name))
                {
                    if (IsEffectActive(Player.m_localPlayer))
                    {
                        ToggleEffectSwitch();
                        player.UpdateEquipmentStatusEffects();
                    }
                }
            }
        }
        else
        {
            if (CurrentSwitchSetting() && WisplightBiomeLogic.Value)
            {
                if (IsEffectActive(Player.m_localPlayer))
                {
                    SetEffectSwitch(false);
                    player.UpdateEquipmentStatusEffects();
                }
            }
        }

        _previousBiome = player.GetCurrentBiome();
    }

    public override void LoadStatusEffect()
    {
        if (!CurrentSwitchSetting())
            return;

        SetStatusEffect("Demister");
    }

    public override bool HasActiveStatusEffect(Humanoid human, out StatusEffect statusEffect)
    {
        if (!CurrentSwitchSetting())
        {
            statusEffect = null;
            return false;
        }

        SetStatusEffect("Demister");
        return base.HasActiveStatusEffect(human, out statusEffect);
    }

    public override void RegisterEffectConfiguration()
    {
        base.RegisterEffectConfiguration();

        ConfigRegistry.BindLocal("Wisplight Client Settings", "Wisplight Effect Key Toggle", KeyCode.L,
            new ConfigDescription("Hotkey to turn Wisplight on and off",
                null,
                new ConfigurationManagerAttributes { Order = 1 }), ref WisplightKeyToggle);

        ConfigRegistry.BindLocal("Wisplight Client Settings", "Wisplight Biome Logic", true,
            new ConfigDescription("If enabled, the Wisplight will automatically turn on when entering Mistlands, and turn off when exiting.",
                null, new ConfigurationManagerAttributes { Order = 2 }), ref WisplightBiomeLogic);

        //Registered button: bound by physical key, so it works on any keyboard layout.
        _wisplightButton = ConfigRegistry.AddButton("AdventureBackpacks_WisplightToggle", WisplightKeyToggle);
    }
}
