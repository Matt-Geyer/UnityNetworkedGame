using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib.Utils;
using Priority_Queue;
using UniRx;


// ReSharper disable PossibleNullReferenceException
namespace Assets.Scripts.Network.StreamSystems
{
    public class EventSystemRx
    {
        private class TxRecord
        {
            public readonly List<ReliableUngEvent> TransmittedEvents = new List<ReliableUngEvent>();
        }
        
        private class ReliableUngEvent : FastPriorityQueueNode
        {
            public UngEvent Event;
            public bool Dropped;
            public ushort Seq;

            public void Serialize(NetDataWriter writer)
            {
                writer.Put(Seq);
                writer.Put(Event.ObjectRep.Id);
                Event.Serialize(writer);
            }

            public void Deserialize(NetDataReader reader)
            {
                Seq = reader.GetUShort();
                byte objId = reader.GetByte();
                Event = (UngEvent)PersistentObjectManager.CreatePersistentObject(objId);
                Event.Deserialize(reader);
            }
        }


        private ushort _nextSeq = ushort.MaxValue - 50;
        private ushort _seq = ushort.MaxValue - 50;
        private readonly FastPriorityQueue<ReliableUngEvent> _reliableEventQueue;
        private readonly List<ReliableUngEvent> _eventWindow;
        private readonly Queue<UngEvent> _outgoingEvents;
        private readonly Queue<TxRecord> _transmissionRecords;
        private readonly Queue<UngEvent> _tempQueue; 
        private readonly Queue<ReliableUngEvent> _tempReliableEvents;
        public readonly IObservable<UngEvent> EventStream;
        private readonly IConnectableObservable<UngEvent> _connEventStream;
        private delegate void HandleNextEvent(UngEvent evt);
        private event HandleNextEvent NextEvent;
        private readonly NLogger _log;

        private class StreamSubscriber
        {
            private readonly IObserver<UngEvent> _observer;

            public StreamSubscriber(IObserver<UngEvent> observer)
            {
                _observer = observer;
            }
            
            public void OnEvent(UngEvent evt)
            {
                _observer.OnNext(evt);
            }
        }

        public EventSystemRx()
        {
            _reliableEventWindowLimit = 20;


            // I think this is the worst case scenario for number of remote reliable events queued assuming other stuff is working correctly
            // With a window size of 10:
            // 1 Remote Send                        Remote Window                     Event Queue
            // 2 [0] (Dropped)                  -   [0]                            -  [] 
            // 3 [1,2,3,4,5,6,7,8,9]            -   [0,1,2,3,4,5,6,7,8,9]          -  [9,8,7,6,5,4,3,2,1]
            // 4 [0,10,11,12,13,14,15,16,17,18] -   [0,10,11,12,13,14,15,16,17,18] -  [18,17,16,15,14,13,12,11,10,9,8,7,6,5,4,3,2,1,0] (19 items ie 2x window size)

            _reliableEventQueue = new FastPriorityQueue<ReliableUngEvent>(_reliableEventWindowLimit * 2);
            _tempQueue = new Queue<UngEvent>();
            _tempReliableEvents = new Queue<ReliableUngEvent>();
            _transmissionRecords = new Queue<TxRecord>();
            _eventWindow = new List<ReliableUngEvent>();
            _outgoingEvents = new Queue<UngEvent>();
            _log = NLogManager.Instance.GetLogger(this);

            _connEventStream = Observable.Create<UngEvent>(observer => 
            {
                StreamSubscriber sub = new StreamSubscriber(observer);

                NextEvent += sub.OnEvent;

                return Disposable.Create(() => { NextEvent -= sub.OnEvent; });
            }).Publish();

            EventStream = _connEventStream.RefCount();
        }

        public void Start()
        {
            _log.Info("EventSystemRx started.");
            _connEventStream.Connect();
        }

        // subscribe to app event streams and do this
        public bool QueueEvent(UngEvent evt)
        {
           _outgoingEvents.Enqueue(evt);
            return true;
        }

        public void WriteToStream(NetDataWriter stream)
        {
            // can re-use these esp since i know that there is a max limit to how many i need at once
            TxRecord record = new TxRecord();

            foreach (ReliableUngEvent tx in _eventWindow)
            {
                if (!tx.Dropped) continue;

                _tempReliableEvents.Enqueue(tx);
                tx.Dropped = false;
                record.TransmittedEvents.Add(tx);
            }

            for(int i = _outgoingEvents.Count; i > 0; i--)
            {
                // todo: also packet size limit checking
                if (_eventWindow.Count >= _reliableEventWindowLimit)
                {
                    _log.Debug("Send window is maxed out");
                    break;
                }
                    
                UngEvent evt = _outgoingEvents.Dequeue();
                
                if (evt.IsReliable)
                {
                    ReliableUngEvent reliableUngEvent = new ReliableUngEvent {Event = evt, Seq = _seq++};
                    _eventWindow.Add(reliableUngEvent);
                    record.TransmittedEvents.Add(reliableUngEvent);
                    _tempReliableEvents.Enqueue(reliableUngEvent);
                }
                else
                {
                    _tempQueue.Enqueue(evt);
                }
            }

            stream.Put((ushort)_tempReliableEvents.Count);
            for (int i = _tempReliableEvents.Count; i > 0; i--)
            {
                ReliableUngEvent reliableEvt = _tempReliableEvents.Dequeue();
                _log.Debug($"Writing reliable event: {reliableEvt.Seq}");
                reliableEvt.Serialize(stream);
            }

            stream.Put((ushort)0);
            
            stream.Put((ushort)_tempQueue.Count);
            for (int i = _tempQueue.Count; i > 0; i--)
            {
                UngEvent evt = _tempQueue.Dequeue();
                stream.Put(evt.ObjectRep.Id);
                evt.Serialize(stream);
            }

            _transmissionRecords.Enqueue(record);
        }

        public void HandleTransmissionNotification(bool received)
        {
            TxRecord record = _transmissionRecords.Dequeue();

            if (received)
            {
                foreach (ReliableUngEvent tx in record.TransmittedEvents)
                {
                    _eventWindow.Remove(tx);
                }
            }
            else
            {
                foreach (ReliableUngEvent tx in record.TransmittedEvents)
                {
                    tx.Dropped = true;
                }
            }
        }

        private bool _rolloverZone;
        private readonly int _reliableEventWindowLimit;

        public void ReadStream(NetDataReader stream)
        {
            int evtCount = stream.GetUShort();
            
            for (int i = 0; i < evtCount; i++)
            {
                ReliableUngEvent evt = new ReliableUngEvent();
                evt.Deserialize(stream);

                _log.Debug($"Received event with sequence {evt.Seq}");

                // flag that there are now values in the queue which could predated a seq that is actually less than
                // their seq
                if (evt.Seq >= ushort.MaxValue - _reliableEventWindowLimit && !_rolloverZone)
                {
                    _log.Debug("Set rollover zone true and updating priority of any rollover values already in the queue");
                    foreach (ReliableUngEvent rue in _reliableEventQueue)
                    {
                        if (rue.Seq < _reliableEventWindowLimit)
                            _reliableEventQueue.UpdatePriority(rue, rue.Seq + ushort.MaxValue);
                    }
                    _rolloverZone = true;

                }

                _log.Debug($"Queuing event with sequence {evt.Seq}");

                float priority = evt.Seq;

                if (evt.Seq <= _reliableEventWindowLimit && _rolloverZone)
                {
                    _log.Debug($"Adding ushort max to priority");
                    priority += ushort.MaxValue;
                }

                _reliableEventQueue.Enqueue(evt, priority);

            }

            while (_reliableEventQueue.Count > 0 && _reliableEventQueue.First.Seq == _nextSeq)
            {

                _log.Debug($"Processing sequence: {_nextSeq}");

                
                if (_nextSeq == 0 && _rolloverZone)
                {
                    _log.Debug("Leaving rollover mode and resetting queue priorities");
                    _rolloverZone = false;
                    foreach (ReliableUngEvent rue in _reliableEventQueue)
                    {
                        _reliableEventQueue.UpdatePriority(rue, rue.Seq);
                    }
                }

                // emit
                NextEvent(_reliableEventQueue.Dequeue().Event);
                _nextSeq++;
            }

            evtCount = stream.GetUShort();

            for (int i = 0; i < evtCount; i++)
            {
                byte objId = stream.GetByte();
                UngEvent evt = (UngEvent) PersistentObjectManager.CreatePersistentObject(objId);
                evt.Deserialize(stream);
                //emit
                NextEvent(evt);
            }
        }
    }
}
