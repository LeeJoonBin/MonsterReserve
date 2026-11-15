using UnityEngine;
using UnityEngine.InputSystem;
public class PlayerInputHandler : MonoBehaviour
{
    [SerializeField] private InputActionReference _aimAction; // 사격 줌인
    [SerializeField] private InputActionReference _castMagicAction; // 발사
    [SerializeField] private InputActionReference _detectAction; // 탐지, 추적키
    [SerializeField] private InputActionReference _healAction;
    [SerializeField] private InputActionReference _interactAction;
    [SerializeField] private InputActionReference _jumpAction;
    [SerializeField] private InputActionReference _lookAction; //마우스
    [SerializeField] private InputActionReference _meditateAction;
    [SerializeField] private InputActionReference _moveAction;
    [SerializeField] private InputActionReference _magic1Action;
    [SerializeField] private InputActionReference _magic2Action;
    [SerializeField] private InputActionReference _magic3Action;
    [SerializeField] private InputActionReference _magic4Action;
    [SerializeField] private InputActionReference _magic5Action;
    [SerializeField] private InputActionReference _sprintAction;
    
    private PlayerAnimController _playerAnimController;
    
    public static bool IsAiming { get; private set; }
    public static bool IsCasting { get; private set; }

    // PlayerMover, PlayerLook이 읽어 가는 입력값 (값 저장만 하고 판단은 하지 않는다)
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsJumpPressed { get; private set; }
    private void OnEnable()
    {
        BindInputEvents();
    }

    private void OnDisable()
    {
        UnbindInputEvents();
    }

    // PlayerMover가 점프를 처리했다고 알리는 요청. 한 번 누르면 한 번만 뛰게 하려고 둔다
    public void ConsumeJump()
    {
        IsJumpPressed = false;
    }

    private void BindInputEvents()
    {
        _aimAction.action.performed += OnAim;
        _aimAction.action.canceled += OnAim;
        
        _castMagicAction.action.performed += OnCastMagic;
        _castMagicAction.action.canceled += OnCastMagic;
        
        _detectAction.action.performed += OnDetect;
        _detectAction.action.canceled += OnDetect;
        
        _healAction.action.performed += OnHeal;
        _healAction.action.canceled += OnHeal;
        
        _interactAction.action.performed += OnInteract;
        _interactAction.action.canceled += OnInteract;

        _jumpAction.action.performed += OnJump;
        _jumpAction.action.canceled += OnJump;
        
        _lookAction.action.performed += OnLook;
        _lookAction.action.canceled += OnLook;

        _meditateAction.action.performed += OnMeditate;
        _meditateAction.action.canceled += OnMeditate;

        _moveAction.action.performed += OnMove;
        _moveAction.action.canceled += OnMove;

        _magic1Action.action.performed += OnSelectMagic1;
        
        _magic2Action.action.performed += OnSelectMagic2;
        
        _magic3Action.action.performed += OnSelectMagic3;
        
        _magic4Action.action.performed += OnSelectMagic4;
        
        _magic5Action.action.performed += OnSelectMagic5;
        
        _sprintAction.action.performed += OnSprint;
        _sprintAction.action.canceled += OnSprint;

        _aimAction.action.Enable();
        _castMagicAction.action.Enable();
        _detectAction.action.Enable();
        _healAction.action.Enable();
        _interactAction.action.Enable();
        _jumpAction.action.Enable();
        _lookAction.action.Enable();
        _meditateAction.action.Enable();
        _moveAction.action.Enable();
        _magic1Action.action.Enable();
        _magic2Action.action.Enable();
        _magic3Action.action.Enable();
        _magic4Action.action.Enable();
        _magic5Action.action.Enable();
        _sprintAction.action.Enable();
    }

    

    private void UnbindInputEvents()
    {
        _aimAction.action.performed -= OnAim;
        _aimAction.action.canceled -= OnAim;
        
        _castMagicAction.action.performed -= OnCastMagic;
        _castMagicAction.action.canceled -= OnCastMagic;
        
        _detectAction.action.performed -= OnDetect;
        _detectAction.action.canceled -= OnDetect;
        
        _healAction.action.performed -= OnHeal;
        _healAction.action.canceled -= OnHeal;
        
        _interactAction.action.performed -= OnInteract;
        _interactAction.action.canceled -= OnInteract;

        _jumpAction.action.performed -= OnJump;
        _jumpAction.action.canceled -= OnJump;
        
        _lookAction.action.performed -= OnLook;
        _lookAction.action.canceled -= OnLook;

        _meditateAction.action.performed -= OnMeditate;
        _meditateAction.action.canceled -= OnMeditate;

        _moveAction.action.performed -= OnMove;
        _moveAction.action.canceled -= OnMove;

        _magic1Action.action.performed -= OnSelectMagic1;
        
        _magic2Action.action.performed -= OnSelectMagic2;
        
        _magic3Action.action.performed -= OnSelectMagic3;
        
        _magic4Action.action.performed -= OnSelectMagic4;
        
        _magic5Action.action.performed -= OnSelectMagic5;
        
        _sprintAction.action.performed -= OnSprint;
        _sprintAction.action.canceled -= OnSprint;

        _aimAction.action.Disable();
        _castMagicAction.action.Disable();
        _detectAction.action.Disable();
        _healAction.action.Disable();
        _interactAction.action.Disable();
        _jumpAction.action.Disable();
        _lookAction.action.Disable();
        _meditateAction.action.Disable();
        _moveAction.action.Disable();
        _magic1Action.action.Disable();
        _magic2Action.action.Disable();
        _magic3Action.action.Disable();
        _magic4Action.action.Disable();
        _magic5Action.action.Disable();
        _sprintAction.action.Disable();
    }

    private void OnAim(InputAction.CallbackContext context)
    {
        IsAiming = true; // 줌인 확인용
        // 마우스 스피드 감소
        
    }
    
    private void OnCastMagic(InputAction.CallbackContext context)
    {
        // _moveAction = context.ReadValue<Vector2>();
    }

    private void OnDetect(InputAction.CallbackContext context)
    {
        
    }

    private void OnHeal(InputAction.CallbackContext context)
    {
        
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        
    }

    // 점프는 "한 번 누른 사건"이라, 누른 순간 켜 두고 PlayerMover가 처리할 때 끈다.
    // Update(입력)와 FixedUpdate(물리)는 도는 횟수가 달라서, 입력 쪽에서 바로 끄면 FixedUpdate가 못 보고 지나칠 수 있다.
    // 다른 방법 : InputAction.WasPressedThisFrame()을 쓰면 직접 끌 필요는 없지만, 이것도 "프레임" 기준이라 같은 문제를 따져 봐야 한다.
    private void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed) IsJumpPressed = true;
    }

    // canceled는 마우스가 멈출 때 들어오고, 이때 값이 (0,0)이다. 그래서 performed와 같은 코드로 처리해도 된다.
    private void OnLook(InputAction.CallbackContext context)
    {
        LookInput = context.ReadValue<Vector2>();
    }
    private void OnMeditate(InputAction.CallbackContext context)
    {}

    // 키를 모두 떼면 canceled가 (0,0)을 들고 온다
    private void OnMove(InputAction.CallbackContext context)
    {
        MoveInput = context.ReadValue<Vector2>();
    }
    private void OnSelectMagic1(InputAction.CallbackContext context){}
    
    private void OnSelectMagic2(InputAction.CallbackContext context){}
    private void OnSelectMagic3(InputAction.CallbackContext context){}
    private void OnSelectMagic4(InputAction.CallbackContext context){}
    private void OnSelectMagic5(InputAction.CallbackContext context){}
    // 누를 때는 performed, 뗄 때는 canceled가 오므로 context.performed가 곧 "누르고 있는가"다
    private void OnSprint(InputAction.CallbackContext context)
    {
        IsSprinting = context.performed;
    }
}
