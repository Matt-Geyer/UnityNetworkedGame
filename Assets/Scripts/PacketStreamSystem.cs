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


public struct PacketHeader
{
    public byte Seq;
    public byte AckFlag_StartSeq;
    public ushort AckFlag_SeqCount;
    public uint AckFlag;
    public byte DataFlag; 

    public void Deserialize(NetDataReader reader)
    {
        Seq = reader.GetByte();
        AckFlag_SeqCount = reader.GetUShort();
        if (AckFlag_SeqCount > 0)
        {
            AckFlag_StartSeq = reader.GetByte();
            AckFlag = reader.GetUInt();
        }
    }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Seq);
        writer.Put(AckFlag_SeqCount);
        if (AckFlag_SeqCount > 0)
        {
            writer.Put(AckFlag_StartSeq);
            writer.Put(AckFlag);
        }
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
    public byte AckFlag_StartSeq;
    public ushort AckFlag_SeqCount;
    public uint AckFlag;
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

    public uint RemoteSequencesAckedFlag;                          // The flag that holds the data (todo 64 bit or maybe even bitvector)
    public byte RemoteSequencesAckedFlag_StartSeq;                 // the sequence associated with bit position 0 in the flag
    public int RemoteSequenceAckedFlag_SeqCount;                   // # of sequences present in the flag
    public byte RemoteSequencesAckedFlag_EndSeq;                   // The sequence associated with the last bit in the flag (calculated and not sent)
    private const int RemoteSequencesAckedFlag_MaxSeqCount = 32;   // max # of possible sequences (bits) in the flag

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
                NetPacketReader reader = DataReceivedEvents[i].DataReader;
                PacketBuffer[i].Deserialize(reader, true);

                var header = PacketBuffer[i].Header;

                byte endSeq = RemoteSequencesAckedFlag_StartSeq;
                endSeq += (byte)RemoteSequenceAckedFlag_SeqCount;
                endSeq--;

                byte a = byte.MaxValue - 32;
                byte b = byte.MaxValue;
                b -= endSeq;
                byte c = 32;
                c -= b;
   
                sb.AppendLine($"Remote Packet Sequence: {header.Seq}");               
                sb.AppendLine($"AckFlag: {header.AckFlag}");
                sb.AppendLine($"AckStart: {header.AckFlag_StartSeq}");
                sb.AppendLine($"AckEnd: {(byte)(header.AckFlag_StartSeq + (byte)(header.AckFlag_SeqCount - 1))}");
                sb.AppendLine($"AckCount: {header.AckFlag_SeqCount} ");
                sb.AppendLine($"RemoteAckedFlag EndSeq - before: {endSeq}");
                sb.AppendLine($"a: {a} b: {b} c: {c}");

                if (RemoteSequenceAckedFlag_SeqCount == 0)
                {
                    RemoteSequencesAckedFlag = 1;
                    RemoteSequencesAckedFlag_StartSeq = header.Seq;
                    RemoteSequenceAckedFlag_SeqCount = 1;
                }
                // The seq is ahead of the range of our flag (ie a new seq) if either of these things are true
                else if ((header.Seq > endSeq && (header.Seq - endSeq <= 32)) ||
                         (header.Seq < endSeq && (endSeq > a && header.Seq <= c)))
                {
                    while (endSeq != (byte)(header.Seq - 1))
                    {
                        // Ack flag isn't full so clear the next position and increment the seq count
                        if (RemoteSequenceAckedFlag_SeqCount < RemoteSequencesAckedFlag_MaxSeqCount)
                        {
                            RemoteSequencesAckedFlag &= (uint)~(1 << RemoteSequenceAckedFlag_SeqCount);
                            RemoteSequenceAckedFlag_SeqCount++;
                        }
                        // Ack flag is full so shift right which pops the start sequence off leaving a 0
                        // in the last bit position
                        else
                        {
                            RemoteSequencesAckedFlag = RemoteSequencesAckedFlag >> 1;
                            RemoteSequencesAckedFlag_StartSeq++;
                        }

                        endSeq = RemoteSequencesAckedFlag_StartSeq;
                        endSeq += (byte)RemoteSequenceAckedFlag_SeqCount;
                        endSeq--;
                        sb.AppendLine($"NACKing sequence {endSeq}");
                    }

                    // ack this seq in flag and flag end is now this seq
                    if (RemoteSequenceAckedFlag_SeqCount < 32)
                    {
                        RemoteSequencesAckedFlag |= (uint)1 << RemoteSequenceAckedFlag_SeqCount;
                        RemoteSequenceAckedFlag_SeqCount++;
                    }
                    else
                    {
                        RemoteSequencesAckedFlag = RemoteSequencesAckedFlag >> 1 | (uint)1 << 31;
                        RemoteSequencesAckedFlag_StartSeq++;
                    }

                    endSeq = RemoteSequencesAckedFlag_StartSeq;
                    endSeq += (byte)RemoteSequenceAckedFlag_SeqCount;
                    endSeq--;
                    sb.AppendLine($"RemoteAckedFlag EndSeq - after: {endSeq}");
                }
                else
                {
                    // This packet was delivered out of order
                    // or is outside the expected window so don't process it further
                    sb.AppendLine($"{header.Seq} - SKIPPED UNEXPECTED");
                    continue;
                }

                // Notifications..
                if (header.AckFlag_SeqCount > 0)
                {
                    if (Seq_LastNotified == -1)
                    {

                        PacketTransmissionRecord record;
                        while (TransmissionRecords.Peek().Seq != header.AckFlag_StartSeq)
                        {
                            record = TransmissionRecords.Dequeue();
                            Seq_LastNotified = record.Seq;
                            record.Received = false;
                            TransmissionNotifications.Enqueue(record);
                        }

                        for (int x = 0; x < header.AckFlag_SeqCount; x++)
                        {
                            record = TransmissionRecords.Dequeue();
                            record.Received = ((header.AckFlag >> x) & 1) == 1;
                            TransmissionNotifications.Enqueue(record);
                            Seq_LastNotified++;
                            Seq_LastNotified = Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
                            //sb.AppendLine($"Seq: {record.Seq} Seq_LastNotified: {Seq_LastNotified} Received: {record.Received}");
                        }
                    }

                    else if (header.AckFlag_StartSeq > Seq_LastNotified && (Seq_LastNotified > 32 || (header.AckFlag_StartSeq < byte.MaxValue - 32)) ||
                            (header.AckFlag_StartSeq < Seq_LastNotified && (Seq_LastNotified > byte.MaxValue - 32 && header.AckFlag_StartSeq < 32)))
                    {

                        // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                        while (Seq_LastNotified != (byte)(header.AckFlag_StartSeq - 1))
                        {
                            PacketTransmissionRecord r = TransmissionRecords.Dequeue();
                            r.Received = false;
                            TransmissionNotifications.Enqueue(r);
                            Seq_LastNotified++;
                            Seq_LastNotified = Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
                            //sb.AppendLine($"Seq: {r.Seq} Seq_LastNotified: {Seq_LastNotified} Received: {r.Received}");
                        }
                        // Now N/ACK all packets based on this ACK flag because it contains entirely new data
                        for (int x = 0; x < header.AckFlag_SeqCount; x++)
                        {
                            PacketTransmissionRecord r = TransmissionRecords.Dequeue();
                            r.Received = ((header.AckFlag >> x) & 1) == 1;
                            TransmissionNotifications.Enqueue(r);
                            Seq_LastNotified++;
                            Seq_LastNotified = Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
                            //sb.AppendLine($"Seq: {r.Seq} Seq_LastNotified: {Seq_LastNotified} Received: {r.Received}");
                        }
                    }
                    else if (
                        header.AckFlag_StartSeq <= Seq_LastNotified ||
                        (header.AckFlag_StartSeq > Seq_LastNotified && header.AckFlag_StartSeq >= byte.MaxValue - 32))
                    {
                        // some of the packets from this flag have already been notified
                        byte sln_plus_one = (byte)Seq_LastNotified;
                        sln_plus_one++;

                        while (header.AckFlag_StartSeq != sln_plus_one && header.AckFlag_SeqCount > 0)
                        {
                            header.AckFlag = header.AckFlag >> 1;
                            header.AckFlag_StartSeq++;
                            header.AckFlag_SeqCount--;
                        }

                        for (int x = 0; x < header.AckFlag_SeqCount; x++)
                        {
                            PacketTransmissionRecord r = TransmissionRecords.Dequeue();
                            r.Received = ((header.AckFlag >> x) & 1) == 1;
                            TransmissionNotifications.Enqueue(r);
                            Seq_LastNotified++;
                            Seq_LastNotified = Seq_LastNotified <= byte.MaxValue ? Seq_LastNotified : 0;
                            //sb.AppendLine($"Seq: {r.Seq} Seq_LastNotified: {Seq_LastNotified} Received: {r.Received}");

                        }
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

                // Each packet we send contains information regarding the last packet we have received from them as well as the payload
                // If we know they received this packet then we can stop sending the reliable data from that packet

                // Update the acks we are sending based on what we know the remote stream now knows


                // give packet to systems..
            }

            DataReceivedEvents.Clear();
            Debug.Log(sb.ToString());
        }
        catch (Exception e)
        {
            sb.AppendLine(e.Message);
            Debug.Log(sb.ToString());
            throw e;
        }
    }

    private void UpdateOutgoing()
    {
        StringBuilder sb = new StringBuilder($"UpdateOutgoing() - Frame: {Time.frameCount} Seq: {Seq}\n");

        // flow control
        if (TransmissionRecords.Count >= 32)
        {
            Debug.Log("ACK WINDOW LIMIT REACHED.. HALTING OUTGOING COMMS");
            return;
        }

        // fill in our reusable packet struct with the data for this sequence
        sendPacket.Header.Seq = Seq;
        sendPacket.Header.AckFlag_StartSeq = RemoteSequencesAckedFlag_StartSeq;
        sendPacket.Header.AckFlag_SeqCount = (ushort)RemoteSequenceAckedFlag_SeqCount;
        sendPacket.Header.AckFlag = RemoteSequencesAckedFlag;

        sb.AppendLine($"Generated Packet Seq: {sendPacket.Header.Seq}");
        sb.AppendLine($"RemoteAckFlag: {RemoteSequencesAckedFlag}");
        sb.AppendLine($"RemoteAckFlag_StartSeq: {RemoteSequencesAckedFlag_StartSeq}");
        sb.AppendLine($"RemoteAckFlag_SeqCount: {RemoteSequenceAckedFlag_SeqCount}");

        // Create a transmission record for the packet
        PacketTransmissionRecord record = new PacketTransmissionRecord
        {
            Received = false,
            Seq = sendPacket.Header.Seq,
            AckFlag = sendPacket.Header.AckFlag,
            AckFlag_SeqCount = sendPacket.Header.AckFlag_SeqCount,
            AckFlag_StartSeq = sendPacket.Header.AckFlag_StartSeq,
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

}