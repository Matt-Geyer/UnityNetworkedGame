using Disruptor;
using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using UnityEngine;

public class UdpMessageEventPublishArgs
{
    public byte[] Buffer;
    public int Offset;
    public int Size;
    public IPEndPoint Endpoint;
    public UdpSendType SendType;
}

public class RingBufferOutgoingUdpMessageSender : IAsyncUdpMessageSender
{
    private readonly RingBuffer<OutgoingUdpMessage> messageBuffer;

    public RingBufferOutgoingUdpMessageSender(RingBuffer<OutgoingUdpMessage> buffer)
    {
        messageBuffer = buffer ?? throw new ArgumentNullException();
    }

    public void Send(byte[] buffer, int offset, int size, IPEndPoint remoteEndpoint, UdpSendType sendType)
    {
        // Todo arg obj pool ?
        UdpMessageEventPublishArgs args = new UdpMessageEventPublishArgs
        {
            Offset = 0,
            Size = size,
            SendType = sendType,
            Endpoint = new IPEndPoint(IPAddress.Any, 0)
        };

        StringBuilder output = new StringBuilder();
        output.AppendLine($"Frame: {Time.frameCount} Attemtping to publish OutgoingUdpMessage to ringbuffer ");
        output.AppendLine($"buffer.length: {buffer.Length}");
        output.AppendLine($"offset: {offset}");
        output.AppendLine($"size: {size}");
        output.AppendLine($"endpoint: {remoteEndpoint.Address}:{remoteEndpoint.Port}");
        Debug.Log(output.ToString());

        // needs to be a deep copy so that the sending process can keep going and potentially use/mutate the buffer that it sent
        args.Buffer = new byte[size];
        Buffer.BlockCopy(buffer, offset, args.Buffer, 0, size);
        args.Endpoint.Address = remoteEndpoint.Address;
        args.Endpoint.Port = remoteEndpoint.Port;
        messageBuffer.PublishEvent(OutgoingUdpMessageTranslator.StaticInstance, args);
    }
}
