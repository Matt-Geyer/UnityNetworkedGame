using AiUnity.NLog.Core;
using LiteNetLib.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts
{
    public sealed class PlayerControlledObjectSystem : IPlayerControlledObjectSystem
    {
        private class UserInputWindow
        {
            public UserInputSample[] Input;
            public int First;
            public int Last;
            public int Count;
            public int Max;
            public ushort Seq;
            public ushort UpperSeqWindow;
            public IUserInputUtils Sampler;

            public void Init(int max)
            {
                Input = new UserInputSample[max];
                for (int i = 0; i < max; i++)
                {
                    Input[i] = new UserInputSample();
                }
                First = Last = 0;
                Count = 0;
                Max = max;
                UpperSeqWindow = (ushort)(ushort.MaxValue - Max);
            }

            public int SampleUserInput()
            {
                if (Count == Max) return -1;
                int sampleIndex = Last;
                Sampler.Sample(Input[sampleIndex]);
                Input[sampleIndex].Seq = Seq;
                Last = ++Last < Max ? Last : 0;
                Count++;
                Seq++;
                return sampleIndex;
            }

            public void AckSeq(ushort seq)
            {
                // if seq > and inside window 
                // 223 is byte.MaxValue - 32
                ushort firstSeq = Input[First].Seq;

                if (firstSeq == seq ||
                    seq > firstSeq && seq - firstSeq <= Max ||
                    seq < firstSeq && firstSeq > UpperSeqWindow && seq < (ushort) (Max - (ushort.MaxValue - firstSeq)))
                {
                    // drop moves off the front of the window until the window starts at seq + 1 or count = 0
                    int targetSeq = seq + 1;
                    while (Count > 0 && Input[First].Seq != targetSeq)
                    {
                        First = ++First < Max ? First : 0;
                        Count--;
                    }
                }
            }
        }

        private readonly UserInputWindow _playerInputWindow;

        private readonly List<UserInputSample> _playerInputsToTransmit;

        private ushort _seq;

        private int _seqLastProcessed = -1;

        public PlayerControlledObject ControlledObject { get; set; }

        private readonly NLogger _log;

        public PlayerControlledObjectSystem()
        {
            _log = NLogManager.Instance.GetLogger(this);

            _playerInputWindow = new UserInputWindow
            {
                Sampler = new UserInputUtils()
            };

            _playerInputWindow.Init(360);

            // Since we are always going to transmit the last 3 moves I figured this
            // was a simple way to simplify the transmit logic to not have to check if there are at least 3 
            for (int i = 0; i < 3; i++)
                _playerInputWindow.SampleUserInput();

            _playerInputsToTransmit = new List<UserInputSample>(3)
        {
            _playerInputWindow.Input[0],
            _playerInputWindow.Input[1],
            _playerInputWindow.Input[2]
        };
        }


        public void StartReplicating(PlayerControlledObject pco)
        {
            // start replicating this object which is controlled by a player (not the player who will be receiving this state tho)
        }

        public void StopReplicating(PlayerControlledObject pco)
        {
            // stop replicating a pco
        }

        public void WriteClientToServerStream(NetDataWriter stream)
        {
            // write players last 3 moves to stream
            _playerInputsToTransmit[0].Serialize(stream);
            _playerInputsToTransmit[1].Serialize(stream);
            _playerInputsToTransmit[2].Serialize(stream);

            _log.Debug($"_playerInputsToTransmit[0].seq:{ _playerInputsToTransmit[0].Seq}");
            _log.Debug($"_playerInputsToTransmit[1].seq:{ _playerInputsToTransmit[1].Seq}");
            _log.Debug($"_playerInputsToTransmit[2].seq:{ _playerInputsToTransmit[2].Seq}");

        }

        public void WriteServerToClientStream(NetDataWriter stream)
        {
            // Send id of last move that was received from client
            stream.Put((ushort)_seqLastProcessed);

            // Send new pco state
            ControlledObject.Serialize(stream);

            // Send state of all pco that are being replicated by this system
        }

        public void ProcessClientToServerStream(NetDataReader stream)
        {
            // The players last 3 moves are always transmitted with the last move being the most recent
            _playerInputsToTransmit[0].Deserialize(stream);
            _playerInputsToTransmit[1].Deserialize(stream);
            _playerInputsToTransmit[2].Deserialize(stream);

            Debug.Log("Read client inputs: ");
            Debug.Log($"seq: {_playerInputsToTransmit[0].Seq} Move:{_playerInputsToTransmit[0].MoveDirection}");
            Debug.Log($"seq: {_playerInputsToTransmit[1].Seq} Move:{_playerInputsToTransmit[1].MoveDirection}");
            Debug.Log($"seq: {_playerInputsToTransmit[2].Seq} Move:{_playerInputsToTransmit[2].MoveDirection}");


            // In a 0 packet loss scenario Input [1] was last sequence and input [2] is this sequence
            // but we will look further back, and if they are all new then apply all 3 moves        
            ushort nextMoveSeq = (ushort)(_seqLastProcessed + 1);
            Debug.Log($"LastProcessedMoveSeq: {_seqLastProcessed} NextMove: {nextMoveSeq}");
            int i = 2;
            for (; i >= 0; i--)
            {
                Debug.Log($"_playerInputsToTransmit[{i}].seq: {_playerInputsToTransmit[i].Seq}");
                if (_playerInputsToTransmit[i].Seq == nextMoveSeq) break;
            }


            i = i >= 0 ? i : 0;

            // This should always have at least one new move but up to 3
            for (int j = i; j <= 2; j++)
            {
                Debug.Log($"Looking at _playerInputsToTransmit[{j}]");
                ControlledObject.ApplyInput(_playerInputsToTransmit[j]);
                _seqLastProcessed = _playerInputsToTransmit[j].Seq;
                Debug.Log($"Applied _playerInputsToTransmit[{j}] with seq: {_playerInputsToTransmit[j].Seq}");
            }
        }

        public void ProcessServerToClientStream(NetDataReader stream)
        {
            // read id of last processed move and use it to update
            // the buffer of stored moves
            _seqLastProcessed = stream.GetUShort();
            _log.Debug($"_seqLastProcessed from server: {_seqLastProcessed}");


            _playerInputWindow.AckSeq((ushort)_seqLastProcessed);

            _log.Debug($"Updated _playerInputWindow");

            // read state of player obj and set it using remainder of moves in buffer to predict again
            ControlledObject.Deserialize(stream);

            _log.Debug($"Read controlled object state");


            // read state of all replicated pco and predict
            for (int i = 0; i < _playerInputWindow.Count; i++)
            {
                ControlledObject.ApplyInput(_playerInputWindow.Input[_playerInputWindow.First + i]);
            }

            _log.Debug($"Finished applying un-acked moves");
        }

        public void UpdateControlledObject()
        {
            // sample move
            int nextSampleIndex = _playerInputWindow.SampleUserInput();



            if (nextSampleIndex >= 0)
            {
                _log.Debug($"Current Sample - _playerInputWindow[{nextSampleIndex}] seq: {_playerInputWindow.Input[nextSampleIndex].Seq} Move: {_playerInputWindow.Input[nextSampleIndex].MoveDirection}");

                // apply move 
                ControlledObject.ApplyInput(_playerInputWindow.Input[nextSampleIndex]);

                // Update packets to transmit 
                _playerInputsToTransmit.RemoveAt(0);
                _playerInputsToTransmit.Add(_playerInputWindow.Input[nextSampleIndex]);
            }
        }
    }
}

