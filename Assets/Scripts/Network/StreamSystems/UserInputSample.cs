using LiteNetLib.Utils;
using UnityEngine;

namespace Assets.Scripts.Network.StreamSystems
{
    public class UserInputSample : SeqBase
    {
        public Vector3 MoveDirection;

        public Quaternion CameraRotation;

        public ushort PressedCount;

        public ushort[] Pressed;

        public UserInputSample()
        {
            MoveDirection = new Vector3();
        }

        public void UpdateFromCurrentInput()
        {
            MoveDirection.x = Input.GetAxis("Horizontal");
            MoveDirection.z = Input.GetAxis("Vertical");
            MoveDirection.y = 0;
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(Seq);
            // No movement in Y dir
            writer.Put(MoveDirection.x);
            writer.Put(MoveDirection.z);
            //writer.Put(PressedCount);
 
            //for(int i = 0; i < PressedCount; i++)
            //{
            //    writer.Put(Pressed[i]);
            //}
        }

        public void Deserialize(NetDataReader reader)
        {
            Seq = reader.GetUShort();
            MoveDirection.x = reader.GetFloat();
            MoveDirection.z = reader.GetFloat();
            MoveDirection.y = 0;
            //PressedCount = reader.GetUShort();
            //for(int i = 0; i < PressedCount; i++)
            //{
            //    Pressed[i] = reader.GetUShort();
            //}
        }

        public override string ToString()
        {
            return $"Seq: {Seq} Move: {MoveDirection.ToString()}";
        }
    }
}