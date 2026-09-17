using UnityEngine;

namespace Weapon
{
    public class ShellEjector : MonoBehaviour
    {
        [Header("抛壳配置")]
        [SerializeField] private GameObject shellPrefab; // 弹壳Prefab
        [SerializeField] private Transform ejectionPoint; // 抛壳口锚点
        [SerializeField] private float ejectionForce = 3f; // 抛出初速度
        [SerializeField] private float torqueForce = 20f; // 旋转冲量

        [Header("枪口特效配置")]
        [SerializeField] private ParticleSystem muzzleFlash; // 枪口粒子
        [SerializeField] private Light muzzleLight; // 枪口动态光
        [SerializeField] private float lightDuration = 0.03f; //动态光持续时间

        private float _lightTimer;

        private void Update()
        {
            //处理枪口闪光平滑熄灭
            if(muzzleLight != null && muzzleLight.enabled)
            {
                _lightTimer -= Time.deltaTime;
                if(_lightTimer <= 0)
                {
                    muzzleLight.enabled = false;
                }
            }
        }
    
        public void TriggerEffects()
        {
            TriggerEjectEffects();
        }

        public void TriggerEjectEffects()
        {
            if (muzzleFlash != null)
            {
                muzzleFlash.Play();
            }

            if (muzzleLight != null)
            {
                muzzleLight.enabled = true;
                _lightTimer = lightDuration;
            }

            if (shellPrefab == null || ejectionPoint == null)
            {
                return;
            }

            GameObject shell = Instantiate(shellPrefab, ejectionPoint.position, ejectionPoint.rotation);
            if (!shell.TryGetComponent(out Rigidbody rb))
            {
                return;
            }

            Vector3 ejectDir = ejectionPoint.forward + Random.insideUnitSphere * 0.1f;
            rb.AddForce(ejectDir.normalized * ejectionForce, ForceMode.Impulse);

            Vector3 randomTorque = new Vector3(
                Random.Range(-torqueForce, torqueForce),
                Random.Range(-torqueForce, torqueForce),
                Random.Range(-torqueForce, torqueForce));
            rb.AddTorque(randomTorque, ForceMode.Impulse);
        }
    }
}