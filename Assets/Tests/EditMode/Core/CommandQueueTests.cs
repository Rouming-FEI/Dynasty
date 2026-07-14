using NUnit.Framework;
using Core;
using Core.Command;

namespace Dynasty.Tests
{
    public class CommandQueueTests
    {
        private sealed class CountingCommand : ICommand
        {
            public int Tick => 0;
            public int PlayerId => 0;
            public int Executed;
            public void Execute(WorldState state) { Executed++; }
        }

        [Test]
        public void 单批上限_超出部分被丢弃()
        {
            var queue = new CommandQueue();
            var cmds = new CountingCommand[GameConfig.CommandBatchLimit + 4];
            for (int i = 0; i < cmds.Length; i++)
            {
                cmds[i] = new CountingCommand();
                queue.Enqueue(cmds[i]);
            }

            queue.ExecuteAll(new WorldState(1));

            int executed = 0;
            foreach (var c in cmds) if (c.Executed > 0) executed++;
            Assert.AreEqual(GameConfig.CommandBatchLimit, executed,
                "单批只执行前 CommandBatchLimit 条");
            Assert.AreEqual(0, queue.Count, "超出部分被清空（drain），不残留");
        }

        [Test]
        public void 链式命令_未超上限时同批全部执行()
        {
            var queue = new CommandQueue();
            for (int i = 0; i < GameConfig.CommandBatchLimit; i++)
                queue.Enqueue(new CountingCommand());

            queue.ExecuteAll(new WorldState(1));

            Assert.AreEqual(0, queue.Count, "全部执行完毕");
        }
    }
}
