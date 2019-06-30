using System.Collections;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Disruptor;
using Disruptor.Dsl;
using LiteNetLib;
using UnityEngine;

namespace Assets.Scripts.Network
{
    public class UdpNetworkBehavior : MonoBehaviour
    {
        /// <summary>
        ///     How often to send update event to the game reactor
        /// </summary>
        private const float GameReactorUpdateTimeF = 1f / 60f;

        private readonly NetManagerEvent _checkTimeoutEvent = new NetManagerEvent
            {EventId = NetManagerEvent.Event.CheckTimeouts};

        private NetManagerEvent[] _batchedEvents;
        private CancellationTokenSource _cancellationSource;
        private RingBuffer<OutgoingUdpMessage> _outgoingMessageBuffer;
        private EventPoller<OutgoingUdpMessage> _outgoingMessagePoller;
        private RingBufferOutgoingUdpMessageSender _outgoingUdpMessageSender;
        private Thread _processOutgoing;
        private RingBuffer<UdpMessage> _receivedMessageBuffer;
        private EventPoller<UdpMessage> _receivedMessagePoller;
        private UdpSocket _socket;
        private AsyncUdpSocketListener _socketListener;
        private AsyncUdpSocketSender _socketSender;
        private GameEvent _tempEvent;
        private float _timeSinceLastUpdate;
        private GameEvent _updateEvent;

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
        public float CheckTimeoutFrequencySeconds = 1.0f;

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

        /// <summary>
        ///     The game server reactor that will react to messages
        /// </summary>
        public NetEventReactor RGameReactor;

        public NetManager RNetManager;

        /// <summary>
        ///     Whether or not the connection should bind the socket to a local endpoint
        /// </summary>
        public bool ShouldBind = true;

        /// <summary>
        ///     Whether or not a connection should try to be established
        /// </summary>
        public bool ShouldConnect = false;

        // Start is called before the first frame update
        private void Start()
        {
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
            _receivedMessagePoller = _receivedMessageBuffer.NewPoller();
            _receivedMessageBuffer.AddGatingSequences(_receivedMessagePoller.Sequence);

            _outgoingMessageBuffer = RingBuffer<OutgoingUdpMessage>.Create(
                ProducerType.Single,
                OutgoingUdpMessage.DefaultFactory,
                256,
                new BusySpinWaitStrategy());
            _outgoingMessagePoller = _outgoingMessageBuffer.NewPoller();
            _outgoingMessageBuffer.AddGatingSequences(_outgoingMessagePoller.Sequence);

            // This makes a deep copy of the sent message before publishing it to the outgoing message buffer
            // It makes a deep copy because the buffers containing the data in the main thread could change
            // before the data has a chance to be copied into the ring buffer. Prob want to think of a way around this.. maybe more ring buffers
            _outgoingUdpMessageSender = new RingBufferOutgoingUdpMessageSender(_outgoingMessageBuffer);

            // The NetManager reactor. TODO: Benchmark and see if I have actually made any improvement by implementing disruptor
            // Though I do like the idea of clearing up the locks and doing all the logic in the main thread esp since
            // the game server will need to routinely access connected client info
            RNetManager = new NetManager(null, _outgoingUdpMessageSender) {DisconnectTimeout = 600};
            RGameReactor.RNetManager = RNetManager;
            _updateEvent = new GameEvent {EventId = GameEvent.Event.Update};
            _tempEvent = new GameEvent(); // reusable event for the update loop

            //if (!ShouldBind)
            //{
            //    R_NetManager.SimulateLatency = true;
            //    R_NetManager.SimulatePacketLoss = true;
            //    R_NetManager.SimulationMaxLatency = 10;
            //    R_NetManager.SimulationMinLatency = 0;
            //}

            _cancellationSource = new CancellationTokenSource();
            _processOutgoing = new Thread(SendOutgoingUdpMessages)
            {
                IsBackground = true,
                Name = "UdpServer"
            };

            // Every frame the previous frames events get replaced with new output events created by the NetManager reactor for that frame
            _batchedEvents = new NetManagerEvent[MaxUdpMessagesPerFrame];
            for (int i = 0; i < MaxUdpMessagesPerFrame; i++)
                _batchedEvents[i] = new NetManagerEvent {EventId = NetManagerEvent.Event.UdpMessage};

            // BIND 
            if (ShouldBind)
                _socket.BindLocalIpv4(BindAddress, BindPort);

            // CONNECT - TEMPORARY - NEEDS TO BE SOME SORT OF STATE MACHINE
            if (ShouldConnect)
                RNetManager.Connect(ConnectAddress, ConnectPort, "somekey");

            // No point actually awaiting this call.. it kicks off a 
            // recurring execution where the finish method always calls the start method again
            _socketListener.StartAsyncReceive();


            // Start thread that polls for outgoing udp messages and sends them on the socket
            _processOutgoing.Start(new object[] {_socketSender, _outgoingMessagePoller, _cancellationSource.Token});

            // Start coroutine that will send the 
            StartCoroutine("SendCheckTimeoutEvent");

            if (ShouldBind)
                StartCoroutine("SendUpdateEvent");
        }


        // Need to think about whether this breaks determinism of the reactor or not
        private IEnumerator SendCheckTimeoutEvent()
        {
            while (true)
            {
                RNetManager.React(_checkTimeoutEvent);
                yield return new WaitForSeconds(CheckTimeoutFrequencySeconds);
            }
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

        // Update is called once per frame
        private void Update()
        {
            // Reset processed message count
            int msgEventThisFrame = 0;

            // Poll until max messages or no more messages to receive, copying the message data 
            // into the batched events buffer
            _receivedMessagePoller.Poll((message, sequence, endOfBatch) =>
            {
                _batchedEvents[msgEventThisFrame].Message = message;
                msgEventThisFrame++;
                return msgEventThisFrame < MaxUdpMessagesPerFrame;
            });

            // _BatchedEvents is filled with messages pulled off the wire, so process (react to) them
            // which will mutate the reactor state and then return a set of output events to be further handled
            for (int i = 0; i < msgEventThisFrame; i++) RNetManager.React(_batchedEvents[i]);

            // React to all of the output events with the a game server reactor
            _tempEvent.EventId = GameEvent.Event.NetEvent;
            while (RNetManager.NetEventsQueue.Count > 0)
            {
                _tempEvent.NetEvent = RNetManager.NetEventsQueue.Dequeue();

                RGameReactor.React(_tempEvent);
            }

            _timeSinceLastUpdate += Time.deltaTime;

            if (_timeSinceLastUpdate < GameReactorUpdateTimeF) return;

            _timeSinceLastUpdate = 0;

            RGameReactor.React(_updateEvent);
        }

        private void OnDestroy()
        {
            StopCoroutine("SendTimeoutEvent");
            _cancellationSource.Cancel();
            _processOutgoing.Join();
        }
    }
}