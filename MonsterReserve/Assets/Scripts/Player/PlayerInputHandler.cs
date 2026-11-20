using UnityEngine;
using System;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    [SerializeField] private InputActionReference _aimAction; // 사격 줌인
    [SerializeField] private InputActionReference _castMagicAction; // 발사
    [SerializeField] private InputActionReference _detectAction; // 탐지, 추적키
    [SerializeField] private InputActionReference _healAction;
    [SerializeField] private InputActionReference _interactAction;
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
    
    public bool IsAiming { get; private set; }
    public event Action<bool> OnAimChanged;
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsMeditateHeld { get; private set; }
    private bool _hasCastInput;
    private bool _hasInteractInput;
    private void OnEnable()
    {
        BindInputEvents();
    }

    private void OnDisable()
    {
        UnbindInputEvents();
    }

    public bool ConsumeInteractInput()
    {
        if (!_hasInteractInput)
        {
            return false;
        }
        _hasInteractInput = false;
        return true;
    }
    public bool ConsumeCastInput()
    {
        if (!_hasCastInput)
        {
            return false;
        }
        _hasCastInput = false;
        return true;
    }

    private void OnAim(InputAction.CallbackContext context)
    {
        IsAiming = context.performed;
        OnAimChanged?.Invoke(IsAiming); // 조준 상태가 바뀌면 알림
    }
    
    private void OnCastMagic(InputAction.CallbackContext context)
    {
        _hasCastInput = true;
    }

    private void OnDetect(InputAction.CallbackContext context)
    {
        
    }

    private void OnHeal(InputAction.CallbackContext context)
    {
        
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        _hasInteractInput = true;
    }

    private void OnLook(InputAction.CallbackContext context)
    {
        LookInput = context.ReadValue<Vector2>();
    }
    private void OnMeditate(InputAction.CallbackContext context)
    {
        IsMeditateHeld = context.performed;
    }

    private void OnMove(InputAction.CallbackContext context)
    {
        MoveInput = context.ReadValue<Vector2>();
    }
    private void OnSelectMagic1(InputAction.CallbackContext context){}
    private void OnSelectMagic2(InputAction.CallbackContext context){}
    private void OnSelectMagic3(InputAction.CallbackContext context){}
    private void OnSelectMagic4(InputAction.CallbackContext context){}
    private void OnSelectMagic5(InputAction.CallbackContext context){}
    private void OnSprint(InputAction.CallbackContext context)
    {
        IsSprinting = context.performed;
    }
    private void BindInputEvents()
    {
        _aimAction.action.performed += OnAim;
        _aimAction.action.canceled += OnAim;
        
        _castMagicAction.action.performed += OnCastMagic;
        
        _detectAction.action.performed += OnDetect;
        _detectAction.action.canceled += OnDetect;
        
        _healAction.action.performed += OnHeal;
        _healAction.action.canceled += OnHeal;
        
        _interactAction.action.performed += OnInteract;

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
        
        _detectAction.action.performed -= OnDetect;
        _detectAction.action.canceled -= OnDetect;
        
        _healAction.action.performed -= OnHeal;
        _healAction.action.canceled -= OnHeal;
        
        _interactAction.action.performed -= OnInteract;

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
}
