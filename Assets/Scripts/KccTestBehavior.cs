using KinematicCharacterController;
using UnityEngine;

namespace Assets.Scripts
{
    public class KccTestBehavior : MonoBehaviour
    {
        // Start is called before the first frame update
        private void Start()
        {
            KinematicCharacterSystem.AutoSimulation = false;

            KinematicCharacterSystem.Simulate(
                Time.fixedDeltaTime,
                KinematicCharacterSystem.CharacterMotors,
                KinematicCharacterSystem.CharacterMotors.Count,
                KinematicCharacterSystem.PhysicsMovers,
                KinematicCharacterSystem.PhysicsMovers.Count);
        }

        // Update is called once per frame
        private void FixedUpdate()
        {
         

            Debug.Log($"Simulating {KinematicCharacterSystem.CharacterMotors.Count} motors");

            KinematicCharacterSystem.Simulate(
                Time.deltaTime,
                KinematicCharacterSystem.CharacterMotors,
                KinematicCharacterSystem.CharacterMotors.Count,
                KinematicCharacterSystem.PhysicsMovers,
                KinematicCharacterSystem.PhysicsMovers.Count);
        }
    }
}