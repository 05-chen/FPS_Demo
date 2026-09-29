using Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 出生传送、贴地与虚空回收。
/// </summary>
public sealed class PlayerSpawnState
{
    readonly Transform _transform;
    readonly CharacterController _characterController;
    readonly CharacterMotor _motor;
    readonly NetworkTransform _networkTransform;
    readonly ClientNetworkTransform _clientNetworkTransform;
    readonly float _voidY;
    readonly float _groundProbeUp;
    readonly float _groundProbeDown;
    readonly Vector3 _redSpawnFallback;
    readonly Vector3 _blueSpawnFallback;

    Vector3 _lastSpawnPosition;
    Quaternion _lastSpawnRotation = Quaternion.identity;
    bool _hasLastSpawnPose;

    public PlayerSpawnState(
        Transform transform,
        CharacterController characterController,
        CharacterMotor motor,
        NetworkTransform networkTransform,
        ClientNetworkTransform clientNetworkTransform,
        float voidY,
        float groundProbeUp,
        float groundProbeDown,
        Vector3 redSpawnFallback,
        Vector3 blueSpawnFallback)
    {
        _transform = transform;
        _characterController = characterController;
        _motor = motor;
        _networkTransform = networkTransform;
        _clientNetworkTransform = clientNetworkTransform;
        _voidY = voidY;
        _groundProbeUp = groundProbeUp;
        _groundProbeDown = groundProbeDown;
        _redSpawnFallback = redSpawnFallback;
        _blueSpawnFallback = blueSpawnFallback;
    }

    public void TeleportToSpawn(Vector3 position, Quaternion rotation, bool isSpawned)
    {
        Vector3 grounded = SnapToGround(position);
        _lastSpawnPosition = grounded;
        _lastSpawnRotation = rotation;
        _hasLastSpawnPose = true;

        bool wasEnabled = _characterController != null && _characterController.enabled;
        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        if (_clientNetworkTransform != null)
        {
            _clientNetworkTransform.TeleportToSpawn(grounded, rotation);
        }
        else
        {
            _transform.SetPositionAndRotation(grounded, rotation);
            if (_networkTransform != null && isSpawned && _networkTransform.CanCommitToTransform)
            {
                _networkTransform.Teleport(grounded, rotation, _transform.localScale);
            }
        }

        _motor?.ResetVertical();
        if (_characterController != null)
        {
            _characterController.enabled = wasEnabled;
        }
    }

    public void TeleportCharacter(Vector3 position, bool isSpawned)
    {
        bool wasEnabled = _characterController != null && _characterController.enabled;
        if (_characterController != null)
        {
            _characterController.enabled = false;
        }

        _transform.position = position;
        _motor?.ResetVertical();

        if (_characterController != null)
        {
            _characterController.enabled = wasEnabled;
        }

        if (_networkTransform == null || !isSpawned || !_networkTransform.CanCommitToTransform)
        {
            return;
        }

        _networkTransform.Teleport(position, _transform.rotation, _transform.localScale);
    }

    public bool ShouldRecoverFromVoid(bool isDead)
    {
        if (isDead)
        {
            return false;
        }

        return _transform.position.y <= _voidY;
    }

    public void ResolveVoidRecoveryPose(TeamId team, TeamId resolvedTeam, out Vector3 spawn, out Quaternion rotation)
    {
        if (TryResolveTeamSpawnPose(resolvedTeam, resolvedTeam, out spawn, out rotation))
        {
            return;
        }

        spawn = _hasLastSpawnPose
            ? _lastSpawnPosition
            : (team == TeamId.Blue ? _blueSpawnFallback : _redSpawnFallback);
        rotation = _hasLastSpawnPose ? _lastSpawnRotation : _transform.rotation;
    }

    /// <param name="currentResolvedTeam">当前已解析阵营；仅当与请求阵营一致时才使用缓存出生点。</param>
    public bool TryResolveTeamSpawnPose(
        TeamId team,
        TeamId currentResolvedTeam,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!TeamIdUtil.IsPlayable(team))
        {
            return false;
        }

        if (Managers.SpawnManager.Instance != null &&
            Managers.SpawnManager.Instance.TryGetSpawnPose(team, out position, out rotation))
        {
            return true;
        }

        if (_hasLastSpawnPose && currentResolvedTeam == team)
        {
            position = _lastSpawnPosition;
            rotation = _lastSpawnRotation;
            return true;
        }

        return false;
    }

    public void GetPrefabFallbackSpawn(TeamId team, out Vector3 position, out Quaternion rotation)
    {
        position = team == TeamId.Blue ? _blueSpawnFallback : _redSpawnFallback;
        rotation = _transform.rotation;
    }

    public Vector3 SnapToGround(Vector3 position)
    {
        Vector3 origin = position + Vector3.up * _groundProbeUp;
        float distance = _groundProbeUp + _groundProbeDown;
        int mask = ~(_transform != null ? 1 << _transform.gameObject.layer : 0);

        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            return position;
        }

        float extra = 1f;
        if (_characterController != null)
        {
            float scaleY = _transform.lossyScale.y;
            float bottomOffset =
                (_characterController.center.y - _characterController.height * 0.5f) * scaleY;
            extra = -bottomOffset + _characterController.skinWidth * scaleY;
        }

        return new Vector3(position.x, hit.point.y + extra, position.z);
    }
}
