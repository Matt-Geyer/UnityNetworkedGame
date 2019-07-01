using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib.Utils;

namespace Assets.Scripts
{
    public sealed class ControlledObjectSystemClient : ControlledObjectSystemBase
    {
        private readonly NLogger _log;

        private readonly List<UserInputSample> _playerInputsToTransmit;

        private readonly SlidingWindow<UserInputSample> _window;

        public ControlledObjectSystemClient()
        {
            _log = NLogManager.Instance.GetLogger(this);
            
            _window = new SlidingWindow<UserInputSample>(360, () => new UserInputSample());

            _playerInputsToTransmit = new List<UserInputSample>
            {
                _window.GetNextAvailable(),
                _window.GetNextAvailable(),
                _window.GetNextAvailable()
            };
        }

        public override void ReadPacketStream(NetDataReader stream)
        {
            // read id of last processed move and use it to update
            // the buffer of stored moves
            SeqLastProcessed = stream.GetUShort();
            _log.Debug($"SeqLastProcessed from server: {SeqLastProcessed}");
            

            _window.AckSeq((ushort)SeqLastProcessed);

            _log.Debug("Updated InputWindow");

            // read state of player obj and set it using remainder of moves in buffer to predict again
            _log.Debug($"Player position before {CurrentlyControlledObject.Entity.transform.position}");
            CurrentlyControlledObject.Deserialize(stream);
            _log.Debug($"Player position after {CurrentlyControlledObject.Entity.transform.position}");

            _log.Debug("Read controlled object state");

            if (_window.Count == 0) return;

            int i = _window.First;
            while (i != _window.Last)
            {
                //_log.Debug($"Player position before {CurrentlyControlledObject.Entity.transform.position}");
                //CurrentlyControlledObject.ApplyInput(_window.Items[i]);
                //_log.Debug($"Player position after {CurrentlyControlledObject.Entity.transform.position}");
                i = ++i < _window.Max ? i : 0;
            }

            // read state of all replicated pco and predict
            //for (int i = 0; i < _window.Count; i++)
            //    CurrentlyControlledObject.ApplyInput(InputWindow.Items[InputWindow.First + i]);

            _log.Debug("Finished applying un-acked moves");
        }

        public override void WriteToPacketStream(NetDataWriter stream)
        {
            // write players last 3 moves to stream
            _playerInputsToTransmit[0].Serialize(stream);
            _playerInputsToTransmit[1].Serialize(stream);
            _playerInputsToTransmit[2].Serialize(stream);

            _log.Debug($"_playerInputsToTransmit[0].seq:{_playerInputsToTransmit[0].Seq}");
            _log.Debug($"_playerInputsToTransmit[1].seq:{_playerInputsToTransmit[1].Seq}");
            _log.Debug($"_playerInputsToTransmit[2].seq:{_playerInputsToTransmit[2].Seq}");
        }

        public override void UpdateControlledObject()
        {
            // sample move
            UserInputSample nextSample = _window.GetNextAvailable();

            if (nextSample == null)
            {
                _log.Debug("User input window was full so stopped sampling input");
                return;
            }

            nextSample.UpdateFromCurrentInput();

            _log.Debug($"Sampled next move: {nextSample}");

            // apply move 
            _log.Debug($"Object position before move: {CurrentlyControlledObject.Entity.transform.position}");
            CurrentlyControlledObject.ApplyInput(nextSample);
            _log.Debug($"Object position after move: {CurrentlyControlledObject.Entity.transform.position}");
            // Update packets to transmit 
            _playerInputsToTransmit.RemoveAt(0);
            _playerInputsToTransmit.Add(nextSample);
        }
    }
}