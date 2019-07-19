using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public class TestEvent : UngEvent
    {
        public static PersistentObjectRep StaticObjectRep;

        public override PersistentObjectRep ObjectRep
        {
            get => StaticObjectRep;
            set => StaticObjectRep = value;
        }
        
        public string Message;

        public override void Deserialize(NetDataReader reader)
        {
            Message = reader.GetString();
        }

        public override void Serialize(NetDataWriter writer)
        {
            writer.Put(Message);
        }
    }
}