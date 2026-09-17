using UnityEngine;

namespace Weapon
{
    public class ProceduralRecoil : MonoBehaviour
    {
        [Header("核心引用")]
        [SerializeField] Transform weaponHolder;
        [SerializeField] WeaponADS weaponADS;

        [Header("枪械后坐力(Weapon Kick)- 腰射")]
        [SerializeField] Vector3 hipWeaponRecoilRot = new Vector3(-5f, 2f, 2f);
        [SerializeField] Vector3 hipWeaponRecoilPos = new Vector3(0f, -0.02f, -0.1f);

        [Header("枪械后坐力(Weapon Kick)- 机瞄(ADS)")]
        [SerializeField] Vector3 adsWeaponRecoilRot = new Vector3(-2.5f, 0.8f, 0.8f);
        [SerializeField] Vector3 adsWeaponRecoilPos = new Vector3(0f, -0.01f, -0.04f);

        [Header("镜头后坐力(Camera Kick)")]
        [SerializeField] Vector2 cameraRecoilPitch = new Vector2(-2f, -3f);
        [SerializeField] Vector2 cameraRecoilYaw = new Vector2(-1f, 1f);

        [Header("物理平滑与恢复参数")]
        [SerializeField] float snappiness = 20f;
        [SerializeField] float returnSpeed = 10f;

        Vector3 _currentWeaponRot;
        Vector3 _targetWeaponRot;
        Vector3 _currentWeaponPos;
        Vector3 _targetWeaponPos;
        Vector2 _currentCameraRecoil;
        Vector2 _targetCameraRecoil;

        /// <summary>
        /// 给 FirstPersonLook 叠加的镜头偏移。x = Pitch，y = Yaw。独立衰减，不写进玩家鼠标角度。
        /// </summary>
        public Vector2 CameraRecoilOffset => _currentCameraRecoil;

        void Awake()
        {
            if (weaponADS == null)
            {
                weaponADS = GetComponent<WeaponADS>();
                if (weaponADS == null)
                {
                    weaponADS = GetComponentInParent<WeaponADS>();
                }
            }
        }

        void Update()
        {
            _targetWeaponRot = Vector3.Lerp(_targetWeaponRot, Vector3.zero, returnSpeed * Time.deltaTime);
            _targetWeaponPos = Vector3.Lerp(_targetWeaponPos, Vector3.zero, returnSpeed * Time.deltaTime);
            _targetCameraRecoil = Vector2.Lerp(_targetCameraRecoil, Vector2.zero, returnSpeed * Time.deltaTime);

            _currentWeaponRot = Vector3.Slerp(_currentWeaponRot, _targetWeaponRot, snappiness * Time.deltaTime);
            _currentWeaponPos = Vector3.Lerp(_currentWeaponPos, _targetWeaponPos, snappiness * Time.deltaTime);
            _currentCameraRecoil = Vector2.Lerp(_currentCameraRecoil, _targetCameraRecoil, snappiness * Time.deltaTime);
        }

        void LateUpdate()
        {
            if (weaponHolder == null)
            {
                return;
            }

            // ADS 在 Update 里写好腰射/机瞄姿态，这里只叠加后坐力，避免互相覆盖。
            weaponHolder.localRotation *= Quaternion.Euler(_currentWeaponRot);
            weaponHolder.localPosition += _currentWeaponPos;
        }

        public void ApplyRecoil()
        {
            bool isAiming = weaponADS != null && weaponADS.IsAiming;

            Vector3 rotKick = isAiming ? adsWeaponRecoilRot : hipWeaponRecoilRot;
            Vector3 posKick = isAiming ? adsWeaponRecoilPos : hipWeaponRecoilPos;

            float randomYaw = Random.Range(-rotKick.y, rotKick.y);
            float randomRoll = Random.Range(-rotKick.z, rotKick.z);

            _targetWeaponRot += new Vector3(rotKick.x, randomYaw, randomRoll);
            _targetWeaponPos += new Vector3(Random.Range(-posKick.x, posKick.x), posKick.y, posKick.z);

            float camPitch = Random.Range(cameraRecoilPitch.x, cameraRecoilPitch.y);
            float camYaw = Random.Range(cameraRecoilYaw.x, cameraRecoilYaw.y);
            if (isAiming)
            {
                camPitch *= 0.5f;
                camYaw *= 0.5f;
            }

            _targetCameraRecoil += new Vector2(camPitch, camYaw);
        }
    }
}
