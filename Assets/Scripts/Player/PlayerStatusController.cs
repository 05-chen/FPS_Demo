using Core;
using UnityEngine;

namespace Player
{
    public class PlayerStatusController : MonoBehaviour
    {
        [Header("当前玩家伤情")]
        public InjuryState currentInjury = InjuryState.None;

        [Header("长按放弃")]
        [SerializeField] float giveUpHoldSeconds = 1.5f;

        [Header("调试快捷键（仍需先按 Alt+F9 打开测试模式）")]
        public bool enableDebugKeys = true;

        PlayerController _playerController;
        PlayerHealth _playerHealth;
        float _giveUpHold;

        void Awake()
        {
            _playerController = GetComponent<PlayerController>();
            _playerHealth = GetComponent<PlayerHealth>();
        }

        void Start()
        {
            if (_playerController == null)
            {
                _playerController = GetComponent<PlayerController>();
            }

            if (_playerHealth == null)
            {
                _playerHealth = GetComponent<PlayerHealth>();
            }
        }

        void Update()
        {
            if (!IsLocallyControlled())
            {
                return;
            }

            TickGiveUpHold();

            if (!enableDebugKeys || !DebugCommandGate.IsEnabled)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                ResetStatus();
                _playerHealth?.DebugForceRevive();
            }

            if (Input.GetKeyDown(KeyCode.F2))
            {
                ApplyInjury(InjuryState.Light_Arms);
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                ApplyInjury(InjuryState.Crippled_Legs);
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                ApplyInjury(InjuryState.DBNO_Torso);
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                ApplyInjury(InjuryState.InstanceDeath_Head);
            }
        }

        bool IsLocallyControlled()
        {
            if (_playerController == null)
            {
                return true;
            }

            if (_playerController.IsSpawned)
            {
                return _playerController.IsOwner;
            }

            return _playerController.IsControlled;
        }

        void TickGiveUpHold()
        {
            bool canGiveUp = currentInjury == InjuryState.DBNO_Torso &&
                             _playerHealth != null &&
                             _playerHealth.IsDowned &&
                             !_playerHealth.IsDead;

            if (!canGiveUp)
            {
                _giveUpHold = 0f;
                UI.CombatStatusUI.Instance?.SetGiveUpProgress(0f);
                return;
            }

            if (Input.GetKey(KeyCode.Space))
            {
                _giveUpHold += Time.unscaledDeltaTime;
            }
            else
            {
                _giveUpHold = 0f;
            }

            float duration = Mathf.Max(0.1f, giveUpHoldSeconds);
            UI.CombatStatusUI.EnsureInstance().SetGiveUpProgress(_giveUpHold / duration);
            if (_giveUpHold < duration)
            {
                return;
            }

            _giveUpHold = 0f;
            _playerHealth.RequestGiveUp();
        }

        /// <summary>
        /// 核心接口：接受并应用新的伤情 Debuff
        /// </summary>
        public void ApplyInjury(InjuryState newInjury)
        {
            Debug.Log($"[PlayerStatus] 收到反弹伤情: {newInjury}");
            currentInjury = newInjury;
            Debug.Log($"<color=cyan>[PlayerStatus]</color> 玩家伤情状态更新为: {currentInjury}");

            if (!IsLocallyControlled())
            {
                return;
            }

            if (newInjury == InjuryState.DBNO_Torso)
            {
                // 躯干 → 倒地 HUD（与正式受击一致，不依赖调试门闩）
                _playerHealth?.RequestEnterDowned();
                return;
            }

            if (newInjury == InjuryState.InstanceDeath_Head)
            {
                // 爆头 → 正式死亡黑屏读秒（假人镜面反弹 / F5 共用；F5 仍由上方 Alt+F9 门闩拦截）
                _playerHealth?.RequestInstantKill();
                return;
            }

            if (GameplayGate.CurrentMode == GameplayGate.Mode.InputLocked &&
                (_playerHealth == null || _playerHealth.LifeState == PlayerLifeState.Alive))
            {
                GameplayGate.Release();
            }
        }

        public void SetInjuryFromLifeState(InjuryState injury)
        {
            currentInjury = injury;
            if (injury != InjuryState.DBNO_Torso)
            {
                _giveUpHold = 0f;
            }
        }

        /// <summary>
        /// 复位/治疗接口
        /// </summary>
        public void ResetStatus()
        {
            currentInjury = InjuryState.None;
            _giveUpHold = 0f;
            if (IsLocallyControlled())
            {
                UI.CombatStatusUI.Instance?.SetGiveUpProgress(0f);
            }

            Debug.Log($"<color=cyan>[PlayerStatus]</color> 玩家伤情状态更新为: {currentInjury}");
        }
    }
}
