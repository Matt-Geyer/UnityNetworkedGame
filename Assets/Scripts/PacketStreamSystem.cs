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
    public const uint LastBitTrueMask = (uint)(1) << 31;
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
    public void DropUntilStartSeqEquals(byte seq)
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
    private readonly Queue<PacketTransmissionRecord> TransmissionNotifications;

    // The NetPeer for the remote stream
    private readonly NetPeer Peer;

    // Packet writer
    private readonly NetDataWriter NetWriter;

    // The packet we will constantly re-use to send data to the remote stream
    private NewPacket sendPacket;

    public PacketStreamSystem(NetPeer _peer)
    {
        TransmissionRecords = new Queue<PacketTransmissionRecord>();
        TransmissionNotifications = new Queue<PacketTransmissionRecord>();
        DataReceivedEvents = new List<NetEvent>();
        NetWriter = new NetDataWriter(false, 1500);
        Peer = _peer ?? throw new ArgumentNullException("peer");
        PacketBuffer = new NewPacket[100];
        RemoteSeqAckFlag = new SeqAckFlag();
    }

    public void Update()
    {        
        UpdateIncoming();
        UpdateOutgoing();
    }

    private void UpdateIncoming()
    {
        StringBuilder sb = new StringBuilder($"Update Incoming Frame: {Time.frameCount} Seq: {Seq}\n");

        try
        {
            // Process data received events
            sb.AppendLine($"Received {DataReceivedEvents.Count} packets");
            for (int i = 0; i < DataReceivedEvents.Count; i++)
            {
                // Get data reader from evt which contains the binary data for the packet
                NetPacketReader reader = DataReceivedEvents[i].DataReader;
                // Deserialize packet (including header)
                PacketBuffer[i].Deserialize(reader, true);
                                
                var header = PacketBuffer[i].Header;
                sb.AppendLine($"Received Packet Sequence: {header.Seq}");               
                sb.AppendLine($"Packet AckFlag: {header.AckFlag}");               
                sb.AppendLine($"Local AckedFlag- before: {RemoteSeqAckFlag}");
     
                if (RemoteSeqAckFlag.Seq_Count == 0)
                {
                    // No sequences in the flag so just initialize it with this sequence being acked
                    RemoteSeqAckFlag.InitWithAckedSequence(header.Seq);
                }               
                else if (SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.Seq_End, header.Seq)) 
                {
                    sb.AppendLine($"Received sequence {header.Seq} is ahead of the last sequence in our ack flag: {RemoteSeqAckFlag.Seq_End}");

                    // The seq is ahead of the range of our flag (ie a new seq) but we want to NACK any that 
                    // sequences that are in between the last sequence we acked, and this sequence we are now receiving
                    // since they must have been dropped (or delivered out of order)
                    while (RemoteSeqAckFlag.Seq_End != (byte)(header.Seq - 1))
                    {
                        RemoteSeqAckFlag.NackNextSequence();
                        sb.AppendLine($"NACKed sequence {RemoteSeqAckFlag.Seq_End}");
                    }

                    // Ack this sequence in our flag
                    RemoteSeqAckFlag.AckNextSequence();
                }
                else
                {
                    // This packet was delivered out of order
                    // or is outside the expected window so don't process it further
                    sb.AppendLine($"{header.Seq} - SKIPPED UNEXPECTED");
                    continue;
                }

                // Generate notifications based on ACKs received from the remote stream
                if (header.AckFlag.Seq_Count > 0)
                {
                    if (Seq_LastNotified == -1)
                    {
                        sb.AppendLine("Initializing Seq_LastNotified");
                        // This is the start of us notifying packets.. if any packets were sent but aren't
                        // included in this ack flag then they must have been dropped
                        while (TransmissionRecords.Peek().Seq != header.AckFlag.Seq_Start)
                        {
                            PacketTransmissionRecord record = TransmissionRecords.Dequeue();
                            Seq_LastNotified = record.Seq;
                            record.Received = false;
                            TransmissionNotifications.Enqueue(record);
                            sb.AppendLine($"Seq {record.Seq} was dropped");
                        }
                        
                        GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(header.AckFlag);
                    }
                    else if (SeqIsAheadButInsideWindow32((byte)Seq_LastNotified, header.AckFlag.Seq_Start))
                    {
                        // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                        while (Seq_LastNotified != (byte)(header.AckFlag.Seq_Start - 1))
                        {
                            PacketTransmissionRecord r = TransmissionRecords.Dequeue();
                            r.Received = false;
                            TransmissionNotifications.Enqueue(r);
                            Seq_LastNotified = ++Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
                            sb.AppendLine($"Sequence: {Seq_LastNotified} was dropped");
                        }

                        GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(header.AckFlag);
                    }                   
                    else if (SeqIsInsideRange(header.AckFlag.Seq_Start, header.AckFlag.Seq_End, (byte)Seq_LastNotified))
                    {
                        sb.AppendLine($"Seq_LastNotified ({Seq_LastNotified}) falls inside ack flag ({header.AckFlag.Seq_Start}, {header.AckFlag.Seq_End})");
                        header.AckFlag.DropUntilStartSeqEquals((byte)(Seq_LastNotified + 1));
                        sb.AppendLine($"After dropping already notified sequences header.AckFlag is now: {header.AckFlag}");
                        GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(header.AckFlag);
                    }                  
                }
            }
            sb.AppendLine($"Seq_LastNotified - After: {Seq_LastNotified}");
            sb.AppendLine($"Generated {TransmissionNotifications.Count} transmission notifications");
            sb.AppendLine($"There are now { TransmissionRecords.Count} remaining transmission records in the queue");

            // Process notifications which should be in order
            while (TransmissionNotifications.Count > 0)
            {
                PacketTransmissionRecord record = TransmissionNotifications.Dequeue();

                sb.AppendLine($"Sequence {record.Seq} Recv: {record.Received} ");

                if (record.Received)
                {
                    sb.AppendLine($"Current RemoteSeqAckFlag: {RemoteSeqAckFlag}");
                    sb.AppendLine($"TransmissionNotification AckFlag: {record.AckFlag}");

                    // I think this covers all edge cases?
                    if (RemoteSeqAckFlag.Seq_Start == record.AckFlag.Seq_End)
                    {
                        sb.AppendLine($"Current ACK starts where notification ack ended so we can drop the first seq of our current ack ");
                        RemoteSeqAckFlag.DropStartSequence();
                        sb.AppendLine($"RemoteSeqAckFlag after drop: {RemoteSeqAckFlag}");
                    }
                    else if (SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.Seq_Start, record.AckFlag.Seq_End))
                    {
                        sb.AppendLine($"Current ACK contains several sequence acks that the remote stream no longer needs so delete up to: {(byte)(record.AckFlag.Seq_End + 1)} ");
                        RemoteSeqAckFlag.DropUntilStartSeqEquals((byte)(record.AckFlag.Seq_End + 1));
                        sb.AppendLine($"RemoteSeqAckFlag after drop: {RemoteSeqAckFlag}");
                    }
                }

                // Each packet we send contains information regarding the last packet we have received from them as well as the payload
                // If we know they received this packet then we can stop sending the reliable data from that packet

                // Update the acks we are sending based on what we know the remote stream now knows


                // give packet to systems..
            }
        }
        catch (Exception e)
        {
            sb.AppendLine(e.Message);
            throw e;
        }
        finally
        {
            Debug.Log(sb.ToString());
            DataReceivedEvents.Clear();
        }
    }

   

    private void UpdateOutgoing()
    {
        StringBuilder sb = new StringBuilder($"UpdateOutgoing() - Frame: {Time.frameCount} Seq: {Seq}\n");

        // flow control
        if (TransmissionRecords.Count >= 32)
        {
            sb.Append("ACK WINDOW LIMIT REACHED.. HALTING OUTGOING COMMS");
            Debug.Log(sb.ToString());
            return;
        }

        // fill in our reusable packet struct with the data for this sequence
        sendPacket.Header.Seq = Seq;
        sendPacket.Header.AckFlag = RemoteSeqAckFlag;

        sb.AppendLine($"Generated Packet Seq: {sendPacket.Header.Seq}");
        sb.AppendLine($"RemoteAckFlag: {RemoteSeqAckFlag}");
        //sb.AppendLine($"RemoteAckFlag_StartSeq: {RemoteSequencesAckedFlag_StartSeq}");
        //sb.AppendLine($"RemoteAckFlag_SeqCount: {RemoteSequenceAckedFlag_SeqCount}");

        // Create a transmission record for the packet
        PacketTransmissionRecord record = new PacketTransmissionRecord
        {
            Received = false,
            Seq = sendPacket.Header.Seq,
            AckFlag = RemoteSeqAckFlag
        };

        // let each stream manager write until the packet is full

        // create output events
        TransmissionRecords.Enqueue(record);

        sendPacket.Serialize(NetWriter, true);
        Peer.Send(NetWriter.Data, 0, NetWriter.Length, DeliveryMethod.Unreliable);
        sb.AppendLine($"Sent Bytes: {NetWriter.Length}");
        NetWriter.Reset();

        // Only increment our seq on successful send
        // IE if waiting for acks then seq doesn't increase
        Seq++;

        Debug.Log(sb.ToString());
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
            TransmissionNotifications.Enqueue(r);
            Seq_LastNotified = ++Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
        }
    }

    static bool SeqIsAheadButInsideWindow32(byte current, byte check)
    {
        // 223 is byte.MaxValue - 32
        return ((check > current && (check - current <= 32)) ||
                (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
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