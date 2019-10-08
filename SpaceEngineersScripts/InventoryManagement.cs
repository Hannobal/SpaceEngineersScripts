﻿#region pre_script

using System;
using System.Linq;
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

        VRage.MyFixedPoint maxAutoRefineryOreAmount = 10000;

        readonly int listUpdateFrequency = 10;
        readonly int clearRefinereiesFrequency = 1;
        readonly int clearAssemblersFrequency = 10;
        readonly bool clearIngotsFromIdleAssemblers = true;

        /**********************************************************************
         * END OF CUSTOMIZATION SECTION
         *********************************************************************/
        
        static readonly string oreType = "MyObjectBuilder_Ore";
        static readonly string ingotType = "MyObjectBuilder_Ingot";
        static readonly string componentType = "MyObjectBuilder_Component";
        static readonly string ammoType = "MyObjectBuilder_AmmoMagazine";

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

            private VRage.MyFixedPoint targetAmount = 0;
            private VRage.MyFixedPoint totalAmount = 0;
            private VRage.MyFixedPoint cargoAmount = 0;

            public VRage.MyFixedPoint TargetAmount
            {
                get { return targetAmount; }
            }
            public VRage.MyFixedPoint TotalAmount
            {
                get { return totalAmount; }
            }
            public VRage.MyFixedPoint CargoAmount
            {
                get { return cargoAmount; }
            }
            public float Ratio
            {
                get {
                    if (targetAmount > 0)
                        return (float)totalAmount / (float)targetAmount;
                    else
                        return 1.0f;
                }
            }
            public void Clear()
            {
                totalAmount = 0;
                cargoAmount = 0;
            }
            public void AddAmount(VRage.MyFixedPoint amount)
            {
                totalAmount += amount;
            }
            public void AddCargoAmount(VRage.MyFixedPoint amount)
            {
                cargoAmount += amount;
            }

            public void AddTargetAmount(VRage.MyFixedPoint amount)
            {
                targetAmount += amount;
            }
        };

        Dictionary<string, Dictionary<string, InventorySlot>> globalInventory =
            new Dictionary<string, Dictionary<string, InventorySlot>>();

        string autoRefineryMaterial = "";
        string materialText = "";
        string componentText = "";
        string ammoText = "";
        string otherItemsText = "";
        string systemText = "";
        string debugText = "";

        int listUpdateCounter = 0;
        int clearRefinereiesCounter = 0;
        int clearAssemblersCounter = 0;

        List<IMyTextPanel> textPanels = new List<IMyTextPanel>();
        Dictionary<IMyCubeGrid, List<CargoContainer>> cargoContainers
            = new Dictionary<IMyCubeGrid, List<CargoContainer>>();
        List<IMyRefinery> autoRefineries = new List<IMyRefinery>();
        List<IMyAssembler> allAssemblers = new List<IMyAssembler>();
        List<IMyAssembler> autoAssemblers = new List<IMyAssembler>();
        SortedSet<string> availableMaterials = new SortedSet<string>();

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
            globalInventory.Clear();
            globalInventory.Add(oreType, new Dictionary<string, InventorySlot>());
            globalInventory.Add(ingotType, new Dictionary<string, InventorySlot>());
            globalInventory.Add(componentType, new Dictionary<string, InventorySlot>());
            globalInventory.Add(ammoType, new Dictionary<string, InventorySlot>());
        }

        bool CheckGlobalInventorySlot(
            string TypeId,
            string SubtypeId)
        {
            if (!globalInventory.ContainsKey(TypeId))
                return false;
            if (!globalInventory[TypeId].ContainsKey(SubtypeId))
                return false;
            return true;
        }

        void AddGlobalInventorySlot(
            string TypeId,
            string SubtypeId)
        {
            if (!globalInventory.ContainsKey(TypeId))
            {
                globalInventory.Add(TypeId, new Dictionary<string, InventorySlot>());
                globalInventory[TypeId].Add(SubtypeId, new InventorySlot());
            }
            else if (!globalInventory[TypeId].ContainsKey(SubtypeId))
                globalInventory[TypeId].Add(SubtypeId, new InventorySlot());
        }

        void AddToGlobalInventory(
            MyInventoryItem item, bool isInCargo)
        {
            AddGlobalInventorySlot(item.Type.TypeId, item.Type.SubtypeId);
            globalInventory[item.Type.TypeId][item.Type.SubtypeId].AddAmount(item.Amount);
            if(isInCargo)
                globalInventory[item.Type.TypeId][item.Type.SubtypeId].AddCargoAmount(item.Amount);
        }

        void AddToGlobalInventory(
            string TypeId,
            string SubtypeId,
            VRage.MyFixedPoint targetAmount)
        {
            AddGlobalInventorySlot(TypeId, SubtypeId);
            globalInventory[TypeId][SubtypeId].AddTargetAmount(targetAmount);
        }

        void UpdateTextPanels()
        {
            foreach (IMyTextPanel panel in textPanels) {
                string lowerName = panel.CustomName.ToLower();
                string panelText = "";
                if (lowerName.Contains("material"))
                    panelText += materialText;

                if (lowerName.Contains("component"))
                    panelText += componentText;

                if (lowerName.Contains("ammo")
                    || lowerName.Contains("ammunition"))
                    panelText += ammoText;

                if (lowerName.Contains("other")
                    || lowerName.Contains("ammunition"))
                    panelText += otherItemsText;

                if (lowerName.Contains("debug"))
                    panelText += debugText;

                if (lowerName.Contains("system"))
                    panelText += systemText;

                panel.WriteText(panelText);
                panel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                panel.Font = "Monospace";
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
            allAssemblers.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyAssembler>(allAssemblers,
                x => x.CubeGrid == Me.CubeGrid
            );
            autoAssemblers.Clear();
            foreach(IMyAssembler assembler in allAssemblers)
            {
                if (!assembler.CustomName.Contains("Auto")) continue;
                autoAssemblers.Add(assembler);
            }

            textPanels.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(textPanels,
                x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("Inventory")
            );
        }

        void BuildMaterialsList()
        {
            availableMaterials.Clear();
            if (globalInventory.ContainsKey(oreType))
                foreach (var kvp in globalInventory[oreType])
                    availableMaterials.Add(kvp.Key);
            if (globalInventory.ContainsKey(ingotType))
                foreach (var kvp in globalInventory[ingotType])
                    availableMaterials.Add(kvp.Key);
            // sync dictionaries for ores and ingots:
            foreach (string material in availableMaterials)
            {
                AddGlobalInventorySlot(oreType, material);
                AddGlobalInventorySlot(ingotType, material);
            }
        }

        void UpdateMaterialText()
        {
            materialText = String.Format("{0,-10}{1,8}{2,9}\n", "Material", "Ore ", "Refined ");
            foreach (string key in availableMaterials)
            {
                materialText += String.Format("{0,-10}{1,8}{2,9} {3,6}\n",
                    key,
                    FormatNumber((float)globalInventory[oreType][key].TotalAmount),
                    FormatNumber((float)globalInventory[ingotType][key].TotalAmount),
                    FormatNumber(globalInventory[ingotType][key].Ratio));
            }
            if (autoRefineryMaterial != "")
                materialText += "Automatic refineries processing " + autoRefineryMaterial +"\n";
            materialText += "\n";
        }

        void UpdateInventoryTexts()
        {
            string format = "{0,-20}{1,8}{2,8}\n";
            componentText = String.Format(format, "Component", "Amount", "Ratio"); ;
            ammoText = String.Format(format, "Ammunition", "Amount", "Ratio"); ;
            otherItemsText = String.Format(format, "Other items", "Amount", "Ratio"); ;
            foreach (var kvp in globalInventory)
            {
                if (kvp.Key == ingotType || kvp.Key == oreType)
                    continue;
                else if (kvp.Key == componentType)
                    AddInventoryText(kvp.Value, format, ref componentText);
                else if (kvp.Key == ammoType)
                    AddInventoryText(kvp.Value, format, ref ammoText);
                else
                    AddInventoryText(kvp.Value, format, ref otherItemsText);
            }
            componentText += "\n";
            ammoText += "\n";
            otherItemsText += "\n";
        }

        void AddInventoryText(
            Dictionary<string,InventorySlot> inv,
            string format,
            ref string text)
        {
            SortedSet<string> keys = new SortedSet<string>(inv.Keys);
            foreach (var key in keys)
            {
                text += String.Format(format,
                    key,
                    FormatNumber((float)inv[key].TotalAmount),
                    FormatNumber(inv[key].Ratio));
            }
        }

        void UpdateInventory()
        {
            ClearGlobalInventory();
            AddTargetAmountsToGlobalInventory();
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
                foreach (MyInventoryItem item in items)
                {
                    AddToGlobalInventory(item, isCargo);
                }
            }
            BuildMaterialsList();
        }

        void AddTargetAmountsToGlobalInventory()
        {
            string[] lines = Me.CustomData.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );
            string type = "";
            for (int lineNo = 0; lineNo < lines.Length; ++lineNo)
            {
                string line = lines[lineNo].Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#') continue;
                List<string> words = SplitWhitespace(line);
                if (words.Count == 1)
                {
                    string lowerWord = words[0].ToLower();
                    if (lowerWord.Contains("ingot"))
                        type = ingotType;
                    else if (lowerWord.Contains("component"))
                        type = componentType;
                    else if (lowerWord.Contains("ammo"))
                        type = ammoType;
                    else
                        type = words[0];
                }
                else if (words.Count == 2)
                {
                    if (type.Length == 0)
                    {
                        Echo("syntax error on line " + lineNo.ToString() + " of Custom Data: "
                            + " No type defined before Subtype");
                    }
                    else
                    {
                        int amount;
                        if (int.TryParse(words[1], out amount)) {
                            AddToGlobalInventory(type, words[0], amount);
                        }
                        else
                        {
                            Echo("syntax error on line " + lineNo.ToString() + " of Custom Data" +
                                "Could not parse " + words[1] + " as integer");
                        }
                    }
                }
                else
                {
                    Echo("syntax error on line " + lineNo.ToString() + " of Custom Data");
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
            List<CargoContainer>[] sortLists = new List<CargoContainer>[8];
            for (int i = 0; i < 8; ++i)
                sortLists[i] = new List<CargoContainer>();
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
                sortLists[sortKey].Add(container);
            }

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
            foreach (IMyAssembler assembler in allAssemblers)
                ClearAssembler(assembler);
        }
        void ClearAssembler(IMyAssembler assembler)
        {
            ClearInventory(assembler.OutputInventory, assembler.CubeGrid);
            if (!clearIngotsFromIdleAssemblers) return;
            if (assembler.IsProducing) return;
            ClearInventory(assembler.InputInventory, assembler.CubeGrid);
        }

        void UpdateProduction()
        {
            // update production list:
            Dictionary<string, VRage.MyFixedPoint> inProduction =
                new Dictionary<string, VRage.MyFixedPoint>();
            foreach(IMyAssembler assembler in autoAssemblers)
            {
                List<MyProductionItem> items = new List<MyProductionItem>();
                assembler.GetQueue(items);
                foreach (MyProductionItem item in items)
                {
                    if (!inProduction.ContainsKey(item.BlueprintId.SubtypeName))
                        inProduction.Add(item.BlueprintId.SubtypeName, item.Amount);
                    else
                        inProduction[item.BlueprintId.SubtypeName] += item.Amount;
                }
            }

            // find which is needed:
            Dictionary<string, InventorySlot> missingTypes = new Dictionary<string, InventorySlot>();
            foreach (var kvp in globalInventory[componentType])
            {
                if (kvp.Value.TargetAmount <= 0) continue;
                InventorySlot slot = new InventorySlot();
                slot.AddAmount(kvp.Value.TotalAmount);
                slot.AddTargetAmount(kvp.Value.TargetAmount);
                if (inProduction.ContainsKey(kvp.Key))
                    slot.AddAmount(inProduction[kvp.Key]);
                if (slot.TotalAmount >= slot.TargetAmount) continue;
                missingTypes.Add(kvp.Key, slot);
            }

            // now add to production
            foreach (var kvp in missingTypes.OrderBy(p => p.Value.Ratio))
            {
                debugText += String.Format("{0:-15} {1:0.000000}\n", kvp.Key,kvp.Value.Ratio);
                // doesn't work out of the box (throws "not set to an instance of an object") exception
                MyDefinitionId blueprint = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/" + kvp.Key);
                //new MyItemType(componentType, kvp.Key);
                try
                {
                    autoAssemblers[0].AddQueueItem(blueprint, kvp.Value.TargetAmount - kvp.Value.TotalAmount);
                } catch
                {
                    try
                    {
                        blueprint = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/" + kvp.Key+"Component");
                        autoAssemblers[0].AddQueueItem(blueprint, kvp.Value.TargetAmount - kvp.Value.TotalAmount);
                    }
                    catch
                    {
                        debugText += ("MyObjectBuilder_BlueprintDefinition/" + kvp.Key + "Component\n");
                    }
                }
                
            }
        }

        void FillRefineries()
        {
            autoRefineryMaterial = "";
            if (autoRefineries.Count == 0) return;
            // first find the material wich needs to be refined
            float minRatio = 999999.9f;
            foreach(string material in availableMaterials)
            {
                if (globalInventory[oreType][material].CargoAmount == 0)
                    continue;
                if (globalInventory[ingotType][material].TargetAmount == 0)
                    continue;
                float ratio = globalInventory[ingotType][material].Ratio;
                if (ratio<minRatio)
                {
                    minRatio = ratio;
                    autoRefineryMaterial = material;
                }
            }
            if (autoRefineryMaterial == "")
                return;

            VRage.MyFixedPoint amountPerRef =
                globalInventory[oreType][autoRefineryMaterial].CargoAmount;
            amountPerRef.RawValue /= autoRefineries.Count;
            if (amountPerRef > maxAutoRefineryOreAmount)
                amountPerRef = maxAutoRefineryOreAmount;

            foreach (var refinery in autoRefineries)
                MoveItemFromStorageToInventory(
                    new MyItemType(oreType, autoRefineryMaterial),
                    refinery.GetInventory(0),
                    amountPerRef);
        }

        void MoveItemFromStorageToInventory(
            MyItemType type,
            IMyInventory targetInv,
            VRage.MyFixedPoint amount
            )
        {
            VRage.MyFixedPoint remainingAmount = amount;
            foreach(CargoContainer container in cargoContainers[Me.CubeGrid])
            {
                IMyInventory inventory = container.container.GetInventory();
                if (!inventory.IsConnectedTo(targetInv)) continue;

                for (int i = 0; i < inventory.ItemCount; ++i)
                {
                    MyInventoryItem item = (MyInventoryItem) inventory.GetItemAt(i);
                    if (item.Type != type) continue;
                    if (!inventory.CanTransferItemTo(targetInv, item.Type))
                        continue;
                    VRage.MyFixedPoint transferrableAmount = remainingAmount;
                    if (item.Amount < transferrableAmount)
                        transferrableAmount = item.Amount;
                    if(inventory.TransferItemTo(targetInv, item, transferrableAmount))
                    {
                        remainingAmount -= transferrableAmount;
                        if (remainingAmount == 0) return;
                    }
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
                ClearRefineries();

            clearAssemblersCounter = (++clearAssemblersCounter % clearAssemblersFrequency);
            if (clearAssemblersCounter == 0)
                ClearAssemblers();

            UpdateInventory();

            if (clearRefinereiesCounter == 0)
            {
                FillRefineries();
                UpdateProduction();
            }

            UpdateMaterialText();
            UpdateInventoryTexts();
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