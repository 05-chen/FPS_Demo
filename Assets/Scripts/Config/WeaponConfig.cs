using UnityEngine;

namespace Config
{
    /// <summary>
    /// 武器默认参数。可挂到 PlayerWeapon，或由目录加载后作为全局默认。
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "FPS/Config/Weapon Config", order = 12)]
    public sealed class WeaponConfig : ScriptableObject
    {
        [Header("射击")]
        [Min(0.01f)]
        [SerializeField] float fireRate = 0.2f;
        [Min(1f)]
        [SerializeField] float fireRange = 100f;
        [Min(1)]
        [SerializeField] int hitscanDamage = 20;

        public float FireRate => Mathf.Max(0.01f, fireRate);
        public float FireRange => Mathf.Max(1f, fireRange);
        public int HitscanDamage => Mathf.Max(1, hitscanDamage);
    }
}
