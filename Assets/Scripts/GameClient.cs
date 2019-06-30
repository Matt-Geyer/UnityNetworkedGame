using System;
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

        public readonly PacketStreamSystem PacketStream;

        public readonly NetPeer Peer;

        public readonly IPlayerControlledObjectSystem PlayerControlledObjectSys;

        public readonly ReplicationSystem Replication;

        public State CurrentState;

        public int EntityId;


        public GameClient(NetPeer peer)
        {
            Peer = peer ?? throw new ArgumentNullException(nameof(peer));
            Replication = new ReplicationSystem();
            PlayerControlledObjectSys = new PlayerControlledObjectSystem();
            PacketStream = new PacketStreamSystem(Peer, Replication, PlayerControlledObjectSys);
        }
    }
}