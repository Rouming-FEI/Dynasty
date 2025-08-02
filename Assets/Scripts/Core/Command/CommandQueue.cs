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

        public void ExecuteAll(WorldState state)
        {
            int depth = 0;
            while (_count > 0)
            {
                depth++;
                if (depth > GameConfig.CommandMaxDepth)
                {
                    while (_count > 0) Dequeue(); // drain
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
