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
    private void OnEnable()
    {
        BindInputEvents();
    }

    private void OnDisable()
    {
        UnbindInputEvents();    
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
        
        _meditateAction.action.performed += OnMeditate;
        _meditateAction.action.canceled += OnMeditate;
        
        _moveAction.action.performed += OnMove;
        
        _magic1Action.action.performed += OnSelectMagic1;
        
        _magic2Action.action.performed += OnSelectMagic2;
        
        _magic3Action.action.performed += OnSelectMagic3;
        
        _magic4Action.action.performed += OnSelectMagic4;
        
        _magic5Action.action.performed += OnSelectMagic5;
        
        _sprintAction.action.performed += OnSprint;
        
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
        
        _meditateAction.action.performed -= OnMeditate;
        _meditateAction.action.canceled -= OnMeditate;
        
        _moveAction.action.performed -= OnMove;
        
        _magic1Action.action.performed -= OnSelectMagic1;
        
        _magic2Action.action.performed -= OnSelectMagic2;
        
        _magic3Action.action.performed -= OnSelectMagic3;
        
        _magic4Action.action.performed -= OnSelectMagic4;
        
        _magic5Action.action.performed -= OnSelectMagic5;
        
        _sprintAction.action.performed -= OnSprint;
        
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

    private void OnJump(InputAction.CallbackContext context)
    {
        
    }

    private void OnLook(InputAction.CallbackContext context)
    {
        
    }
    private void OnMeditate(InputAction.CallbackContext context)
    {}

    private void OnMove(InputAction.CallbackContext context)
    {
        
    }
    private void OnSelectMagic1(InputAction.CallbackContext context){}
    
    private void OnSelectMagic2(InputAction.CallbackContext context){}
    private void OnSelectMagic3(InputAction.CallbackContext context){}
    private void OnSelectMagic4(InputAction.CallbackContext context){}
    private void OnSelectMagic5(InputAction.CallbackContext context){}
    private void OnSprint(InputAction.CallbackContext context){}
}
