using System;
using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib.Utils;
using Mystery.Graphing;
using Priority_Queue;
using Rewired.Data.Mapping;
using UniRx;
using UnityEngine;
// ReSharper disable PossibleNullReferenceException

namespace Assets.Scripts.Network.StreamSystems
{


    public abstract class UngEvent : IPersistentObject
    {
        public virtual PersistentObjectRep ObjectRep { get; set; }

        public bool IsReliable;

        public abstract void Deserialize(NetDataReader reader);

        public abstract void Serialize(NetDataWriter writer);
    }

    public class TestEvent : UngEvent
    {
        public static PersistentObjectRep StaticObjectRep;

        public override PersistentObjectRep ObjectRep
        {
            get => StaticObjectRep;
            set => StaticObjectRep = value;
        }
        
        public string Message;

        public override void Deserialize(NetDataReader reader)
        {
            Message = reader.GetString();
        }

        public override void Serialize(NetDataWriter writer)
        {
            writer.Put(Message);
        }
    }

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
                byte poid = reader.GetByte();
                Event = (UngEvent)PersistentObjectManager.CreatePersistentObject(poid);
                Event.Deserialize(reader);
            }
        }


        private ushort _nextSeq;
        private ushort _seq;
        private FastPriorityQueue<ReliableUngEvent> _reliableEventQueue;
        private readonly List<ReliableUngEvent> _eventWindow;
        private readonly Queue<UngEvent> _outgoingEvents;
        private readonly Queue<TxRecord> _transmissionRecords;
        private readonly Queue<UngEvent> _tempQueue; 
        private readonly Queue<ReliableUngEvent> _tempReliableEvents;

        public IObservable<UngEvent> EventStream;
        private IConnectableObservable<UngEvent> _connEventStream;

        private delegate void HandleNextEvent(UngEvent evt);

        private event HandleNextEvent NextEvent;
        private NLogger _log;

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
            _reliableEventQueue = new FastPriorityQueue<ReliableUngEvent>(2000);
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
            // early check for full window?
            TxRecord record = new TxRecord();

            foreach (ReliableUngEvent tx in _eventWindow)
            {
                if (!tx.Dropped) continue;

                _log.Debug("ADDING NACKED EVENT");

                _tempReliableEvents.Enqueue(tx);
                tx.Dropped = false;
                record.TransmittedEvents.Add(tx);
            }

            for(int i = _outgoingEvents.Count; i > 0; i--)
            {
                UngEvent evt = _outgoingEvents.Dequeue();

                // if window or write limit is reached stop
                if (evt.IsReliable)
                {
                    ReliableUngEvent revt = new ReliableUngEvent {Event = evt, Seq = _seq++};
                    _eventWindow.Add(revt);
                    record.TransmittedEvents.Add(revt);
                    _tempReliableEvents.Enqueue(revt);
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
                reliableEvt.Serialize(stream);
            }

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
                _log.Debug($"Acking transmission with {record.TransmittedEvents.Count} events");
                _log.Debug($"eventWindow count: {_eventWindow.Count}");
                foreach (ReliableUngEvent tx in record.TransmittedEvents)
                {
                    _eventWindow.Remove(tx);
                }
                _log.Debug($"eventWindow count: {_eventWindow.Count}");
            }
            else
            {
                foreach (ReliableUngEvent tx in record.TransmittedEvents)
                {
                    tx.Dropped = true;
                }
            }
        }

        public void ReadStream(NetDataReader stream)
        {
            int evtCount = stream.GetUShort();
            
            for (int i = 0; i < evtCount; i++)
            {
                ReliableUngEvent evt = new ReliableUngEvent();
                evt.Deserialize(stream);

                if (evt.Seq == _nextSeq)
                {
                    // emit
                    NextEvent(evt.Event);
                    _nextSeq++;
                }
                else
                {
                    _reliableEventQueue.Enqueue(evt, evt.Seq);
                }
            }

            while (_reliableEventQueue.Count > 0 && _reliableEventQueue.First.Seq == _nextSeq)
            {
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
