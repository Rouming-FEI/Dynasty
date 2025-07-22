namespace Dynasty.Core
{
    public class GenericPool<T> where T : class, new()
    {
        private T[] _items;
        private int _count;

        public int Available => _count;
        public int Capacity => _items?.Length ?? 0;

        public GenericPool(int initialCapacity = 16)
        {
            _items = new T[initialCapacity];
        }

        public T Rent()
        {
            if (_count > 0)
            {
                _count--;
                T item = _items[_count];
                _items[_count] = null;
                return item;
            }
            return new T();
        }

        public void Return(T item)
        {
            if (item == null) return;
            if (_count >= _items.Length)
            {
                System.Array.Resize(ref _items, _items.Length * 2);
            }
            _items[_count] = item;
            _count++;
        }

        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
                Return(new T());
        }
    }
}
