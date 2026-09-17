using UnityEngine;
using System;
using System.Collections;
using Core;

namespace Enemy
{
    public class PracticeDummyHealth : MonoBehaviour
    {
        [Header("血量设置")]
        public int maxHealth = 100;
        public int currentHealth;

        [Header("当前伤情")]
        public InjuryState currentState = InjuryState.None;

        [Header("死亡后自动恢复")]
        [SerializeField] float deathRecoverDelay = 3f;

        public event Action OnHealthOrStateChanged;

        Coroutine _recoverRoutine;

        private void Start()
        {
            ResetDummy(false);
        }

        public void ApplyHit(DetailedBodyPart hitboxType, string weaponType)
        {
            if (currentState == InjuryState.InstanceDeath_Head)
            {
                Debug.Log($"<color=gray>[假人受击]</color> 假人已死亡，等待恢复中，忽略本次 {weaponType} 攻击");
                return;
            }

            InjuryState injuryFromHit = MapHitboxToInjury(hitboxType);
            ApplyDamage(hitboxType);
            currentState = injuryFromHit;

            if (currentHealth <= 0 && currentState != InjuryState.InstanceDeath_Head)
            {
                currentHealth = 0;
            }

            OnHealthOrStateChanged?.Invoke();

            Debug.Log($"[DummyHit] 假人部位: {hitboxType}, 算出的伤情: {currentState}");
            Debug.Log($"<color=yellow>[假人受击]</color> 武器: {weaponType}, 部位: {hitboxType}, 剩余HP: {currentHealth}, 当前伤情: {currentState}");

            ReflectInjuryToLocalPlayer();

            if (currentState == InjuryState.InstanceDeath_Head)
            {
                BeginDeathRecover();
            }
        }

        public void ResetDummy(bool alsoResetPlayer = true)
        {
            currentHealth = maxHealth;
            currentState = InjuryState.None;
            OnHealthOrStateChanged?.Invoke();
            Debug.Log("<color=cyan>[假人]</color> 状态已刷新：Health 满、伤情 None");

            if (alsoResetPlayer)
            {
                Player.PlayerStatusController playerStatus = FindLocalPlayerStatus();
                playerStatus?.ResetStatus();
            }
        }

        void ApplyDamage(DetailedBodyPart hitboxType)
        {
            switch (hitboxType)
            {
                case DetailedBodyPart.Head:
                    currentHealth = 0;
                    break;
                case DetailedBodyPart.Torso:
                    currentHealth = Mathf.Max(0, currentHealth - 50);
                    break;
                case DetailedBodyPart.Legs:
                    currentHealth = Mathf.Max(0, currentHealth - 25);
                    break;
                case DetailedBodyPart.Arms:
                    currentHealth = Mathf.Max(0, currentHealth - 15);
                    break;
            }
        }

        static InjuryState MapHitboxToInjury(DetailedBodyPart hitboxType)
        {
            switch (hitboxType)
            {
                case DetailedBodyPart.Head:
                    return InjuryState.InstanceDeath_Head;
                case DetailedBodyPart.Torso:
                    return InjuryState.DBNO_Torso;
                case DetailedBodyPart.Legs:
                    return InjuryState.Crippled_Legs;
                case DetailedBodyPart.Arms:
                    return InjuryState.Light_Arms;
                default:
                    return InjuryState.None;
            }
        }

        void BeginDeathRecover()
        {
            if (_recoverRoutine != null)
            {
                return;
            }

            _recoverRoutine = StartCoroutine(RecoverAfterDelay());
        }

        IEnumerator RecoverAfterDelay()
        {
            Debug.Log($"<color=cyan>[假人]</color> {deathRecoverDelay:0.#} 秒后恢复无伤状态");
            yield return new WaitForSeconds(deathRecoverDelay);
            _recoverRoutine = null;
            ResetDummy(true);
        }

        void ReflectInjuryToLocalPlayer()
        {
            Player.PlayerStatusController playerStatus = FindLocalPlayerStatus();
            if (playerStatus == null)
            {
                Debug.LogWarning("[镜面反弹 Warning] 场景中未找到正在操控的 PlayerStatusController!");
                return;
            }

            playerStatus.ApplyInjury(currentState);
            Debug.Log($"<color=pink>[镜面反弹]</color> 已将假人伤情 {currentState} 同步给玩家 {playerStatus.gameObject.name}！");
        }

        static Player.PlayerStatusController FindLocalPlayerStatus()
        {
            PlayerController localPlayer = PlayerController.FindLocalOwnedPlayer();
            if (localPlayer == null)
            {
                var players = PlayerRegistry.All;
                for (int i = 0; i < players.Count; i++)
                {
                    PlayerController player = players[i];
                    if (player != null && player.IsControlled)
                    {
                        localPlayer = player;
                        break;
                    }
                }
            }

            if (localPlayer != null)
            {
                Player.PlayerStatusController fromPlayer = localPlayer.GetComponent<Player.PlayerStatusController>();
                if (fromPlayer != null)
                {
                    return fromPlayer;
                }
            }

            return FindFirstObjectByType<Player.PlayerStatusController>();
        }
    }
}
