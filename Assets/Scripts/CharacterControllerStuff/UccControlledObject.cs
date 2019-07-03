using Assets.Scripts.Network.StreamSystems;
using LiteNetLib.Utils;
using Opsive.UltimateCharacterController.Character;
using Opsive.UltimateCharacterController.Game;
using UnityEngine;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class UccControlledObject : ControlledObject
    {
        public UltimateCharacterLocomotion PLocomotion;

    
        public override void ApplyMoveDirection(float horizontalMovement, float forwardMovement)
        {
            KinematicObjectManager.SetCharacterMovementInput(PLocomotion.KinematicObjectIndex,
                horizontalMovement, forwardMovement);
        }

        public override void Deserialize(NetDataReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Debug.Log($"READ POS: {pos}");
            PLocomotion.SetPosition(pos, false);
        }

        public override void Serialize(NetDataWriter writer)
        {
            writer.Put(Entity.transform.position.x);
            writer.Put(Entity.transform.position.y);
            writer.Put(Entity.transform.position.z);
            Debug.Log($"WROTE POS: {Entity.transform.position}");
        }
    }
}