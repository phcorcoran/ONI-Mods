﻿using System.Linq;
using FMODUnity;
using KSerialization;
using Newtonsoft.Json;
using UnityEngine;
using MemberSerialization = KSerialization.MemberSerialization;
using Random = UnityEngine.Random;

#pragma warning disable 649

namespace MightyVincent
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public class LaserTurret : StateMachineComponent<LaserTurret.Instance>
    {
        private static readonly HashedString _HASH_ROTATION = (HashedString) "rotation";
        [EventRef] private string _rotateSound = "AutoMiner_rotate";
        [MyCmpGet] private Rotatable _rotatable;
        [MyCmpReq] private Operational _operational;

//        [MyCmpGet] private KSelectable _selectable;

        [SerializeField] public Color noFilterTint = FilteredStorage.NO_FILTER_TINT;
        [SerializeField] public Color filterTint = FilteredStorage.FILTER_TINT;

        public int visualizerX;
        public int visualizerY;
        public int visualizerWidth;
        public int visualizerHeight;

        private const float _TURN_RATE = 180f;
        private float _armRot = -45f;
        private Vector2I _xy0;
        private Rect _rangeRect;
        private GameObject _armGo;
        private KBatchedAnimController _armAnimCtrl;
        private KAnimLink _link;
        private LoopingSounds _loopingSounds;
        private bool _rotateSoundPlaying;
        private GameObject _hitEffectPrefab;
        private GameObject _hitEffect;
        private KPrefabID _target;
        private int _targetCell;
        private Vector3 _targetDirection;

        private bool IsLogicConnected => Game.Instance.logicCircuitManager.GetNetworkForCell(GetComponent<LogicPorts>().GetPortCell(LogicOperationalController.PORT_ID)) != null;

        private bool IsTargetMoved => _target.transform.hasChanged || _targetCell != GetTargetCell;

        private int GetTargetCell => Grid.PosToCell(_target);

        private Vector3 GetTargetPosCcc => Grid.CellToPosCCC(GetTargetCell, Grid.SceneLayer.FXFront2);

        private Vector3 GetTargetDirection
        {
            get
            {
                var posCcc = GetTargetPosCcc;
                posCcc.z = 0.0f;
                var position = _armGo.transform.position;
                position.z = 0.0f;
                return posCcc - position;
            }
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            simRenderLoadBalance = true;
            GetComponent<TreeFilterable>().OnFilterChanged += OnFilterChanged;
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            // self position
            _xy0 = Grid.PosToXY(transform.position);

            // range detector
            var anchorMinRotatedOffset = _rotatable.GetRotatedCellOffset(new CellOffset(visualizerX, visualizerY));
            var anchorMinRotated = Grid.CellToPos2D(Grid.OffsetCell(Grid.PosToCell(_xy0), anchorMinRotatedOffset));
            var sizeRotatedOffset = _rotatable.GetRotatedCellOffset(new CellOffset(visualizerWidth - 1, visualizerHeight - 1));
            _rangeRect = new Rect(anchorMinRotated, sizeRotatedOffset.ToVector3());

            // anim
            _hitEffectPrefab = Assets.GetPrefab((Tag) EffectConfigs.AttackSplashId);
            var component = GetComponent<KBatchedAnimController>();
            var armName = component.name + ".gun";
            _armGo = new GameObject(armName);
            _armGo.SetActive(false);
            _armGo.transform.parent = component.transform;
            _loopingSounds = _armGo.AddComponent<LoopingSounds>();
            _rotateSound = GlobalAssets.GetSound(_rotateSound);
            _armGo.AddComponent<KPrefabID>().PrefabTag = new Tag(armName);
            _armAnimCtrl = _armGo.AddComponent<KBatchedAnimController>();
            _armAnimCtrl.AnimFiles = new[] {component.AnimFiles[0]};
            _armAnimCtrl.initialAnim = "gun";
            _armAnimCtrl.isMovable = true;
            _armAnimCtrl.sceneLayer = Grid.SceneLayer.TransferArm;
            component.SetSymbolVisiblity((KAnimHashedString) "gun_target", false);
            Vector3 column = component.GetSymbolTransform(new HashedString("gun_target"), out _).GetColumn(3);
            column.z = Grid.GetLayerZ(Grid.SceneLayer.TransferArm);
            _armGo.transform.SetPosition(column);
            _armGo.SetActive(true);
            _link = new KAnimLink(component, _armAnimCtrl);
            RotateArm(0f, 0f);
            ClearTarget();
            smi.StartSM();
            Subscribe((int) GameHashes.CopySettings, OnCopySettings);
        }

        private void RefreshTarget()
        {
            var creature = GetClosestAttackableCreature();
            if (creature == null) return;
            _target = creature;
            _targetCell = GetTargetCell;
            _targetDirection = GetTargetDirection;
            _operational.SetActive(true);
        }

        private void ClearTarget()
        {
            _target = null;
            _targetCell = int.MinValue;
            _targetDirection = Vector3.negativeInfinity;
            _operational.SetActive(false);
        }

        private KPrefabID GetClosestAttackableCreature()
        {
            var cavityInfo = Game.Instance.roomProber.GetCavityForCell(Grid.PosToCell(_xy0));
            if (cavityInfo == null) return null;

            KPrefabID targetCreature = null;
            float targetAge = int.MinValue;
            float targetIncubation = int.MaxValue;

            var tags = GetComponent<TreeFilterable>().GetTags();
            var creatures = cavityInfo.creatures.FindAll(id => id != null && id.GetComponent<Health>() != null && id.HasAnyTags(tags));

            var logicConnected = IsLogicConnected;

//            Debug.Log("-------------------------------------------------------------");
//            Debug.Log($"creatures: {cavityInfo.creatures.Count}");
            foreach (var creature in creatures)
            {
                if (logicConnected && creature.GetComponent<Health>().IsDefeated())
                {
                    // someone is still dying but not dead
                    return null;
                }
//                Debug.Log($"creature: {(creature == null).ToString()} {creature.ToString()} {JsonUtility.ToJson(creature)}");

                if (!IsAttackable(creature)) continue;
                // attackable
                var age = Db.Get().Amounts.Age.Lookup(creature).value;
                if (age > targetAge)
                {
                    // oldest
                    targetCreature = creature;
                    targetAge = age;
                }
                else if (age.Equals(targetAge))
                {
                    var incubationAmount = Db.Get().Amounts.Incubation.Lookup(creature);
                    if (incubationAmount == null || incubationAmount.value >= targetIncubation) continue;

                    // lowest incubation
                    targetCreature = creature;
                    targetIncubation = incubationAmount.value;
                }
            }

            return targetCreature;
        }

        private bool IsAttackable(KPrefabID creature)
        {
//            Health health;
            return creature != null
                   && !creature.HasTag(GameTags.Creatures.Bagged)
                   && !creature.HasTag(GameTags.Trapped)
//                   && (bool) (health = creature.GetComponent<Health>()) && !health.IsDefeated()
                   && IsReachable(creature.transform.position);
        }

        private bool IsReachable(Vector3 position)
        {
            var xy1 = Grid.PosToXY(position);
            return Grid.IsValidCell(Grid.PosToCell(position))
                   && _rangeRect.Contains(position, true)
                   && Grid.TestLineOfSight(_xy0.x, _xy0.y, xy1.x, xy1.y, AttackBlockingCallback);
        }

        public static bool AttackBlockingCallback(int cell)
        {
            return Grid.Foundation[cell] || Grid.HasDoor[cell] || Grid.Solid[cell];
        }

        private void AttackCreature(KPrefabID creature)
        {
            var health = creature.GetComponent<Health>();
            if (!(bool) health)
                return;
            health.Damage(Random.Range(0f, 1f) * creature.GetComponent<AttackableBase>().GetDamageMultiplier());
        }

        private bool IsAimed(out float deltaAngle)
        {
            /*
             moved -> updateDirection, updateAngle -> false
             
             !moved, !rightAngle -> updateAngle -> false 
             
             !moved, rightAngle -> true 
             */
            var moved = IsTargetMoved;
            if (moved)
            {
                _targetCell = GetTargetCell;
                _targetDirection = GetTargetDirection;
            }

            // angle
            deltaAngle = MathUtil.Wrap(-180f, 180f, MathUtil.AngleSigned(Vector3.up, Vector3.Normalize(_targetDirection), Vector3.forward) - _armRot);
            return !moved && Mathf.Approximately(deltaAngle, 0.0f);
        }

        // --------------------------------- states --------------------------------------
        private void OnCopySettings(object data)
        {
            var go = (GameObject) data;
            if (go == null || go.GetComponent<LaserTurret>() == null)
                return;
            ClearTarget();
            RefreshTarget();
        }

        private void OnFilterChanged(Tag[] tags)
        {
            GetComponent<KBatchedAnimController>().TintColour = tags == null || tags.Length == 0 ? noFilterTint : filterTint;
            ClearTarget();
            RefreshTarget();
        }

        public void ExitAttack()
        {
            StopAttackEffect();
            StopRotateSound();
            ClearTarget();
        }

        public void UpdateAttack(float dt)
        {
            // attackable
            if (!IsAttackable(_target))
            {
                StopAttackEffect();
                ClearTarget();
                return;
            }

            // aim
            if (!IsAimed(out var deltaAngle))
            {
                StopAttackEffect();
                // rotate
                StartRotateSound();
                RotateArm(deltaAngle, dt);
                return;
            }

            StopRotateSound();
            StartAttackEffect();

            // attack
            AttackCreature(_target);
        }

        private void StartAttackEffect()
        {
            // gun effect
            var animName = (HashedString) "gun_digging";
            if (_armAnimCtrl.CurrentAnim.hash != animName)
            {
                var armPosition = _armGo.transform.position;
                _armAnimCtrl.GetBatchInstanceData().SetClipRadius(armPosition.x, armPosition.y, Mathf.Clamp(_targetDirection.sqrMagnitude, 2f, float.MaxValue), true);
                _armAnimCtrl.Play(animName, KAnim.PlayMode.Loop);
            }

            // hit effect
            if (_hitEffectPrefab == null)
                return;
            if (_hitEffect != null)
            {
                if (_target.transform.hasChanged)
                {
                    // move hit effect
                    _hitEffect.transform.SetPositionAndRotation(_target.transform.position, _hitEffect.transform.rotation);
                }

                return;
            }

            // create hit effect
            _hitEffect = GameUtil.KInstantiate(_hitEffectPrefab, GetTargetPosCcc, Grid.SceneLayer.FXFront2);
            _hitEffect.SetActive(true);
            var component = _hitEffect.GetComponent<KBatchedAnimController>();
            component.sceneLayer = Grid.SceneLayer.FXFront2;
            component.initialMode = KAnim.PlayMode.Loop;
            component.enabled = true;
        }

        private void StopAttackEffect()
        {
            // gun effect
            var animName = (HashedString) "gun";
            if (_armAnimCtrl.CurrentAnim.hash != animName)
                _armAnimCtrl.Play(animName, KAnim.PlayMode.Loop);

            // hit effect
            if (_hitEffectPrefab == null || _hitEffect == null)
                return;
            _hitEffect.DeleteObject();
            _hitEffect = null;
        }

        private void RotateArm(float deltaAngle, float dt)
        {
            deltaAngle = Mathf.Clamp(deltaAngle, -_TURN_RATE * dt, _TURN_RATE * dt);
            _armRot += deltaAngle;
            _armRot = MathUtil.Wrap(-180f, 180f, _armRot);
            _armGo.transform.rotation = Quaternion.Euler(0.0f, 0.0f, _armRot);
            _loopingSounds.SetParameter(_rotateSound, _HASH_ROTATION, _armRot);
        }

        private void StartRotateSound()
        {
            if (_rotateSoundPlaying)
                return;
            _loopingSounds.StartSound(_rotateSound);
            _rotateSoundPlaying = true;
        }

        private void StopRotateSound()
        {
            if (!_rotateSoundPlaying)
                return;
            _loopingSounds.StopSound(_rotateSound);
            _rotateSoundPlaying = false;
        }


        public class Instance : GameStateMachine<States, Instance, LaserTurret, object>.GameInstance
        {
            public Instance(LaserTurret master)
                : base(master)
            {
            }
        }

        public class States : GameStateMachine<States, Instance, LaserTurret>
        {
            public State Off;
            public ReadyStates On;

            public override void InitializeStates(out BaseState baseState)
            {
                baseState = Off;
                root.DoNothing();
                Off
                    .PlayAnim("off")
                    .EventTransition(GameHashes.OperationalChanged, On,
                        smi => smi.GetComponent<Operational>().IsOperational);
                On.DefaultState(On.Idle)
                    .EventTransition(GameHashes.OperationalChanged, Off,
                        smi => !smi.GetComponent<Operational>().IsOperational);
                On.Idle
                    .PlayAnim("on")
                    .EventTransition(GameHashes.ActiveChanged, On.Attack,
                        smi => smi.GetComponent<Operational>().IsActive)
                    .Update((smi, dt) => smi.master.RefreshTarget(), UpdateRate.SIM_1000ms);
                On.Attack
                    .PlayAnim("working")
                    .Exit(smi => smi.master.ExitAttack())
                    .EventTransition(GameHashes.ActiveChanged, On.Idle,
                        smi => !smi.GetComponent<Operational>().IsActive)
                    .Update((smi, dt) => smi.master.UpdateAttack(dt), UpdateRate.SIM_33ms);
            }

            public class ReadyStates : State
            {
                public State Idle;
                public State Attack;
            }
        }
    }
}