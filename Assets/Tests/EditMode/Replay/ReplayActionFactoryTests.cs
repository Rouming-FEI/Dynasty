using Core.Command;
using Core.GAS;
using Core.Replay;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Locations;
using Gameplay.Movement;
using NUnit.Framework;

namespace Dynasty.Tests.Replay
{
    public class ReplayActionFactoryTests
    {
        [Test]
        public void Factory_RoundTripsUseSkill()
        {
            var cmd = new UseSkillCommand(3, 1, 2, 0);
            var r = ReplayActionFactory.ToRecord(cmd);
            Assert.That(r.Kind, Is.EqualTo(ReplayActionKind.UseSkill));
            Assert.That(r.IntArg1, Is.EqualTo(2));
            Assert.That(r.IntArg2, Is.EqualTo(0));
            ICommand back = ReplayActionFactory.Create(r, 3);
            var use = back as UseSkillCommand;
            Assert.That(use, Is.Not.Null);
            Assert.That(use.SkillIndex, Is.EqualTo(2));
            Assert.That(use.TargetPlayerId, Is.EqualTo(0));
            Assert.That(use.Tick, Is.EqualTo(3));
            Assert.That(use.PlayerId, Is.EqualTo(1));
        }

        [Test]
        public void Factory_RoundTripsAttackAndMove()
        {
            var dmg = new DamageCommand(1, 2, 3, 1.5f);
            var rd = ReplayActionFactory.ToRecord(dmg);
            Assert.That(rd.Kind, Is.EqualTo(ReplayActionKind.Attack));
            var backDmg = (DamageCommand)ReplayActionFactory.Create(rd, 1);
            Assert.That(backDmg.TargetPlayerId, Is.EqualTo(3));
            Assert.That(backDmg.Amount, Is.EqualTo(1.5f).Within(1e-4f));

            var mv = new MoveCommand(2, 0, 5);
            var rm = ReplayActionFactory.ToRecord(mv);
            Assert.That(rm.Kind, Is.EqualTo(ReplayActionKind.Move));
            Assert.That(((MoveCommand)ReplayActionFactory.Create(rm, 2)).TargetLocationId, Is.EqualTo(5));
        }

        [Test]
        public void Factory_UnknownKind_ReturnsNull()
        {
            var r = new ReplayActionRecord { Kind = ReplayActionKind.Unknown, PlayerId = 0 };
            Assert.That(ReplayActionFactory.Create(r, 0), Is.Null);
        }

        [Test]
        public void Factory_Attack_RoundTripsDamageType()
        {
            var dmg = new DamageCommand(1, 2, 3, 2f, DamageType.True);
            var rd = ReplayActionFactory.ToRecord(dmg);
            var backDmg = (DamageCommand)ReplayActionFactory.Create(rd, 1);
            Assert.That(backDmg.Type, Is.EqualTo(DamageType.True), "Attack 记录应保留伤害类型");
        }

        private static ICommand MakeCommand(ReplayActionKind kind)
        {
            switch (kind)
            {
                case ReplayActionKind.UseSkill: return new UseSkillCommand(1, 0, 2, 3);
                case ReplayActionKind.Move: return new MoveCommand(1, 0, 5);
                case ReplayActionKind.WasteAP: return new WasteAPCommand(1, 0);
                case ReplayActionKind.UseItem: return new UseItemCommand(1, 0, 2, 3);
                case ReplayActionKind.PickupItem: return new PickupItemCommand(1, 0, 7);
                case ReplayActionKind.ThrowHammer: return new ThrowHammerCommand(1, 0, 3);
                case ReplayActionKind.Demolish: return new DemolishCommand(1, 0);
                case ReplayActionKind.Reload: return new ReloadCommand(1, 0);
                case ReplayActionKind.FireGun: return new FireGunCommand(1, 0, 3);
                case ReplayActionKind.SeedPlant: return new SeedPlantCommand(1, 0);
                case ReplayActionKind.SeedRemove: return new SeedRemoveCommand(1, 0);
                case ReplayActionKind.DrawBlade: return new DrawBladeCommand(1, 0);
                case ReplayActionKind.SheatheBlade: return new SheatheBladeCommand(1, 0);
                case ReplayActionKind.Unbind: return new UnbindCommand(1, 0);
                case ReplayActionKind.ToggleDoor: return new ToggleDoorCommand(1, 0);
                case ReplayActionKind.ToggleTideSwitch: return new ToggleTideSwitchCommand(1, 0);
                case ReplayActionKind.RemoveThreshold: return new RemoveThresholdCommand(1, 0);
                case ReplayActionKind.MountTurret: return new MountTurretCommand(1, 0);
                case ReplayActionKind.LoadTurret: return new LoadTurretCommand(1, 0);
                case ReplayActionKind.AimTurret: return new AimTurretCommand(1, 0);
                case ReplayActionKind.FireTurret: return new FireTurretCommand(1, 0, 3);
                case ReplayActionKind.DismountTurret: return new DismountTurretCommand(1, 0);
                case ReplayActionKind.Attack: return new DamageCommand(1, 0, 3, 1.5f);
                case ReplayActionKind.ChaosSimulate: return new ChaosSimulateCommand(1, 0, 3, 1);
                default: return null;
            }
        }

        [Test]
        public void Factory_AllKinds_RoundTrip()
        {
            foreach (ReplayActionKind kind in System.Enum.GetValues(typeof(ReplayActionKind)))
            {
                if (kind == ReplayActionKind.Unknown) continue;
                ICommand cmd = MakeCommand(kind);
                Assert.That(cmd, Is.Not.Null, $"缺少 {kind} 的构造");
                ReplayActionRecord r = ReplayActionFactory.ToRecord(cmd);
                Assert.That(r.Kind, Is.EqualTo(kind), $"{kind} ToRecord 错配");
                ICommand back = ReplayActionFactory.Create(r, 1);
                Assert.That(back, Is.Not.Null, $"{kind} Create 返回 null");
                ReplayActionRecord r2 = ReplayActionFactory.ToRecord(back);
                Assert.That(r2.Kind, Is.EqualTo(kind), $"{kind} 往返错配");
                Assert.That(r2.IntArg1, Is.EqualTo(r.IntArg1), $"{kind} IntArg1 往返不一致");
                Assert.That(r2.IntArg2, Is.EqualTo(r.IntArg2), $"{kind} IntArg2 往返不一致");
                Assert.That(r2.FloatArg, Is.EqualTo(r.FloatArg), $"{kind} FloatArg 往返不一致");
            }
        }
    }
}
