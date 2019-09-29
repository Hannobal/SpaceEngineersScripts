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

        // do not change the following variables:
        static readonly string oreType = "MyObjectBuilder_Ore";
        static readonly string ingotType = "MyObjectBuilder_Ingot";
        static readonly string componentType = "MyObjectBuilder_Component";
        static readonly string ammoType = "MyObjectBuilder_AmmoMagazine";

        readonly Dictionary<string, Dictionary<string, VRage.MyFixedPoint>> targetAmounts
            = new Dictionary<string, Dictionary<string, VRage.MyFixedPoint>>
    {
        // here you can set the amounts to be produced
        { ingotType, new Dictionary<string, VRage.MyFixedPoint> {
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
        } },
        { componentType, new Dictionary<string, VRage.MyFixedPoint> {
            { "Superconductor",      100 },
        } },
        { ammoType, new Dictionary<string, VRage.MyFixedPoint> {
        } }
    };

        readonly int listUpdateFrequency = 10;
        readonly int clearRefinereiesFrequency = 1;
        readonly int clearAssemblersFrequency = 10;
        readonly bool clearIngotsFromIdleAssemblers = true;

        /**********************************************************************
         * END OF CUSTOMIZATION SECTION
         *********************************************************************/

        class ItemInInventory
        {
            public ItemInInventory(IMyInventory inv, int itemIdx)
            {
                inventory = inv;
                itemIndex = itemIdx;
            }
            public IMyInventory inventory;
            public int itemIndex;
        }

        class InventorySlot
        {
            public VRage.MyFixedPoint targetAmount = 0;
            public VRage.MyFixedPoint totalAmount = 0;
            public float ratio = 1f;
            public List<ItemInInventory> items = new List<ItemInInventory>();
            public void Clear()
            {
                items.Clear();
                totalAmount = 0;
                ratio = 1f;
            }
        };

        Dictionary<string, Dictionary<string, InventorySlot>> globalInventory =
            new Dictionary<string, Dictionary<string, InventorySlot>>();

        string materialText = "";
        string componentText = "";
        string ammoText = "";
        string systemText = "";
        string debugText = "";

        int listUpdateCounter = 0;
        int clearRefinereiesCounter = 0;
        int clearAssemblersCounter = 0;

        List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
        Dictionary<IMyCubeGrid, List<CargoContainer>> cargoContainers
            = new Dictionary<IMyCubeGrid, List<CargoContainer>>();
        List<IMyRefinery> autoRefineries = new List<IMyRefinery>();
        List<IMyAssembler> assemblers = new List<IMyAssembler>();

        // splits a string by whitespace except for parts in double quotes
        List<string> SplitWhitespace(string str, bool IgnoreInts = false)
        {
            List<string> Parts = new List<string>();
            var Matches = System.Text.RegularExpressions.Regex.Matches(str, @"[\""].+?[\""]|[^ ]+");
            for (int i = 0; i < Matches.Count; i++)
            {
                string m = Matches[i].Groups[0].Captures[0].ToString();
                int j;
                if (IgnoreInts && Int32.TryParse(m, out j) == true) continue;
                Parts.Add(m.Replace("\"", ""));
            }
            return Parts;
        }

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

            public bool Contains(MyItemType type)
            {
                IMyInventory inv = container.GetInventory();
                List<MyInventoryItem> items = new List<MyInventoryItem>();
                inv.GetItems(items);
                foreach (MyInventoryItem item in items)
                {
                    if (item.Type == type)
                    {
                        return true;
                    }
                }
                return false;
            }

            public readonly IMyCargoContainer container;
            public readonly int acceptorBits;
        }

        void ClearGlobalInventory()
        {
            // we only clear the amounts
            foreach(var kvp in globalInventory)
            {
                foreach(var kvp2 in kvp.Value)
                {

                }
            }
        }

        void AddToGlobalInventory(
            IMyInventory inventory,
            MyInventoryItem item,
            int itemIndex)
        {
            if (!globalInventory.ContainsKey(item.Type.TypeId))
                globalInventory.Add(item.Type.TypeId, new Dictionary<string, InventorySlot>());
            if (!globalInventory[item.Type.TypeId].ContainsKey(item.Type.SubtypeId))
                globalInventory[item.Type.TypeId].Add(item.Type.SubtypeId, new InventorySlot());

            globalInventory[item.Type.TypeId][item.Type.SubtypeId].totalAmount += item.Amount;
            globalInventory[item.Type.TypeId][item.Type.SubtypeId].items.Add(
                new ItemInInventory(inventory, itemIndex));
        }

        void AddToGlobalInventory(
            string TypeId,
            string SubtypeId,
            VRage.MyFixedPoint targetAmount)
        {
            if (!globalInventory.ContainsKey(TypeId))
                globalInventory.Add(TypeId, new Dictionary<string, InventorySlot>());
            if (!globalInventory[TypeId].ContainsKey(SubtypeId))
                globalInventory[TypeId].Add(SubtypeId, new InventorySlot());

            globalInventory[TypeId][SubtypeId].targetAmount += targetAmount;
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
            SortedSet<string> keys = new SortedSet<string>();
            if(globalInventory.ContainsKey(oreType))
                foreach(var kvp in globalInventory[oreType])
                    keys.Add(kvp.Key);
            if (globalInventory.ContainsKey(ingotType))
                foreach (var kvp in globalInventory[ingotType])
                    keys.Add(kvp.Key);
            foreach (string key in keys)
            {
                float ore = 0f;
                float ingots = 0f;
                float ratio = 1f;
                if (globalInventory[oreType].ContainsKey(key))
                    ore = (float)globalInventory[oreType][key].totalAmount;

                if (globalInventory[ingotType].ContainsKey(key))
                    ingots = (float) globalInventory[ingotType][key].totalAmount;
                if (globalInventory[ingotType].ContainsKey(key)
                    && globalInventory[ingotType][key].targetAmount > 0)
                {
                    ratio = ingots
                        / (float)globalInventory[ingotType][key].targetAmount;
                }

                materialText += String.Format("{0,-10}{1,8}{2,9} {3,6}\n",
                    key,
                    FormatNumber(ore),
                    FormatNumber(ingots),
                    FormatNumber(ratio));
            }
        }

        void UpdateInventory()
        {
            // clear inventory
            foreach (var entry in globalInventory)
            {
                foreach (var entry2 in entry.Value)
                {
                    entry2.Value.items.Clear();
                    entry2.Value.totalAmount = 0;
                }
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
            bool isCargo = (block is IMyCargoContainer);
            for (int i=0;i<block.InventoryCount;++i)
            {
                IMyInventory inventory = block.GetInventory(i);
                List<MyInventoryItem> items = new List<MyInventoryItem>();
                inventory.GetItems(items);
                for(int j=0; j<items.Count;j++)
                {
                    if (!globalInventory.ContainsKey(items[j].Type.TypeId))
                    {
                        globalInventory.Add(items[j].Type.TypeId, new Dictionary<string, InventorySlot>());
                        globalInventory[items[j].Type.TypeId].Add(items[j].Type.SubtypeId, new InventorySlot());
                    }
                    else if (!globalInventory[items[j].Type.TypeId].ContainsKey(items[j].Type.SubtypeId))
                        globalInventory[items[j].Type.TypeId].Add(items[j].Type.SubtypeId, new InventorySlot());

                    globalInventory[items[j].Type.TypeId][items[j].Type.SubtypeId].totalAmount += items[j].Amount;
                    if(isCargo)
                    {
                        globalInventory[items[j].Type.TypeId][items[j].Type.SubtypeId].items.Add(new ItemInInventory(inventory,j));
                    }
                }
            }
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

        void FindCargoSpace(
            MyInventoryItem item,
            IMyInventory currentInv,
            List<CargoContainer> inputContainers,
            List<CargoContainer> output)
        {
            //perfect match   contains item   sufficient space
            //1               1                  1 = 7 (ideal)
            //1               1                  0 = 6
            //1               0                  1 = 5
            //1               0                  0 = 4
            //0               1                  1 = 3
            //0               1                  0 = 2
            //0               0                  1 = 1
            //0               0                  0 = 0
            float itemVolume = ItemVolume(item);
            output.Clear();
            Echo("CP0");
            List<CargoContainer>[] sortLists = new List<CargoContainer>[8];
            for (int i = 0; i < 8; ++i)
                sortLists[i] = new List<CargoContainer>();
            Echo("CP1");
            foreach (CargoContainer container in inputContainers)
            {
                int parsedType = (int) ParseType(item);
                if (0 == (parsedType & container.acceptorBits)) continue;
                IMyInventory inv = container.container.GetInventory();
                if (!currentInv.CanTransferItemTo(inv,item.Type)) continue;
                float freeSpace = inv.MaxVolume.RawValue - inv.CurrentVolume.RawValue;
                if (freeSpace == 0f) continue;

                int sortKey = 0;

                if (freeSpace >= itemVolume)
                    sortKey ^= 1;
                if(container.Contains(item.Type))
                    sortKey ^= 2;
                if (parsedType == container.acceptorBits)
                    // container ONLY accepts this type of Item (e.g. ores)
                    sortKey ^= 4;
                Echo("SortKey=" + sortKey.ToString());
                sortLists[sortKey].Add(container);
            }

            Echo("CPX");
            for (int i=7; i>=0; --i)
                output.AddRange(sortLists[i]);
        }

        bool CheckFilterMatch(MyItemType type, List<string> filter)
        {
            if (filter == null) return true;
            if (filter.Count == 0) return true;
            for (int i = 0; i < filter.Count; i++)
            {
                if (type.TypeId.Contains(filter[i])) return true;
                if (type.SubtypeId.Contains(filter[i])) return true;
            }
            return false;
        }

        void MoveToContainer(
            MyInventoryItem item,
            IMyInventory currentInv,
            int itemIndex,
            List<CargoContainer> containers)
        {
            List<CargoContainer> candidateContainers = new List<CargoContainer>();
            FindCargoSpace(item, currentInv, containers, candidateContainers);
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

        void ClearAllInventories(
            IMyCubeGrid sourceGrid,
            IMyCubeGrid targetGrid,
            List<string> filter)
        {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks,
                x => x.HasInventory&& x.CubeGrid == sourceGrid);
            foreach (var block in blocks)
            {
                if (block is IMyReactor) continue;
                if (block is IMyGasGenerator) continue;
                if (block is IMySmallMissileLauncher) continue;
                if (block is IMySmallMissileLauncherReload) continue;
                if (block is IMySmallGatlingGun) continue;
                if (block is IMyLargeTurretBase) continue;
                for (int i=0; i<block.InventoryCount; ++i)
                {
                    ClearInventory(block.GetInventory(i), targetGrid, filter);
                }
            }
        }

        void ClearAssemblers()
        {
            foreach (IMyAssembler assembler in assemblers)
                ClearAssembler(assembler);
        }
        void ClearAssembler(IMyAssembler assembler)
        {
            ClearInventory(assembler.OutputInventory, assembler.CubeGrid);
            if (!clearIngotsFromIdleAssemblers) return;
            if (assembler.IsProducing) return;
            ClearInventory(assembler.InputInventory, assembler.CubeGrid);
        }

        void FillRefineries()
        {

        }

        void ClearRefineries()
        {
            foreach (IMyRefinery refinery in autoRefineries)
                ClearRefinery(refinery);
        }
        void ClearRefinery(IMyRefinery refinery)
        {
            for(int i=0; i<refinery.InventoryCount; ++i)
                ClearInventory(refinery.GetInventory(i), refinery.CubeGrid);
        }

        void ClearInventory(
            IMyInventory inv,
            IMyCubeGrid targetGrid,
            List<string> filter = null)
        {
            for (int j = inv.ItemCount - 1; j >= 0; --j)
            {
                MyInventoryItem item = (MyInventoryItem)inv.GetItemAt(j);
                if (!CheckFilterMatch(item.Type, filter)) continue;
                MoveToContainer(item, inv, j, cargoContainers[targetGrid]);
            }
        }
        void PushPullCargo(List<string> args)
        {
            // args[0] = "Push"/"Pull"
            // args[1] = connector name
            // args[2] = filter
            if (args.Count < 2 || args.Count > 3)
            {
                debugText = "Error: wrong syntax for command \""+args[0]+"\":\n";
                debugText += args[0]+" <Connector Name> [Filter]";
                return;
            }
            if (args.Count == 2) args.Add(""); // empty filter

            IMyShipConnector connector = GridTerminalSystem.GetBlockWithName(args[1]) as IMyShipConnector;
            if(connector == null)
            {
                debugText = "Error: no ship connector named \"" + args[1] + "\" found\n";
                return;
            }
            if(connector.Status != MyShipConnectorStatus.Connected)
            {
                debugText = "Error: ship connector named \"" + args[1] + "\" is not connected\n";
                return;
            }

            IMyCubeGrid fromGrid, toGrid;
            if (args[0] == "Push")
            {
                toGrid = connector.OtherConnector.CubeGrid;
                fromGrid = connector.CubeGrid;
            }
            else
            {
                fromGrid = connector.OtherConnector.CubeGrid;
                toGrid = connector.CubeGrid;
            }
            List<string> filter = SplitWhitespace(args[2]);
            ClearAllInventories(fromGrid, toGrid, filter);
        }
        Program() {
            RebuildBlockLists();
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
        }

        void Update()
        {
            listUpdateCounter = (++listUpdateCounter % listUpdateFrequency);
            if (listUpdateCounter == 0)
                RebuildBlockLists();

            clearRefinereiesCounter = (++clearRefinereiesCounter % clearRefinereiesFrequency);
            if (clearRefinereiesCounter == 0)
            {
                ClearRefineries();
                FillRefineries();
            }

            clearAssemblersCounter = (++clearAssemblersCounter % clearAssemblersFrequency);
            if (clearAssemblersCounter == 0)
                ClearAssemblers();

            UpdateInventory();
            UpdateMaterialText();
            UpdateTextPanels();
        }

        void ParseArguments(string arguments)
        {
            List<string> args = SplitWhitespace(arguments);
            if (args.Count == 0)
            {
                debugText = "Error: empty command";
                return;
            }
            if (args[0] == "Pull" || args[0] == "Push")
            {
                PushPullCargo(args);
            }
            else if (args[0] == "Sort")
            {
                ClearAllInventories(Me.CubeGrid, Me.CubeGrid, null);
            }
        }

        void Main(string arguments)
        {
            if (arguments.Length != 0)
                ParseArguments(arguments);
            else
                Update();
        }

        #region post_script
    }
}
#endregion post_script