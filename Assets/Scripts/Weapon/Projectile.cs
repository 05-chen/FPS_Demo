using UnityEngine;
using Unity.Netcode;
using System.Collections;
using Enemy;
using Weapon;

/// <summary>
/// 实体弹：用上一帧到这一帧的连续射线扫描路径，避免高速近距离穿模。
/// 重力与寿命在脚本里算，不再依赖刚体离散碰撞。
/// </summary>
public class Projectile : NetworkBehaviour
{
    // 与 Hitscan 同容量：多节肢体 Hitbox 会让单条扫描出现更多命中。
    static readonly RaycastHit[] SweepHits = new RaycastHit[32];

    [Header("Projectile Settings")]
    [SerializeField] float speed = 50f;
    [SerializeField] float lifeTime = 3f;
    [SerializeField] int damage = 25;
    [SerializeField] float shooterIgnoreSeconds = 0.1f;
    [SerializeField] float gravityMultiplier = 1f;
    [SerializeField] LayerMask hitLayers = ~0;
    [SerializeField] GameObject hitEffectPrefab;

    [Header("Components")]
    [SerializeField] Rigidbody rb;

    Vector3 _previousPosition;
    Vector3 _velocity;
    ulong _shooterClientId;
    GameObject _shooterRoot;
    float _spawnTime;
    bool _consumed;

    /// <summary>联网时在服务端模拟；单机未 Host 时在本地实例上模拟。</summary>
    bool HasSimAuthority
    {
        get
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return true;
            }

            return IsServer;
        }
    }

    void Awake()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;
        }

        Collider selfCollider = GetComponent<Collider>();
        if (selfCollider != null)
        {
            selfCollider.enabled = false;
        }

        _spawnTime = Time.time;
        _previousPosition = transform.position;
    }

    public void Initialize(ulong shooterClientId, Vector3 launchVelocity, GameObject shooterRoot = null)
    {
        _shooterClientId = shooterClientId;
        _shooterRoot = shooterRoot;
        _spawnTime = Time.time;
        _consumed = false;
        _previousPosition = transform.position;
        _velocity = launchVelocity + transform.forward * speed;
    }

    void Start()
    {
        _previousPosition = transform.position;
        if (HasSimAuthority)
        {
            StartCoroutine(DespawnAfterLifeTime());
        }
    }

    void FixedUpdate()
    {
        if (!HasSimAuthority || _consumed)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        _velocity += Physics.gravity * gravityMultiplier * deltaTime;
        Vector3 newPosition = _previousPosition + _velocity * deltaTime;
        Vector3 direction = newPosition - _previousPosition;
        float distance = direction.magnitude;
        if (distance <= 0.0001f)
        {
            return;
        }

        Vector3 travel = direction / distance;
        if (TrySweepHit(_previousPosition, travel, distance, out RaycastHit hit, out BodyPartHitbox hitbox))
        {
            transform.position = hit.point;
            _previousPosition = hit.point;
            ProcessHit(hit, hitbox);
            return;
        }

        ApplyPosition(newPosition);
        _previousPosition = newPosition;
        if (_velocity.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(_velocity.normalized, Vector3.up);
        }
    }

    bool TrySweepHit(Vector3 origin, Vector3 travel, float distance, out RaycastHit hit, out BodyPartHitbox hitbox)
    {
        hit = default;
        hitbox = null;
        int count = Physics.RaycastNonAlloc(
            origin,
            travel,
            SweepHits,
            distance,
            hitLayers,
            // 与 Hitscan 一致：必须打到 Trigger 部位盒，否则 Owner 权威下远端 CC 关闭时打不中。
            QueryTriggerInteraction.Collide);

        int valid = 0;
        for (int i = 0; i < count; i++)
        {
            if (ShouldIgnoreCollider(SweepHits[i].collider) || BodyPartHitbox.IsInvisibleTrigger(SweepHits[i].collider))
            {
                continue;
            }

            SweepHits[valid] = SweepHits[i];
            valid++;
        }

        if (valid <= 0)
        {
            return false;
        }

        BodyPartHitbox bestHitbox = BodyPartHitbox.PickBestAlongRay(SweepHits, valid);
        if (bestHitbox != null)
        {
            // 直接把最佳 Hitbox 的自有命中点带出去，特效坐标便落在真实皮肤上，而不是 CharacterController 表面。
            hitbox = bestHitbox;
            return TryFindHitForHitbox(bestHitbox, valid, out hit);
        }

        int closest = 0;
        for (int i = 1; i < valid; i++)
        {
            if (SweepHits[i].distance < SweepHits[closest].distance)
            {
                closest = i;
            }
        }

        hit = SweepHits[closest];
        return true;
    }

    static bool TryFindHitForHitbox(BodyPartHitbox hitbox, int valid, out RaycastHit hit)
    {
        hit = default;
        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < valid; i++)
        {
            BodyPartHitbox box = SweepHits[i].collider.GetComponentInParent<BodyPartHitbox>();
            if (box != hitbox)
            {
                continue;
            }

            if (SweepHits[i].distance < bestDistance)
            {
                bestDistance = SweepHits[i].distance;
                hit = SweepHits[i];
                found = true;
            }
        }

        return found;
    }

    void ProcessHit(RaycastHit hit, BodyPartHitbox hitbox)
    {
        bool isHeadshot = hitbox != null && hitbox.bodyPart == DetailedBodyPart.Head;
        Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;

        BroadcastOrSpawnHitVfx(hit.point, normal, isHeadshot);

        string colliderName = hit.collider != null ? hit.collider.name : "null";
        Debug.Log($"[HitCheck] 打中了物体: {colliderName}, 是否找到HitEffectSpawner: {HitEffectSpawner.Instance != null}");

        // 真人身上没有 PracticeDummyHealth，只有假人才需要走 OnHit 的伤情反馈，避免在 Player 上刷警告。
        if (hitbox != null && hitbox.GetComponentInParent<PracticeDummyHealth>() != null)
        {
            hitbox.OnHit(damage);
        }

        PlayerHealth targetHealth = hit.collider != null
            ? hit.collider.GetComponentInParent<PlayerHealth>()
            : null;
        if (targetHealth != null && !IsShooter(targetHealth))
        {
            targetHealth.TakeDamage(damage, isHeadshot);
        }

        DespawnSafely();
    }

    void BroadcastOrSpawnHitVfx(Vector3 point, Vector3 normal, bool isHeadshot)
    {
        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            NetworkObject != null &&
            NetworkObject.IsSpawned)
        {
            BroadcastHitVfxClientRpc(point, normal, isHeadshot);
            return;
        }

        HitEffectSpawner.EnsureInstance()?.SpawnHitEffectAt(point, normal, isHeadshot);
        SpawnLocalHitPrefab(point, normal);
    }

    [ClientRpc]
    void BroadcastHitVfxClientRpc(Vector3 point, Vector3 normal, bool isHeadshot)
    {
        HitEffectSpawner.EnsureInstance()?.SpawnHitEffectAt(point, normal, isHeadshot);
        SpawnLocalHitPrefab(point, normal);
    }

    void SpawnLocalHitPrefab(Vector3 point, Vector3 normal)
    {
        if (hitEffectPrefab == null)
        {
            return;
        }

        Quaternion rotation = normal.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(normal)
            : Quaternion.identity;
        Destroy(Instantiate(hitEffectPrefab, point, rotation), 2f);
    }

    void ApplyPosition(Vector3 position)
    {
        if (rb != null)
        {
            rb.position = position;
        }

        transform.position = position;
    }

    bool ShouldIgnoreCollider(Collider col)
    {
        if (col == null)
        {
            return true;
        }

        Transform hitTransform = col.transform;
        if (hitTransform == transform || hitTransform.IsChildOf(transform))
        {
            return true;
        }

        if (_shooterRoot == null || Time.time - _spawnTime >= shooterIgnoreSeconds)
        {
            return false;
        }

        return hitTransform == _shooterRoot.transform || hitTransform.IsChildOf(_shooterRoot.transform);
    }

    IEnumerator DespawnAfterLifeTime()
    {
        yield return new WaitForSeconds(lifeTime);
        DespawnSafely();
    }

    bool IsShooter(PlayerHealth targetHealth)
    {
        if (targetHealth == null)
        {
            return false;
        }

        if (_shooterRoot != null && targetHealth.gameObject == _shooterRoot)
        {
            return true;
        }

        return _shooterClientId != 0 && targetHealth.OwnerClientId == _shooterClientId;
    }

    void DespawnSafely()
    {
        if (_consumed)
        {
            return;
        }

        _consumed = true;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            NetworkObject != null &&
            NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
            return;
        }

        Destroy(gameObject);
    }
}
