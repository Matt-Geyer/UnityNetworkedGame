using LiteNetLib;

namespace Assets.Scripts.Network
{
    public class GameEvent
    {
        public enum Event
        {
            NetEvent,
            Update
        }

        public Event EventId;

        public NetEvent NetEvent;
    }
}