using System.Collections.Generic;
using AiUnity.NLog.Core;
using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;
using LiteNetLib.Utils;
using UnityEditor;
using UnityEngine;

namespace Assets.Scripts.CharacterControllerStuff
{

    public class MoveInfo : SeqBase
    {

        public UserInputSample UserInput;

        public KinematicCharacterMotorState MotorState;
    }

    public class KccControlledObjectSystemServer : ControlledObjectSystemBase
    {
        private readonly NLogger _log;

        private readonly UserInputSample[] _receivedPlayerInputs;

        public KccControlledObjectSystemServer()
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
            stream.Put((ushort)SeqLastProcessed);

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
            ushort nextMoveSeq = (ushort)(SeqLastProcessed + 1);
            _log.Debug($"LastProcessedMoveSeq: {SeqLastProcessed} NextMove: {nextMoveSeq}");
            int i = 2;
            for (; i >= 0; i--)
            {
                _log.Debug($"_playerInputsToTransmit[{i}].seq: {_receivedPlayerInputs[i].Seq}");
                if (_receivedPlayerInputs[i].Seq == nextMoveSeq) break;
            }

            // if nextMoveSeq isn't found then i will be -1
            if (i == -1)
            {
                if (!SequenceHelper.SeqIsAheadButInsideWindow(nextMoveSeq, _receivedPlayerInputs[0].Seq, 360))
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
                CurrentlyControlledObject.ApplyMoveDirection(_receivedPlayerInputs[j].MoveDirection.z, _receivedPlayerInputs[j].MoveDirection.x);
                //_log.Debug($"Object position after: {CurrentlyControlledObject.Entity.transform.position}");
                SeqLastProcessed = _receivedPlayerInputs[j].Seq;
            }

        }
    }

    public class KccControlledObjectSystemClient : ControlledObjectSystemBase
    {
        private readonly NLogger _log;

        private readonly List<UserInputSample> _playerInputsToTransmit;

        private readonly SlidingWindow<UserInputSample> _window;

        private readonly SlidingList<MoveInfo> _simpleWindow;

        public static void SerializeVector3(Vector3 vec, NetDataWriter writer)
        {
            writer.Put(vec.x);
            writer.Put(vec.y);
            writer.Put(vec.z);
        }

        public static void DeserializeVector3(ref Vector3 vec, NetDataReader reader)
        {
            vec.x = reader.GetFloat();
            vec.y = reader.GetFloat();
            vec.z = reader.GetFloat();
        }

        public static void SerializeMotorState(KinematicCharacterMotorState state, NetDataWriter writer)
        {
            SerializeVector3(state.Position, writer);
            SerializeVector3(state.AttachedRigidbodyVelocity, writer);
            SerializeVector3(state.BaseVelocity, writer);
        }

        public static void DeserializeMotorState(ref KinematicCharacterMotorState state, NetDataReader reader)
        {
            DeserializeVector3(ref state.Position, reader);
            DeserializeVector3(ref state.AttachedRigidbodyVelocity, reader);
            DeserializeVector3(ref state.BaseVelocity, reader);
        }

        public KccControlledObjectSystemClient()
        {
            _log = NLogManager.Instance.GetLogger(this);

            _window = new SlidingWindow<UserInputSample>(360, () => new UserInputSample());

            _simpleWindow = new SlidingList<MoveInfo>(360, () => new MoveInfo { UserInput = new UserInputSample()});

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
            var stateAtSequence = _simpleWindow.AckSequence((ushort)SeqLastProcessed);
            // read state of player obj and set it using remainder of moves in buffer to predict again

            var serverState = new KinematicCharacterMotorState();

            DeserializeMotorState(ref serverState, stream);

            //serverState.Deserialize(stream);

            KccControlledObject kcc = (KccControlledObject) CurrentlyControlledObject;

            List<KinematicCharacterMotor> motors = new List<KinematicCharacterMotor> {kcc.Controller.Motor};

            if (_simpleWindow.Items.Count <= 0 || stateAtSequence == null) return;
            
            Vector3 difference = stateAtSequence.MotorState.Position - serverState.Position;

            var cs = kcc.Controller.Motor.GetState();
            float distance = difference.magnitude;

            _log.Debug($"Sequence: {stateAtSequence.Seq} SeqLastProcessed: {SeqLastProcessed}");
            _log.Debug($"Server Position: ({serverState.Position.x},{serverState.Position.y},{serverState.Position.z})");
            _log.Debug($"Client Position: ({stateAtSequence.MotorState.Position.x},{stateAtSequence.MotorState.Position.y},{stateAtSequence.MotorState.Position.z})");
            _log.Debug($"Distance: {distance}");

            if (distance > 2)
            {
                // correct
                cs.Position = serverState.Position;
                cs.AttachedRigidbodyVelocity = serverState.AttachedRigidbodyVelocity;
                cs.BaseVelocity = serverState.BaseVelocity;

                kcc.Controller.Motor.ApplyState(cs);

                // clear input window?
                _simpleWindow.Items.Clear();
            }
            else if (distance > .05)
            {
                stateAtSequence.MotorState.Position = serverState.Position;
                stateAtSequence.MotorState.AttachedRigidbodyVelocity = serverState.AttachedRigidbodyVelocity;
                stateAtSequence.MotorState.BaseVelocity = serverState.BaseVelocity;

                kcc.Controller.Motor.ApplyState(stateAtSequence.MotorState);
                //kcc.ApplyMoveDirection(stateAtSequence.UserInput.MoveDirection.z, stateAtSequence.UserInput.MoveDirection.x);

                //KinematicCharacterSystem.Simulate(Time.fixedDeltaTime, motors, 1, null, 0);

                for (int i = 0; i < _simpleWindow.Items.Count; i++)
                {
                    UserInputSample input = _simpleWindow.Items[i].UserInput;

                    CurrentlyControlledObject.ApplyMoveDirection(input.MoveDirection.z, input.MoveDirection.x);

                    KinematicCharacterSystem.Simulate(Time.fixedDeltaTime, motors, 1, null, 0);
                }

                // what is distance between what we actually are and what we now calculate we should be at
                KinematicCharacterMotorState predictedState = kcc.Controller.Motor.GetState();
                Vector3 difVector3 = predictedState.Position - cs.Position;

                DebugGraph.Log("Prediction Mismatch", difVector3.magnitude);

                if (difVector3.magnitude >= .01)
                {
                    cs.Position += difVector3 * 0.1f;
                }
                else
                {
                    //cs.Position = predictedState.Position;
                }
           
                kcc.Controller.Motor.ApplyState(cs);
                //kcc.Controller.Motor.SetPosition(cs.Position);

            }

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
            MoveInfo nextMove = _simpleWindow.GetNextAvailable();
     
            if (nextMove == null)
            {
                _log.Debug("User input window was full so stopped sampling input");
                return;
            }

            UserInputSample nextSample = nextMove.UserInput;//  _window.GetNextAvailable();
            nextSample.Seq = nextMove.Seq;
            
            nextSample.UpdateFromCurrentInput();

            var kcc = (KccControlledObject) CurrentlyControlledObject;

            // apply move 
            CurrentlyControlledObject.ApplyMoveDirection(nextSample.MoveDirection.z, nextSample.MoveDirection.x);

            nextMove.MotorState = kcc.Controller.Motor.GetState();

            // Update packets to transmit 
            _playerInputsToTransmit.RemoveAt(0);
            _playerInputsToTransmit.Add(nextSample);
        }
    }

    public class KccControlledObject : ControlledObject
    {

        public MyCharacterController Controller;

        public override void ApplyMoveDirection(float horizontalMovement, float forwardMovement)
        {
            PlayerCharacterInputs inputs = new PlayerCharacterInputs
            {
                MoveAxisForward = forwardMovement,
                MoveAxisRight = horizontalMovement
            };

            Controller.SetInputs(ref inputs);

        }

        public override void Deserialize(NetDataReader reader)
        {
            KinematicCharacterMotorState motorState = Controller.Motor.GetState();

            KccControlledObjectSystemClient.DeserializeMotorState(ref motorState, reader);
            
            Controller.Motor.ApplyState(motorState);


            //Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            //Debug.Log($"READ POS: {pos}");
            //Controller.Motor.SetPosition(pos);
        }

        public override void Serialize(NetDataWriter writer)
        {
            KccControlledObjectSystemClient.SerializeMotorState(Controller.Motor.GetState(), writer);
            Debug.Log($"WROTE POS: {Controller.Motor.transform.position}");
        }
    }
}
