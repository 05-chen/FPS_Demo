using System.Collections.Generic;
using Core;
using Unity.Netcode;
using UnityEngine;

namespace Managers
{
    /// <summary>
    /// 进房后先不生成玩家。选完红/蓝后，才 Instantiate + SpawnAsPlayerObject。
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class SpawnManager : NetworkBehaviour
    {
        public static SpawnManager Instance { get; private set; }

        [Header("阵营复活区域 List (挂载带 BoxCollider 的 Trigger)")]
        [SerializeField] private List<BoxCollider> redSpawnZones = new List<BoxCollider>();
        [SerializeField] private List<BoxCollider> blueSpawnZones = new List<BoxCollider>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ConfigureDelayedPlayerSpawn(NetworkManager.Singleton);
        }

        /// <summary>
        /// 必须在 StartHost / StartClient 之前调用：批准进房，但不自动生成 PlayerPrefab。
        /// </summary>
        public static void ConfigureDelayedPlayerSpawn(NetworkManager network)
        {
            if (network == null || network.IsListening)
            {
                return;
            }

            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.EnableSceneManagement = true;
            if (network.ConnectionApprovalCallback == null)
            {
                network.ConnectionApprovalCallback = ApproveWithoutAutoPlayer;
            }
        }

        static void ApproveWithoutAutoPlayer(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = false;
            response.Pending = false;
        }

        /// <summary>
        /// 清掉场景里内置摆放的 Player 占位体。
        /// 场景每次重载都会把预制体实例重新实例化，NGO 的 OnServerLoadedScene 会把它当成
        /// in-scene NetworkObject 生成，于是在真正生成的玩家之外凭空多出一个人。必须在生成玩家之前调用。
        /// </summary>
        public void PurgeScenePlacedPlayers()
        {
            if (!IsServer)
            {
                return;
            }

            NetworkObject[] networkObjects = FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < networkObjects.Length; i++)
            {
                NetworkObject candidate = networkObjects[i];

                // 只有 SpawnAsPlayerObject 生成的真正玩家 IsPlayerObject 才为 true，
                // 场景内置摆放的占位体一律为 false，用这个字段把两者区分开。
                if (candidate == null || candidate.IsPlayerObject || !candidate.TryGetComponent(out PlayerController _))
                {
                    continue;
                }

                GameLog.Warn("Spawn", "清掉场景内置的玩家占位体：" + candidate.name);

                // 已经被 NGO 生成过的占位体只能走 Despawn；直接 Destroy 不会广播销毁消息，客户端会留下幽灵。
                if (candidate.IsSpawned)
                {
                    candidate.Despawn(true);
                }
                else
                {
                    candidate.gameObject.SetActive(false);
                    Destroy(candidate.gameObject);
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void SubmitFactionServerRpc(int teamValue, ServerRpcParams rpcParams = default)
        {
            TeamId team = TeamIdUtil.FromNetwork(teamValue);
            if (!TeamIdUtil.IsPlayable(team))
            {
                GameLog.Warn("Spawn", "非法阵营值：" + teamValue);
                return;
            }

            if (SteamLobbySession.Instance != null)
            {
                SteamLobbySession.Instance.OnClientChoseFaction(rpcParams.Receive.SenderClientId, team);
            }
        }

        /// <summary>
        /// 主机可直接调用。不必先查 ConnectedClients，避免单机点了按钮却静默失败。
        /// </summary>
        public bool SpawnForClient(TeamId team, ulong clientId)
        {
            if (!IsServer)
            {
                GameLog.Warn("Spawn", "只有主机/服务器能生成玩家。");
                return false;
            }

            if (!TeamIdUtil.IsPlayable(team))
            {
                GameLog.Warn("Spawn", "无法为未选择的阵营生成玩家。");
                return false;
            }

            BoxCollider targetZone = GetRandomSpawnZone(team);
            if (targetZone == null)
            {
                GameLog.Warn("Spawn", "没有配置对应阵营的出生区域。");
                return false;
            }

            Vector3 spawnPosition = GetRandomPointInZone(targetZone);
            Quaternion spawnRotation = targetZone.transform.rotation;

            NetworkObject playerNet = null;
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
            {
                playerNet = client.PlayerObject;
            }

            if (playerNet == null)
            {
                playerNet = SpawnPlayerForClient(clientId, spawnPosition, spawnRotation);
                if (playerNet == null)
                {
                    return false;
                }
            }

            PlayerController playerController = playerNet.GetComponent<PlayerController>();
            playerController?.PersistTeam(team);

            PlayerHealth health = playerNet.GetComponent<PlayerHealth>();
            health?.RememberSpawnTeam(team);

            ClientRpcParams targetClient = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            NotifyPlayerSpawnedClientRpc((int)team, spawnPosition, spawnRotation, targetClient);

            if (health != null && health.IsDead)
            {
                health.Revive();
            }

            return true;
        }

        public void RequestFactionSelectForClient(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            ClientRpcParams targetClient = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            ShowFactionSelectClientRpc(targetClient);
        }

        [ClientRpc]
        void ShowFactionSelectClientRpc(ClientRpcParams rpcParams = default)
        {
            // 必须走 SteamLobbyUI：它会 HideLobbyVisuals，否则大厅面板（创建房间/邀请码）会压在选阵营下面。
            if (SteamLobbyUI.Instance != null)
            {
                SteamLobbyUI.Instance.ShowFactionSelectRequestedByServer();
                return;
            }

            GameplayGate.Block();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            UI.FactionSelectUI factionUi = UI.FactionSelectUI.Instance;
            if (factionUi == null)
            {
                factionUi = FindFirstObjectByType<UI.FactionSelectUI>(FindObjectsInactive.Include);
            }

            if (factionUi == null)
            {
                GameLog.Warn("Spawn", "找不到选阵营界面，重连玩家无法选阵营。");
                return;
            }

            factionUi.gameObject.SetActive(true);
            factionUi.ShowUI(true);
        }

        static NetworkObject SpawnPlayerForClient(ulong clientId, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            GameObject prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("[SpawnManager] NetworkManager 未指定 PlayerPrefab，无法生成玩家。");
                return null;
            }

            GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Destroy(instance);
                Debug.LogError("[SpawnManager] PlayerPrefab 缺少 NetworkObject。");
                return null;
            }

            networkObject.SpawnAsPlayerObject(clientId, true);
            ApplySpawnTransform(instance, spawnPosition, spawnRotation);
            return networkObject;
        }

        static void ApplySpawnTransform(GameObject playerObj, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            CharacterController characterController = playerObj.GetComponent<CharacterController>();
            bool wasEnabled = characterController != null && characterController.enabled;
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            playerObj.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            if (characterController != null)
            {
                characterController.enabled = wasEnabled;
            }
        }

        /// <summary>
        /// 随机抽取阵营下属的一个复活区域
        /// </summary>
        private BoxCollider GetRandomSpawnZone(TeamId team)
        {
            List<BoxCollider> list = team == TeamId.Red
                ? redSpawnZones
                : team == TeamId.Blue
                    ? blueSpawnZones
                    : null;
            if (list == null || list.Count == 0)
            {
                return null;
            }

            return list[Random.Range(0, list.Count)];
        }

        /// <summary>
        /// 供 PlayerController 虚空回收 / 阵营传送：从场景区域取点，避免预制体旧坐标。
        /// </summary>
        public bool TryGetSpawnPose(TeamId team, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            BoxCollider zone = GetRandomSpawnZone(team);
            if (zone == null)
            {
                return false;
            }

            position = GetRandomPointInZone(zone);
            rotation = zone.transform.rotation;
            return true;
        }

        /// <summary>
        /// 核心算法：在指定的 BoxCollider 区域内随机生成无碰撞卡嵌的安全坐标
        /// </summary>
        private Vector3 GetRandomPointInZone(BoxCollider zoneCollider)
        {
            Bounds bounds = zoneCollider.bounds;
            Vector3 randomPoint = zoneCollider.transform.position;
            bool validPointFound = false;
            int maxAttempts = 10; // 最多尝试 10 次寻找无阻挡的安全点

            for (int i = 0; i < maxAttempts; i++)
            {
                // 在 BoxCollider 范围内的 X 和 Z 轴随机抽点
                float randomX = Random.Range(bounds.min.x, bounds.max.x);
                float randomZ = Random.Range(bounds.min.z, bounds.max.z);
                
                // Y 轴取区域底部，并抬高 0.5 米防止与地面模型穿插
                float pointY = bounds.min.y + 0.5f; 

                Vector3 candidatePoint = new Vector3(randomX, pointY, randomZ);

                // 服务端物理检测：检查半径 0.8 米球体内是否有障碍物或他人
                if (!Physics.CheckSphere(candidatePoint, 0.8f))
                {
                    randomPoint = candidatePoint;
                    validPointFound = true;
                    break;
                }
            }

            // 若 10 次检测均被占用，回退至区域中心点
            return validPointFound ? randomPoint : bounds.center;
        }

        [ClientRpc]
        private void NotifyPlayerSpawnedClientRpc(int teamValue, Vector3 pos, Quaternion rot, ClientRpcParams rpcParams = default)
        {
            TeamId team = TeamIdUtil.FromNetwork(teamValue);
            UI.CombatStatusUI.Instance?.Hide();

            if (UI.FactionSelectUI.Instance != null)
            {
                UI.FactionSelectUI.Instance.OnSpawnSuccess(team, pos, rot);
            }

            GameplayGate.Release();
            SteamLobbyUI.HideOverviewForGameplay();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}