using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class AsyncUdpSocketListener
    {
        public delegate void UdpMessageReceived(byte[] buffer, int bufferLength, IPEndPoint remoteEndpoint);

        private readonly byte[] _receiveBytes;
        private EndPoint _receiveFromEp;
        public UdpSocket Socket;

        public AsyncUdpSocketListener(UdpSocket socket)
        {
            _receiveFromEp = new IPEndPoint(IPAddress.Any, 0);
            _receiveBytes = new byte[1024]; // max packet size
            Socket = socket;
        }

        public event UdpMessageReceived ReceivedUdpMessageEvent;


        public async Task StartAsyncReceive()
        {
            try
            {
                Socket.Socket.BeginReceiveFrom(
                    _receiveBytes,
                    0,
                    _receiveBytes.Length,
                    SocketFlags.None,
                    ref _receiveFromEp,
                    OnFinishedReceiveFrom,
                    Socket.Socket);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                await Task.Delay(100);
#pragma warning disable 4014
                StartAsyncReceive();
#pragma warning restore 4014
            }
        }

        private void OnFinishedReceiveFrom(IAsyncResult ar)
        {
            Socket socket = (Socket) ar.AsyncState;
            try
            {
                int bytes = socket.EndReceiveFrom(ar, ref _receiveFromEp);

                ReceivedUdpMessageEvent.Invoke(_receiveBytes, bytes, (IPEndPoint) _receiveFromEp);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
#pragma warning disable 4014
            StartAsyncReceive();
#pragma warning restore 4014
        }
    }
}