using System.Collections.Generic;
using System.Linq;
using AiUnity.NLog.Core;
using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;
using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts.CharacterControllerStuff
{

    public class ControlledObjectServerEvent
    {
        public ushort SeqLastProcessed;
        public KinematicCharacterMotorState MotorState;
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

        public ControlledObjectServerEvent GetControlledObjectEventFromStream(NetDataReader stream)
        {
            ControlledObjectServerEvent frame = new ControlledObjectServerEvent
            {
                SeqLastProcessed = stream.GetUShort()
            };
            DeserializeMotorState(ref frame.MotorState, stream);
            return frame;
        }

        public void FixedUpdate_ServerReconcile(ControlledObjectServerEvent serverUpdate)
        {
            SeqLastProcessed = serverUpdate.SeqLastProcessed;

            _log.Debug($"SeqLastProcessed from server: {SeqLastProcessed}");

            var stateAtSequence = _simpleWindow.AckSequence((ushort)SeqLastProcessed);

            KccControlledObject kcc = (KccControlledObject)CurrentlyControlledObject;

            List<KinematicCharacterMotor> motors = new List<KinematicCharacterMotor> { kcc.Controller.Motor };

            if (_simpleWindow.Items.Count <= 0 || stateAtSequence == null) return;

            Vector3 difference = stateAtSequence.MotorState.Position - serverUpdate.MotorState.Position;

            var cs = kcc.Controller.Motor.GetState();
            float distance = difference.magnitude;

            _log.Debug($"Sequence: {stateAtSequence.Seq} SeqLastProcessed: {SeqLastProcessed}");
            _log.Debug($"Server Position: ({serverUpdate.MotorState.Position.x},{serverUpdate.MotorState.Position.y},{serverUpdate.MotorState.Position.z})");
            _log.Debug($"Client Position: ({stateAtSequence.MotorState.Position.x},{stateAtSequence.MotorState.Position.y},{stateAtSequence.MotorState.Position.z})");
            _log.Debug($"Distance: {distance}");

            if (distance > 2)
            {
                // correct
                cs.Position = serverUpdate.MotorState.Position;
                cs.AttachedRigidbodyVelocity = serverUpdate.MotorState.AttachedRigidbodyVelocity;
                cs.BaseVelocity = serverUpdate.MotorState.BaseVelocity;

                kcc.Controller.Motor.ApplyState(cs);

                // clear input window?
                _simpleWindow.Items.Clear();
            }
            else if (distance > .1)
            {
                stateAtSequence.MotorState.Position = serverUpdate.MotorState.Position;
                stateAtSequence.MotorState.AttachedRigidbodyVelocity = serverUpdate.MotorState.AttachedRigidbodyVelocity;
                stateAtSequence.MotorState.BaseVelocity = serverUpdate.MotorState.BaseVelocity;

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

                //kcc.Controller.Motor.ApplyState(cs);
                kcc.Controller.Motor.SetPosition(cs.Position);

            }
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

            _log.Debug($"******** PLAYER INPUTS SEQ: {_playerInputsToTransmit.Last().Seq}");

        }
    }
}