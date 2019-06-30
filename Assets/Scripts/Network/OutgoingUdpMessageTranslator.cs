using System;
using System.Collections;
using System.Collections.Generic;
using Disruptor;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class OutgoingUdpMessageTranslator : IEventTranslatorOneArg<OutgoingUdpMessage, UdpMessageEventPublishArgs>
    {
        public static OutgoingUdpMessageTranslator StaticInstance = new OutgoingUdpMessageTranslator();

        public void TranslateTo(OutgoingUdpMessage @event, long sequence, UdpMessageEventPublishArgs arg0)
        {
            @event.Endpoint.Address = arg0.Endpoint.Address;
            @event.Endpoint.Port = arg0.Endpoint.Port;
            @event.Size = arg0.Size;
            Buffer.BlockCopy(arg0.Buffer, arg0.Offset, @event.Buffer, 0, arg0.Size);
        }
    }
}