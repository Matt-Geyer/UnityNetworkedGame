using Disruptor;
using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;


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


public class OutgoingUdpMessage
{
    public IPEndPoint Endpoint;
    public byte[] Buffer;
    public int Size;
    public UdpSendType SendType;
    public static Func<OutgoingUdpMessage> DefaultFactory = () => new OutgoingUdpMessage { Buffer = new byte[1024], Endpoint = new IPEndPoint(IPAddress.Any, 0) };
}



