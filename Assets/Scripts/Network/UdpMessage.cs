using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class UdpMessage
    {
        public IPEndPoint Endpoint;
        public byte[] Buffer;
        public int DataSize;
        public static Func<UdpMessage> DefaultFactory = () => new UdpMessage { Buffer = new byte[1024], Endpoint = new IPEndPoint(IPAddress.Any, 0) };
    }
}