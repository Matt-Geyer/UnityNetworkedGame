using LiteNetLib.Utils;
using Opsive.UltimateCharacterController.Character;
using Opsive.UltimateCharacterController.Game;
using UnityEngine;

namespace Assets.Scripts.Network.StreamSystems
{
    public class ControlledObject : IPersistentObject
    {
        public static PersistentObjectRep StaticObjectRep;

        public GameObject Entity;
        public CharacterController PlayerController;
        public UltimateCharacterLocomotion PLocomotion;

        public PersistentObjectRep ObjectRep
        {
            get => StaticObjectRep;
            set => StaticObjectRep = value;
        }

        public virtual void ApplyInput(UserInputSample input)
        {
            //PlayerController.Move(input.MoveDirection * 2f * (1f / 60f));
            KinematicObjectManager.SetCharacterMovementInput(PLocomotion.KinematicObjectIndex,
                input.MoveDirection.z, input.MoveDirection.x);
        }

        public void Deserialize(NetDataReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
            Debug.Log($"READ POS: {pos}");
            PLocomotion.SetPosition(pos, false);
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Entity.transform.position.x);
            writer.Put(Entity.transform.position.y);
            writer.Put(Entity.transform.position.z);
            Debug.Log($"WROTE POS: {Entity.transform.position}");
        }
    }
}