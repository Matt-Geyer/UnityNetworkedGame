using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;
using UnityEngine;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class MoveInfo : SeqBase
    {

        public UserInputSample UserInput;

        public KinematicCharacterMotorState MotorState;

        public MoveInfo()
        {
            UserInput = new UserInputSample {MoveDirection = new Vector3()};
        }

    }
}