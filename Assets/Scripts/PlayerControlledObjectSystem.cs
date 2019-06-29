using AiUnity.NLog.Core;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;

public class PlayerControlledObject : IPersistentObject
{
    public static PersistentObjectRep StaticObjectRep;

    public GameObject Entity;
    public CharacterController PlayerController;

    public PersistentObjectRep ObjectRep
    {
        get => StaticObjectRep;
        set => StaticObjectRep = value;
    }

    public virtual void ApplyInput(UserInputSample input)
    {
        PlayerController.Move(input.MoveDirection * 2f * (1f / 60f));
    }

    public void Deserialize(NetDataReader reader)
    {
        Entity.transform.SetPositionAndRotation(new Vector3(reader.GetFloat(), 0, reader.GetFloat()), new Quaternion());
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Entity.transform.position.x);
        writer.Put(Entity.transform.position.z);
    }
}


public interface IPlayerControlledObjectSystem
{
    /// <summary>
    /// Start replicating this player controlled object on the client
    /// </summary>
    /// <param name="pco"></param>
    void StartReplicating(PlayerControlledObject pco);

    /// <summary>
    /// Stop replicating this player controlled object on the client
    /// </summary>
    /// <param name="pco"></param>
    void StopReplicating(PlayerControlledObject pco);
    
    /// <summary>
    ///  Called on the client to write data to be read by ProcessClientToServerStream 
    /// </summary>
    /// <param name="stream"></param>
    void WriteClientToServerStream(NetDataWriter stream);

    /// <summary>
    ///  Called on the server to read and process data written by WriteClientToServerStream 
    /// </summary>
    /// <param name="stream"></param>
    void ProcessClientToServerStream(NetDataReader stream);

    /// <summary>
    ///  Called on the server to write data to be read by ProcessServerToClientStream 
    /// </summary>
    /// <param name="stream"></param>
    void WriteServerToClientStream(NetDataWriter stream);

    /// <summary>
    ///  Called on the client to read and process data written by WriteServerToClientStream 
    /// </summary>
    /// <param name="stream"></param>
    void ProcessServerToClientStream(NetDataReader stream);

    /// <summary>
    /// Sample move, add to buffer, apply
    /// </summary>
    void UpdateControlledObject();

    PlayerControlledObject ControlledObject { get; set; }
        
}



public class PlayerControlledObjectSystem : IPlayerControlledObjectSystem
{
    class UserInputWindow
    {
        public UserInputSample[] Input;
        public int First;
        public int Last;
        public int Count;
        public int Max;
        public ushort Seq;
        public ushort UpperSeqWindow;
        public IUserInputUtils Sampler;       

    

        public void Init(int max)
        {
            Input = new UserInputSample[max];
            for (int i = 0; i < max; i++)
            {
                Input[i] = new UserInputSample();
            }
            First = Last = 0;
            Count = 0;
            Max = max;
            UpperSeqWindow = (ushort)(ushort.MaxValue - Max);
        }

        public int SampleUserInput()
        {
            if (Count == Max) return -1;
            int sampleIndex = Last;
            Sampler.Sample(Input[sampleIndex]);
            Input[sampleIndex].Seq = Seq;
            Last = ++Last < Max ? Last : 0;
            Count++;
            Seq++;
            return sampleIndex;
        }

        public void AckSeq(ushort Seq)
        {
            // if seq > and inside window 
            // 223 is byte.MaxValue - 32
            ushort FirstSeq = Input[First].Seq;

            if  (FirstSeq == Seq ||
                (Seq > FirstSeq && (Seq - FirstSeq <= Max)) ||
                (Seq < FirstSeq && (FirstSeq > UpperSeqWindow && Seq < (ushort)(Max - (ushort.MaxValue - FirstSeq)))))
            {
                // drop moves off the front of the window until the window starts at Seq + 1 or count = 0
                int targetSeq = Seq + 1;
                while (Count > 0 && Input[First].Seq != targetSeq)
                {
                    First = ++First < Max ? First : 0;
                    Count--;
                }
            }
        }
    }

    private UserInputWindow PlayerInputWindow;

    private List<UserInputSample> PlayerInputsToTransmit;

    public ushort Seq;

    public int Seq_LastProcessed = -1;

    public PlayerControlledObject ControlledObject { get; set; }

    private readonly NLogger Log;

    public PlayerControlledObjectSystem()
    {
        Log = NLogManager.Instance.GetLogger(this);

        PlayerInputWindow = new UserInputWindow
        {
            Sampler = new UserInputUtils()
        };

        PlayerInputWindow.Init(360);

        // Since we are always going to transmit the last 3 moves I figured this
        // was a simple way to simplify the transmit logic to not have to check if thare are at least 3 
        for (int i = 0; i < 3; i++)
            PlayerInputWindow.SampleUserInput();

        PlayerInputsToTransmit = new List<UserInputSample>(3)
        {
            PlayerInputWindow.Input[0],
            PlayerInputWindow.Input[1],
            PlayerInputWindow.Input[2]
        };
    }


    public void StartReplicating(PlayerControlledObject pco)
    {
        // start replicating this object which is controlled by a player (not the player who will be receiving this state tho)
    }

    public void StopReplicating(PlayerControlledObject pco)
    {
        // stop replicating a pco
    }

    public void WriteClientToServerStream(NetDataWriter stream)
    {
        // write players last 3 moves to stream
        PlayerInputsToTransmit[0].Serialize(stream);
        PlayerInputsToTransmit[1].Serialize(stream);
        PlayerInputsToTransmit[2].Serialize(stream);

        Log.Debug($"PlayerInputsToTransmit[0].Seq:{ PlayerInputsToTransmit[0].Seq}");
        Log.Debug($"PlayerInputsToTransmit[1].Seq:{ PlayerInputsToTransmit[1].Seq}");
        Log.Debug($"PlayerInputsToTransmit[2].Seq:{ PlayerInputsToTransmit[2].Seq}");

    }

    public void WriteServerToClientStream(NetDataWriter stream)
    {
        // Send id of last move that was received from client
        stream.Put((ushort)Seq_LastProcessed);

        // Send new pco state
        ControlledObject.Serialize(stream);

        // Send state of all pco that are being replicated by this system
    }

    public void ProcessClientToServerStream(NetDataReader stream)
    {
        // The players last 3 moves are always transmitted with the last move being the most recent
        PlayerInputsToTransmit[0].Deserialize(stream);
        PlayerInputsToTransmit[1].Deserialize(stream);
        PlayerInputsToTransmit[2].Deserialize(stream);

        Debug.Log("Read client inputs: ");
        Debug.Log($"Seq: {PlayerInputsToTransmit[0].Seq} Move:{PlayerInputsToTransmit[0].MoveDirection}");
        Debug.Log($"Seq: {PlayerInputsToTransmit[1].Seq} Move:{PlayerInputsToTransmit[1].MoveDirection}");
        Debug.Log($"Seq: {PlayerInputsToTransmit[2].Seq} Move:{PlayerInputsToTransmit[2].MoveDirection}");


        // In a 0 packet loss scenario Input [1] was last sequence and input [2] is this sequence
        // but we will look further back, and if they are all new then apply all 3 moves        
        ushort nextMoveSeq = (ushort)(Seq_LastProcessed + 1);
        Debug.Log($"LastProcessedMoveSeq: {Seq_LastProcessed} NextMove: {nextMoveSeq}");
        int i = 2;
        for (; i >= 0; i--)
        {
            Debug.Log($"PlayerInputsToTransmit[{i}].Seq: {PlayerInputsToTransmit[i].Seq}");
            if (PlayerInputsToTransmit[i].Seq == nextMoveSeq) break;
        }


        i = i >= 0 ? i : 0;

        // This should always have at least one new move but up to 3
        for (int j = i; j <= 2; j++)
        {
            Debug.Log($"Looking at PlayerInputsToTransmit[{j}]");
            ControlledObject.ApplyInput(PlayerInputsToTransmit[j]);
            Seq_LastProcessed = PlayerInputsToTransmit[j].Seq;
            Debug.Log($"Applied PlayerInputsToTransmit[{j}] with Seq: {PlayerInputsToTransmit[j].Seq}");
        }
    }

    public void ProcessServerToClientStream(NetDataReader stream)
    {
        // read id of last processed move and use it to update
        // the buffer of stored moves
        Seq_LastProcessed = stream.GetUShort();
        Log.Debug($"Seq_LastProcessed from server: {Seq_LastProcessed}");

        
        PlayerInputWindow.AckSeq((ushort)Seq_LastProcessed);

        Log.Debug($"Updated PlayerInputWindow");

        // read state of player obj and set it using remainder of moves in buffer to predict again
        ControlledObject.Deserialize(stream);

        Log.Debug($"Read controlled object state");


        // read state of all replicated pco and predict
        for (int i = 0; i < PlayerInputWindow.Count; i++)
        {
            ControlledObject.ApplyInput(PlayerInputWindow.Input[PlayerInputWindow.First + i]);
        }

        Log.Debug($"Finished applying un-acked moves");
    }

    public void UpdateControlledObject()
    {
        // sample move
        int nextSampleIndex = PlayerInputWindow.SampleUserInput();

    

        if (nextSampleIndex >= 0)
        {
            Log.Debug($"Current Sample - PlayerInputWindow[{nextSampleIndex}] Seq: {PlayerInputWindow.Input[nextSampleIndex].Seq} Move: {PlayerInputWindow.Input[nextSampleIndex].MoveDirection}");

            // apply move 
            ControlledObject.ApplyInput(PlayerInputWindow.Input[nextSampleIndex]);

            // Update packets to transmit 
            PlayerInputsToTransmit.RemoveAt(0);
            PlayerInputsToTransmit.Add(PlayerInputWindow.Input[nextSampleIndex]);
        }
    }
}
