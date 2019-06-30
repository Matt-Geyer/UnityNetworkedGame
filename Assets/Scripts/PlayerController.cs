using UnityEngine;

namespace Assets.Scripts
{
    /// <summary>
    /// This doesn't do anything yet..
    /// </summary>
    public class PlayerController : MonoBehaviour
    {

        public float Speed;

        private CharacterController Controller;

        public void Start()
        {
            Controller = GetComponent<CharacterController>();
            Speed = 2f;
        }

        public void Update()
        {
            //var sample = new UserInputSample { Pressed = new ushort[100] };
            //UserInputUtils.Sample(sample);
            //ApplyInput(sample);
        }

        public void FixedUpdate()
        {
            var sample = new UserInputSample { Pressed = new ushort[100] };
            UserInputUtils.Sample(sample);
            ApplyInput(sample);
        }

        public void ApplyInput(UserInputSample input)
        {

            float magnitude = input.MoveDirection.magnitude;

            if (magnitude > 1) input.MoveDirection.Normalize();
        

            Controller.Move(input.MoveDirection * Time.fixedDeltaTime * Speed);


            for (int i = 0; i < input.PressedCount; i++)
            {
                KeyCode pressed = (KeyCode)input.Pressed[i];
                switch(pressed)
                {
                    case KeyCode.W:
                        Debug.Log("Pressed W");
                        break;
                }
            }
        }
    }
}
