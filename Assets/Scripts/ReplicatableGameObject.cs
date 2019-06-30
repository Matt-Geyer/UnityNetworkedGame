using System;
using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts
{
    public class ReplicatableGameObject : ReplicatableObject
    {
        [Flags]
        public enum StateFlag
        {
            None = 0,
            Position = 1,
            Rotation = 2
        }

        public static readonly StateFlag AllStates =
            StateFlag.Position |
            StateFlag.Rotation;

        public Vector3 Position;
 
        private ReplicatableGameObject _lastFrame;

        public StateFlag ChangedStates;

        public static PersistentObjectRep StaticObjectRep;

        public override PersistentObjectRep ObjectRep
        {
            get => StaticObjectRep;
            set => StaticObjectRep = value;
        }

        public override void Serialize(NetDataWriter writer, uint mask)
        {
            StateFlag stateFlag = (StateFlag)mask;

            Debug.Log($"mask: {mask}  Mask: {stateFlag}");

            if ((stateFlag & StateFlag.Position) == StateFlag.Position)
            {
                writer.Put((byte)1);
                writer.Put(Position.x);
                writer.Put(Position.y);
                writer.Put(Position.z);
                Debug.Log($"Wrote positions {Position.ToString()}");
            }
            else
            {
                Debug.Log($"No position to write");
                writer.Put((byte)0);
            }
        }

        public override void Serialize(NetDataWriter writer)
        {
            Serialize(writer, (uint)AllStates);
        }

        public override void Deserialize(NetDataReader reader)
        {
            // when deserialized that means this object is being replicated
            // so i think the interpolation logic has to exist somewhat inside this obj
            // at the very least prev frame data?

            if (reader.GetByte() == 1)
            {
                Position.x = reader.GetFloat();
                Position.y = reader.GetFloat();
                Position.z = reader.GetFloat();
            }
        }

        public override void UpdateStateMask()
        {
            if (_lastFrame == null)
            {
                ChangedStates = AllStates;
                _lastFrame = new ReplicatableGameObject();
            }
            else
            {
                // not sure exactly how this will look just yet
                ChangedStates = StateFlag.None;
                if (_lastFrame.Position != Position)
                {
                    Debug.Log($"{Time.frameCount} : Position changed!");
                    ChangedStates |= StateFlag.Position;
                }
            }

            _lastFrame.CopyStateFrom(this);

            uint changedMask = Convert.ToUInt32(ChangedStates);

            foreach (ReplicationRecord r in ReplicationRecords.Values)
            {
                r.StateMask |= changedMask;
            }
        }

        public void CopyStateFrom(ReplicatableGameObject original)
        {
            Position = original.Position;
        }
    }
}