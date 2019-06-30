using System;
using System.Net;
using LiteNetLib;

namespace Assets.Scripts.Network
{
    public class OutgoingUdpMessage
    {
        public IPEndPoint Endpoint;
        public byte[] Buffer;
        public int Size;
        public UdpSendType SendType;
        public static Func<OutgoingUdpMessage> DefaultFactory = () => new OutgoingUdpMessage { Buffer = new byte[1024], Endpoint = new IPEndPoint(IPAddress.Any, 0) };
    }
}