using UnityEngine;
using Enemy;

public enum DetailedBodyPart
{
    Head,
    Torso,
    Legs,
    Arms
}

[RequireComponent(typeof(Collider))]
public class BodyPartHitbox : MonoBehaviour
{
    public DetailedBodyPart bodyPart = DetailedBodyPart.Torso;

    public void TakeHit(string weaponType)
    {
        Debug.Log($"<color=green>[假人 Hitbox 被击中]</color> HitboxType: {bodyPart} | 武器: {weaponType}");

        PracticeDummyHealth dummyHealth = GetComponentInParent<PracticeDummyHealth>(true);
        if (dummyHealth == null)
        {
            Debug.LogWarning($"[假人 Hitbox] 父级未找到 PracticeDummyHealth，无法把 {bodyPart} 交给假人。");
            return;
        }

        dummyHealth.ApplyHit(bodyPart, weaponType);
    }

    public void OnHit(int damage)
    {
        TakeHit("Projectile");
    }

    /// <summary>
    /// 战区圈、出生区等 Trigger 没有网格，射线打到会在半空生成弹孔。
    /// 只保留部位 Hitbox；墙和地面不是 Trigger，不受影响。
    /// </summary>
    public static bool IsInvisibleTrigger(Collider collider) =>
        collider != null && collider.isTrigger && collider.GetComponentInParent<BodyPartHitbox>() == null;

    /// <summary>
    /// 沿射线选部位。先锁住最近的那个角色，再在他身上取最高优先级：头 &gt; 躯干 &gt; 四肢。
    /// 手臂在前不会让扫描停住，但不会穿透到后面另一个人。
    /// </summary>
    public static BodyPartHitbox PickBestAlongRay(RaycastHit[] hits, int count)
    {
        if (hits == null || count <= 0)
        {
            return null;
        }

        System.Array.Sort(hits, 0, count, DistanceComparer.Instance);

        Transform lockedRoot = null;
        BodyPartHitbox best = null;
        int bestPriority = -1;
        for (int i = 0; i < count; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || col is CharacterController)
            {
                // CharacterController 体积通常大于所有骨骼 Hitbox，会挡在最近处。必须跳过，不能 break。
                continue;
            }

            BodyPartHitbox hitbox = col.GetComponentInParent<BodyPartHitbox>();
            if (hitbox == null)
            {
                // 战区 / 出生区等 Trigger 不是遮挡物：穿透继续找部位盒。
                if (col.isTrigger)
                {
                    continue;
                }

                // 角色/假人身上的衣服、装备等实心盒没有 Hitbox，不能当墙；
                // 否则手臂命中后会被它们挡住，扫不到后面的躯干。
                if (BelongsToDamageableCharacter(col))
                {
                    Transform characterRoot = col.transform.root;
                    if (lockedRoot == null)
                    {
                        lockedRoot = characterRoot;
                    }
                    else if (characterRoot != lockedRoot)
                    {
                        break;
                    }

                    continue;
                }

                // 真正的环境实心碰撞体才终止，避免穿墙打到身后别人。
                break;
            }

            Transform root = hitbox.transform.root;
            if (lockedRoot == null)
            {
                lockedRoot = root;
            }
            else if (root != lockedRoot)
            {
                break;
            }

            int priority = Priority(hitbox.bodyPart);
            if (priority > bestPriority)
            {
                bestPriority = priority;
                best = hitbox;
            }
        }

        return best;
    }

    public static BodyPartHitbox PickBestInOverlaps(Collider[] colliders, int count)
    {
        if (colliders == null || count <= 0)
        {
            return null;
        }

        BodyPartHitbox best = null;
        int bestPriority = -1;
        for (int i = 0; i < count; i++)
        {
            Collider col = colliders[i];
            if (col == null)
            {
                continue;
            }

            BodyPartHitbox hitbox = col.GetComponentInParent<BodyPartHitbox>();
            if (hitbox == null)
            {
                continue;
            }

            int priority = Priority(hitbox.bodyPart);
            if (priority > bestPriority)
            {
                bestPriority = priority;
                best = hitbox;
            }
        }

        return best;
    }

    /// <summary>头 3，躯干 2，手臂和腿 1。数值越大越优先。</summary>
    static int Priority(DetailedBodyPart part)
    {
        switch (part)
        {
            case DetailedBodyPart.Head:
                return 3;
            case DetailedBodyPart.Torso:
                return 2;
            default:
                return 1;
        }
    }

    /// <summary>真人与假人共用的部位扣血。躯干明显高于四肢，避免打中胸口像打空。</summary>
    public static int HitDamage(DetailedBodyPart part)
    {
        switch (part)
        {
            case DetailedBodyPart.Torso:
                return 50;
            case DetailedBodyPart.Legs:
                return 25;
            case DetailedBodyPart.Arms:
                return 15;
            default:
                return 20;
        }
    }

    /// <summary>碰撞体是否挂在可受伤角色（玩家或假人）上。</summary>
    static bool BelongsToDamageableCharacter(Collider collider) =>
        collider != null
        && (collider.GetComponentInParent<PlayerHealth>() != null
            || collider.GetComponentInParent<PracticeDummyHealth>() != null);

    sealed class DistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly DistanceComparer Instance = new DistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
