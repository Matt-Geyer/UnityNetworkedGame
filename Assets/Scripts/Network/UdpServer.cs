using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AiUnity.NLog.Core;
using Disruptor;
using Disruptor.Dsl;
using LiteNetLib;
using UniRx;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public sealed class UdpServer
    {
        private readonly CancellationTokenSource _cancellationSource;
        private readonly IConnectableObservable<UdpMessage> _connRingBufferReceivedUdpMessageStream;
        private readonly NLogger _log;
        private readonly EventPoller<OutgoingUdpMessage> _outgoingMessagePoller;
        private readonly Thread _processOutgoing;
        private readonly RingBuffer<UdpMessage> _receivedMessageBuffer;
        private readonly Queue<UdpMessage> _receivedUdpMessages;
        private readonly UdpSocket _socket;
        private readonly AsyncUdpSocketListener _socketListener;
        private readonly IObservable<UdpMessage> _socketReceivedUdpMessageStream;
        private readonly AsyncUdpSocketSender _socketSender;

        /// <summary>
        ///     The local endpoint address to bind to
        /// </summary>
        public string BindAddress = "0.0.0.0";

        /// <summary>
        ///     The local endpoint port to bind the socket to
        /// </summary>
        public int BindPort = 40069;

        /// <summary>
        ///     Messages are polled and sent in a loop in a background thread.
        ///     This controls how many will be polled before Thread.Sleep is called
        /// </summary>
        public int MaxUdpMessageSendBeforeSleep = 1000;

        /// <summary>
        /// </summary>
        public RingBufferOutgoingUdpMessageSender OutgoingUdpMessageSender;

        public IObservable<UdpMessage> UdpMessageStream;
        private IDisposable _socketStreamSub;

        public UdpServer()
        {
            _log = NLogManager.Instance.GetLogger(this);
            _receivedUdpMessages = new Queue<UdpMessage>();
            // UDP Socket Listener/Sender initialization
            _socket = new UdpSocket();
            _socketSender = new AsyncUdpSocketSender(_socket);
            _socket.Socket.ReceiveTimeout = 1000;

            // Init ring buffers
            _receivedMessageBuffer = RingBuffer<UdpMessage>.Create(
                ProducerType.Single,
                UdpMessage.DefaultFactory,
                256,
                new BusySpinWaitStrategy());
            EventPoller<UdpMessage> receivedMessagePoller = _receivedMessageBuffer.NewPoller();
            _receivedMessageBuffer.AddGatingSequences(receivedMessagePoller.Sequence);

            RingBuffer<OutgoingUdpMessage> outgoingMessageBuffer = RingBuffer<OutgoingUdpMessage>.Create(
                ProducerType.Single,
                OutgoingUdpMessage.DefaultFactory,
                256,
                new BusySpinWaitStrategy());
            _outgoingMessagePoller = outgoingMessageBuffer.NewPoller();
            outgoingMessageBuffer.AddGatingSequences(_outgoingMessagePoller.Sequence);

            // This makes a deep copy of the sent message before publishing it to the outgoing message buffer
            // It makes a deep copy because the buffers containing the data in the main thread could change
            // before the data has a chance to be copied into the ring buffer. Prob want to think of a way around this.. maybe more ring buffers
            OutgoingUdpMessageSender = new RingBufferOutgoingUdpMessageSender(outgoingMessageBuffer);

            _cancellationSource = new CancellationTokenSource();
            _processOutgoing = new Thread(SendOutgoingUdpMessages)
            {
                Name = "UdpServer"
            };

            // Create a sequence by starting a thread that will call socket.ReceiveFrom and emit the resulting bytes.
            // It is only used internally by the server to add the bytes to the ring buffer, then the messages will be pulled
            // off the RingBuffer periodically on the main thread
            _socketReceivedUdpMessageStream = Observable.Create<UdpMessage>(observer =>
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

                CancellationToken cancel = cancellationTokenSource.Token;

                Thread thread = new Thread(() =>
                {
                    EndPoint receiveFromEp = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receiveBytes = new byte[1400];

                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        try
                        {
                            int bytesRead =
                                _socket.Socket.ReceiveFrom(receiveBytes, SocketFlags.None, ref receiveFromEp);

                            if (bytesRead <= 0) continue;

                            observer.OnNext(new UdpMessage
                                {Buffer = receiveBytes, DataSize = bytesRead, Endpoint = (IPEndPoint) receiveFromEp});
                        }
                        catch (SocketException se)
                        {
                            // ReSharper disable once SwitchStatementMissingSomeCases
                            switch (se.SocketErrorCode)
                            {
                                case SocketError.Interrupted:
                                case SocketError.ConnectionReset:
                                case SocketError.MessageSize:
                                case SocketError.TimedOut:
                                    Task.Delay(100).Wait();
                                    break;
                                default:
                                    _log.Error(se, "Udp socket");
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(e);
                            Task.Delay(200).Wait();
                        }
                    }
                });

                thread.Start();

                return Disposable.Create(() =>
                {
                    cancellationTokenSource.Cancel();
                });
            });

            // Every update poll the RingBuffer and emit any UdpMessages
            _connRingBufferReceivedUdpMessageStream = Observable.Create<UdpMessage>(observer =>
            {
                return Observable.EveryUpdate().Subscribe(_ =>
                {
                    receivedMessagePoller.Poll(HandleMessagePollerEvent);
        
                    for (int i = _receivedUdpMessages.Count; i > 0; i--)
                        observer.OnNext(_receivedUdpMessages.Dequeue());
                });
            }).Publish();

            // Expose the stream of UdpMessages polled off the RingBuffer for application to process
            UdpMessageStream = _connRingBufferReceivedUdpMessageStream.RefCount();
        }


        public void Start()
        {
            _log.Info($"UdpServer started. Binding to: {BindAddress}:{BindPort}");

            // Bind socket to port and address
            _socket.BindLocalIpv4(BindAddress, BindPort);

            // Start thread that polls for outgoing udp messages and sends them on the socket
            _processOutgoing.Start(new object[] {_socketSender, _outgoingMessagePoller, _cancellationSource.Token});

            // Subscribe to the stream of UdpMessages coming off of the socket.. this will happen off the main thread
            _socketStreamSub = _socketReceivedUdpMessageStream
                .Subscribe(msg => { _receivedMessageBuffer.PublishEvent(UdpMessageTranslator.StaticInstance, msg); });

            // Start the streams
            //_socketReceivedUdpMessageStream.Connect();
            _connRingBufferReceivedUdpMessageStream.Connect();
        }

        public void Stop()
        {
            _log.Info("Stopping UdpServer threads");
            _cancellationSource.Cancel();
            _processOutgoing.Join();
            _socketStreamSub.Dispose();
        }

        private bool HandleMessagePollerEvent(UdpMessage message, long sequence, bool endOfBatch)
        {
            _receivedUdpMessages.Enqueue(message);
            return true;
        }

        /// <summary>
        /// </summary>
        /// <param name="arg"></param>
        private async void SendOutgoingUdpMessages(object arg)
        {
            object[] args = (object[]) arg;

            AsyncUdpSocketSender sender = (AsyncUdpSocketSender) args[0];
            EventPoller<OutgoingUdpMessage> outgoingMessagePoller = (EventPoller<OutgoingUdpMessage>) args[1];
            CancellationToken cancellation = (CancellationToken) args[2];

            int eventsThisIteration = 0;
            while (!cancellation.IsCancellationRequested)
            {
                // poll for send events
                outgoingMessagePoller.Poll((m, s, eob) =>
                {
                    // Send udp message 
                    if (m.SendType == UdpSendType.SendTo) sender.BeginSendTo(m.Buffer, 0, m.Size, m.Endpoint);

                    eventsThisIteration++;
                    return eventsThisIteration < MaxUdpMessageSendBeforeSleep;
                });

                await Task.Delay(1, cancellation);
            }
        }
    }
}