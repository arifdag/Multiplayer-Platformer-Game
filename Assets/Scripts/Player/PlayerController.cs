using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private Vector2 speedInterval = new Vector2(5, 10);
    [SerializeField] private float jumpSpeed = 8f; 
    [Tooltip("How long (seconds) it takes to go from min→max speed")] [SerializeField, Min(0.01f)]
    private float runRampTime = 1f;
    [SerializeField] float gravity = -9.81f;
    [SerializeField, Min(0.01f)] private float gravityMultiplier = 2f;



    private CharacterController _characterController;
    private Animator _animator;
    
    private bool _isGrounded;
    private bool _lastFacingRight = true;
    private bool _isDancing = false;
    bool _isLaunched = false;

    
    
    private float _horizontalInput;
    private float _targetHorizontalSpeed;
    private float _runTimer;
    private Vector3 _velocity;


    // Animator hashes
    private static readonly int JumpAnimTrigger = Animator.StringToHash("Jump"); 
    private static readonly int IsWalking = Animator.StringToHash("IsWalking");
    private static readonly int IsRunning = Animator.StringToHash("IsRunning");
    private static readonly int IsDancing = Animator.StringToHash("IsDancing");

    private void Awake()
    {
        _characterController = GetComponent<CharacterController>();
        _animator = GetComponent<Animator>();
    }

    void Update()
    {
        _isGrounded = _characterController.isGrounded;

        // If grounded and falling, reset vertical velocity to a small negative value
        // This helps stick to slopes and prevents bouncing off them.
        if (_isGrounded && _velocity.y < 0)
        {
            _velocity.y = -2f;
        }

        ProcessDanceInput();

        if (!_isDancing)
        {
            ProcessMovementInput(); // Calculates _targetHorizontalSpeed and _horizontalInput
            ProcessJumpInput();     // Handles jump logic

            // Set horizontal velocity
            _velocity.x = _targetHorizontalSpeed;

            // Animation & Direction based on input
            bool isMovingInput = Mathf.Abs(_horizontalInput) > 0.01f;
            bool isRunningInput = isMovingInput && Input.GetKey(KeyCode.LeftShift);
            UpdateMovementAnimation(isMovingInput, isRunningInput);

            if (isMovingInput)
            {
                if (_horizontalInput < 0 && _lastFacingRight)
                {
                    ChangeDirection();
                }
                else if (_horizontalInput > 0 && !_lastFacingRight)
                {
                    ChangeDirection();
                }
            }
        }
        else // Is Dancing
        {
            _velocity.x = 0f; // Stop horizontal movement
            _horizontalInput = 0f; // Clear input for animation purposes
            _targetHorizontalSpeed = 0f;
            UpdateMovementAnimation(false, false); // Ensure movement anims are off
        }

        // Apply gravity
        _velocity.y += gravity * gravityMultiplier * Time.deltaTime;

        // Move the CharacterController
        _characterController.Move(_velocity * Time.deltaTime);
    }
    

    private void ProcessMovementInput()
    {
        _horizontalInput = Input.GetAxisRaw("Horizontal");

        bool isTryingToMove = Mathf.Abs(_horizontalInput) > 0.01f;
        bool isRunning = isTryingToMove && Input.GetKey(KeyCode.LeftShift);

        _runTimer += (isRunning ? 1 : -1) * Time.deltaTime;
        _runTimer = Mathf.Clamp(_runTimer, 0f, runRampTime);

        float tNorm = (runRampTime > 0f) ? _runTimer / runRampTime : 1f;
        tNorm = Mathf.Clamp01(tNorm);
        float easedT = Mathf.SmoothStep(0f, 1f, tNorm);

        float currentSpeed = Mathf.Lerp(speedInterval.x, speedInterval.y, easedT);
        _targetHorizontalSpeed = _horizontalInput * currentSpeed;
    }

    private void UpdateMovementAnimation(bool isMoving, bool isRunning)
    {
         if (!_isDancing)
         {
             _animator.SetBool(IsWalking, isMoving && !isRunning);
             _animator.SetBool(IsRunning, isRunning);
         } else {
             _animator.SetBool(IsWalking, false);
             _animator.SetBool(IsRunning, false);
         }
    }

    private void ProcessJumpInput()
    {
        // Jump if the button is pressed and grounded
        if (Input.GetButtonDown("Jump") && _isGrounded)
        {
            _velocity.y = jumpSpeed; // Set vertical velocity for jump
            _animator.SetTrigger(JumpAnimTrigger);
            // _isGrounded will become false on the next frame after Move() lifts it
        }
    }
    
    private void ProcessDanceInput()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            _isDancing = true;
            _animator.SetBool(IsDancing, true);
        }
        if (_isDancing)
        {
            // Check for any key press *except* H to stop dancing
            // This logic allows holding H to continue dancing if desired,
            // but any other key will break it.
            if (Input.anyKeyDown && !Input.GetKeyDown(KeyCode.H))
            {
                _isDancing = false;
                _animator.SetBool(IsDancing, false);
            }
        }
    }
    
    private void ChangeDirection()
    {
        if (_isDancing) return;
        transform.Rotate(0f, 180f, 0f, Space.World);
        _lastFacingRight = !_lastFacingRight;
    }
    
    /// Give the player an instant velocity. Handled over time in Update().
    public void Launch(Vector3 initialVelocity)
    {
        _velocity = initialVelocity;
    }
}