using System;
using System.Collections.Generic;
using AdventureBackpacks.Extensions;

namespace AdventureBackpacks.Features;

// Marks the vanilla calls that check or pay for a recipe or a piece (HaveRequirements, ConsumeResources inside
// DoCrafting/UpdatePlacement, the requirement UI). While active, the local player's Inventory.CountItems/HaveItem
// also count the equipped backpack and Inventory.RemoveItem(name, ...) pays from it, see InventoryPatches.
// Nested calls share one context; another mod can switch it off around its own crafting code with Suppress.
public static class CraftingContext
{
    private static int _activeDepth;
    private static readonly HashSet<string> SuppressionSources = new();

    public static bool IsSuppressed => SuppressionSources.Count > 0;

    public static bool IsActive => _activeDepth > 0 && !IsSuppressed;

    public static Inventory ActiveBackpackInventory { get; private set; }
    public static bool CanCraftFromBackpack { get; private set; }

    public static void Enter()
    {
        if (++_activeDepth == 1)
            ResolveActiveBackpack();
    }

    public static void Exit()
    {
        if (--_activeDepth <= 0)
            Reset();
    }

    public static void Reset()
    {
        _activeDepth = 0;
        ActiveBackpackInventory = null;
        CanCraftFromBackpack = false;
    }

    private static void ResolveActiveBackpack()
    {
        CanCraftFromBackpack = CraftFromBackpack.CanCraftFromBackpack(Player.m_localPlayer, out var backpackInventory);
        ActiveBackpackInventory = CanCraftFromBackpack ? backpackInventory : null;
    }

    public static void Suppress(string modIdentifier)
    {
        if (!string.IsNullOrEmpty(modIdentifier))
            SuppressionSources.Add(modIdentifier);
    }

    public static void Unsuppress(string modIdentifier)
    {
        if (!string.IsNullOrEmpty(modIdentifier))
            SuppressionSources.Remove(modIdentifier);
    }
}
