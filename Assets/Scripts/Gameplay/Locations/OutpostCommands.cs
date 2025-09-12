using Core;
using Core.Command;
using Core.GAS;
using Gameplay.Characters;

namespace Gameplay.Locations
{
    /// <summary>
    /// 上塔：消耗 1 AP。须身处地狱前哨(8)且未在塔上。
    /// 添加 OnTurret + CannotAct 标签——塔上不可移动/攻击/用道具/放技能
    /// （由现有 CanMove/CanAct 检查自动阻断），只能操作炮台或下塔。
    /// </summary>
    public class MountTurretCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public MountTurretCommand() { }
        public MountTurretCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || !c.CanAct || c.ActionPoints <= 0) return;
            if (c.CurrentLocationId != OutpostRule.OutpostLocationId) return;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret)) return; // 已在塔上，无效操作不扣 AP

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.Tags.AddTag(GameplayTagConstants.OnTurret);
            c.ASC.Tags.AddTag(GameplayTagConstants.CannotAct);
        }
    }

    /// <summary>
    /// 装弹：消耗 1 AP。须在塔上（OnTurret）。设置 TurretLoaded 标签。
    /// 注意：塔上角色带 CannotAct，故炮台命令不检查 CanAct，只检查存活 + OnTurret。
    /// </summary>
    public class LoadTurretCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public LoadTurretCommand() { }
        public LoadTurretCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret)) return;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.TurretLoaded)) return; // 已装弹，无效操作不扣 AP

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.Tags.AddTag(GameplayTagConstants.TurretLoaded);
        }
    }

    /// <summary>瞄准：消耗 1 AP。须在塔上且已装弹。设置 TurretAimed 标签。</summary>
    public class AimTurretCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public AimTurretCommand() { }
        public AimTurretCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret)) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.TurretLoaded)) return;
            if (c.ASC.Tags.HasTag(GameplayTagConstants.TurretAimed)) return; // 已瞄准，无效操作不扣 AP

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            c.ASC.Tags.AddTag(GameplayTagConstants.TurretAimed);
        }
    }

    /// <summary>
    /// 发射：消耗 1 AP。须在塔上且已装弹+已瞄准。
    /// 对目标玩家造成 3 物理伤害（不限地点/距离），随后清除装弹+瞄准标签——
    /// 再次发射需重新装弹+瞄准。
    /// </summary>
    public class FireTurretCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int TargetPlayerId { get; set; }

        public FireTurretCommand() { }
        public FireTurretCommand(int tick, int playerId, int targetPlayerId)
        {
            Tick = tick; PlayerId = playerId; TargetPlayerId = targetPlayerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            GameplayTagContainer tags = c.ASC.Tags;
            if (!tags.HasTag(GameplayTagConstants.OnTurret)) return;
            if (!tags.HasTag(GameplayTagConstants.TurretLoaded)) return;
            if (!tags.HasTag(GameplayTagConstants.TurretAimed)) return;

            Character target = state.GetCharacterByPlayer(TargetPlayerId);
            if (target == null || target.IsDead) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);

            // 先清弹药/瞄准状态再结算伤害，避免伤害管线触发的连锁事件读到中间状态
            tags.RemoveTag(GameplayTagConstants.TurretLoaded);
            tags.RemoveTag(GameplayTagConstants.TurretAimed);

            var evt = new GameplayEvent(GameplayEventTypes.TurretFired, c.Id, target.Id);
            state.Events.Dispatch(GameplayEventTypes.TurretFired, ref evt);

            DamagePipeline.ApplyDamage(state, c.Id, target.Id, 3f, DamageType.Physical);
        }
    }

    /// <summary>
    /// 下塔：消耗 1 AP。清除全部炮台标签（含 CannotAct），并自受 1 物理伤害（跳下炮塔）。
    /// 伤害来源记为环境（-1），与实验室潮汐等地点伤害一致。
    /// </summary>
    public class DismountTurretCommand : ICommand
    {
        public int Tick { get; set; }
        public int PlayerId { get; set; }

        public DismountTurretCommand() { }
        public DismountTurretCommand(int tick, int playerId)
        {
            Tick = tick; PlayerId = playerId;
        }

        public void Execute(WorldState state)
        {
            Character c = state.GetCharacterByPlayer(PlayerId);
            if (c == null || c.IsDead || c.ActionPoints <= 0) return;
            if (!c.ASC.Tags.HasTag(GameplayTagConstants.OnTurret)) return;

            c.ASC.ModifyAttributeCurrentValue(AttributeConstants.ActionPoints, -1f);
            OutpostRule.ClearTurretTags(c);

            DamagePipeline.ApplyDamage(state, -1, c.Id, 1f, DamageType.Physical);
        }
    }
}
