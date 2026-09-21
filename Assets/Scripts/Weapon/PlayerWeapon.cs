using System;
using Unity.Netcode;
using UnityEngine;
using Enemy;
using Weapon;

public enum WeaponType
{
    Hitscan,   // 原本的射线枪
    Projectile // 新的实体弹道枪
}

public class PlayerWeapon : NetworkBehaviour
{
    [Header("Weapon Modes")]
    [SerializeField] private WeaponType currentType = WeaponType.Hitscan;

    [Header("Shared Settings")]
    [SerializeField] private float fireRate = 0.2f;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private AudioClip gunSound;

    [Header("Hitscan Settings")]
    [SerializeField] private float fireRange = 100f;
    [SerializeField] private int hitscanDamage = 20;
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Projectile Settings")]
    [SerializeField] private GameObject bulletPrefab;

    [Header("后坐力")]
    [SerializeField] ProceduralRecoil proceduralRecoil;

    [Header("抛壳与枪口")]
    [SerializeField] ShellEjector shellEjector;

    private float _nextFireTime;

    public Vector2 CameraRecoilOffset =>
        proceduralRecoil != null ? proceduralRecoil.CameraRecoilOffset : Vector2.zero;

    /// <summary>本帧实际开了一枪（已过冷却）时触发，供动画层拉 Fire Trigger。</summary>
    public event Action Fired;

    /// <summary>换弹流程开始时触发；当前无完整换弹系统，由 NotifyReload 显式抛出。</summary>
    public event Action Reloaded;

    void Awake()
    {
        if (proceduralRecoil == null)
        {
            proceduralRecoil = GetComponent<ProceduralRecoil>();
            if (proceduralRecoil == null)
            {
                proceduralRecoil = GetComponentInChildren<ProceduralRecoil>(true);
            }
        }

        if (shellEjector == null)
        {
            shellEjector = GetComponent<ShellEjector>();
            if (shellEjector == null)
            {
                shellEjector = GetComponentInChildren<ShellEjector>(true);
            }
        }
    }

    /// <summary>
    /// NGO 仅在 NetworkObject 已 Spawn 且网络在监听时才会真正执行 ServerRpc。
    /// 单机练习的场景玩家通常未 Spawn，必须走本地权威分支。
    /// </summary>
    bool CanSendWeaponRpc =>
        IsSpawned &&
        NetworkManager.Singleton != null &&
        NetworkManager.Singleton.IsListening;

    ulong ShooterClientId =>
        IsSpawned ? OwnerClientId : 0;

    public void PerformShoot(GameplayInputState input)
    {
        // 武器切换必须位于最顶部，不受开火输入与冷却时间影响
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            currentType = WeaponType.Hitscan;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            currentType = WeaponType.Projectile;
        }

        if (!input.FirePressed || Time.time < _nextFireTime) return;
        _nextFireTime = Time.time + fireRate;
        PlayMuzzleSound();
        proceduralRecoil?.ApplyRecoil();
        shellEjector?.TriggerEjectEffects();
        // 动画与射击解耦：只通知「开了一枪」，不反向依赖 Animator。
        Fired?.Invoke();

        switch (currentType)
        {
            case WeaponType.Hitscan:
                ShootHitscan();
                break;

            case WeaponType.Projectile:
                ShootProjectile();
                break;
        }
    }

    /// <summary>
    /// 轻量换弹通知入口。完整换弹逻辑落地后可在开始换弹处改调这里。
    /// </summary>
    public void NotifyReload()
    {
        Reloaded?.Invoke();
    }

    void PlayMuzzleSound()
    {
        if (IsSpawned && !IsOwner)
        {
            return;
        }

        AudioSource muzzleSource = null;
        if (playerCamera != null)
        {
            muzzleSource = playerCamera.GetComponent<AudioSource>();
            if (muzzleSource == null)
            {
                muzzleSource = playerCamera.gameObject.AddComponent<AudioSource>();
            }

            muzzleSource.enabled = true;
            muzzleSource.mute = false;
            muzzleSource.playOnAwake = false;
            muzzleSource.loop = false;
            muzzleSource.spatialBlend = 0f;
            muzzleSource.volume = 1f;
            muzzleSource.ignoreListenerPause = true;
        }

        AudioListener.pause = false;
        AudioListener.volume = 1f;
        HitEffectSpawner.EnsureInstance()?.PlayFireSound(muzzleSource, gunSound);
    }

    private void ShootHitscan()
    {
        if (playerCamera == null)
        {
            Debug.LogError("[PlayerWeapon] playerCamera 未赋值，无法射击！");
            return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        if (CanSendWeaponRpc)
        {
            SubmitHitscanServerRpc(ray.origin, ray.direction);
        }
        else
        {
            ExecuteHitscan(ray.origin, ray.direction);
        }
    }

    [ServerRpc]
    private void SubmitHitscanServerRpc(Vector3 origin, Vector3 direction)
    {
        ExecuteHitscan(origin, direction);
    }

    // 手臂分上臂/前臂/手多节后，一根射线可能沿肢体命中多次，留够槽位避免溢出丢命中。
    static readonly RaycastHit[] HitscanHits = new RaycastHit[32];

    void ExecuteHitscan(Vector3 origin, Vector3 direction)
    {
        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction,
            HitscanHits,
            fireRange,
            hitLayers,
            // 远端玩家关掉了 CharacterController，部位盒是 Trigger；Ignore 会导致房主打不中客户端。
            QueryTriggerInteraction.Collide);

        int valid = 0;
        for (int i = 0; i < hitCount; i++)
        {
            if (IsSelfCollider(HitscanHits[i].collider) || BodyPartHitbox.IsInvisibleTrigger(HitscanHits[i].collider))
            {
                continue;
            }

            HitscanHits[valid] = HitscanHits[i];
            valid++;
        }

        if (valid <= 0)
        {
            return;
        }

        BodyPartHitbox hitbox = BodyPartHitbox.PickBestAlongRay(HitscanHits, valid);
        bool isHeadshot = hitbox != null && hitbox.bodyPart == DetailedBodyPart.Head;

        // 真人身上没有 PracticeDummyHealth，只有假人才需要走 TakeHit 的伤情反馈，避免在 Player 上刷警告。
        if (hitbox != null && hitbox.GetComponentInParent<PracticeDummyHealth>() != null)
        {
            hitbox.TakeHit("Hitscan");
        }

        if (TryGetHitscanEffectHit(hitbox, valid, out RaycastHit effectHit))
        {
            BroadcastOrSpawnHitVfx(effectHit.point, effectHit.normal, isHeadshot);
        }

        ApplyHitscanPlayerDamage(valid, hitbox);
    }

    void BroadcastOrSpawnHitVfx(Vector3 point, Vector3 normal, bool isHeadshot)
    {
        if (CanSendWeaponRpc)
        {
            BroadcastHitVfxClientRpc(point, normal, isHeadshot);
            return;
        }

        HitEffectSpawner.EnsureInstance()?.SpawnHitEffectAt(point, normal, isHeadshot);
    }

    [ClientRpc]
    void BroadcastHitVfxClientRpc(Vector3 point, Vector3 normal, bool isHeadshot)
    {
        HitEffectSpawner.EnsureInstance()?.SpawnHitEffectAt(point, normal, isHeadshot);
    }

    bool IsSelfCollider(Collider col)
    {
        if (col == null)
        {
            return true;
        }

        Transform hitTransform = col.transform;
        return hitTransform == transform || hitTransform.IsChildOf(transform);
    }

    static bool TryGetHitscanEffectHit(BodyPartHitbox hitbox, int hitCount, out RaycastHit effectHit)
    {
        effectHit = default;
        if (hitCount <= 0)
        {
            return false;
        }

        if (hitbox != null)
        {
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = HitscanHits[i].collider;
                if (col == null)
                {
                    continue;
                }

                BodyPartHitbox box = col.GetComponentInParent<BodyPartHitbox>();
                if (box != hitbox)
                {
                    continue;
                }

                if (HitscanHits[i].distance < bestDistance)
                {
                    bestDistance = HitscanHits[i].distance;
                    effectHit = HitscanHits[i];
                    found = true;
                }
            }

            if (found)
            {
                return true;
            }
        }

        int closest = 0;
        for (int i = 1; i < hitCount; i++)
        {
            if (HitscanHits[i].distance < HitscanHits[closest].distance)
            {
                closest = i;
            }
        }

        effectHit = HitscanHits[closest];
        return effectHit.collider != null;
    }

    void ApplyHitscanPlayerDamage(int hitCount, BodyPartHitbox bestHitbox)
    {
        float closest = float.MaxValue;
        PlayerHealth closestHealth = null;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = HitscanHits[i];
            if (hit.collider == null)
            {
                continue;
            }

            PlayerHealth targetHealth = hit.collider.GetComponentInParent<PlayerHealth>();
            if (targetHealth == null || targetHealth.gameObject == gameObject)
            {
                continue;
            }

            if (hit.distance < closest)
            {
                closest = hit.distance;
                closestHealth = targetHealth;
            }
        }

        if (closestHealth == null)
        {
            return;
        }

        // 复用 PickBestAlongRay 选出的最佳部位结论，不再用「最近命中」重算爆头；
        // 同时确认该 Hitbox 属于同一个受击者，避免射线穿透多人时把他人的头算到当前目标身上。
        bool instantKill = bestHitbox != null
            && bestHitbox.bodyPart == DetailedBodyPart.Head
            && bestHitbox.GetComponentInParent<PlayerHealth>() == closestHealth;

        if (bestHitbox != null && bestHitbox.GetComponentInParent<PlayerHealth>() == closestHealth)
        {
            closestHealth.TakeDamage(hitscanDamage, bestHitbox.bodyPart);
            return;
        }

        closestHealth.TakeDamage(hitscanDamage, instantKill);
    }

    private void ShootProjectile()
    {
        if (playerCamera == null)
        {
            Debug.LogError("[PlayerWeapon] playerCamera 未赋值，无法射击！");
            return;
        }

        Vector3 spawnPos = muzzlePoint != null
            ? muzzlePoint.position
            : playerCamera.transform.position + playerCamera.transform.forward;
        Quaternion spawnRot = Quaternion.LookRotation(playerCamera.transform.forward);

        if (CanSendWeaponRpc)
        {
            SpawnProjectileServerRpc(spawnPos, spawnRot);
        }
        else
        {
            SpawnProjectileAuthoritative(spawnPos, spawnRot);
        }
    }

    [ServerRpc]
    private void SpawnProjectileServerRpc(Vector3 position, Quaternion rotation)
    {
        SpawnProjectileAuthoritative(position, rotation);
    }

    void SpawnProjectileAuthoritative(Vector3 position, Quaternion rotation)
    {
        if (bulletPrefab == null)
        {
            Debug.LogError("[PlayerWeapon] bulletPrefab 未赋值，无法生成子弹！");
            return;
        }

        GameObject bulletInstance = Instantiate(bulletPrefab, position, rotation);
        if (bulletInstance == null)
        {
            Debug.LogError("[PlayerWeapon] Instantiate bulletPrefab 失败！");
            return;
        }

        if (!bulletInstance.TryGetComponent<NetworkObject>(out NetworkObject netObj))
        {
            Debug.LogError("[PlayerWeapon] bulletInstance 缺少 NetworkObject 组件！");
            Destroy(bulletInstance);
            return;
        }

        if (CanSendWeaponRpc)
        {
            netObj.Spawn();
        }

        if (bulletInstance.TryGetComponent<Projectile>(out var projectile))
        {
            projectile.Initialize(ShooterClientId, Vector3.zero, gameObject);
        }
    }
}
