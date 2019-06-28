using Disruptor;
using Disruptor.Dsl;
using GameUDPLibrary;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


public class GamePacket
{
    public enum PacketType
    {
        SERVERUPDATE,
        CLIENTUPDATE,
        INIT,
    }

    public PacketType Type;

    public ushort Seq;


    // UPDATE FROM SERVER
    public ushort LastClientSeq;
    public uint PositionCount;
    public Vector3[] Positions;

    // UPDATE FROM CLIENT
    public UserInputSample UserInput;

    // INIT FROM SERVER
    public ushort EntityId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put((uint)Type);
        writer.Put(Seq);

        if (Type == PacketType.SERVERUPDATE)
        {
            writer.Put(LastClientSeq);
            writer.Put(PositionCount);
            for (int i = 0; i < PositionCount; i++)
            {
                writer.Put(Positions[i].x);
                writer.Put(Positions[i].y);
                writer.Put(Positions[i].z);
            }
        }
        else if (Type == PacketType.CLIENTUPDATE)
        {
            UserInput.Serialize(writer);
        }
        else if(Type == PacketType.INIT)
        {
            writer.Put(EntityId);
        } 
    }

    public void Deserialize(NetDataReader reader)
    {
        Type = (PacketType)reader.GetUInt();
        Seq = reader.GetUShort();

        if (Type == PacketType.SERVERUPDATE)
        {
            LastClientSeq = reader.GetUShort();
            PositionCount = reader.GetUInt();
            for (int i = 0; i < PositionCount; i++)
            {
                Positions[i].x = reader.GetFloat();
                Positions[i].y = reader.GetFloat();
                Positions[i].z = reader.GetFloat();
            }
        }
        else if (Type == PacketType.CLIENTUPDATE)
        {
            UserInput.Deserialize(reader);
        }
        else if (Type == PacketType.INIT)
        {
            EntityId = reader.GetUShort();
        }
    }
}

public interface IGameEventReactor
{
    void React(GameEvent evt);
}

public class GameEvent
{
    public enum Event
    {
        NETEVENT,
        UPDATE
    }

    public Event EventId;

    public NetEvent NetEvent;

}

public abstract class ScriptableNetEventReactor : ScriptableObject, IGameEventReactor
{
    public ILogger log;

    public IAsyncUdpMessageSender UdpMessageSender;

    public NetManager R_NetManager;

    public virtual void Initialize(ILogger logger)
    {
        log = logger;
    }

    public abstract void React(GameEvent evt);
}







public class UdpNetworkBehavior : MonoBehaviour
{
    /// <summary>
    /// The game server reactor that will react to messages
    /// </summary>
    public ScriptableNetEventReactor R_GameReactor;

    /// <summary>
    /// Whether or not a connection should try to be established
    /// </summary>
    public bool ShouldConnect = false;

    /// <summary>
    /// The port to connect to
    /// </summary>
    public int ConnectPort = 40069;

    /// <summary>
    /// The host to connect to
    /// </summary>
    public string ConnectAddress = "127.0.0.1";

    /// <summary>
    /// Whether or not the connection should bind the socket to a local endpoint
    /// </summary>
    public bool ShouldBind = true;

    /// <summary>
    /// The local endpoint port to bind the socket to
    /// </summary>
    public int BindPort = 40069;

    /// <summary>
    /// The local endpoint address to bind to 
    /// </summary>
    public string BindAddress = "0.0.0.0";

    /// <summary>
    /// Maximum number of UdpMessage (incoming) events to process in once frame
    /// </summary>
    public int MaxUdpMessagesPerFrame = 100;

    /// <summary>
    /// Messages are polled and sent in a loop in a background thread.
    /// This controls how many will be polled before Thread.Sleep is called
    /// </summary>
    public int MaxUdpMessageSendBeforeSleep = 1000;

    /// <summary>
    /// How often the CheckTimeout logic should run
    /// </summary>
    public float CheckTimeoutFrequencySeconds = 1.0f/60.0f;

    private UdpSocket _Socket;
    private AsyncUdpSocketListener _SocketListener;
    private AsyncUdpSocketSender _SocketSender;
    private RingBuffer<UdpMessage> _ReceivedMessageBuffer;
    private EventPoller<UdpMessage> _ReceivedMessagePoller;
    private RingBuffer<OutgoingUdpMessage> _OutgoingMessageBuffer;
    private EventPoller<OutgoingUdpMessage> _OutgoingMessagePoller;
    private Thread _ProcessOutgoing;
    private CancellationTokenSource _CancellationSource;
    public NetManager R_NetManager;
    private RingBufferOutgoingUdpMessageSender _OutgoingUdpMessageSender;
    private NetManagerEvent[] _BatchedEvents;
    private readonly NetManagerEvent _CheckTimeoutEvent = new NetManagerEvent { EventId = NetManagerEvent.Event.CheckTimeouts };
    private GameEvent _UpdateEvent;
    private GameEvent _TempEvent;

    // Start is called before the first frame update
    void Start()
    {     
       
        // UDP Socket Listener/Sender initialization
        _Socket = new UdpSocket();
        _SocketListener = new AsyncUdpSocketListener(_Socket);
        _SocketSender = new AsyncUdpSocketSender(_Socket);

        // Add to ringbuffer from listener async callbacks which happen off the main thread
        _SocketListener.ReceivedUdpMessageEvent += AddUdpMessageToReceivedBuffer;

        // Init ring buffers
        _ReceivedMessageBuffer = RingBuffer<UdpMessage>.Create(
            ProducerType.Single,
            UdpMessage.DefaultFactory,
            256,
            new BusySpinWaitStrategy());
        _ReceivedMessagePoller = _ReceivedMessageBuffer.NewPoller();
        _ReceivedMessageBuffer.AddGatingSequences(_ReceivedMessagePoller.Sequence);

        _OutgoingMessageBuffer = RingBuffer<OutgoingUdpMessage>.Create(
            ProducerType.Single,
            OutgoingUdpMessage.DefaultFactory,
            256,
            new BusySpinWaitStrategy());
        _OutgoingMessagePoller = _OutgoingMessageBuffer.NewPoller();
        _OutgoingMessageBuffer.AddGatingSequences(_OutgoingMessagePoller.Sequence);

        // This makes a deep copy of the sent message before publishing it to the outging message buffer
        // It makes a deep copy because the buffers containing the data in the main thread could change
        // before the data has a chance to be copied into the ring buffer. Prob want to think of a way around this.. maybe more ring buffers
        _OutgoingUdpMessageSender = new RingBufferOutgoingUdpMessageSender(_OutgoingMessageBuffer);

        // The NetManager reactor. TODO: Benchmark and see if I have actually made any improvement by implementing LMAX
        // Though I do like the idea of clearing up the locks and doing all the logic in the main thread esp since
        // the game server will need to routinely access connected client info
        R_NetManager = new NetManager(null, _OutgoingUdpMessageSender) { DisconnectTimeout = 600 };
        R_GameReactor.R_NetManager = R_NetManager;
        _UpdateEvent = new GameEvent { EventId = GameEvent.Event.UPDATE };
        _TempEvent = new GameEvent(); // reusable event for the update loop

        //if (!ShouldBind)
        //{
        //    R_NetManager.SimulateLatency = true;
            R_NetManager.SimulatePacketLoss = true;
        //    R_NetManager.SimulationMaxLatency = 10;
        //    R_NetManager.SimulationMinLatency = 0;
        //}

 


        _CancellationSource = new CancellationTokenSource();
        _ProcessOutgoing = new Thread(SendOutgoingUdpMessages)
        {
            IsBackground = true,
            Name = "UdpServer"
        };

        // Every frame the previous frames events get replaced with new output events created by the NetManager reactor for that frame
        _BatchedEvents = new NetManagerEvent[MaxUdpMessagesPerFrame];
        for (int i = 0; i < MaxUdpMessagesPerFrame; i++)
        {
            _BatchedEvents[i] = new NetManagerEvent { EventId = NetManagerEvent.Event.UdpMessage };
        }

        // BIND 
        if (ShouldBind)
            _Socket.BindLocalIpv4(BindAddress, BindPort);

        // CONNECT - TEMPORARY PROB NEEDS TO BE SOME SORT OF STATE MACHINE
        if (ShouldConnect)
            R_NetManager.Connect(ConnectAddress, ConnectPort, "somekey");

        // No point actually awaiting this call.. it kicks off a 
        // recurring execution where the finish method always calls the start method again
        _SocketListener.StartAsyncReceive();

            
        // Start thread that polls for outgoing udp messages and sends them on the socket
        _ProcessOutgoing.Start(new object[] { _SocketSender, _OutgoingMessagePoller, _CancellationSource.Token });

        // Start coroutine that will send the 
        StartCoroutine("SendCheckTimeoutEvent");

        if (ShouldBind)
            StartCoroutine("SendUpdateEvent");

    }

    private IEnumerator SendUpdateEvent()
    {
        while (true)
        {
           

            yield return new WaitForSeconds(1/20f);
        }
    }

   // Need to think about whether this breaks determinsm of the reactor or not
    private IEnumerator SendCheckTimeoutEvent()
    {
        while (true)
        {
            R_NetManager.React(_CheckTimeoutEvent);
            yield return new WaitForSeconds(1f);
        }
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="arg"></param>
    private async void SendOutgoingUdpMessages(object arg)
    {
        object[] args = (object[])arg;

        AsyncUdpSocketSender sender = (AsyncUdpSocketSender)args[0];
        EventPoller<OutgoingUdpMessage> poller = (EventPoller<OutgoingUdpMessage>)args[1];
        CancellationToken cancellation = (CancellationToken)args[2];
    
        int eventsThisIteration = 0;
        while (!cancellation.IsCancellationRequested)
        {
            // poll for send events
            poller.Poll((m, s, eob) =>
            {
                // Send udp message 
                if (m.SendType == UdpSendType.SendTo)
                {
                    sender.BeginSendTo(m.Buffer, 0, m.Size, m.Endpoint);
                }

                eventsThisIteration++;
                return eventsThisIteration < MaxUdpMessageSendBeforeSleep;
            });

            await Task.Delay(1);
        }
    }

    // Will happen in worker pool threads
    // but only one at a time so still single producer
    private void AddUdpMessageToReceivedBuffer(byte[] buffer, int bufferLength, IPEndPoint remoteEndpoint)
    {
        _ReceivedMessageBuffer.PublishEvent(UdpMessageTranslator.StaticInstance, buffer, bufferLength, remoteEndpoint);
    }

    private void FixedUpdate()
    {
        
    }

    private float timeSinceLastUpdate = 0;

    // Update is called once per frame
    void Update()
    {
        // Reset processed message count
        int msgEventThisFrame = 0;

        // Poll until max messages or no more messages to receive, copying the message data 
        // into the batched events buffer
        _ReceivedMessagePoller.Poll((UdpMessage message, long sequence, bool endOfBatch) =>
        {
            //Debug.Log($"Sequence: {sequence} Data: {message.Buffer[0]}");
            _BatchedEvents[msgEventThisFrame].Message = message;
            msgEventThisFrame++;
            return msgEventThisFrame < MaxUdpMessagesPerFrame;
        });

        // _BatchedEvents is filled with messages pulled off the wire, so process (react to) them
        // which will mutate the reactor state and then return a set of output events to be further handled
        //Debug.Log($"NetManager Reacting to {msgEventThisFrame} events");
        for (int i = 0; i < msgEventThisFrame; i++)
        {
            R_NetManager.React(_BatchedEvents[i]);
        }

        // React to all of the output events with the a game server reactor
        //Debug.Log($"NetManager produced {R_NetManager.NetEventsQueue.Count} events");
        _TempEvent.EventId = GameEvent.Event.NETEVENT;
        while (R_NetManager.NetEventsQueue.Count > 0)
        {
            
            _TempEvent.NetEvent = R_NetManager.NetEventsQueue.Dequeue();
            //Debug.Log($"{Time.frameCount} - Evt: {_TempEvent.NetEvent.Type}");
            R_GameReactor.React(_TempEvent);
        }

        timeSinceLastUpdate += Time.deltaTime;
        if (timeSinceLastUpdate > (1f/60f))
        {
            timeSinceLastUpdate = 0;
            // TODO clean this up 
            R_GameReactor.React(_UpdateEvent);
        }

        //Debug.Log(R_NetManager.Statistics.ToString());
    }

    private void OnDestroy()
    {
        StopCoroutine("SendTimeoutEvent");
        _CancellationSource.Cancel();
        _ProcessOutgoing.Join();
    } 
}
