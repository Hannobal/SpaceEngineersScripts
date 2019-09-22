#region pre_script

using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Library;
using System.Text;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Common;
using Sandbox.Game;
using VRage.Collections;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;

namespace Scripts
{
    class Program : MyGridProgram
    {
        #endregion pre_script

        VRage.MyFixedPoint MaxAutoRefineryOreAmount = 10000;

        readonly Dictionary<string, VRage.MyFixedPoint> targetIngots
            = new Dictionary<string, VRage.MyFixedPoint>
        {
            { "Cobalt",      200 },
            { "Gold",        200 },
            { "Iron",      10000 },
            { "Magnesium",   100 },
            { "Nickel",      600 },
            { "Platinum",    100 },
            { "Silicon",    1000 },
            { "Silver",     1000 },
            { "Stone",      1000 },
            { "Uranium",     100 },
        };

        readonly Dictionary<string, VRage.MyFixedPoint> targetComponents
            = new Dictionary<string, VRage.MyFixedPoint>
        {

        };

        string materialText="";
        string componentText = "";
        string ammoText = "";
        string systemText = "";
        string debugText = "";
        int listUpdateFrequency = 10;
        int listUpdateCounter = 0;
        int clearRefinereiesFrequency = 1;
        int clearRefinereiesCounter = 0;
        int clearAssemblersFrequency = 10;
        int clearAssemblersCounter = 0;

        Dictionary<ItemType, Dictionary<string, VRage.MyFixedPoint>> inventory
            = new Dictionary<ItemType, Dictionary<string, VRage.MyFixedPoint>>
            {
                { ItemType.Ore, new Dictionary<string, VRage.MyFixedPoint>() },
                { ItemType.Ingot, new Dictionary<string, VRage.MyFixedPoint>() },
                { ItemType.Component, new Dictionary<string, VRage.MyFixedPoint>() },
                { ItemType.Ammunition, new Dictionary<string, VRage.MyFixedPoint>() },
                { ItemType.Other, new Dictionary<string, VRage.MyFixedPoint>() }
            };

        List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
        Dictionary<IMyCubeGrid, List<CargoContainer>> cargoContainers
            = new Dictionary<IMyCubeGrid, List<CargoContainer>>();
        List<IMyRefinery> autoRefineries = new List<IMyRefinery>();
        List<IMyAssembler> assemblers = new List<IMyAssembler>();

        string FormatNumber(float num)
        {
            if(num < 1e3f)
                return String.Format("{0:0.00} ", num);
            else if (num < 1e6f)
                return String.Format("{0:0.00}k", num * 1e-3f);
            else if (num < 1e9f)
                return String.Format("{0:0.00}M", num * 1e-6f);
            else if (num < 1e12f)
                return String.Format("{0:0.00}G", num * 1e-9f);
            else if (num < 1e15f)
                return String.Format("{0:0.00}P", num * 1e-15f);
            else
                return String.Format("{0:0.} ", num);
        }

        enum ItemType
        {
            Ore = 1,
            Ingot = 2,
            Component = 4,
            Ammunition = 8,
            Other = 16
        }

        static ItemType ParseType(MyInventoryItem item)
        {
            MyItemInfo info = item.Type.GetItemInfo();
            if (info.IsOre) return ItemType.Ore;
            if (info.IsIngot) return ItemType.Ingot;
            if (info.IsComponent) return ItemType.Component;
            if (info.IsAmmo) return ItemType.Ammunition;
            return ItemType.Other;
        }

        static int GetAcceptorBits(string entityName)
        {
            int bits = 0;
            if (entityName.Contains("Material"))
            {
                bits |= (int)ItemType.Ore;
                bits |= (int)ItemType.Ingot;
            }
            if (entityName.Contains("Ore"))
                bits |= (int)ItemType.Ore;
            if (entityName.Contains("Ingot"))
                bits |= (int)ItemType.Ingot;
            if (entityName.Contains("Component"))
                bits |= (int)ItemType.Component;
            if (entityName.Contains("Ammo") || entityName.Contains("Ammunition"))
                bits |= (int)ItemType.Ammunition;

            if (bits == 0) bits = 255; // accept everything
            return bits;
        }

        class CargoContainer
        {
            public CargoContainer(IMyCargoContainer container_)
            {
                container = container_;
                acceptorBits = GetAcceptorBits(container.CustomName);

            }
            public readonly IMyCargoContainer container;
            public readonly int acceptorBits;
        }

        void UpdateTextPanels()
        {
            foreach (IMyTextPanel panel in textPanels) {
                Echo("Test");
                Echo(panel.CustomName);
                if (panel.CustomName.Contains("Material"))
                {
                    panel.WriteText(materialText);
                }
                else if (panel.CustomName.Contains("Component"))
                    panel.WriteText(componentText);
                else if (panel.CustomName.Contains("Ammo")
                    || panel.CustomName.Contains("Ammunition"))
                    panel.WriteText(ammoText);
                else if (panel.CustomName.Contains("Debug"))
                    panel.WriteText(debugText);
                else
                    panel.WriteText(systemText);
            }
        }

        void RebuildBlockLists()
        {
            cargoContainers.Clear();
            List<IMyCargoContainer> containers = new List<IMyCargoContainer>();
            GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(containers);
            foreach(IMyCargoContainer container in containers)
            {
                if (!cargoContainers.ContainsKey(container.CubeGrid))
                    cargoContainers.Add(container.CubeGrid, new List<CargoContainer>());
                cargoContainers[container.CubeGrid].Add(new CargoContainer(container));
            }

            autoRefineries.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyRefinery>(autoRefineries,
                x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("Auto")
            );
            assemblers.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyAssembler>(assemblers,
                x => x.CubeGrid == Me.CubeGrid
            );

            textPanels.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(textPanels,
                x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("Inventory")
            );
        }

        void UpdateMaterialText()
        {
            materialText = String.Format("{0,-10}{1,8}{2,9}\n", "Material", "Ore ", "Refined ");
            if(inventory.Count ==0 )
            {
                materialText += "No ores or ingots found.";
                return;
            }
            SortedSet<string> keys = new SortedSet<string>();
            foreach(var kvp in inventory[ItemType.Ore])
                keys.Add(kvp.Key);
            foreach (var kvp in inventory[ItemType.Ingot])
                keys.Add(kvp.Key);
            foreach (var kvp in targetIngots)
                keys.Add(kvp.Key);
            foreach (string key in keys)
            {
                float ore = 0f;
                float ingots = 0f;
                float ratio = 1f;
                if (inventory[ItemType.Ore].ContainsKey(key))
                    ore = (float)inventory[ItemType.Ore][key];

                if (inventory[ItemType.Ingot].ContainsKey(key))
                    ingots = (float) inventory[ItemType.Ingot][key];
                if (targetIngots.ContainsKey(key) && targetIngots[key] > 0)
                {
                    ratio = ingots
                        / (float) targetIngots[key];
                }

                materialText += String.Format("{0,-10}{1,8}{2,9}{3,4}\n",
                    key,
                    FormatNumber(ore),
                    FormatNumber(ingots),
                    ratio.ToString());
            }
        }

        void UpdateInventory()
        {
            // clear inventory
            foreach (var entry in inventory)
            {
                entry.Value.Clear();
            }
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);
            foreach (IMyTerminalBlock block in blocks)
            {
                UpdateInventory(block);
            }
            //foreach(CargoContainer container in cargoContainers[Me.CubeGrid])
            //{
            //    UpdateInventory(container.container);
            //}

        }

        void UpdateInventory(IMyTerminalBlock block)
        {
            //Echo(block.CustomName);
            for (int i=0;i<block.InventoryCount;++i)
            {
                UpdateInventory(block.GetInventory(i));
            }
        }

        void UpdateInventory(IMyInventory inventory)
        {
            //Echo("inv "+" "+ inventory.CurrentVolume.RawValue.ToString() +"/"+ inventory.MaxVolume.RawValue.ToString());
            List<MyInventoryItem> items = new List<MyInventoryItem>();
            inventory.GetItems(items);
            foreach (MyInventoryItem item in items)
            {
                UpdateInventory(item);
            }
        }

        void UpdateInventory(MyInventoryItem item)
        {
            ItemType type = ParseType(item);
            Dictionary<string, VRage.MyFixedPoint> inv = inventory[type];
            if (!inv.ContainsKey(item.Type.SubtypeId))
                inv.Add(item.Type.SubtypeId, 0);
            inv[item.Type.SubtypeId] += item.Amount;
            //Echo("vol="+(item.Amount.RawValue*item.Type.GetItemInfo().Volume).ToString());
            Echo("amt="+item.Amount.ToString());
        }

        float ItemVolume(MyInventoryItem item)
        {
            return item.Amount.RawValue * item.Type.GetItemInfo().Volume;
        }

        VRage.MyFixedPoint ItemAmount(MyItemType type, float volume)
        {
            return new VRage.MyFixedPoint
            {
                RawValue = (long) (volume / type.GetItemInfo().Volume)
            };
        }

        List<CargoContainer> FindCargoSpace(
            MyInventoryItem item,
            IMyInventory currentInv,
            List<CargoContainer> containers)
        {
            float itemVolume = ItemVolume(item);
            List<CargoContainer> output = new List<CargoContainer>();
            List<CargoContainer> containsItemAndEnoughRoom = new List<CargoContainer>();
            List<CargoContainer> containsItemAndLimitedRoom = new List<CargoContainer>();
            List<CargoContainer> doesntContainItemAndEnoughRoom = new List<CargoContainer>();
            List<CargoContainer> doesntContainItemAndLimitedRoom = new List<CargoContainer>();
            foreach (CargoContainer container in containers)
            {
                if (0 == ((int) ParseType(item) & container.acceptorBits)) continue;
                IMyInventory inv = container.container.GetInventory();
                if (!currentInv.CanTransferItemTo(inv,item.Type)) continue;
                float freeSpace = inv.MaxVolume.RawValue - inv.CurrentVolume.RawValue;
                if (freeSpace == 0f) continue;
                bool enoughRoom =  freeSpace < itemVolume;
                bool containsItemType = false;
                List<MyInventoryItem> items = new List<MyInventoryItem>();
                inv.GetItems(items);
                foreach (MyInventoryItem otherItem in items)
                {
                    if (item.Type == otherItem.Type)
                    {
                        containsItemType = true;
                        break;
                    }
                }
                if (containsItemType && enoughRoom)
                    output.Add(container);
                else if (containsItemType)
                    containsItemAndLimitedRoom.Add(container);
                else if (containsItemType)
                    doesntContainItemAndEnoughRoom.Add(container);
                else
                    doesntContainItemAndLimitedRoom.Add(container);
            }
            output.AddRange(containsItemAndLimitedRoom);
            output.AddRange(doesntContainItemAndEnoughRoom);
            output.AddRange(doesntContainItemAndLimitedRoom);
            return output;
        }

        void MoveToContainer(
            MyInventoryItem item,
            IMyInventory currentInv,
            int itemIndex,
            List<CargoContainer> containers)
        {
            List<CargoContainer> candidateContainers =
                FindCargoSpace(item, currentInv, containers);
            foreach(CargoContainer container in candidateContainers)
            {
                IMyInventory targetInv = container.container.GetInventory();
                float remainingItemVolume = ItemVolume(item);
                float transferrableVolume = Math.Min(remainingItemVolume,
                    targetInv.MaxVolume.RawValue - targetInv.CurrentVolume.RawValue);
                if(currentInv.TransferItemTo(
                    targetInv,
                    item,
                    ItemAmount(item.Type, transferrableVolume)
                    ) )
                {
                    remainingItemVolume -= transferrableVolume;
                    if (remainingItemVolume == 0f) break;
                }
            }
        }

        void ClearRefineries()
        {
            foreach (IMyRefinery refinery in autoRefineries)
                ClearRefinery(refinery);
        }
        void ClearRefinery(IMyRefinery refinery)
        {
            for(int i=0; i<refinery.InventoryCount; ++i)
            {
                IMyInventory inv = refinery.GetInventory(i);
                for(int j=inv.ItemCount-1; j>=0; --j)
                {
                    MyInventoryItem item = (MyInventoryItem) inv.GetItemAt(j);
                    MoveToContainer(item, inv, j, cargoContainers[Me.CubeGrid]);
                }
            }
        }

        Program() {
            RebuildBlockLists();
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        void Main(string arguments)
        {
            listUpdateCounter = (++listUpdateCounter % listUpdateFrequency);
            if (listUpdateCounter == 0)
                RebuildBlockLists();

            clearRefinereiesCounter = (++clearRefinereiesCounter % clearRefinereiesFrequency);
            Echo(clearRefinereiesCounter.ToString());
            if (clearRefinereiesCounter == 0)
            {
                Echo("Clear Ref");
                ClearRefineries();
            }

            UpdateInventory();
            UpdateMaterialText();
            UpdateTextPanels();
        }

        #region post_script
    }
}
#endregion post_script