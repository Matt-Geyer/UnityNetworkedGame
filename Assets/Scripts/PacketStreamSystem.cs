using AiUnity.NLog.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;


/// <summary>
/// Represents a flag where each bit position represents whether or not a sequence was acked
/// If the bit was set it was ACKed otherwise it was NACKed 
/// TODO: Add DEBUG conditional checks on the data
/// </summary>
public struct SeqAckFlag
{
    public const int AckFlagSize = 32;
    public const uint LastBitTrueMask = (uint)1 << 31;
    public uint Data;
    public byte Seq_Start;
    public byte Seq_Count;
    public byte Seq_End;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Seq_Count);
        if (Seq_Count > 0)
        {
            writer.Put(Seq_Start);
            writer.Put(Data);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Seq_Count = reader.GetByte();
        if (Seq_Count > 0)
        {
            Seq_Start = reader.GetByte();
            Data = reader.GetUInt();
            Seq_End = (byte)(Seq_Start + Seq_Count - 1);
        }
    }

    public void InitFromData(uint data, byte start, byte count)
    {
        Data = data;
        Seq_Start = start;
        Seq_Count = count;
        Seq_End = (byte)(start + count - 1);
    }

    public void InitWithAckedSequence(byte seq)
    {
        Seq_Start = Seq_End = seq;
        Data = 1;
        Seq_Count = 1;
    }

    public bool IsAck(int seqBitPositionInFlag)
    {
        return ((Data >> seqBitPositionInFlag) & 1) == 1;
    }

    public void NackNextSequence()
    {
        if (Seq_Count < AckFlagSize)
        {
            Data &= (uint)~(1 << Seq_Count);
            Seq_Count++;
            Seq_End++;
        }
        else
        {
            Data = Data >> 1;
            Seq_Start++;
            Seq_End++;
        }
    }

    public void AckNextSequence()
    {
        if (Seq_Count < AckFlagSize)
        {
            Data |= (uint)1 << Seq_Count;
            Seq_Count++;
            Seq_End++;
        }
        else
        {
            Data = Data >> 1 | LastBitTrueMask;
            Seq_Start++;
            Seq_End++;
        }
    }

    /// <summary>
    /// Seq must be within (seq_start, seq_end) or this will fuck up
    /// </summary>
    /// <param name="seq"></param>
    public void DropStartSequenceUntilItEquals(byte seq)
    {
        while (Seq_Start != seq)
        {
            DropStartSequence();
        }
    }

    public void DropStartSequence()
    {
        Data = Data >> 1;
        Seq_Start++;
        Seq_Count--;
        // end seq stays the same
    }

    public override string ToString()
    {
        return $"Seq_Count: {Seq_Count}  Seq_Start: {Seq_Start}  Seq_End: {Seq_End}  Data: {Data}";
    }
}


public struct PacketHeader
{
    public byte Seq;

    public SeqAckFlag AckFlag;

    public byte DataFlag; 

    public void Deserialize(NetDataReader reader)
    {
        Seq = reader.GetByte();
        AckFlag.Deserialize(reader);
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Seq);
        AckFlag.Serialize(writer);
    }
}


public struct NewPacket
{
    public PacketHeader Header;
    
    public void Deserialize(NetDataReader reader, bool includeHeader = false)
    {
        if (includeHeader) Header.Deserialize(reader);
    }

    public void Serialize(NetDataWriter writer, bool includeHeader = false)
    {
        if (includeHeader) Header.Serialize(writer);
    }
}


public class PacketTransmissionRecord
{
    // Packet stream system data
    public byte Seq; 
    public SeqAckFlag AckFlag;
    public bool Received;

    // manager data
    public List<ReplicatedObjectTransmissionRecord> ReplicationTransmissions = new List<ReplicatedObjectTransmissionRecord>();
}


public class PacketStreamSystem
{    
    // This streams current sequence
    public byte Seq = 0; 

    // Seq are always notified in order
    public int Seq_LastNotified = -1;

    // The latest sequence that we have received from the remote stream
    public byte Seq_Remote = 0;

    public SeqAckFlag RemoteSeqAckFlag;

    // This will be filled externally with the DataReceived NetEvents from socket for this peer
    public readonly List<NetEvent> DataReceivedEvents;

    // Pre-allocated buffer of packet structs to use for processing incoming events
    public NewPacket[] PacketBuffer;

    // Information about sent packets stored in the order they were sent and expected to be processed in order
    private readonly Queue<PacketTransmissionRecord> TransmissionRecords;

    // A PacketTransmissionRecord becomes a notification once we have determined if the packet it references was received or not
    private readonly List<PacketTransmissionRecord> TransmissionNotifications;

    // The NetPeer for the remote stream
    private readonly NetPeer Peer;

    // Packet writer
    private readonly NetDataWriter NetWriter;

    // The packet we will constantly re-use to send data to the remote stream
    private NewPacket sendPacket;

    private readonly ReplicationSystem Replication;

    private readonly IPlayerControlledObjectSystem PCOSystem;

    private readonly NLogger Log;

    public PacketStreamSystem(NetPeer _peer, ReplicationSystem replication, IPlayerControlledObjectSystem playerControlledObjectSystem)
    {
        Peer = _peer ?? throw new ArgumentNullException("peer");
        Replication = replication ?? throw new ArgumentNullException("replication");
        PCOSystem = playerControlledObjectSystem ?? throw new ArgumentNullException("playerControlledObjectSystem");
        Log = NLogManager.Instance.GetLogger(this);
        TransmissionRecords = new Queue<PacketTransmissionRecord>();
        TransmissionNotifications = new List<PacketTransmissionRecord>();
        DataReceivedEvents = new List<NetEvent>();
        NetWriter = new NetDataWriter(false, 1500);
        PacketBuffer = new NewPacket[100];
        RemoteSeqAckFlag = new SeqAckFlag();
    }

    public void UpdateIncoming(bool host = false)
    {
        if (DataReceivedEvents.Count == 0) return;

        Log.Debug($"UPDATE INCOMING - Frame: {Time.frameCount} Seq: {Seq}\n");

        try
        {
            // Process data received events
            Log.Debug($"Received {DataReceivedEvents.Count} packets");
            for (int i = 0; i < DataReceivedEvents.Count; i++)
            {
                // Get data reader from evt which contains the binary data for the packet
                NetPacketReader reader = DataReceivedEvents[i].DataReader;
                // Deserialize packet (including header)
                PacketBuffer[i].Deserialize(reader, true);
                                
                var header = PacketBuffer[i].Header;
                Log.Debug($"Received Packet Sequence: {header.Seq}");               
                Log.Debug($"Packet AckFlag: {header.AckFlag}");               
                Log.Debug($"Local AckedFlag- before: {RemoteSeqAckFlag}");
     
                if (RemoteSeqAckFlag.Seq_Count == 0)
                {
                    // No sequences in the flag so just initialize it with this sequence being acked
                    RemoteSeqAckFlag.InitWithAckedSequence(header.Seq);
                }               
                else if (SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.Seq_End, header.Seq)) 
                {
                    Log.Debug($"Received sequence {header.Seq} is ahead of the last sequence in our ack flag: {RemoteSeqAckFlag.Seq_End}");

                    // The seq is ahead of the range of our flag (ie a new seq) but we want to NACK any that 
                    // sequences that are in between the last sequence we acked, and this sequence we are now receiving
                    // since they must have been dropped (or delivered out of order)
                    while (RemoteSeqAckFlag.Seq_End != (byte)(header.Seq - 1))
                    {
                        RemoteSeqAckFlag.NackNextSequence();
                        Log.Debug($"NACKed sequence {RemoteSeqAckFlag.Seq_End}");
                    }

                    // Ack this sequence in our flag
                    RemoteSeqAckFlag.AckNextSequence();
                }
                else
                {
                    // This packet was delivered out of order
                    // or is outside the expected window so don't process it further
                    Log.Debug($"{header.Seq} - SKIPPED UNEXPECTED");
                    continue;
                }

                // Generate notifications based on ACKs received from the remote stream
                if (header.AckFlag.Seq_Count > 0)
                {
                    if (Seq_LastNotified == -1)
                    {
                        Log.Debug("Initializing Seq_LastNotified");
                        // This is the start of us notifying packets.. if any packets were sent but aren't
                        // included in this ack flag then they must have been dropped
                        while (TransmissionRecords.Peek().Seq != header.AckFlag.Seq_Start)
                        {
                            PacketTransmissionRecord record = TransmissionRecords.Dequeue();
                            Seq_LastNotified = record.Seq;
                            record.Received = false;
                            TransmissionNotifications.Add(record);
                            Log.Debug($"Seq {record.Seq} was dropped");
                        }
                        // Notify based on sequences in flag
                        GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(header.AckFlag);
                    }
                    else if (SeqIsAheadButInsideWindow32((byte)Seq_LastNotified, header.AckFlag.Seq_Start))
                    {
                        // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                        while (Seq_LastNotified != (byte)(header.AckFlag.Seq_Start - 1))
                        {
                            PacketTransmissionRecord r = TransmissionRecords.Dequeue();
                            r.Received = false;
                            TransmissionNotifications.Add(r);
                            Seq_LastNotified = ++Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
                            Log.Debug($"Sequence: {Seq_LastNotified} was dropped");
                        }
                        // Notify based on sequences in flag
                        GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(header.AckFlag);
                    }                   
                    else if (SeqIsInsideRangeInclusive(header.AckFlag.Seq_Start, header.AckFlag.Seq_End, (byte)Seq_LastNotified))
                    {
                        Log.Debug($"{Seq_LastNotified} is inside ack flag range");
                        // Drop sequences we have already notified                                            
                        header.AckFlag.DropStartSequenceUntilItEquals((byte)(Seq_LastNotified + 1));

                        // Notify based on sequences remaining in flag
                        GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(header.AckFlag);
                    }             
                }

                Log.Debug("Finished generating notifications");

                // Give stream to each system to process
                // todo this will be generalized to an ordered list of stream readers and writers
                if (host)
                {
                    Log.Debug("Giving stream to PCOSystem");
                    PCOSystem.ProcessClientToServerStream(reader);
                }
                else
                {
                    Log.Debug("Giving stream to PCOSystem");
                    PCOSystem.ProcessServerToClientStream(reader);
                    Log.Debug("Giving stream to ReplicationSystem");
                    Replication.ProcessReplicationData(reader);
                }
            }
            Log.Debug($"Seq_LastNotified - After: {Seq_LastNotified}");
            Log.Debug($"Generated {TransmissionNotifications.Count} transmission notifications");
            Log.Debug($"There are now { TransmissionRecords.Count} remaining transmission records in the queue");

            // Process notifications which should be in order
            foreach (PacketTransmissionRecord record in TransmissionNotifications)
            {
                Log.Debug($"Sequence {record.Seq} Recv: {record.Received} ");

                if (record.Received)
                {           
                    // Drop the start of our current ack flage since we know that the remote stream
                    // knows about the sequence that it represents
                    // I think this covers all edge cases?
                    if (record.AckFlag.Seq_Count > 0)
                    {
                        if (RemoteSeqAckFlag.Seq_Start == record.AckFlag.Seq_End)
                        {
                            RemoteSeqAckFlag.DropStartSequence();
                        }
                        else if (SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.Seq_Start, record.AckFlag.Seq_End))
                        {
                            RemoteSeqAckFlag.DropStartSequenceUntilItEquals((byte)(record.AckFlag.Seq_End + 1));
                        }
                    }
                }
            }

            if (host)
            {
               
                Replication.ProcessNotifications(TransmissionNotifications);
            }
        }
        catch (Exception e)
        {
            Log.Debug(e.Message);
            throw e;
        }
        finally
        {
            DataReceivedEvents.Clear();
            TransmissionNotifications.Clear();
        }
    }

    public void UpdateOutgoing(bool host = false)
    {
        Log.Debug($"UpdateOutgoing() - Frame: {Time.frameCount} Seq: {Seq}\n");

        // flow control
        if (TransmissionRecords.Count >= 32)
        {
            Log.Debug("ACK WINDOW LIMIT REACHED.. HALTING OUTGOING COMMS");
            return;
        }

        // fill in our reusable packet struct with the data for this sequence
        sendPacket.Header.Seq = Seq;
        sendPacket.Header.AckFlag = RemoteSeqAckFlag;

        Log.Debug($"Generated Packet Seq: {sendPacket.Header.Seq}");
        Log.Debug($"RemoteAckFlag: {RemoteSeqAckFlag}");
       
        // Create a transmission record for the packet
        PacketTransmissionRecord record = new PacketTransmissionRecord
        {
            Received = false,
            Seq = sendPacket.Header.Seq,
            AckFlag = RemoteSeqAckFlag
        };

        // Write the packet header to the stream
        sendPacket.Serialize(NetWriter, true);

        // let each stream manager write until the packet is full
        if (host)
        {
            PCOSystem.WriteServerToClientStream(NetWriter);
            Replication.WriteReplicationData(NetWriter, record);
        }
        else
        {
            PCOSystem.WriteClientToServerStream(NetWriter);
        }
        
        // create output events
        TransmissionRecords.Enqueue(record);

        Peer.Send(NetWriter.Data, 0, NetWriter.Length, DeliveryMethod.Unreliable);
        Log.Debug($"Sent Bytes: {NetWriter.Length}");
        NetWriter.Reset();

        // Only increment our seq on successful send
        // IE if waiting for acks then seq doesn't increase
        Seq++;
    }

    /// <summary>
    /// AckFlag must start at Seq_LastNotified + 1
    /// All seqs in flag will be notified
    /// </summary>
    /// <param name="flag"></param>
    /// <param name="seq_count"></param>
    private void GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(SeqAckFlag flag)
    {
        // Notify based on the flag
        for (int seqBitPos = 0; seqBitPos < flag.Seq_Count; seqBitPos++)
        {
            PacketTransmissionRecord r = TransmissionRecords.Dequeue();
            r.Received = flag.IsAck(seqBitPos);
            TransmissionNotifications.Add(r);
            Seq_LastNotified = ++Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
        }
    }

    static bool SeqIsAheadButInsideWindow32(byte current, byte check)
    {
        // 223 is byte.MaxValue - 32
        return ((check > current && (check - current <= 32)) ||
                (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
    }

    static bool SeqIsEqualOrAheadButInsideWindow32(byte current, byte check)
    {
        // 223 is byte.MaxValue - 32
        return (current == check ||
               (check > current && (check - current <= 32)) ||
               (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
    }

    static bool SeqIsInsideRangeInclusive(byte start, byte end, byte check)
    {
        return SeqIsEqualOrAheadButInsideWindow32(check, end) && SeqIsEqualOrAheadButInsideWindow32(start, check);
    }


    // assumed that start and end are valid in terms of their distance from each other
    // being at most 32 and start being < end in terms of a sequence timeline
    static bool SeqIsInsideRange(byte start, byte end, byte check)
    {
        // if the end of the range is ahead of the check value, and the check value is ahead of the start of the range
        // then the check value must be inside of the range
        return SeqIsAheadButInsideWindow32(check, end) && SeqIsAheadButInsideWindow32(start, check);
    }

}