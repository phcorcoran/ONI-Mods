﻿using System.Linq;
using Harmony;
using UnityEngine;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable RedundantAssignment

namespace MightyVincent
{
    internal class SimplerPipPlantRulePatches
    {
        private const string _MOD_ID = "1859560108";
        private const string _CONFIG_FILE = "config.json";

        public static void OnLoad()
        {
            ConfigLoader.Load(_MOD_ID, _CONFIG_FILE);
        }
        
        [HarmonyPatch(typeof(SeedPlantingMonitor.Def), MethodType.Constructor)]
        internal class SeedPlantingMonitor_Def_Constructor
        {
            public static void Postfix(ref float ___searchMinInterval, ref float ___searchMaxInterval)
            {
                ___searchMinInterval = State.Config.searchMinInterval;
                ___searchMaxInterval = State.Config.searchMaxInterval;
            }
        }

        [HarmonyPatch(typeof(PlantableCellQuery), MethodType.Constructor)]
        internal class PlantableCellQuery_Constructor
        {
            public static void Postfix(ref int ___plantDetectionRadius, ref int ___maxPlantsInRadius)
            {
                ___plantDetectionRadius = State.Config.plantDetectionRadius;
                ___maxPlantsInRadius = State.Config.maxPlantsInRadius;
            }
        }

        [HarmonyPatch(typeof(PlantableCellQuery), "CountNearbyPlants")]
        internal class PlantableCellQuery_CountNearbyPlants
        {
            public static bool Prefix(int cell, int radius, out int __result)
            {
//                Debug.Log($"CountNearbyPlants Prefix: {cell}, {radius}");
                Grid.CellToXY(cell, out var x, out var y);
                var side = radius * 2 + 1;
                var x_bottomLeft = x - radius;
                var y_bottomLeft = y - radius;
                var pooledList = ListPool<ScenePartitionerEntry, GameScenePartitioner>.Allocate();

                GameScenePartitioner.Instance.GatherEntries(x_bottomLeft, y_bottomLeft, side, side, GameScenePartitioner.Instance.plants, pooledList);
                var countWithoutTreeBud = pooledList.Count(entry => !(bool) ((Component) entry.obj).GetComponent<TreeBud>());
//                var total = pooledList.Count;
                pooledList.Recycle();
//                Debug.Log($"countWithoutTreeBud: ({x},{y}), {countWithoutTreeBud} / {total}");
                __result = countWithoutTreeBud;
                return false;
            }
        }
    }
}