using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Assets.Scripts.Network
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
}