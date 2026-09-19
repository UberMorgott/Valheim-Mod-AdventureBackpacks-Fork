using AdventureBackpacks.Assets;
using AdventureBackpacks.Components;

namespace AdventureBackpacks.Extensions;

public static class ItemDataExtensions
{
    public static bool IsBackpack(this ItemDrop.ItemData item)
    {
        return Backpacks.BackpackTypes.Contains(item.m_shared.m_name);
    }

    // Live backpack data of the item, or null when the item carries none.
    public static BackpackComponent GetBackpackComponent(this ItemDrop.ItemData item)
    {
        return BackpackDataStore.Get(item);
    }

    // Live backpack data of the item, created on first use.
    public static BackpackComponent GetOrCreateBackpackComponent(this ItemDrop.ItemData item)
    {
        return BackpackDataStore.GetOrCreate(item);
    }
}
