using AiUnity.NLog.Core;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public sealed class ControlledObjectedSystemServer : ControlledObjectSystemBase
    {
        private readonly NLogger _log;

        private readonly UserInputSample[] _receivedPlayerInputs;

        public ControlledObjectedSystemServer()
        {
            _log = NLogManager.Instance.GetLogger(this);
            _receivedPlayerInputs = new UserInputSample[3]
            {
                new UserInputSample(),
                new UserInputSample(),
                new UserInputSample()
            };
        }

        public override void WriteToPacketStream(NetDataWriter stream)
        {
            _log.Debug($"SeqLastProcessed: {SeqLastProcessed}");

            // Send id of last move that was received from client
            stream.Put((ushort) SeqLastProcessed);

            // Send new pco state
            CurrentlyControlledObject.Serialize(stream);

            // Send state of all pco that are being replicated by this system
        }

        public override void ReadPacketStream(NetDataReader stream)
        {
            // The players last 3 moves are always transmitted with the last move being the most recent
            _receivedPlayerInputs[0].Deserialize(stream);
            _receivedPlayerInputs[1].Deserialize(stream);
            _receivedPlayerInputs[2].Deserialize(stream);

            _log.Debug("Read client inputs: \n " +
                       $"Seq: {_receivedPlayerInputs[0].Seq} Move:{_receivedPlayerInputs[0].MoveDirection}\n" +
                       $"Seq: {_receivedPlayerInputs[1].Seq} Move:{_receivedPlayerInputs[1].MoveDirection}\n" +
                       $"Seq: {_receivedPlayerInputs[2].Seq} Move:{_receivedPlayerInputs[2].MoveDirection}\n");

            // In a 0 packet loss scenario Items [1] was last sequence and input [2] is this sequence
            // but we will look further back, and if they are all new then apply all 3 moves        
            ushort nextMoveSeq = (ushort) (SeqLastProcessed + 1);
            _log.Debug($"LastProcessedMoveSeq: {SeqLastProcessed} NextMove: {nextMoveSeq}");
            int i = 2;
            for (; i >= 0; i--)
            {
                _log.Debug($"_playerInputsToTransmit[{i}].seq: {_receivedPlayerInputs[i].Seq}");
                if (_receivedPlayerInputs[i].Seq == nextMoveSeq) break;
            }

            // if nextMoveSeq isn't found then i will be -1
            i = i >= 0 ? i : 0;

            // This should always have at least one new move but up to 3
            for (int j = i; j <= 2; j++)
            {
                _log.Debug($"Looking at _playerInputsToTransmit[{j}]");
                _log.Debug($"Applying input with sequence: {_receivedPlayerInputs[j].Seq} to controlled object");
                _log.Debug($"Object position before: {CurrentlyControlledObject.Entity.transform.position}");
                CurrentlyControlledObject.ApplyInput(_receivedPlayerInputs[j]);
                _log.Debug($"Object position after: {CurrentlyControlledObject.Entity.transform.position}");
                SeqLastProcessed = _receivedPlayerInputs[j].Seq;
            }
        }
    }
}