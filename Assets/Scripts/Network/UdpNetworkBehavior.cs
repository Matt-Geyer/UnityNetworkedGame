using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AiUnity.NLog.Core;
using Disruptor;
using Disruptor.Dsl;
using LiteNetLib;
using UniRx;

namespace Assets.Scripts.Network
{
    public sealed class UdpNetworkBehavior
    {
        private readonly Queue<UdpMessage> _receivedUdpMessages;
        private readonly CancellationTokenSource _cancellationSource;
        private readonly EventPoller<OutgoingUdpMessage> _outgoingMessagePoller;
        private readonly Thread _processOutgoing;
        private readonly RingBuffer<UdpMessage> _receivedMessageBuffer;
        private readonly UdpSocket _socket;
        private readonly AsyncUdpSocketListener _socketListener;
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
        ///     How often the CheckTimeout logic should run
        /// </summary>
        public float CheckTimeoutFrequencySeconds = 20.0f;

        /// <summary>
        ///     The host to connect to
        /// </summary>
        public string ConnectAddress = "127.0.0.1";

        /// <summary>
        ///     The port to connect to
        /// </summary>
        public int ConnectPort = 40069;


        /// <summary>
        ///     Messages are polled and sent in a loop in a background thread.
        ///     This controls how many will be polled before Thread.Sleep is called
        /// </summary>
        public int MaxUdpMessageSendBeforeSleep = 1000;

        /// <summary>
        ///     Maximum number of UdpMessage (incoming) events to process in once frame
        /// </summary>
        public int MaxUdpMessagesPerFrame = 100;
        
        public NetManager RNetManager;

        /// <summary>
        ///     Whether or not the connection should bind the socket to a local endpoint
        /// </summary>
        public bool ShouldBind = true;

        /// <summary>
        ///     Whether or not a connection should try to be established
        /// </summary>
        public bool ShouldConnect = false;

        private readonly IConnectableObservable<UdpMessage> _connectableUdpMessageStream;

        private readonly IObservable<UdpMessage> _udpMessageStream;

        public IObservable<UdpMessage> UdpMessageStream => _udpMessageStream.AsObservable();

        public UdpNetworkBehavior()
        {
            NLogger log = NLogManager.Instance.GetLogger(this);
            _receivedUdpMessages = new Queue<UdpMessage>();
            // UDP Socket Listener/Sender initialization
            _socket = new UdpSocket();
            _socketListener = new AsyncUdpSocketListener(_socket);
            _socketSender = new AsyncUdpSocketSender(_socket);

            // Add to RingBuffer from listener async callbacks which happen off the main thread
            _socketListener.ReceivedUdpMessageEvent += AddUdpMessageToReceivedBuffer;

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
            RingBufferOutgoingUdpMessageSender outgoingUdpMessageSender = new RingBufferOutgoingUdpMessageSender(outgoingMessageBuffer);

            // The NetManager reactor. TODO: Benchmark and see if I have actually made any improvement by implementing disruptor
            // Though I do like the idea of clearing up the locks and doing all the logic in the main thread esp since
            // the game server will need to routinely access connected client info
            RNetManager = new NetManager(null, outgoingUdpMessageSender) {DisconnectTimeout = 600};

            _cancellationSource = new CancellationTokenSource();
            _processOutgoing = new Thread(SendOutgoingUdpMessages)
            {
                Name = "UdpServer"
            };

            // Every frame the previous frames events get replaced with new output events created by the NetManager reactor for that frame
            NetManagerEvent[] batchedEvents = new NetManagerEvent[MaxUdpMessagesPerFrame];
            for (int i = 0; i < MaxUdpMessagesPerFrame; i++)
                batchedEvents[i] = new NetManagerEvent {EventId = NetManagerEvent.Event.UdpMessage};

            _connectableUdpMessageStream = Observable.Create<UdpMessage>(observer =>
            {
                log.Debug("*********************** UDP NET BEHAVIOR - UdpMessage SUB ***************");

                return Observable.EveryUpdate().Subscribe(_ =>
                {
                    receivedMessagePoller.Poll(HandleMessagePollerEvent);

                    for (int i = _receivedUdpMessages.Count; i > 0; i--)
                    {
                        observer.OnNext(_receivedUdpMessages.Dequeue());
                    }
                });
            }).Publish();

            _udpMessageStream = _connectableUdpMessageStream.RefCount();

        }

        public void Start()
        {
            // BIND 
            if (ShouldBind)
                _socket.BindLocalIpv4(BindAddress, BindPort);

            // CONNECT - TEMPORARY - NEEDS TO BE SOME SORT OF STATE MACHINE
            if (ShouldConnect)
                RNetManager.Connect(ConnectAddress, ConnectPort, "somekey");

            // No point actually awaiting this call.. it kicks off a 
            // recurring execution where the finish method always calls the start method again
#pragma warning disable 4014
            _socketListener.StartAsyncReceive();
#pragma warning restore 4014


            // Start thread that polls for outgoing udp messages and sends them on the socket
            _processOutgoing.Start(new object[] {_socketSender, _outgoingMessagePoller, _cancellationSource.Token});

            _connectableUdpMessageStream.Connect();
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

        // Will happen in worker pool threads
        // but only one at a time so still single producer
        private void AddUdpMessageToReceivedBuffer(byte[] buffer, int bufferLength, IPEndPoint remoteEndpoint)
        {
            _receivedMessageBuffer.PublishEvent(UdpMessageTranslator.StaticInstance, buffer, bufferLength,
                remoteEndpoint);
        }
    }
}