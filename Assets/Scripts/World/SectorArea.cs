using System.Collections.Generic;
using Core;
using Unity.Netcode;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 战区/据点 Trigger 感知组件。
    /// 挂载在 Trigger Collider 上，仅在服务端维护当前圈内有效玩家并计算阵营权重。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SectorArea : MonoBehaviour
    {
        [Header("据点数据配置")]
        public SectorData sectorData;

        [Header("权重配置")]
        [Tooltip("该区域占领权重(外圈 Sector=1, 核心据点StrongPoint=3)")]
        [SerializeField] private int captureWeight = 1;

        // 仅在服务端维护圈内 ClientId 集合
        private readonly HashSet<ulong> _playerInArea = new();

        public int CaptureWeight => captureWeight;

        private void OnTriggerEnter(Collider other)
        {
            if(TryGetPlayerClientId(other, out ulong clientId))
            {
                _playerInArea.Add(clientId);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if(TryGetPlayerClientId(other, out ulong clientId))
            {
                _playerInArea.Remove(clientId);
            }
        }

        /// <summary>
        /// 玩家断开连接时，由 SectorManager 回调清理
        /// </summary>
        public void OnClientDisconnected(ulong clientId)
        {
            _playerInArea.Remove(clientId);
        }

        /// <summary>
        /// 评估当前区域内处于 Alive 状态的红蓝阵营有效加权战力
        /// </summary>
        public  void EvaluateActiveWeightedPlayers(out int redWeight, out int blueWeight)
        {
            redWeight = 0;
            blueWeight = 0;

            if(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            var ConnectedClients = NetworkManager.Singleton.ConnectedClients;

            foreach(ulong clientId in _playerInArea)
            {
                if(!ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null) continue;

                if (client.PlayerObject.GetComponent<PlayerHealth>() is PlayerHealth health &&
                    health.LifeState == PlayerLifeState.Alive)
                {
                    TeamId team = PlayerRegistry.GetPlayerTeam(clientId);
                    if (team == TeamId.Red)
                    {
                        redWeight += captureWeight;
                    }
                    else if (team == TeamId.Blue)
                    {
                        blueWeight += captureWeight;
                    }
                }
            }
        }

        /// <summary>
        /// 校验 Trigger 碰撞体是否为已 Spawn 的玩家控制器（遵循项目原有 CharacterController 过滤规则）
        /// </summary>
        private static bool TryGetPlayerClientId(Collider other, out ulong clientId)
        {
            clientId = 0;
            if(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || other == null) return false;

            if (other.GetComponent<CharacterController>() != null &&
                other.GetComponentInParent<PlayerController>() is PlayerController player &&
                player.NetworkObject != null &&
                player.NetworkObject.IsSpawned)
            {
                clientId = player.OwnerClientId;
                return true;
            }

            return false;
        }
    }
}