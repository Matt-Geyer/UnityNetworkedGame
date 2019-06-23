using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserInputUtils 
{

    public static readonly KeyCode[] CheckKeys =
    {
        KeyCode.W,
        KeyCode.A,

    };

    public static readonly ushort[] CheckKeysUshort =
    {
        (ushort)KeyCode.W,
        (ushort)KeyCode.A
    };

    public static void Sample(UserInputSample sample)
    {

        sample.DeltaTime = Time.deltaTime;

        float horizontal = Input.GetAxis("Horizontal");

        sample.MoveDirection.x = horizontal;
        sample.MoveDirection.z = Input.GetAxis("Vertical");
        sample.MoveDirection.y = 0;

        int len = CheckKeys.Length;
        for (int i = 0; i < len; i++)
        {
            if (Input.GetKey(CheckKeys[i]))
            {
                sample.Pressed[sample.PressedCount++] = CheckKeysUshort[i];
            }
        }
    }   
}
