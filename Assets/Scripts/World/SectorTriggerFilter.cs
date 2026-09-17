using Unity.Netcode;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 战区 Trigger 统一过滤：只认玩家根节点 CharacterController，忽略枪模 / 子弹 / Hitbox。
    /// </summary>
    public static class SectorTriggerFilter
    {
        /// <summary>
        /// 判断碰撞体是否可作为占领人头。成功时写出 clientId。
        /// </summary>
        public static bool TryGetPlayerClientId(Collider other, out ulong clientId)
        {
            clientId = 0;
            if (other == null || !other.CompareTag("Player"))
            {
                return false;
            }

            if (other is CharacterController)
            {
                PlayerController player = other.GetComponent<PlayerController>();
                if (player != null)
                {
                    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                    {
                        return false;
                    }

                    if (player.NetworkObject == null || !player.NetworkObject.IsSpawned)
                    {
                        return false;
                    }

                    clientId = player.OwnerClientId;
                    return true;
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (other.TryGetComponent(out SectorTestDummy dummyFromController))
                {
                    clientId = dummyFromController.ClientId;
                    return true;
                }
#endif
            }

#if UNITY_EDITOR
            // EditMode 单测用 CapsuleCollider 代替 CharacterController，避免 ShouldRunBehaviour。
            if (!Application.isPlaying && other.TryGetComponent(out SectorTestDummy dummy))
            {
                clientId = dummy.ClientId;
                return true;
            }
#endif
            return false;
        }
    }
}
