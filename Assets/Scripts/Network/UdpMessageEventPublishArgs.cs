using System.Net;
using LiteNetLib;

namespace Assets.Scripts.Network
{
    public class UdpMessageEventPublishArgs
    {
        public byte[] Buffer;
        public int Offset;
        public int Size;
        public IPEndPoint Endpoint;
        public UdpSendType SendType;
    }
}