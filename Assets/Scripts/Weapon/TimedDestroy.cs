using UnityEngine;

namespace Weapon
{
    /// <summary>
    /// 到时销毁自身。挂在弹壳等短寿命特效预制体上即可。
    /// </summary>
    public class TimedDestroy : MonoBehaviour
    {
        [SerializeField] float lifeTime = 3f;

        void Start()
        {
            Destroy(gameObject, lifeTime);
        }
    }
}
