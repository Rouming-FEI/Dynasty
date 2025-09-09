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

        /// <summary>角色进入该地点时触发。</summary>
        public virtual void OnEnter(WorldState state, Character c) { }

        /// <summary>角色离开该地点时触发。</summary>
        public virtual void OnLeave(WorldState state, Character c) { }

        /// <summary>viewer 是否可以将 target 选为目标。默认不限制。</summary>
        public virtual bool CanTarget(Character viewer, Character target) => true;

        /// <summary>回合结束时触发（预留扩展点，当前未接线）。</summary>
        public virtual void OnTurnEnd(WorldState state) { }
    }
}
