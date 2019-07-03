using System;
using Opsive.UltimateCharacterController.Character.Abilities;
using UnityEngine;
using EventHandler = Opsive.UltimateCharacterController.Events.EventHandler;

namespace Assets.Scripts.CharacterControllerStuff
{
    public class AimBehavior : MonoBehaviour
    {
        // Start is called before the first frame update
        void Awake()
        {
            EventHandler.RegisterEvent<Ability, bool>(gameObject, "OnCharacterAbilityActive", OnCharacterAbilityActive);
        }

        private void OnCharacterAbilityActive(Ability ability, bool activated)
        {
            Debug.Log($"Ability {ability} activated: {activated}");
        }

        // Update is called once per frame
        void Update()
        {
        
        }
    }
}
