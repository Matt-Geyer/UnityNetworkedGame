using AiUnity.NLog.Core;
using Assets.Scripts.Network.StreamSystems;
using LiteNetLib.Utils;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class ControlledObjectClientEvent
    {
        public UserInputSample[] PlayerInputs;

        public ControlledObjectClientEvent()
        {
            PlayerInputs = new UserInputSample[]
            {
                new UserInputSample(),
                new UserInputSample(),
                new UserInputSample()
            };
        }
    }


    public class KccControlledObjectSystemServer : ControlledObjectSystemBase
    {
        private readonly NLogger _log;

        public KccControlledObjectSystemServer()
        {
            _log = NLogManager.Instance.GetLogger(this);
        }

        public override void WriteToPacketStream(NetDataWriter stream)
        {
            _log.Debug($"SeqLastProcessed: {SeqLastProcessed}");

            // Send id of last move that was received from client
            stream.Put((ushort)SeqLastProcessed);

            // Send new pco state
            CurrentlyControlledObject.Serialize(stream);

            // Send state of all pco that are being replicated by this system
        }

        public ControlledObjectClientEvent GetClientEventFromStream(NetDataReader stream)
        {
            // TODO: pooled obj
            ControlledObjectClientEvent evt = new ControlledObjectClientEvent();
            evt.PlayerInputs[0].Deserialize(stream);
            evt.PlayerInputs[1].Deserialize(stream);
            evt.PlayerInputs[2].Deserialize(stream);
            return evt;
        }

        public void HandleClientEvent(ControlledObjectClientEvent evt)
        {
            // In a 0 packet loss scenario Items [1] was last sequence and input [2] is this sequence
            // but we will look further back, and if they are all new then apply all 3 moves        
            ushort nextMoveSeq = (ushort)(SeqLastProcessed + 1);
            _log.Debug($"LastProcessedMoveSeq: {SeqLastProcessed} NextMove: {nextMoveSeq}");
            int i = 2;
            for (; i >= 0; i--)
            {
                _log.Debug($"_playerInputsToTransmit[{i}].seq: {evt.PlayerInputs[i].Seq}");
                if (evt.PlayerInputs[i].Seq == nextMoveSeq) break;
            }

            // if nextMoveSeq isn't found then i will be -1
            if (i == -1)
            {
                if (!SequenceHelper.SeqIsAheadButInsideWindow(nextMoveSeq, evt.PlayerInputs[0].Seq, 360))
                {
                    _log.Debug($"No player moves since sequence: {SeqLastProcessed}");
                    // CurrentlyControlledObject.ApplyMoveDirection(0,0);
                    return;
                }

                i = 0;
            }

            // This should always have at least one new move but up to 3
            for (int j = i; j <= 2; j++)
            {
                //_log.Debug($"Looking at _playerInputsToTransmit[{j}]");
                //_log.Debug($"Applying input with sequence: {_receivedPlayerInputs[j].Seq} to controlled object");
                //_log.Debug($"Object position before: {CurrentlyControlledObject.Entity.transform.position}");
                
                CurrentlyControlledObject.ApplyMoveDirection(evt.PlayerInputs[j].MoveDirection.z, evt.PlayerInputs[j].MoveDirection.x);
                
                // simulate?

                //_log.Debug($"Object position after: {CurrentlyControlledObject.Entity.transform.position}");
                SeqLastProcessed = evt.PlayerInputs[j].Seq;
            }
        }


        public override void ReadPacketStream(NetDataReader stream)
        {
           
        }
    }
}