#if DEBUG
#define SQUIGGLE
#endif

using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts.Network.StreamSystems
{
    public class PacketStreamSystem : IPacketStreamSystem
    {
        // This streams current sequence
        private byte _seq;

        // Seq are always notified in order
        private int _seqLastNotified = -1;

        private SeqAckFlag _remoteSeqAckFlag;

        // This will be filled externally with the DataReceived NetEvents from socket for this peer
        private readonly List<NetEvent> _dataReceivedEvents;

        // Information about sent packets stored in the order they were sent and expected to be processed in order
        private readonly Queue<PacketTransmissionRecord> _transmissionRecords;
        
        // Notifications are guaranteed to be delivered only once and in order so the other stream
        // systems can just store their transmissions in queues and these bools will indicate whether
        // the transmission was dropped or not
        private readonly List<bool> _transmissionNotifications;

        /// <summary>
        /// Right now I'm only using the peer to send packets. I'm thinking that having NetPeer implement this interface will
        /// be better for test purposes so I can use a dummy sender that just puts to log or something like that
        /// </summary>
        private readonly IUnreliablePacketSender _packetSender;

        // Packet writer
        private readonly NetDataWriter _netWriter;

        private PacketHeader _header;

        private readonly NLogger _log;

        private readonly List<IPacketStreamReader> _streamProcessors;

        private readonly List<IPacketStreamWriter> _streamWriters;

        private readonly List<IPacketTransmissionNotificationReceiver> _notificationReceivers;

        public PacketStreamSystem(IUnreliablePacketSender packetSender, List<IPacketStreamReader> streamProcessors, List<IPacketStreamWriter> streamWriters, List<IPacketTransmissionNotificationReceiver> notificationReceivers)
        {
            _packetSender = packetSender ?? throw new ArgumentNullException(nameof(packetSender));
            _streamProcessors = streamProcessors ?? throw new ArgumentNullException(nameof(streamProcessors));
            _streamWriters = streamWriters ?? throw new ArgumentNullException(nameof(streamWriters));
            _notificationReceivers =
                notificationReceivers ?? throw new ArgumentNullException(nameof(notificationReceivers));
            _log = NLogManager.Instance.GetLogger(this);
            _transmissionRecords = new Queue<PacketTransmissionRecord>();
            _dataReceivedEvents = new List<NetEvent>();
            _netWriter = new NetDataWriter(false, 1500);
            _remoteSeqAckFlag = new SeqAckFlag();
            _transmissionNotifications = new List<bool>();
        }

        public void UpdateIncoming(bool host = false)
        {
            if (_dataReceivedEvents.Count == 0) return;

            _log.Debug($"UPDATE INCOMING - Frame: {Time.frameCount} Seq: {_seq}\n");

            DebugGraph.Log("Packets Received", _dataReceivedEvents.Count);

            try
            {
                // Process data received events
                _log.Debug($"Received {_dataReceivedEvents.Count} packets");
                for (int i = 0; i < _dataReceivedEvents.Count; i++)
                {
                    // Get data reader from evt which contains the binary data for the packet
                    NetPacketReader reader = _dataReceivedEvents[i].DataReader;
                    
                    // Deserialize packet (including header)
                    _header.Deserialize(reader);

                    _log.Debug($"Received Packet Sequence: {_header.Seq}");
                    _log.Debug($"Packet AckFlag: {_header.AckFlag}");
                    _log.Debug($"Local AckedFlag- before: {_remoteSeqAckFlag}");

                    if (_remoteSeqAckFlag.SeqCount == 0)
                    {
                        // No sequences in the flag so just initialize it with this sequence being ACKed
                        _remoteSeqAckFlag.InitWithAckedSequence(_header.Seq);
                    }
                    else if (SequenceHelper.SeqIsAheadButInsideWindow32(_remoteSeqAckFlag.EndSeq, _header.Seq))
                    {
                        _log.Debug($"Received sequence {_header.Seq} is ahead of the last sequence in our ack flag: {_remoteSeqAckFlag.EndSeq}");

                        // The seq is ahead of the range of our flag (ie a new seq) but we want to NACK any that 
                        // sequences that are in between the last sequence we ACKed, and this sequence we are now receiving
                        // since they must have been dropped (or delivered out of order)
                        while (_remoteSeqAckFlag.EndSeq != (byte)(_header.Seq - 1))
                        {
                            _remoteSeqAckFlag.NackNextSequence();
                            _log.Debug($"NACKed sequence {_remoteSeqAckFlag.EndSeq}");
                        }

                        // Ack this sequence in our flag
                        _remoteSeqAckFlag.AckNextSequence();
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
                        if (_seqLastNotified == -1)
                        {
                            _log.Debug("Initializing SeqLastNotified");
                            // This is the start of us notifying packets.. if any packets were sent but aren't
                            // included in this ack flag then they must have been dropped
                            while (_transmissionRecords.Peek().Seq != _header.AckFlag.StartSeq)
                            {
                                PacketTransmissionRecord record = _transmissionRecords.Dequeue();
                                _seqLastNotified = record.Seq;
                                _transmissionNotifications.Add(false);
                                _log.Debug($"Seq {record.Seq} was dropped");
                            }
                            // Notify based on sequences in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                        else if (SequenceHelper.SeqIsAheadButInsideWindow32((byte)_seqLastNotified, _header.AckFlag.StartSeq))
                        {
                            // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                            while (_seqLastNotified != (byte)(_header.AckFlag.StartSeq - 1))
                            {
                                _transmissionRecords.Dequeue();
                                _transmissionNotifications.Add(false);
                                _seqLastNotified = ++_seqLastNotified <= byte.MaxValue ? _seqLastNotified : 0;
                                _log.Debug($"Sequence: {_seqLastNotified} was dropped");
                            }
                            // Notify based on sequences in flag
                            GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_header.AckFlag);
                        }
                        else if (SequenceHelper.SeqIsInsideRangeInclusive(_header.AckFlag.StartSeq, _header.AckFlag.EndSeq, (byte)_seqLastNotified))
                        {
                            _log.Debug($"{_seqLastNotified} is inside ack flag range");
                            // Drop sequences we have already notified                                            
                            _header.AckFlag.DropStartSequenceUntilItEquals((byte)(_seqLastNotified + 1));

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
                _log.Debug($"SeqLastNotified - After: {_seqLastNotified}");
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
                _dataReceivedEvents.Clear();
                _transmissionNotifications.Clear();
            }
        }

        public void AddDataReceivedEvents(params NetEvent[] events)
        {
            _dataReceivedEvents.AddRange(events);
        }

        public void AddDataReceivedEvent(NetEvent evt)
        {
            _dataReceivedEvents.Add(evt);
        }

        public void UpdateOutgoing(bool host = false)
        {
            _log.Debug($"UpdateOutgoing() - Frame: {Time.frameCount} Seq: {_seq}\n");

            // flow control
            if (_transmissionRecords.Count >= 32)
            {
                _log.Debug("Skipped sending outgoing data. Max in un-ACKed transmissions reached");
                return;
            }

            // fill in our reusable packet struct with the data for this sequence
            _header.Seq = _seq;
            _header.AckFlag = _remoteSeqAckFlag;

            _log.Debug($"Generated Packet Seq: {_header.Seq}");
            _log.Debug($"RemoteAckFlag: {_remoteSeqAckFlag}");

            // Create a transmission record for the packet
            PacketTransmissionRecord record = new PacketTransmissionRecord
            {
                Seq = _header.Seq,
                AckFlag = _remoteSeqAckFlag
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

            _packetSender.Send(_netWriter.Data, 0, _netWriter.Length); 

            _log.Debug($"Sent Bytes: {_netWriter.Length}");

#if SQUIGGLE
            DebugGraph.Log("Sent Bytes", _netWriter.Length, Color.green);
#endif
            _netWriter.Reset();

            // Only increment our seq on successful send
            // IE if waiting for acks then seq doesn't increase
            _seq++;
        }

        private void OnAckedTransmission(PacketTransmissionRecord record)
        {
            // Drop the start of our current ack flag since we know that the remote stream
            // knows about the sequence that it represents
            // I think this covers all edge cases?
            if (record.AckFlag.SeqCount <= 0) return;

            if (_remoteSeqAckFlag.StartSeq == record.AckFlag.EndSeq)
            {
                _remoteSeqAckFlag.DropStartSequence();
            }
            else if (SequenceHelper.SeqIsAheadButInsideWindow32(_remoteSeqAckFlag.StartSeq, record.AckFlag.EndSeq))
            {
                _remoteSeqAckFlag.DropStartSequenceUntilItEquals((byte)(record.AckFlag.EndSeq + 1));
            }
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
                _seqLastNotified = ++_seqLastNotified <= byte.MaxValue ? _seqLastNotified : 0;
            }
        }
    }
}

