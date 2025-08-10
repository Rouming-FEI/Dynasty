namespace Gameplay.Movement
{
    public struct LocationData
    {
        public int Id;
        public string Name;
        public int[] Connections;
        public int[] InitialItems;
    }

    public class LocationManager
    {
        private LocationData[] _locations;

        public int LocationCount => _locations.Length;

        public LocationManager()
        {
            _locations = new LocationData[]
            {
                new() { Id = 0, Name = "下潜入口A",  Connections = new[] { 4, 5 } },
                new() { Id = 1, Name = "下潜入口B",  Connections = new[] { 4, 5 } },
                new() { Id = 2, Name = "下潜入口C",  Connections = new[] { 4, 5 } },
                new() { Id = 3, Name = "下潜入口D",  Connections = new[] { 4, 5 } },
                new() { Id = 4, Name = "安全屋",     Connections = new[] { 0, 1, 2, 3, 5 }, InitialItems = new[] { 2, 2 } },
                new() { Id = 5, Name = "地狱前哨",   Connections = new[] { 0, 1, 2, 3, 4 } },
            };
        }

        public string GetName(int locationId)
        {
            if (locationId < 0 || locationId >= _locations.Length) return "未知";
            return _locations[locationId].Name;
        }

        public bool IsValidLocation(int locationId)
        {
            return locationId >= 0 && locationId < _locations.Length;
        }

        public int[] GetConnectedLocations(int locationId)
        {
            if (locationId < 0 || locationId >= _locations.Length) return System.Array.Empty<int>();
            return _locations[locationId].Connections;
        }

        public int[] GetInitialItems(int locationId)
        {
            if (locationId < 0 || locationId >= _locations.Length) return null;
            return _locations[locationId].InitialItems;
        }

        public LocationData[] GetAll() => _locations;
    }
}
