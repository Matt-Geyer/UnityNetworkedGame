using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GameUDPLibrary
{
    public class UdpSocket
    {
        public Socket Socket;

        public UdpSocket()
        {
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        }

        public bool BindLocalIpv4(string ip, int port)
        {
            try
            {
                Socket.Bind(new IPEndPoint(IPAddress.Parse(ip), port));
                return true;
            }
            catch (SocketException e)
            {
                Debug.Log(e.Message);
                return false;
            }
        }
    }


    public class AsyncUdpSocketListener
    {
        public UdpSocket Socket;
        private EndPoint recvFromEP;
        private readonly byte[] recvBuffer;

        public delegate void UdpMessageReceived(byte[] buffer, int bufferLength, IPEndPoint remoteEndpoint);

        public UdpMessageReceived OnUdpMessageReceived;

        public event UdpMessageReceived ReceivedUdpMessageEvent;

        public AsyncUdpSocketListener(UdpSocket socket)
        {
            recvFromEP = new IPEndPoint(IPAddress.Any, 0);
            recvBuffer = new byte[1024]; // max packet size
            Socket = socket;
        }


        public async Task StartAsyncReceive()
        {
            try
            {
                Socket.Socket.BeginReceiveFrom(
                    recvBuffer,
                    0,
                    recvBuffer.Length,
                    SocketFlags.None,
                    ref recvFromEP,
                    new AsyncCallback(OnFinishedReceiveFrom),
                    Socket.Socket);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                await Task.Delay(100);
                StartAsyncReceive(); // I beleive this will not actually be recursive (note the green squig)
            }
        }

        private void OnFinishedReceiveFrom(IAsyncResult ar)
        {
            var socket = (Socket)ar.AsyncState;
            try
            {
                int bytes = socket.EndReceiveFrom(ar, ref recvFromEP);
                
                ReceivedUdpMessageEvent.Invoke(recvBuffer, bytes, (IPEndPoint)recvFromEP);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
            StartAsyncReceive();
        }
    }

    public class AsyncUdpSocketSender
    {
        public UdpSocket Socket;

        public AsyncUdpSocketSender(UdpSocket socket)
        {
            Socket = socket;       
        }

        public bool BeginSendTo(byte[] buffer, int offset, int size, EndPoint remoteEndpoint)
        {
            try
            {
                Socket.Socket.BeginSendTo(buffer, offset, size, SocketFlags.None, remoteEndpoint, new AsyncCallback(OnFinishedSendTo), Socket.Socket);
                return true;
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                return false;
            }
        }

        private void OnFinishedSendTo(IAsyncResult result)
        {
            Socket socket = (Socket)result.AsyncState;
            try
            {
                socket.EndSendTo(result);
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
            }
          
        }

    }

}
