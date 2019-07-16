using KinematicCharacterController;
using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class SerializationHelper
    {
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
    }
}