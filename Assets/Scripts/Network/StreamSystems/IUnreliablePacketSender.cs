namespace Assets.Scripts.Network.StreamSystems
{
    public interface IUnreliablePacketSender
    {
        void Send(byte[] data, int offset, int size);
    }
}