using Core;
namespace Core.Command
{
    public class CommandQueue
    {
        private ICommand[] _queue;
        private int _head;
        private int _tail;
        private int _count;

        public int Count => _count;

        public CommandQueue(int initialCapacity = 64)
        {
            _queue = new ICommand[initialCapacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }

        public void Enqueue(ICommand command)
        {
            if (command == null) return;
            if (_count >= _queue.Length)
            {
                var newQueue = new ICommand[_queue.Length * 2];
                for (int i = 0; i < _count; i++)
                    newQueue[i] = _queue[(_head + i) % _queue.Length];
                _queue = newQueue;
                _head = 0;
                _tail = _count;
            }
            _queue[_tail] = command;
            _tail = (_tail + 1) % _queue.Length;
            _count++;
        }

        private ICommand Dequeue()
        {
            if (_count == 0) return null;
            ICommand cmd = _queue[_head];
            _queue[_head] = null;
            _head = (_head + 1) % _queue.Length;
            _count--;
            return cmd;
        }

        /// <summary>
        /// 单批执行：循环取出队首执行并计数，链式命令（执行中 Enqueue）追加到队尾、同批消费；
        /// 执行条数达到 CommandBatchLimit 后，剩余命令 drain 丢弃（批上限熔断）。
        /// 注意：这是「单批命令数上限」，不是递归深度——递归深度限制由 EventBus 承担。
        /// </summary>
        public void ExecuteAll(WorldState state)
        {
            int executed = 0;
            while (_count > 0)
            {
                executed++;
                if (executed > GameConfig.CommandBatchLimit)
                {
                    while (_count > 0) Dequeue(); // drain 剩余：本批不再执行
                    break;
                }
                ICommand cmd = Dequeue();
                cmd.Execute(state);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _queue.Length; i++) _queue[i] = null;
            _head = 0; _tail = 0; _count = 0;
        }
    }
}
