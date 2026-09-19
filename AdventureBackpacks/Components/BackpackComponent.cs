/* BackpackComponent.cs */

using System;
using AdventureBackpacks.Assets;
using AdventureBackpacks.Configuration;
using AdventureBackpacks.Extensions;

namespace AdventureBackpacks.Components
{
    // Live backpack inventory of one backpack item; BackpackDataStore creates it and keeps it in sync
    // with the item's saved custom data.
    public class BackpackComponent
    {
        public static string OldPluginCustomData = "JotunnBackpacks#JotunnBackpacks.BackpackComponent";

        internal BackpackComponent(ItemDrop.ItemData item)
        {
            Item = item;
        }

        public ItemDrop.ItemData Item { get; internal set; }

        // Serialized inventory (base64 ZPackage) stored in the item's custom data.
        public string Value
        {
            get => Item.m_customData.TryGetValue(BackpackDataStore.CustomDataKey, out var value) ? value : "";
            set => Item.m_customData[BackpackDataStore.CustomDataKey] = value;
        }

        private Inventory _backpackInventory;

        public bool IsLoadingInventory = false;
        public bool IsEmptyingBackpack = false;

        private ModLogger _log = AdventureBackpacks.Log;

        public void SetInventory(Inventory inventoryInstance)
        {
            _backpackInventory = inventoryInstance;
            SyncContainerInventory();
            Save(_backpackInventory);
        }

        // Keeps the player's backpack Container pointed at the live inventory instance, so mods that pull
        // items through Container.GetInventory() never act on a stale instance after a Deserialize.
        public void SyncContainerInventory()
        {
            if (Player.m_localPlayer != null && Player.m_localPlayer.IsThisBackpackEquipped(Item))
            {
                var container = Player.m_localPlayer.gameObject.GetComponent<Container>();
                if (container != null && _backpackInventory != null)
                {
                    container.m_inventory = _backpackInventory;
                    container.m_width = _backpackInventory.m_width;
                    container.m_height = _backpackInventory.m_height;
                    if (Item?.m_shared?.m_icons != null && Item.m_shared.m_icons.Length > 0)
                        container.m_bkg = Item.m_shared.m_icons[0];
                }
            }
        }

        public bool InventoryNeedsValidating(Vector2i backpackDimension)
        {
            if (_backpackInventory == null)
                return false;

            return _backpackInventory.m_width != backpackDimension.x || _backpackInventory.m_height != backpackDimension.y;
        }

        public Inventory GetInventory()
        {
            return _backpackInventory;
        }

        public void UpdateContainerSizing(ref Container backpackContainer)
        {
            var inventory = GetInventory();
            if (backpackContainer == null || inventory == null)
                return;

            backpackContainer.m_inventory = inventory;
            backpackContainer.m_width = inventory.m_width;
            backpackContainer.m_height = inventory.m_height;
            if (Item?.m_shared?.m_icons != null && Item.m_shared.m_icons.Length > 0)
                backpackContainer.m_bkg = Item.m_shared.m_icons[0];
        }

        public string Serialize()
        {
            _log.Debug($"[Serialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Starting..");
            // Store the Inventory as a ZPackage
            ZPackage pkg = new ZPackage();

            if (_backpackInventory == null)
                _backpackInventory = Backpacks.NewInventoryInstance(Item.m_shared.m_name, Item.m_quality);

            _backpackInventory.Save(pkg);

            string data = pkg.GetBase64();
            Value = data;
            _log.Debug($"[Serialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Value = {Value}");

            // Return the data to be deserialized in the method below
            return data;
        }

        // This code is run on game start for objects with a BackpackComponent, and it converts the inventory info from string format (ZPackage) to object format (Inventory) so the game can use it.
        public void Deserialize(string data)
        {
            _log.Debug($"[Deserialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Starting..");
            try
            {
                // Reuse the live instance when its dimensions still match, so references held by the
                // player's Container (and by other mods) stay valid; only a resize needs a new instance.
                var type = Item.m_shared.m_name;
                if (!Backpacks.TryGetBackpackItemByName(type, out var backpackDef))
                    return;

                var targetSize = backpackDef.GetInventorySize(Item.m_quality);
                if (_backpackInventory == null || _backpackInventory.m_width != targetSize.x || _backpackInventory.m_height != targetSize.y)
                    _backpackInventory = Backpacks.NewInventoryInstance(type, Item.m_quality);
                else
                    _backpackInventory.m_inventory.Clear();

                _log.Debug($"[Deserialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Value Before = {Value}");
                Value = data;
                _log.Debug($"[Deserialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Value After = {Value}");

                // Deserialising saved inventory data and storing it into the Inventory instance.
                ZPackage pkg = new ZPackage(data);
                _log.Debug($"[Deserialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Inventory Count Before Load: {_backpackInventory.m_inventory.Count}");
                _backpackInventory.Load(pkg);

                _log.Debug($"[Deserialize() - {Item.m_shared.m_name}-Q{Item.m_quality}] Inventory Count After Load: {_backpackInventory.m_inventory.Count}");

                SyncContainerInventory();

                //Update Status Effects
                Backpacks.UpdateStatusEffects(Item);
            }
            catch (Exception ex)
            {
                _log.LogError($" - {Item.m_shared.m_name} Backpack info is corrupt!\n{ex}");
            }
        }

        public void FirstLoad()
        {
            var name = Item.m_shared.m_name;
            _log.Debug($"[FirstLoad - {Item.m_shared.m_name}-Q{Item.m_quality}] {name}");

            // Check whether the item created is of a type contained in backpackTypes
            if (Backpacks.BackpackTypes.Contains(name))
            {
                if (!string.IsNullOrEmpty(Value))
                {
                    Deserialize(Value);
                }
                //Check to see if we have old Jotunn Backpack Component Data
                else if (Item.m_customData.TryGetValue(OldPluginCustomData, out var oldBackpack) && !string.IsNullOrEmpty(oldBackpack))
                {
                    Value = oldBackpack;
                    Deserialize(Value);
                }
                else if (_backpackInventory == null)
                {
                    _log.Debug($"[FirstLoad - {Item.m_shared.m_name}-Q{Item.m_quality}] Backpack null, creating...");
                    _backpackInventory = Backpacks.NewInventoryInstance(name, Item.m_quality);
                    Serialize();
                }
            }
        }

        public void Load()
        {
            _log.Debug($"[Load - {Item.m_shared.m_name}-Q{Item.m_quality}] Starting");
            IsLoadingInventory = true;

            if (!string.IsNullOrEmpty(Value))
            {
                _log.Debug($"[Load - {Item.m_shared.m_name}-Q{Item.m_quality}] Value = {Value}");
                Deserialize(Value);
            }
            else
            {
                if (_backpackInventory == null)
                {
                    _log.Debug($"[Load - {Item.m_shared.m_name}-Q{Item.m_quality}] Backpack null, creating...");
                    var name = Item.m_shared.m_name;
                    _backpackInventory = Backpacks.NewInventoryInstance(name, Item.m_quality);
                }

                Serialize();
            }
            IsLoadingInventory = false;
        }

        public void Save()
        {
            if (_backpackInventory == null)
            {
                Serialize();
                return;
            }

            _log.Debug($"[Save() - {Item.m_shared.m_name}-Q{Item.m_quality}] Starting Value = {Value}");
            _log.Debug($"[Save() - {Item.m_shared.m_name}-Q{Item.m_quality}] Starting backpack count {_backpackInventory.m_inventory.Count}");
            Value = Serialize();
            _log.Debug($"[Save() - {Item.m_shared.m_name}-Q{Item.m_quality}] Ending backpack count {_backpackInventory.m_inventory.Count}");
        }

        public void Save(Inventory backpack)
        {
            if (backpack == null)
            {
                _log.Warning($"[Save(Inventory) - {Item.m_shared.m_name}-Q{Item.m_quality}] Ignoring save: inventory argument was null.");
                return;
            }

            _log.Debug($"[Save(Inventory) - {Item.m_shared.m_name}-Q{Item.m_quality}] Starting backpack count {backpack.m_inventory.Count}");
            _backpackInventory = backpack;
            Save();
        }
    }
}
