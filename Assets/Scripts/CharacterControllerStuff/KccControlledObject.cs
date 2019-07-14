using System.Runtime.InteropServices;
using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;
using LiteNetLib.Utils;
using UnityEditor;

namespace Assets.Scripts.CharacterControllerStuff
{
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
        }
    }
}
