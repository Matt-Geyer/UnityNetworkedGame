using LiteNetLib.Utils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserInputSample
{
    public ushort Seq;

    public float DeltaTime;

    public Vector3 MoveDirection;

    public ushort PressedCount;

    public ushort[] Pressed;


    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Seq);
        writer.Put(DeltaTime);

        // No movement in Y dir
        writer.Put(MoveDirection.x);
        writer.Put(MoveDirection.z);

        writer.Put(PressedCount);
 
        for(int i = 0; i < PressedCount; i++)
        {
            writer.Put(Pressed[i]);
        }
    }

    public void Deserialize(NetDataReader reader)
    {
        Seq = reader.GetUShort();
        DeltaTime = reader.GetFloat();
        MoveDirection.x = reader.GetFloat();
        MoveDirection.z = reader.GetFloat();
        MoveDirection.y = 0;
        PressedCount = reader.GetUShort();
        for(int i = 0; i < PressedCount; i++)
        {
            Pressed[i] = reader.GetUShort();
        }
    }
}