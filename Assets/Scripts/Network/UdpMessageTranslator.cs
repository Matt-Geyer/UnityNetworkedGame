using System;
using System.Net;
using Disruptor;

namespace Assets.Scripts.Network
{
    public class UdpMessageTranslator : IEventTranslatorThreeArg<UdpMessage, byte[], int, IPEndPoint>, IEventTranslatorOneArg<UdpMessage, UdpMessage>
    {
        public static UdpMessageTranslator StaticInstance = new UdpMessageTranslator();

        public void TranslateTo(UdpMessage @event, long sequence, byte[] arg0, int arg1, IPEndPoint arg2)
        {
            @event.Endpoint.Address = arg2.Address;
            @event.Endpoint.Port = arg2.Port;
            @event.DataSize = arg1;
            Buffer.BlockCopy(arg0, 0, @event.Buffer, 0, (int)arg1);
        }

        public void TranslateTo(UdpMessage @event, long sequence, UdpMessage arg0)
        {
            @event.Endpoint.Address = arg0.Endpoint.Address;
            @event.Endpoint.Port = arg0.Endpoint.Port;
            @event.DataSize = arg0.DataSize;
            Buffer.BlockCopy(arg0.Buffer, 0, @event.Buffer, 0, arg0.DataSize);
        }
    }
}