using System.Collections;
using System.Collections.Generic;
using Assets.Scripts.Network.StreamSystems;
using Opsive.UltimateCharacterController.Character;
using Opsive.UltimateCharacterController.Game;
using UnityEngine;
using UnityEngine.EventSystems;

public class UccTest : MonoBehaviour
{
    public UltimateCharacterLocomotion CharacterLocomotion;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        UserInputSample sample = new UserInputSample();
        sample.UpdateFromCurrentInput();
        KinematicObjectManager.SetCharacterMovementInput(CharacterLocomotion.KinematicObjectIndex,sample.MoveDirection.z, sample.MoveDirection.x);
        for (int x = 0; x < 100; x++)
        {
            Debug.Log(CharacterLocomotion.transform.position);
            KinematicObjectManager.FixedCharacterMove(CharacterLocomotion.KinematicObjectIndex);
        }
            

        
    }
}
