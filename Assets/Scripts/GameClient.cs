﻿using System;
using System.Collections.Generic;
using Assets.Scripts.Network.StreamSystems;
using LiteNetLib;

namespace Assets.Scripts
{
    public class GameClient
    {
        public enum State
        {
            Loading,
            Playing
        }

        public readonly IPacketStreamSystem PacketStream;

        public readonly NetPeer Peer;

        public readonly IControlledObjectSystem ControlledObjectSys;

        public readonly ReplicationSystem Replication;

        public State CurrentState;

        public int EntityId;


        public GameClient(NetPeer peer, bool isServer)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            Replication = new ReplicationSystem();

            List<IPacketStreamReader> streamReaders = new List<IPacketStreamReader>();
            List<IPacketStreamWriter> streamWriters = new List<IPacketStreamWriter>();
            List<IPacketTransmissionNotificationReceiver> notificationReceivers = new List<IPacketTransmissionNotificationReceiver>();

            // PacketStreams and Transmission notifications will be given to these systems in 
            // the order they appear in this list so the stream has to be read in the same order it was written
            if (isServer)
            {
                ControlledObjectSys = new ControlledObjectedSystemServer();

                streamWriters.Add(ControlledObjectSys); 
                streamWriters.Add(Replication);           

                streamReaders.Add(ControlledObjectSys);

                notificationReceivers.Add(Replication);
            }
            else
            {
                ControlledObjectSys = new ControlledObjectSystemClient();

                streamWriters.Add(ControlledObjectSys);

                streamReaders.Add(ControlledObjectSys);
                streamReaders.Add(Replication);
            }

            PacketStream = new PacketStreamSystem(Peer, streamReaders, streamWriters, notificationReceivers);
        }
    }
}