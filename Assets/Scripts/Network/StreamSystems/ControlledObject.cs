using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts.Network.StreamSystems
{
    /// <summary>
    /// Base functionality for an object that is controlled by player input and is persisted and updated by the ControlledObject systems
    /// </summary>
    public abstract class ControlledObject : IPersistentObject
    {
        public static PersistentObjectRep StaticObjectRep;

        public GameObject Entity;
        public CharacterController PlayerController;

        public PersistentObjectRep ObjectRep
        {
            get => StaticObjectRep;
            set => StaticObjectRep = value;
        }

        /// <summary>
        /// Deterministically apply the players input.
        /// TODO: This is going to need to be split up into specific functions like apply move direction etc I think
        /// </summary>
        /// <param name="horizontalMovement"></param>
        /// <param name="forwardMovement"></param>
        public abstract void ApplyMoveDirection(float horizontalMovement, float forwardMovement);

        public abstract void Deserialize(NetDataReader reader);

        public abstract void Serialize(NetDataWriter writer);
    }
}