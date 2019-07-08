using System.Collections.Generic;
using AiUnity.NLog.Core;
using LiteNetLib.Utils;

namespace Assets.Scripts.Network.StreamSystems
{
    public sealed class ControlledObjectSystemClient : ControlledObjectSystemBase
    {
        private readonly NLogger _log;

        private readonly List<UserInputSample> _playerInputsToTransmit;

        private readonly SlidingWindow<UserInputSample> _window;

        private readonly SlidingList<UserInputSample> _simpleWindow;


        public ControlledObjectSystemClient()
        {
            _log = NLogManager.Instance.GetLogger(this);

            _window = new SlidingWindow<UserInputSample>(360, () => new UserInputSample());

            _simpleWindow = new SlidingList<UserInputSample>(360, () => new UserInputSample());

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

           // _window.AckSeq((ushort) SeqLastProcessed);
            _simpleWindow.AckSequence((ushort)SeqLastProcessed);
            // read state of player obj and set it using remainder of moves in buffer to predict again
            CurrentlyControlledObject.Deserialize(stream);

            for (int i = 0; i < _simpleWindow.Items.Count; i++)
            {
                for (int k = 0; k < 1000; k++)
                {
                    //CurrentlyControlledObject.ApplyMoveDirection(
                    //    _simpleWindow.Items[i].MoveDirection.z,
                    //    _simpleWindow.Items[i].MoveDirection.x);

                }
               
            }


            //if (_window.Count == 0) return;

            //int i = _window.First;
            //while (i != _window.Last)
            //{
            //    //CurrentlyControlledObject.ApplyMoveDirection(_window.Items[i].MoveDirection.z, _window.Items[i].MoveDirection.x);
            //    i = ++i < _window.Max ? i : 0;
            //}
        }

        public override void WriteToPacketStream(NetDataWriter stream)
        {
            // write players last 3 moves to stream
            _playerInputsToTransmit[0].Serialize(stream);
            _playerInputsToTransmit[1].Serialize(stream);
            _playerInputsToTransmit[2].Serialize(stream);
        }

        public override void UpdateControlledObject()
        {
            // sample move
            UserInputSample nextSample = _simpleWindow.GetNextAvailable();//  _window.GetNextAvailable();
            

            if (nextSample == null)
            {
                _log.Debug("User input window was full so stopped sampling input");
                return;
            }

            nextSample.UpdateFromCurrentInput();
            
            // apply move 
            CurrentlyControlledObject.ApplyMoveDirection(nextSample.MoveDirection.z, nextSample.MoveDirection.x);

            // Update packets to transmit 
            _playerInputsToTransmit.RemoveAt(0);
            _playerInputsToTransmit.Add(nextSample);
        }
    }
}