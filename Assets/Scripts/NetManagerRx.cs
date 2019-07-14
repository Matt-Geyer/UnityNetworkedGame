using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using LiteNetLib;
using UniRx;
using UnityEngine;

namespace Assets.Scripts
{
    public class NetManagerRx
    {
        public IObservable<NetEvent> ReceivedNetEventStream { get; }

        private readonly IConnectableObservable<NetEvent> _connectableReceivedNetEventStream;

        public NetManagerRx(NetManager netManager, IObservable<UdpMessage> receivedMessageStream)
        {
            receivedMessageStream.Subscribe(msg =>
            {
                netManager.OnMsgReceived(msg.Buffer, msg.DataSize, msg.Endpoint);
            });

            _connectableReceivedNetEventStream = Observable.Create<NetEvent>(observer =>
            {
                return Observable.EveryUpdate().Subscribe(_ =>
                {
                    for (int i = netManager.NetEventsQueue.Count; i > 0; i--)
                    {
                        observer.OnNext(netManager.NetEventsQueue.Dequeue());
                    }
                });
            }).Publish();

            ReceivedNetEventStream = _connectableReceivedNetEventStream.RefCount();
        }

        public void Start()
        {
            _connectableReceivedNetEventStream.Connect();
        }
    }
}