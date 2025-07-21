namespace Dynasty.Core
{
    public struct GameplayEvent
    {
        public int EventType;
        public int SourceId;
        public int TargetId;
        public float Magnitude;
        public float Arg1;
        public float Arg2;
        public int IntArg1;
        public int IntArg2;

        public GameplayEvent(int eventType, int sourceId, int targetId)
        {
            EventType = eventType;
            SourceId = sourceId;
            TargetId = targetId;
            Magnitude = 0f;
            Arg1 = 0f;
            Arg2 = 0f;
            IntArg1 = 0;
            IntArg2 = 0;
        }
    }
}
