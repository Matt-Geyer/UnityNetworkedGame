#if DEBUG
#define SQUIGGLE
#endif

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
        
        // Notifications are guaranteed to be delivered only once and in order so the other stream
        // systems can just store their transmissions in queues and these bools will indicate whether
        // the transmission was dropped or not (in theory!)
        private readonly List<bool> _transmissionNotifications;

        // The NetPeer for the remote stream
        private readonly NetPeer _peer;

        // Packet writer
        private readonly NetDataWriter _netWriter;

        private PacketHeader _header;

        private readonly NLogger _log;

        private readonly List<IPacketStreamReader> _streamProcessors;

        private readonly List<IPacketStreamWriter> _streamWriters;

        private readonly List<IPacketTransmissionNotificationReceiver> _notificationReceivers;


        public PacketStreamSystem(NetPeer peer, List<IPacketStreamReader> streamProcessors, List<IPacketStreamWriter> streamWriters, List<IPacketTransmissionNotificationReceiver> notificationReceivers)
        {
            _peer = peer ?? throw new ArgumentNullException(nameof(peer));
            _streamProcessors = streamProcessors ?? throw new ArgumentNullException(nameof(streamProcessors));
            _streamWriters = streamWriters ?? throw new ArgumentNullException(nameof(streamWriters));
            _notificationReceivers =
                notificationReceivers ?? throw new ArgumentNullException(nameof(notificationReceivers));
            _log = NLogManager.Instance.GetLogger(this);
            _transmissionRecords = new Queue<PacketTransmissionRecord>();
            DataReceivedEvents = new List<NetEvent>();
            _netWriter = new NetDataWriter(false, 1500);
            RemoteSeqAckFlag = new SeqAckFlag();
            _transmissionNotifications = new List<bool>();
        }

        public void UpdateIncoming(bool host = false)
        {
            if (DataReceivedEvents.Count == 0) return;

            _log.Debug($"UPDATE INCOMING - Frame: {Time.frameCount} Seq: {Seq}\n");

            DebugGraph.Log("Packets Received", DataReceivedEvents.Count);

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
                    else if (SequenceHelper.SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.EndSeq, _header.Seq))
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
                                _transmissionNotifications.Add(false);
                                _log.Debug($"Seq {record.Seq} was dropped");
                            }
                            // Notify based on sequences in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                        else if (SequenceHelper.SeqIsAheadButInsideWindow32((byte)SeqLastNotified, _header.AckFlag.StartSeq))
                        {
                            // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                            while (SeqLastNotified != (byte)(_header.AckFlag.StartSeq - 1))
                            {
                                _transmissionRecords.Dequeue();
                                _transmissionNotifications.Add(false);
                                SeqLastNotified = ++SeqLastNotified <= byte.MaxValue ? SeqLastNotified : 0;
                                _log.Debug($"Sequence: {SeqLastNotified} was dropped");
                            }
                            // Notify based on sequences in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                        else if (SequenceHelper.SeqIsInsideRangeInclusive(_header.AckFlag.StartSeq, _header.AckFlag.EndSeq, (byte)SeqLastNotified))
                        {
                            _log.Debug($"{SeqLastNotified} is inside ack flag range");
                            // Drop sequences we have already notified                                            
                            _header.AckFlag.DropStartSequenceUntilItEquals((byte)(SeqLastNotified + 1));

                            // Notify based on sequences remaining in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                    }

                    _log.Debug("Finished generating notifications");

                    // Give stream to each system to processors in the order they were added
                   foreach (IPacketStreamReader streamProcessor in _streamProcessors)
                    {
                        streamProcessor.ReadPacketStream(reader);
                    }
                }
                _log.Debug($"SeqLastNotified - After: {SeqLastNotified}");
                _log.Debug($"Generated {_transmissionNotifications.Count} transmission notifications");
                _log.Debug($"There are now { _transmissionRecords.Count} remaining transmission records in the queue");
                
                // Give notifications to any stream writers that are interested in whether or not their transmissions made it
                foreach (IPacketTransmissionNotificationReceiver notificationReceiver in _notificationReceivers)
                {
                    notificationReceiver.ReceiveNotifications(_transmissionNotifications);
                }
            }
            catch (Exception e)
            {
                _log.Debug(e.Message);
                throw;
            }
            finally
            {
                DataReceivedEvents.Clear();
                _transmissionNotifications.Clear();
            }
        }

        private void OnAckedTransmission(PacketTransmissionRecord record)
        {
            // Drop the start of our current ack flag since we know that the remote stream
            // knows about the sequence that it represents
            // I think this covers all edge cases?
            if (record.AckFlag.SeqCount <= 0) return;

            if (RemoteSeqAckFlag.StartSeq == record.AckFlag.EndSeq)
            {
                RemoteSeqAckFlag.DropStartSequence();
            }
            else if (SequenceHelper.SeqIsAheadButInsideWindow32(RemoteSeqAckFlag.StartSeq, record.AckFlag.EndSeq))
            {
                RemoteSeqAckFlag.DropStartSequenceUntilItEquals((byte) (record.AckFlag.EndSeq + 1));
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
                Seq = _header.Seq,
                AckFlag = RemoteSeqAckFlag
            };

            // Write the packet header to the stream
            _header.Serialize(_netWriter);

            // let each stream manager write until the packet is full
            foreach (IPacketStreamWriter streamWriter in _streamWriters)
            {
                streamWriter.WriteToPacketStream(_netWriter);
            }
            
            // create output events
            _transmissionRecords.Enqueue(record);

            _peer.Send(_netWriter.Data, 0, _netWriter.Length, DeliveryMethod.Unreliable);

            _log.Debug($"Sent Bytes: {_netWriter.Length}");

#if SQUIGGLE
            DebugGraph.Log("Sent Bytes", _netWriter.Length, Color.green);
#endif
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
                PacketTransmissionRecord record = _transmissionRecords.Dequeue();
                if (flag.IsAck(seqBitPos))
                    OnAckedTransmission(record);
                _transmissionNotifications.Add(flag.IsAck(seqBitPos));
                SeqLastNotified = ++SeqLastNotified <= byte.MaxValue ? SeqLastNotified : 0;
            }
        }


        // assumed that start and end are valid in terms of their distance from each other
        // being at most 32 and start being < end in terms of a sequence timeline
    }
}

