using UnityEngine;

namespace UnityInspection
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float walkSpeed = 4f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float lookSensitivity = 2f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private CharacterController characterController;
        private float pitch;
        private float yaw;
        private float verticalVelocity;

        public bool InputLocked { get; private set; }

        public void Configure(Transform cameraTransform)
        {
            this.cameraTransform = cameraTransform;
        }

        public void SetInputLocked(bool locked)
        {
            InputLocked = locked;
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (cameraTransform == null)
            {
                cameraTransform = GetComponentInChildren<Camera>()?.transform;
            }

            yaw = transform.eulerAngles.y;
        }

        private void Update()
        {
            if (InputLocked || characterController == null)
            {
                return;
            }

            UpdateLook();
            UpdateMovement();
        }

        private void UpdateLook()
        {
            if (cameraTransform == null || !Input.GetMouseButton(1))
            {
                return;
            }

            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch = Mathf.Clamp(
                pitch - Input.GetAxis("Mouse Y") * lookSensitivity,
                minPitch,
                maxPitch);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void UpdateMovement()
        {
            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            input = Vector3.ClampMagnitude(input, 1f);

            float speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? sprintSpeed
                : walkSpeed;
            Vector3 movement = transform.TransformDirection(input) * speed;

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            movement.y = verticalVelocity;
            characterController.Move(movement * Time.deltaTime);
        }
    }
}
