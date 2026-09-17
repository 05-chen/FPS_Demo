using UnityEngine;
using UnityEngine.UI;
using Core;

namespace Enemy
{
    public class PracticeDummyHud : MonoBehaviour
    {
        [Header("组件相关")]
        public PracticeDummyHealth targetHealth;
        public Text infoText;

        private Transform _mainCameraTransform;

        private void OnEnable()
        {
            BindHealth();
            UpdateHudText();
        }

        private void Start()
        {
            BindHealth();

            if (Camera.main != null)
            {
                _mainCameraTransform = Camera.main.transform;
            }

            UpdateHudText();
        }

        void BindHealth()
        {
            if (targetHealth == null)
            {
                targetHealth = GetComponentInParent<PracticeDummyHealth>(true);
            }

            if (targetHealth == null)
            {
                return;
            }

            targetHealth.OnHealthOrStateChanged -= UpdateHudText;
            targetHealth.OnHealthOrStateChanged += UpdateHudText;
        }

        private void OnDestroy()
        {
            if (targetHealth != null)
            {
                targetHealth.OnHealthOrStateChanged -= UpdateHudText;
            }
        }

        private void LateUpdate()
        {
            if (_mainCameraTransform == null && Camera.main != null)
            {
                _mainCameraTransform = Camera.main.transform;
            }

            if (_mainCameraTransform == null)
            {
                return;
            }

            // World Space UI 朝向相机（正面朝向玩家）
            transform.rotation = Quaternion.LookRotation(transform.position - _mainCameraTransform.position);
        }

        private void UpdateHudText()
        {
            if (targetHealth == null || infoText == null) return;

            string stateName = GetFormattedStateName(targetHealth.currentState);
            infoText.text = $"Health: {targetHealth.currentHealth} | State: {stateName}";
        }

        private string GetFormattedStateName(InjuryState state)
        {
            return state switch
            {
                InjuryState.None => "Healthy",
                InjuryState.Light_Arms => "Light/Arms",
                InjuryState.Crippled_Legs => "Crippled/Legs",
                InjuryState.DBNO_Torso => "DBNO/Torso",
                InjuryState.InstanceDeath_Head => "Instant Death",
                _ => "Unknown",
            };
        }
    }
}
