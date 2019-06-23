using LiteNetLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameClient 
{
    public readonly NetPeer Peer;

    public readonly PacketStreamSystem PacketStream;

    public ushort LastProcessedSequence;

    public int EntityId;

    public enum State
    {
        LOADING,
        PLAYING
    }

    public State CurrentState;

    public GameClient()
    {

    }

    public GameClient(NetPeer peer)
    {
        Peer = peer ?? throw new ArgumentNullException("peer");
        PacketStream = new PacketStreamSystem(Peer);
    }
}

