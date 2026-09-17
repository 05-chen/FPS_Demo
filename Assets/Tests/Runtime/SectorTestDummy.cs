using System.Collections.Generic;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 黑盒物理测试用假人。挂在带 CharacterController + Player 标签的根节点上，不走 NGO Spawn。
    /// </summary>
    public sealed class SectorTestDummy : MonoBehaviour
    {
        static readonly HashSet<ulong> TrackedIds = new HashSet<ulong>();

        [SerializeField] ulong clientId = 9001;

        public ulong ClientId => clientId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => TrackedIds.Clear();

        /// <summary>占领计分遇到未联网的 Dummy Id 时不要当幽灵清掉。</summary>
        public static bool IsTracked(ulong id) => TrackedIds.Contains(id);

        void OnEnable()
        {
            if (clientId != 0)
            {
                TrackedIds.Add(clientId);
            }
        }

        void OnDisable()
        {
            TrackedIds.Remove(clientId);
        }

        /// <summary>测试工厂：指定假人 ClientId。</summary>
        public void AssignClientId(ulong id)
        {
            TrackedIds.Remove(clientId);
            clientId = id;
            if (isActiveAndEnabled && clientId != 0)
            {
                TrackedIds.Add(clientId);
            }
        }
    }
}
