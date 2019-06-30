using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Assets.Scripts.Network
{
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
                Socket.Socket.BeginSendTo(buffer, offset, size, SocketFlags.None, remoteEndpoint, OnFinishedSendTo,
                    Socket.Socket);
                return true;
            }
            catch (Exception e)
            {
                Debug.Log(e.Message);
                return false;
            }
        }

        private static void OnFinishedSendTo(IAsyncResult result)
        {
            Socket socket = (Socket) result.AsyncState;
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