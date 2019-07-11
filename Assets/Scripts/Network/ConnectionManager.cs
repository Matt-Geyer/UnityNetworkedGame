
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using LiteNetLib;
using SecureRemotePassword;
using UniRx;
using UnityEngine;
using BitStream = BKSystem.IO.BitStream;


namespace Assets.Scripts.Network
{
    public sealed class ConnectionManager
    {
        private readonly IObservable<string> _stringStream;
        private readonly IAsyncUdpMessageSender _udpMessageSender;
        private readonly List<IObserver<string>> _observers;

        private readonly SrpClient _srpClient;
        private readonly SrpServer _srpServer;

        private readonly BitStream _stream;

        public ConnectionManager()
        {
            _srpClient = new SrpClient();
            _srpServer = new SrpServer();

            _stream = new BitStream(1400 * 8);
            _stringStream = Observable.Create<string>(obs =>
            {
                _observers.Add(obs);
                return Disposable.Create(() => _observers.Remove(obs));
            });
            _observers = new List<IObserver<string>>();
        }
        
        public void ProcessMessages(UdpMessage[] messages, int msgCount)
        {
            foreach (UdpMessage message in messages)
            {
                _stream.Flush();
                _stream.Write(message.Buffer, 0, message.DataSize);
                
                // check protocol id
                _stream.Read(out byte pid);
                if (pid != 112) { continue; }
                
                // look for auth / new conn

                // publish if legit
            }

            foreach (IObserver<string> observer in _observers)
            {
                observer.OnNext("new message ya'll");
            }
        }


        public void SendConnectionRequest()
        {

        }

        private const int HeaderSize = 1;

        /// <summary>
        /// Don't allow this if not in correct state? IE SendConnect request 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="endpoint"></param>
        private void SendUdpMessage(byte[] data, int offset, int count, IPEndPoint endpoint)
        {
            int packetDataSize = count - offset;

            // add header then data then send
            byte[] packet = new byte[packetDataSize + HeaderSize];

            //pid
            packet[0] = 112;



            _udpMessageSender.Send(packet, packetDataSize + HeaderSize, count, endpoint, UdpSendType.SendTo);
        }

    }
}
