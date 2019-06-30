using AiUnity.NLog.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    public class PacketStreamSystem
    {
        // This streams current sequence
        public byte Seq;

        // Seq are always notified in order
        public int SeqLastNotified = -1;

        public SeqAckFlag RemoteSeqAckFlag;

        // This will be filled externally with the DataReceived NetEvents from socket for this peer
        public readonly List<NetEvent> DataReceivedEvents;

        // Information about sent packets stored in the order they were sent and expected to be processed in order
        private readonly Queue<PacketTransmissionRecord> _transmissionRecords;

        // A PacketTransmissionRecord becomes a notification once we have determined if the packet it references was received or not
        private readonly List<PacketTransmissionRecord> _transmissionNotifications;

        // The NetPeer for the remote stream
        private readonly NetPeer _peer;

        // Packet writer
        private readonly NetDataWriter _netWriter;

        private PacketHeader _header;

        private readonly ReplicationSystem _replicationSystem;

        private readonly IPlayerControlledObjectSystem _controlledObjectSystem;

        private readonly NLogger _log;

        public PacketStreamSystem(NetPeer peer, ReplicationSystem replication, IPlayerControlledObjectSystem playerControlledObjectSystem)
        {
            _peer = peer ?? throw new ArgumentNullException(nameof(peer));
            _replicationSystem = replication ?? throw new ArgumentNullException(nameof(replication));
            _controlledObjectSystem = playerControlledObjectSystem ?? throw new ArgumentNullException(nameof(playerControlledObjectSystem));
            _log = NLogManager.Instance.GetLogger(this);
            _transmissionRecords = new Queue<PacketTransmissionRecord>();
            _transmissionNotifications = new List<PacketTransmissionRecord>();
            DataReceivedEvents = new List<NetEvent>();
            _netWriter = new NetDataWriter(false, 1500);
            RemoteSeqAckFlag = new SeqAckFlag();
        }

        public void UpdateIncoming(bool host = false)
        {
            if (DataReceivedEvents.Count == 0) return;

            _log.Debug($"UPDATE INCOMING - Frame: {Time.frameCount} Seq: {Seq}\n");

            try
            {
                // Process data received events
                _log.Debug($"Received {DataReceivedEvents.Count} packets");
                for (int i = 0; i < DataReceivedEvents.Count; i++)
                {
                    // Get data reader from evt which contains the binary data for the packet
                    NetPacketReader reader = DataReceivedEvents[i].DataReader;
                    
                    // Deserialize packet (including header)
                    _header.Deserialize(reader);

                    _log.Debug($"Received Packet Sequence: {_header.Seq}");
                    _log.Debug($"Packet AckFlag: {_header.AckFlag}");
                    _log.Debug($"Local AckedFlag- before: {RemoteSeqAckFlag}");

                    if (RemoteSeqAckFlag.SeqCount == 0)
                    {
                        // No sequences in the flag so just initialize it with this sequence being ACKed
                        RemoteSeqAckFlag.InitWithAckedSequence(_header.Seq);
                    }
                    else if (SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.EndSeq, _header.Seq))
                    {
                        _log.Debug($"Received sequence {_header.Seq} is ahead of the last sequence in our ack flag: {RemoteSeqAckFlag.EndSeq}");

                        // The seq is ahead of the range of our flag (ie a new seq) but we want to NACK any that 
                        // sequences that are in between the last sequence we ACKed, and this sequence we are now receiving
                        // since they must have been dropped (or delivered out of order)
                        while (RemoteSeqAckFlag.EndSeq != (byte)(_header.Seq - 1))
                        {
                            RemoteSeqAckFlag.NackNextSequence();
                            _log.Debug($"NACKed sequence {RemoteSeqAckFlag.EndSeq}");
                        }

                        // Ack this sequence in our flag
                        RemoteSeqAckFlag.AckNextSequence();
                    }
                    else
                    {
                        // This packet was delivered out of order
                        // or is outside the expected window so don't process it further
                        _log.Debug($"{_header.Seq} - SKIPPED UNEXPECTED");
                        continue;
                    }

                    // Generate notifications based on ACKs received from the remote stream
                    if (_header.AckFlag.SeqCount > 0)
                    {
                        if (SeqLastNotified == -1)
                        {
                            _log.Debug("Initializing SeqLastNotified");
                            // This is the start of us notifying packets.. if any packets were sent but aren't
                            // included in this ack flag then they must have been dropped
                            while (_transmissionRecords.Peek().Seq != _header.AckFlag.StartSeq)
                            {
                                PacketTransmissionRecord record = _transmissionRecords.Dequeue();
                                SeqLastNotified = record.Seq;
                                record.Received = false;
                                _transmissionNotifications.Add(record);
                                _log.Debug($"Seq {record.Seq} was dropped");
                            }
                            // Notify based on sequences in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                        else if (SeqIsAheadButInsideWindow32((byte)SeqLastNotified, _header.AckFlag.StartSeq))
                        {
                            // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                            while (SeqLastNotified != (byte)(_header.AckFlag.StartSeq - 1))
                            {
                                PacketTransmissionRecord r = _transmissionRecords.Dequeue();
                                r.Received = false;
                                _transmissionNotifications.Add(r);
                                SeqLastNotified = ++SeqLastNotified <= byte.MaxValue ? SeqLastNotified : 0;
                                _log.Debug($"Sequence: {SeqLastNotified} was dropped");
                            }
                            // Notify based on sequences in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                        else if (SeqIsInsideRangeInclusive(_header.AckFlag.StartSeq, _header.AckFlag.EndSeq, (byte)SeqLastNotified))
                        {
                            _log.Debug($"{SeqLastNotified} is inside ack flag range");
                            // Drop sequences we have already notified                                            
                            _header.AckFlag.DropStartSequenceUntilItEquals((byte)(SeqLastNotified + 1));

                            // Notify based on sequences remaining in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                    }

                    _log.Debug("Finished generating notifications");

                    // Give stream to each system to process
                    // todo this will be generalized to an ordered list of stream readers and writers
                    if (host)
                    {
                        _log.Debug("Giving stream to _controlledObjectSystem");
                        _controlledObjectSystem.ProcessClientToServerStream(reader);
                    }
                    else
                    {
                        _log.Debug("Giving stream to _controlledObjectSystem");
                        _controlledObjectSystem.ProcessServerToClientStream(reader);
                        _log.Debug("Giving stream to ReplicationSystem");
                        _replicationSystem.ProcessReplicationData(reader);
                    }
                }
                _log.Debug($"SeqLastNotified - After: {SeqLastNotified}");
                _log.Debug($"Generated {_transmissionNotifications.Count} transmission notifications");
                _log.Debug($"There are now { _transmissionRecords.Count} remaining transmission records in the queue");

                // Process notifications which should be in order
                foreach (PacketTransmissionRecord record in _transmissionNotifications)
                {
                    _log.Debug($"Sequence {record.Seq} Received: {record.Received} ");

                    // Nothing for packet stream to do with this record
                    if (!record.Received) continue;

                    // Drop the start of our current ack flag since we know that the remote stream
                    // knows about the sequence that it represents
                    // I think this covers all edge cases?
                    if (record.AckFlag.SeqCount <= 0) continue;

                    if (RemoteSeqAckFlag.StartSeq == record.AckFlag.EndSeq)
                    {
                        RemoteSeqAckFlag.DropStartSequence();
                    }
                    else if (SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.StartSeq, record.AckFlag.EndSeq))
                    {
                        RemoteSeqAckFlag.DropStartSequenceUntilItEquals((byte)(record.AckFlag.EndSeq + 1));
                    }
                }

                if (host)
                {

                    _replicationSystem.ProcessNotifications(_transmissionNotifications);
                }
            }
            catch (Exception e)
            {
                _log.Debug(e.Message);
                throw e;
            }
            finally
            {
                DataReceivedEvents.Clear();
                _transmissionNotifications.Clear();
            }
        }

        public void UpdateOutgoing(bool host = false)
        {
            _log.Debug($"UpdateOutgoing() - Frame: {Time.frameCount} Seq: {Seq}\n");

            // flow control
            if (_transmissionRecords.Count >= 32)
            {
                _log.Debug("Skipped sending outgoing data. Max in un-ACKed transmissions reached");
                return;
            }

            // fill in our reusable packet struct with the data for this sequence
            _header.Seq = Seq;
            _header.AckFlag = RemoteSeqAckFlag;

            _log.Debug($"Generated Packet Seq: {_header.Seq}");
            _log.Debug($"RemoteAckFlag: {RemoteSeqAckFlag}");

            // Create a transmission record for the packet
            PacketTransmissionRecord record = new PacketTransmissionRecord
            {
                Received = false,
                Seq = _header.Seq,
                AckFlag = RemoteSeqAckFlag
            };

            // Write the packet header to the stream
            _header.Serialize(_netWriter);

            // let each stream manager write until the packet is full
            if (host)
            {
                _controlledObjectSystem.WriteServerToClientStream(_netWriter);
                _replicationSystem.WriteReplicationData(_netWriter, record);
            }
            else
            {
                _controlledObjectSystem.WriteClientToServerStream(_netWriter);
            }

            // create output events
            _transmissionRecords.Enqueue(record);

            _peer.Send(_netWriter.Data, 0, _netWriter.Length, DeliveryMethod.Unreliable);
            _log.Debug($"Sent Bytes: {_netWriter.Length}");
            _netWriter.Reset();

            // Only increment our seq on successful send
            // IE if waiting for acks then seq doesn't increase
            Seq++;
        }

        /// <summary>
        /// AckFlag must start at SeqLastNotified + 1
        /// All seqs in flag will be notified
        /// </summary>
        /// <param name="flag"></param>
        private void GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(SeqAckFlag flag)
        {
            // Notify based on the flag
            for (int seqBitPos = 0; seqBitPos < flag.SeqCount; seqBitPos++)
            {
                PacketTransmissionRecord r = _transmissionRecords.Dequeue();
                r.Received = flag.IsAck(seqBitPos);
                _transmissionNotifications.Add(r);
                SeqLastNotified = ++SeqLastNotified <= byte.MaxValue ? SeqLastNotified : 0;
            }
        }

        public static bool SeqIsAheadButInsideWindow32(byte current, byte check)
        {
            // 223 is byte.MaxValue - 32
            return ((check > current && (check - current <= 32)) ||
                    (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
        }

        public static bool SeqIsEqualOrAheadButInsideWindow32(byte current, byte check)
        {
            // 223 is byte.MaxValue - 32
            return (current == check ||
                   (check > current && (check - current <= 32)) ||
                   (check < current && (current > 223 && check < (byte)(32 - (byte.MaxValue - current)))));
        }

        public static bool SeqIsInsideRangeInclusive(byte start, byte end, byte check)
        {
            return SeqIsEqualOrAheadButInsideWindow32(check, end) && SeqIsEqualOrAheadButInsideWindow32(start, check);
        }


        // assumed that start and end are valid in terms of their distance from each other
        // being at most 32 and start being < end in terms of a sequence timeline
        public static bool SeqIsInsideRange(byte start, byte end, byte check)
        {
            // if the end of the range is ahead of the check value, and the check value is ahead of the start of the range
            // then the check value must be inside of the range
            return SeqIsAheadButInsideWindow32(check, end) && SeqIsAheadButInsideWindow32(start, check);
        }
    }

}

