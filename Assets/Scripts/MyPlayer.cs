using Animancer;
using UnityEngine;


namespace Assets.Scripts
{
    public class MyPlayer : MonoBehaviour
    {

        [SerializeField] private AnimancerComponent _animancer;

        [SerializeField] private AnimationClip _idleClip;

        [SerializeField] private FloatControllerState.Serializable _walkBlendTree;


        public ExampleCharacterCamera OrbitCamera;
        public Transform CameraFollowPoint;
        public MyCharacterController Character;

        private const string MouseXInput = "Mouse X";
        private const string MouseYInput = "Mouse Y";
        private const string MouseScrollInput = "Mouse ScrollWheel";
        private const string HorizontalInput = "Horizontal";
        private const string VerticalInput = "Vertical";

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;

            // Tell camera to follow transform
            OrbitCamera.SetFollowTransform(CameraFollowPoint);

            // Ignore the character's collider(s) for camera obstruction checks
            OrbitCamera.IgnoredColliders.Clear();
            OrbitCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());

            _animancer.Transition(_walkBlendTree);

            _walkBlendTree.State.Playable.SetBool("Walking", true);

            // _animancer.Play(_idleClip);

        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            HandleCameraInput();
            HandleCharacterInput();
        }

        private void HandleCameraInput()
        {
            // Create the look input vector for the camera
            float mouseLookAxisUp = Input.GetAxisRaw(MouseYInput);
            float mouseLookAxisRight = Input.GetAxisRaw(MouseXInput);
            Vector3 lookInputVector = new Vector3(mouseLookAxisRight, mouseLookAxisUp, 0f);

            // Prevent moving the camera while the cursor isn't locked
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                lookInputVector = Vector3.zero;
            }

            // Input for zooming the camera (disabled in WebGL because it can cause problems)
            float scrollInput = -Input.GetAxis(MouseScrollInput);
#if UNITY_WEBGL
        scrollInput = 0f;
#endif

            // Apply inputs to the camera
            OrbitCamera.UpdateWithInput(Time.deltaTime, scrollInput, lookInputVector);

            // Handle toggling zoom level
            if (Input.GetMouseButtonDown(1))
            {
                OrbitCamera.TargetDistance = (OrbitCamera.TargetDistance == 0f) ? OrbitCamera.DefaultDistance : 0f;
            }
        }

        private void HandleCharacterInput()
        {
            PlayerCharacterInputs characterInputs = new PlayerCharacterInputs
            {
                MoveAxisForward = Input.GetAxis(VerticalInput),
                MoveAxisRight = Input.GetAxis(HorizontalInput),
                CameraRotation = OrbitCamera.Transform.rotation
            };

            // Build the CharacterInputs struct

            Vector3 moveDir = new Vector3(characterInputs.MoveAxisRight,0, characterInputs.MoveAxisForward);

            float magnitude = moveDir.magnitude;

            if (magnitude > 1)
                moveDir.Normalize();

            Vector3 localDir = Character.transform.InverseTransformDirection(moveDir);

            // Apply inputs to character
            Character.SetInputs(ref characterInputs);

            _walkBlendTree.State.Playable.SetFloat("RelativeVertical", moveDir.z);
            _walkBlendTree.State.Playable.SetFloat("RelativeHorizontal", moveDir.x);
            _walkBlendTree.State.Playable.SetFloat("Speed", magnitude);
        }
    }
}