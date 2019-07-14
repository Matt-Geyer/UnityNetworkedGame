using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UniRx;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class UdpSocketListenerRx
    {
        public IObservable<UdpMessage> ReceivedMessageStream;

        public UdpSocket Socket;
        private readonly IConnectableObservable<UdpMessage> _connStream;

        public UdpSocketListenerRx(UdpSocket socket)
        {

            _connStream = Observable.Create<UdpMessage>(observer =>
            {

                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

                CancellationToken cancel = cancellationTokenSource.Token;

                Debug.Log("*************** UdpMESSAGE SUBUFSDEFDS***********");

                Thread t = new Thread(() =>
                {
                    EndPoint receiveFromEp = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receiveBytes = new byte[1024];
                    Socket = socket;
                    
                    while (!cancel.IsCancellationRequested)
                    {
                        try
                        {
                            int bytesRead = Socket.Socket.ReceiveFrom(receiveBytes, SocketFlags.None, ref receiveFromEp);

                            if (bytesRead <= 0) continue;

                            observer.OnNext(new UdpMessage { Buffer = receiveBytes, DataSize = bytesRead, Endpoint = (IPEndPoint)receiveFromEp });

                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e);
                        }
                    }
                });

                t.Start();

                return Disposable.Create(() => 
                {
                    cancellationTokenSource.Cancel();
                    t.Join();
                });
            }).Publish();

            ReceivedMessageStream = _connStream.RefCount();
        }

        public void Start()
        {
            _connStream.Connect();
        }
    }


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