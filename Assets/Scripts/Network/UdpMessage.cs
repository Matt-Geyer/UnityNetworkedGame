using Disruptor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;


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

public class UdpMessage
{
    public IPEndPoint Endpoint;
    public byte[] Buffer;
    public int DataSize;

    public static Func<UdpMessage> DefaultFactory = () => new UdpMessage { Buffer = new byte[1024], Endpoint = new IPEndPoint(IPAddress.Any, 0) };

}