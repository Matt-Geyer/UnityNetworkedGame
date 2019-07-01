using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts
{
    public class ControlledObject : IPersistentObject
    {
        public static PersistentObjectRep StaticObjectRep;

        public GameObject Entity;
        public CharacterController PlayerController;

        public PersistentObjectRep ObjectRep
        {
            get => StaticObjectRep;
            set => StaticObjectRep = value;
        }

        public virtual void ApplyInput(UserInputSample input)
        {
            PlayerController.Move(input.MoveDirection * 2f * (1f / 60f));
        }

        public void Deserialize(NetDataReader reader)
        {
            Vector3 pos = new Vector3(reader.GetFloat(), 0.0001f, reader.GetFloat());
            Debug.Log($"READ POS: {pos}");
            Entity.transform.SetPositionAndRotation(pos, new Quaternion());
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Entity.transform.position.x);
            writer.Put(Entity.transform.position.z);
            Debug.Log($"WROTE POS: {Entity.transform.position}");
        }
    }
}