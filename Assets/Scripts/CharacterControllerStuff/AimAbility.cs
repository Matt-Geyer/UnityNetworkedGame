using Opsive.UltimateCharacterController.Character;
using Opsive.UltimateCharacterController.Character.Abilities;
using Opsive.UltimateCharacterController.Character.MovementTypes;
using Opsive.UltimateCharacterController.ThirdPersonController.Character.MovementTypes;
using UnityEngine;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class AimAbility : Ability
    {
        public override bool AbilityWillStart()
        {
            Debug.Log("STARTING AIM ABILITY");
            TopDown ucl = (TopDown)m_GameObject.GetComponent<UltimateCharacterLocomotion>().ActiveMovementType;

            ucl.LookInMoveDirection = false;


            return true;
        }

        public override void WillTryStopAbility()
        {
            Debug.Log("STOPPING AIM ABILITY");
            TopDown ucl = (TopDown)m_GameObject.GetComponent<UltimateCharacterLocomotion>().ActiveMovementType;

            ucl.LookInMoveDirection = true;

        }
    }
}
