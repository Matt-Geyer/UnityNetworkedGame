using LiteNetLib;

namespace Assets.Scripts.Network
{
    public abstract class NetEventReactor : IGameEventReactor
    {
        public NetManager RNetManager;
        
        public abstract void React(GameEvent evt);
    }
}