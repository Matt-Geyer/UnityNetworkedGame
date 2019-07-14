using System;
using System.Collections.Generic;
using System.Diagnostics;
using AiUnity.NLog.Core;
using LiteNetLib;
using LiteNetLib.Utils;
using UniRx;

namespace Assets.Scripts.Network.StreamSystems
{
    // Generated from net event 
    public class GamePacketReceived
    {
        public int Data;

        public List<PacketTxRecord> Notifications;

        // controlled object data

        // events 

        // replication data

        // data blocks
    }

    public sealed class PacketStreamRx
    {
        // Information about sent packets stored in the order they were sent and expected to be processed in order
        private readonly Queue<PacketTransmissionRecord> _transmissionRecords;

        // Notifications are guaranteed to be delivered only once and in order so the other stream
        // systems can just store their transmissions in queues and these bools will indicate whether
        // the transmission was dropped or not
        private readonly Queue<bool> _transmissionNotifications;

        // This streams current sequence
        private byte _seq;

        // Seq are always notified in order
        private int _seqLastNotified = -1;

        private SeqAckFlag _remoteSeqAckFlag;

        public readonly IObservable<NetDataReader> GamePacketStream;
        public readonly IObservable<bool> TransmissionNotificationStream;
        public readonly IObservable<NetDataWriter> OutgoingPacketStream;
        private readonly Queue<GamePacketReceived> _receivedGamePackets;
        private readonly NLogger _log;
        private SeqAckFlag _packetAckFlag = new SeqAckFlag();
        private readonly IConnectableObservable<bool> _connNotificationStream;
        private readonly IConnectableObservable<NetDataReader> _connReaderStream;
        private readonly IConnectableObservable<NetDataWriter> _connOutgoingPacketStream;
        
        public PacketStreamRx(IObservable<NetEvent> dataReceivedEvents, IObservable<long> updateNotificationEvents, IObservable<long> generatePacketEvents)
        {
            _log = NLogManager.Instance.GetLogger(this);
            _transmissionNotifications = new Queue<bool>();
            _transmissionRecords = new Queue<PacketTransmissionRecord>();
            _remoteSeqAckFlag = new SeqAckFlag();
            
            _connReaderStream = Observable.Create<NetDataReader>(observer =>
            {
                _log.Debug("*********** SUBBING TO DATA RECV *************");
                return dataReceivedEvents.Subscribe(evt =>
                {
                    NetDataReader reader = GetReaderAndUpdateAckFlag(evt);

                    if (reader == null) return;

                    // read the seq ack flag and fire notifications before emitting reader
                    // which means that notifications will be processed before packet data events are generated
                    // not sure if that matters..
                    GenerateNotifications(reader);

                    observer.OnNext(reader);
                });
            }).Publish();

            _connNotificationStream = Observable.Create<bool>(observer =>
            {
                _log.Debug("************************ Notification SUB *******************");
                return updateNotificationEvents.Subscribe(_ =>
                {
                    _log.Debug("Updating notifications");
                    for (int i = _transmissionNotifications.Count; i > 0; i--)
                        observer.OnNext(_transmissionNotifications.Dequeue());
                });

            }).Publish();

            _connOutgoingPacketStream = Observable.Create<NetDataWriter> (observer =>
            {
                _log.Debug("************************ OUTGOING PACKET STREAM SUB *******************");
                return generatePacketEvents.Subscribe(_ =>
                {
                    // Check max transmissions
                    if (_transmissionRecords.Count >= 32)
                    {
                        _log.Debug("Transmission records full.. halting outgoing");
                        return;
                    }

                    // Eventually change to a pool
                    NetDataWriter packetWriter = new NetDataWriter(false, 1500);

                    // Write current sequence into packet
                    packetWriter.Put(_seq);
                    _log.Debug($"Wrote sequence: {_seq}");
                    
                    // Write remote sequence ack flag
                    _remoteSeqAckFlag.Serialize(packetWriter);

                    // Create and store a transmission record while updating the seq counter
                    _transmissionRecords.Enqueue(new PacketTransmissionRecord
                    {
                        Seq = _seq,
                        AckFlag = _remoteSeqAckFlag
                    });

                    // Emit the writer so that downstream other things will add to it
                    // and eventually it will be sent
                    observer.OnNext(packetWriter);

                    _seq++;

                });
            }).Publish();

            OutgoingPacketStream = _connOutgoingPacketStream.RefCount();

            // might change my mind on this and react to a specific event that drains the notification queue
            TransmissionNotificationStream = _connNotificationStream.RefCount();

            GamePacketStream = _connReaderStream.RefCount();
        }

        public void Start()
        {
            _connReaderStream.Connect();
            _connNotificationStream.Connect();
            _connOutgoingPacketStream.Connect();
        }

        private void GenerateNotifications(NetDataReader reader)
        {
            _packetAckFlag.Deserialize(reader);

            if (_packetAckFlag.SeqCount <= 0)
            {
                _log.Debug("No sequences in remote ack flag");
                return;
            }

            if (_seqLastNotified == -1)
            {
                _log.Debug("Initializing SeqLastNotified");
                // This is the start of us notifying packets.. if any packets were sent but aren't
                // included in this ack flag then they must have been dropped
                while (_transmissionRecords.Peek().Seq != _packetAckFlag.StartSeq)
                {
                    PacketTransmissionRecord record = _transmissionRecords.Dequeue();
                    _seqLastNotified = record.Seq;
                    _transmissionNotifications.Enqueue(false);
                    _log.Debug($"Seq {record.Seq} was dropped");
                }
                // Notify based on sequences in flag
                GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_packetAckFlag);
            }
            else if (SequenceHelper.SeqIsAheadButInsideWindow32((byte)_seqLastNotified, _packetAckFlag.StartSeq))
            {
                // NACK all packets up until the start of this ACK flag because they must have been lost or delivered out of order
                while (_seqLastNotified != (byte)(_packetAckFlag.StartSeq - 1))
                {
                    _transmissionRecords.Dequeue();
                    _transmissionNotifications.Enqueue(false);
                    _seqLastNotified = ++_seqLastNotified <= byte.MaxValue ? _seqLastNotified : 0;
                    _log.Debug($"Sequence: {_seqLastNotified} was dropped");
                }
                // Notify based on sequences in flag
                GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_packetAckFlag);
            }
            else if (SequenceHelper.SeqIsInsideRangeInclusive(_packetAckFlag.StartSeq, _packetAckFlag.EndSeq, (byte)_seqLastNotified))
            {
                _log.Debug($"{_seqLastNotified} is inside ack flag range");
                // Drop sequences we have already notified                                            
                _packetAckFlag.DropStartSequenceUntilItEquals((byte)(_seqLastNotified + 1));

                // Notify based on sequences remaining in flag
                GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(_packetAckFlag);
            }
            

           

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

        private void GenerateNotificationsFromAckFlagAndUpdateSeqLastNotified(SeqAckFlag packetAckFlag)
        {
            // Notify based on the flag
            for (int seqBitPos = 0; seqBitPos < packetAckFlag.SeqCount; seqBitPos++)
            {
                PacketTransmissionRecord record = _transmissionRecords.Dequeue();
                if (packetAckFlag.IsAck(seqBitPos))
                    OnAckedTransmission(record);
                _transmissionNotifications.Enqueue(packetAckFlag.IsAck(seqBitPos));
                _seqLastNotified = ++_seqLastNotified <= byte.MaxValue ? _seqLastNotified : 0;
            }
        }

        private NetPacketReader GetReaderAndUpdateAckFlag(NetEvent evt)
        {
            // generate GamePacketRecieved from
            // Get data reader from evt which contains the binary data for the packet
            NetPacketReader reader = evt.DataReader;

            // Deserialize packet (including header)
            //_header.Deserialize(reader);

            byte packetSeq = reader.GetByte();

            _log.Debug($"Received Packet Sequence: {packetSeq}");
            _log.Debug($"Local AckedFlag- before: {_remoteSeqAckFlag}");

            if (_remoteSeqAckFlag.SeqCount == 0)
            {
                // No sequences in the flag so just initialize it with this sequence being ACKed
                _remoteSeqAckFlag.InitWithAckedSequence(packetSeq);
            }
            else if (SequenceHelper.SeqIsAheadButInsideWindow32(_remoteSeqAckFlag.EndSeq, packetSeq))
            {
                _log.Debug($"Received sequence {packetSeq} is ahead of the last sequence in our ack flag: {_remoteSeqAckFlag.EndSeq}");

                // The seq is ahead of the range of our flag (ie a new seq) but we want to NACK any that 
                // sequences that are in between the last sequence we ACKed, and this sequence we are now receiving
                // since they must have been dropped (or delivered out of order)
                while (_remoteSeqAckFlag.EndSeq != (byte)(packetSeq - 1))
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
                _log.Debug($"{packetSeq} - SKIPPED UNEXPECTED");
                return null;
            }
            return reader;
        }
    }
}