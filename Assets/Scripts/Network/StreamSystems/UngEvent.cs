using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public abstract class UngEvent : IPersistentObject
    {
        public virtual PersistentObjectRep ObjectRep { get; set; }

        public bool IsReliable;

        public abstract void Deserialize(NetDataReader reader);

        public abstract void Serialize(NetDataWriter writer);
    }
}