using Core.Command;
using Core.GAS;
using Gameplay.Characters;
using Gameplay.Combat;
using Gameplay.Items;
using Gameplay.Locations;
using Gameplay.Movement;

namespace Core.Replay
{
    /// <summary>输入命令 ↔ ReplayActionRecord 双向映射。</summary>
    public static class ReplayActionFactory
    {
        /// <summary>把输入命令参数化为记录；不支持的类型返回 Kind=255 的记录。</summary>
        public static ReplayActionRecord ToRecord(ICommand cmd)
        {
            var r = new ReplayActionRecord
            {
                Tick = cmd.Tick,
                PlayerId = cmd.PlayerId,
                Kind = ReplayActionKind.Unknown
            };
            switch (cmd)
            {
                case UseSkillCommand c:
                    r.Kind = ReplayActionKind.UseSkill; r.IntArg1 = c.SkillIndex; r.IntArg2 = c.TargetPlayerId; break;
                case MoveCommand c:
                    r.Kind = ReplayActionKind.Move; r.IntArg1 = c.TargetLocationId; break;
                case WasteAPCommand c:
                    r.Kind = ReplayActionKind.WasteAP; break;
                case UseItemCommand c:
                    r.Kind = ReplayActionKind.UseItem; r.IntArg1 = c.ItemIndex; r.IntArg2 = c.TargetPlayerId; break;
                case PickupItemCommand c:
                    r.Kind = ReplayActionKind.PickupItem; r.IntArg1 = c.ItemId; break;
                case ThrowHammerCommand c:
                    r.Kind = ReplayActionKind.ThrowHammer; r.IntArg1 = c.TargetPlayerId; break;
                case DemolishCommand c:
                    r.Kind = ReplayActionKind.Demolish; break;
                case ReloadCommand c:
                    r.Kind = ReplayActionKind.Reload; break;
                case FireGunCommand c:
                    r.Kind = ReplayActionKind.FireGun; r.IntArg1 = c.TargetPlayerId; break;
                case SeedPlantCommand c:
                    r.Kind = ReplayActionKind.SeedPlant; break;
                case SeedRemoveCommand c:
                    r.Kind = ReplayActionKind.SeedRemove; break;
                case DrawBladeCommand c:
                    r.Kind = ReplayActionKind.DrawBlade; break;
                case SheatheBladeCommand c:
                    r.Kind = ReplayActionKind.SheatheBlade; break;
                case UnbindCommand c:
                    r.Kind = ReplayActionKind.Unbind; break;
                case ToggleDoorCommand c:
                    r.Kind = ReplayActionKind.ToggleDoor; break;
                case ToggleTideSwitchCommand c:
                    r.Kind = ReplayActionKind.ToggleTideSwitch; break;
                case RemoveThresholdCommand c:
                    r.Kind = ReplayActionKind.RemoveThreshold; break;
                case MountTurretCommand c:
                    r.Kind = ReplayActionKind.MountTurret; break;
                case LoadTurretCommand c:
                    r.Kind = ReplayActionKind.LoadTurret; break;
                case AimTurretCommand c:
                    r.Kind = ReplayActionKind.AimTurret; break;
                case FireTurretCommand c:
                    r.Kind = ReplayActionKind.FireTurret; r.IntArg1 = c.TargetPlayerId; break;
                case DismountTurretCommand c:
                    r.Kind = ReplayActionKind.DismountTurret; break;
                case ChaosSimulateCommand c:
                    r.Kind = ReplayActionKind.ChaosSimulate; r.IntArg1 = c.ItemId; r.IntArg2 = c.TargetPlayerId; break;
                case DamageCommand c:
                    r.Kind = ReplayActionKind.Attack; r.IntArg1 = c.TargetPlayerId; r.FloatArg = c.Amount;
                    r.IntArg3 = (int)c.Type; break;
                default:
                    break; // 预置 Kind=Unknown 已生效
            }
            return r;
        }

        /// <summary>按记录重建命令实例（tick 以记录为准）；未知 Kind 返回 null。</summary>
        public static ICommand Create(ReplayActionRecord r, int tick)
        {
            int pid = r.PlayerId;
            switch (r.Kind)
            {
                case ReplayActionKind.UseSkill: return new UseSkillCommand(tick, pid, r.IntArg1, r.IntArg2);
                case ReplayActionKind.Move: return new MoveCommand(tick, pid, r.IntArg1);
                case ReplayActionKind.WasteAP: return new WasteAPCommand(tick, pid);
                case ReplayActionKind.UseItem: return new UseItemCommand(tick, pid, r.IntArg1, r.IntArg2);
                case ReplayActionKind.PickupItem: return new PickupItemCommand(tick, pid, r.IntArg1);
                case ReplayActionKind.ThrowHammer: return new ThrowHammerCommand(tick, pid, r.IntArg1);
                case ReplayActionKind.Demolish: return new DemolishCommand(tick, pid);
                case ReplayActionKind.Reload: return new ReloadCommand(tick, pid);
                case ReplayActionKind.FireGun: return new FireGunCommand(tick, pid, r.IntArg1);
                case ReplayActionKind.SeedPlant: return new SeedPlantCommand(tick, pid);
                case ReplayActionKind.SeedRemove: return new SeedRemoveCommand(tick, pid);
                case ReplayActionKind.DrawBlade: return new DrawBladeCommand(tick, pid);
                case ReplayActionKind.SheatheBlade: return new SheatheBladeCommand(tick, pid);
                case ReplayActionKind.Unbind: return new UnbindCommand(tick, pid);
                case ReplayActionKind.ToggleDoor: return new ToggleDoorCommand(tick, pid);
                case ReplayActionKind.ToggleTideSwitch: return new ToggleTideSwitchCommand(tick, pid);
                case ReplayActionKind.RemoveThreshold: return new RemoveThresholdCommand(tick, pid);
                case ReplayActionKind.MountTurret: return new MountTurretCommand(tick, pid);
                case ReplayActionKind.LoadTurret: return new LoadTurretCommand(tick, pid);
                case ReplayActionKind.AimTurret: return new AimTurretCommand(tick, pid);
                case ReplayActionKind.FireTurret: return new FireTurretCommand(tick, pid, r.IntArg1);
                case ReplayActionKind.DismountTurret: return new DismountTurretCommand(tick, pid);
                case ReplayActionKind.ChaosSimulate: return new ChaosSimulateCommand(tick, pid, r.IntArg1, r.IntArg2);
                case ReplayActionKind.Attack: return new DamageCommand(tick, pid, r.IntArg1, r.FloatArg,
                    (DamageType)r.IntArg3);
                default: return null;
            }
        }
    }
}
