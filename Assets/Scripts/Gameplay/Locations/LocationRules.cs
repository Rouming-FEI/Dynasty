using Core;
using Gameplay.Characters;

namespace Gameplay.Locations
{
    /// <summary>
    /// 地点规则基类：为特殊地点挂载进入/离开钩子、目标可见性过滤与回合结束处理。
    /// 由 LocationManager 按 LocationId 注册到 Rules 数组，MoveCommand 在移动时触发 OnLeave/OnEnter。
    /// </summary>
    public abstract class LocationRule
    {
        public int LocationId;

        /// <summary>
        /// 角色能否进入该地点。MoveCommand 在扣除 AP 之前调用，
        /// 返回 false 时移动被拒绝且不消耗 AP（如实验室大门关闭）。
        /// </summary>
        public virtual bool CanEnter(WorldState state, Character c) => true;

        /// <summary>角色进入该地点时触发。</summary>
        public virtual void OnEnter(WorldState state, Character c) { }

        /// <summary>角色离开该地点时触发。</summary>
        public virtual void OnLeave(WorldState state, Character c) { }

        /// <summary>viewer 是否可以将 target 选为目标。默认不限制。</summary>
        public virtual bool CanTarget(Character viewer, Character target) => true;

        /// <summary>回合结束时触发（由 TurnManager.EndTurn 遍历调用，用于地点周期机制如深度潮汐）。</summary>
        public virtual void OnTurnEnd(WorldState state) { }

        /// <summary>返回本规则的可变状态 hash，用于确定性自检。子类 override 返回实际状态组合。</summary>
        public virtual long StateHash() => 0;
    }
}
