using LiteNetLib;

namespace Assets.Scripts.Network
{
    public abstract class NetEventReactor : IGameEventReactor
    {
        public NetManager RNetManager;
        
        public virtual void React(GameEvent evt) { }
    }
}