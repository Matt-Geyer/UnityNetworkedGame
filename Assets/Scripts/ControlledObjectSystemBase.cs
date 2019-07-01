using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public abstract class ControlledObjectSystemBase : IControlledObjectSystem
    {
        protected int SeqLastProcessed = -1;

        public virtual ControlledObject CurrentlyControlledObject { get; set; }
        
        public virtual void UpdateControlledObject()
        {
        }

        public virtual void StartReplicating(ControlledObject pco)
        {
            // start replicating this object which is controlled by a player (not the player who will be receiving this state tho)
        }

        public virtual void StopReplicating(ControlledObject pco)
        {
            // stop replicating a pco
        }

        public abstract void WriteToPacketStream(NetDataWriter stream);
        
        public abstract void ReadPacketStream(NetDataReader stream);
    }
}