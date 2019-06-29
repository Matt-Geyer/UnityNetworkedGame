using AiUnity.NLog.Core;
using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameClient 
{
    public readonly NetPeer Peer;

    public readonly PacketStreamSystem PacketStream;

    public readonly ReplicationSystem Replication;

    public readonly IPlayerControlledObjectSystem PlayerControlledObjectSys;

    public ushort LastProcessedSequence;

    public int EntityId;

    private readonly NLogger Log;

    public enum State
    {
        LOADING,
        PLAYING
    }

    public State CurrentState;

    public GameClient(NetPeer peer)
    {
        Peer = peer ?? throw new ArgumentNullException("peer");
        Log = NLogManager.Instance.GetLogger(this);
        Replication = new ReplicationSystem();
        PlayerControlledObjectSys = new PlayerControlledObjectSystem();
        PacketStream = new PacketStreamSystem(Peer, Replication, PlayerControlledObjectSys);
    }
}

