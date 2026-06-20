using UnityEngine;

namespace Greenkeeper.Unity.Input
{
    /// <summary>
    /// Minimal first-person controller for walking the course to inspect it (GDD §4.5). WASD move +
    /// mouse look on a CharacterController. Deliberately simple and readable — this is the body the
    /// player uses to scout greens (Phase 3) and, later, to play.
    ///
    /// SETUP (Phase 0.2):
    ///   1. Create an empty GameObject "Player".
    ///   2. Add a CharacterController component to it (default size is fine).
    ///   3. Add this component to it.
    ///   4. Make the Main Camera a CHILD of Player, local position ~ (0, 1.6, 0), rotation zero.
    ///   5. Drop a Plane primitive at the origin as the ground, then press Play.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Speeds")]
        public float walkSpeed = 6f;
        public float lookSpeed = 2f;
        public float gravity = -20f;
        public float jumpHeight = 1.2f;

        [Header("Look limits")]
        public float minPitch = -85f;
        public float maxPitch = 85f;

        private CharacterController _controller;
        private Transform _camera;
        private float _pitch;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _camera = GetComponentInChildren<Camera>() != null
                ? GetComponentInChildren<Camera>().transform
                : transform;
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Update()
        {
            Look();
            Move();
        }

        private void Look()
        {
            float mouseX = UnityEngine.Input.GetAxis("Mouse X") * lookSpeed;
            float mouseY = UnityEngine.Input.GetAxis("Mouse Y") * lookSpeed;

            transform.Rotate(Vector3.up, mouseX);

            _pitch = Mathf.Clamp(_pitch - mouseY, minPitch, maxPitch);
            _camera.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            float h = UnityEngine.Input.GetAxisRaw("Horizontal");
            float v = UnityEngine.Input.GetAxisRaw("Vertical");
            Vector3 move = (transform.right * h + transform.forward * v);
            if (move.sqrMagnitude > 1f) move.Normalize();
            move *= walkSpeed;

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f; // keep grounded
                if (UnityEngine.Input.GetButtonDown("Jump"))
                    _verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
            }
            else
            {
                _verticalVelocity += gravity * Time.deltaTime;
            }

            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);
        }
    }
}
