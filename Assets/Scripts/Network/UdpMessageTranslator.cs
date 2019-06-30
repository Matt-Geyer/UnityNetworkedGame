using System;
using System.Net;
using Disruptor;

namespace Assets.Scripts.Network
{
    public class UdpMessageTranslator : IEventTranslatorThreeArg<UdpMessage, byte[], int, IPEndPoint>
    {
        public static UdpMessageTranslator StaticInstance = new UdpMessageTranslator();

        public void TranslateTo(UdpMessage @event, long sequence, byte[] arg0, int arg1, IPEndPoint arg2)
        {
            @event.Endpoint.Address = arg2.Address;
            @event.Endpoint.Port = arg2.Port;
            @event.DataSize = arg1;
            Buffer.BlockCopy(arg0, 0, @event.Buffer, 0, (int)arg1);
        }
    }
}