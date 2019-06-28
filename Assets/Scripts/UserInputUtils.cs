using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IUserInputUtils
{
    void Sample(UserInputSample sample);
}


public class UserInputUtils  : IUserInputUtils
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

    void IUserInputUtils.Sample(UserInputSample sample) 
    {
        Sample(sample);
    }

    public static void Sample(UserInputSample sample)
    {
        sample.MoveDirection.x = Input.GetAxis("Horizontal"); 
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
