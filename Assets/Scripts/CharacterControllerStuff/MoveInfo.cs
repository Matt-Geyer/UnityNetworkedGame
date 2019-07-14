using Assets.Scripts.Network.StreamSystems;
using KinematicCharacterController;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class MoveInfo : SeqBase
    {

        public UserInputSample UserInput;

        public KinematicCharacterMotorState MotorState;
    }
}