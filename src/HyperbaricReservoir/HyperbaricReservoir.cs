using System.Reflection;
using Harmony;
using UnityEngine;

#pragma warning disable 649

namespace MightyVincent
{
    public class HyperbaricReservoir : KMonoBehaviour
    {
        private float _endMass;
        private MeterController _meter;
        private float _startMass;
        [MyCmpGet] private Storage _storage;

        [MyCmpReq] public Operational operational;
        private readonly FieldInfo _logicOperationalFlagGetter = AccessTools.Field(typeof(LogicOperationalController), "logicOperationalFlag");

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _meter = new MeterController(GetComponent<KBatchedAnimController>(), "meter_target", "meter", Meter.Offset.Infront, Grid.SceneLayer.NoLayer, "meter_fill", "meter_OL");
            Subscribe((int) GameHashes.OnStorageChange, OnStorageChange);
            Subscribe((int) HyperbaricReservoirHashes.OnConduitConsumerUpdateStart, OnConduitConsumerUpdateStart);
            Subscribe((int) HyperbaricReservoirHashes.OnConduitConsumerUpdateEnd, OnConduitConsumerUpdateEnd);
            Subscribe((int) HyperbaricReservoirHashes.OnConduitDispenserUpdateStart, OnConduitDispenserUpdateStart);
            OnStorageChange(null);
        }

        private void OnStorageChange(object data)
        {
            _meter.SetPositionPercent(Mathf.Clamp01(_storage.MassStored() / _storage.capacityKg));
        }

        private void OnConduitConsumerUpdateStart(object o)
        {
            GetComponent<ConduitConsumer>().alwaysConsume = GetComponent<EnergyConsumer>().IsPowered;
            if (!(o is Storage storage)) return;
            _startMass = storage.MassStored();
        }

        private void OnConduitConsumerUpdateEnd(object o)
        {
            if (!(o is Storage storage)) return;
            _endMass = storage.MassStored();
            operational.SetActive(_startMass < _endMass);
        }

        private void OnConduitDispenserUpdateStart(object o)
        {
            var logicOperationalFlag = (Operational.Flag) _logicOperationalFlagGetter.GetValue(GetComponent<LogicOperationalController>());
            GetComponent<ConduitDispenser>().alwaysDispense = GetComponent<BuildingEnabledButton>().IsEnabled && GetComponent<Operational>().GetFlag(logicOperationalFlag);
        }

    }
}