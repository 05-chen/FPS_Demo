using Core;
using UnityEngine;

namespace World
{
    /// <summary>
    /// 战区占领的纯规则（无 NGO / 无场景依赖），供运行时与白盒单测共用。
    /// </summary>
    public static class SectorCaptureRules
    {
        /// <summary>
        /// 严格邻接链：该阵营能否在本战区推进占领。
        /// 充要条件：左右邻居至少有一个已属于该阵营。不因本区已占领而自动解锁（后方 HQ 受邻接保护）。
        /// </summary>
        /// <param name="leftOwnedByTeam">左邻居是否已被【当前 team】占领。</param>
        /// <param name="rightOwnedByTeam">右邻居是否已被【当前 team】占领。</param>
        public static bool IsActiveFrontlineFor(
            TeamId team,
            TeamId selfOwner,
            bool leftOwnedByTeam,
            bool rightOwnedByTeam)
        {
            if (!TeamIdUtil.IsPlayable(team))
            {
                return false;
            }

            _ = selfOwner;
            return leftOwnedByTeam || rightOwnedByTeam;
        }

        /// <summary>
        /// 按邻居归属判断前线。传入的是邻居当前 Owner（Red/Blue/None），内部再换成「是否己方」。
        /// </summary>
        public static bool IsActiveFrontlineFor(
            TeamId team,
            TeamId selfOwner,
            TeamId leftNeighborOwner,
            TeamId rightNeighborOwner)
        {
            return IsActiveFrontlineFor(
                team,
                selfOwner,
                leftNeighborOwner == team,
                rightNeighborOwner == team);
        }

        /// <summary>红多 / 蓝多 / 人数相等冻结 的顶栏趋势文案。</summary>
        public static string CaptureTrendLabel(int redWeight, int blueWeight)
        {
            if (redWeight > blueWeight)
            {
                return "→ 红方推进";
            }

            if (blueWeight > redWeight)
            {
                return "← 蓝方推进";
            }

            return redWeight > 0 || blueWeight > 0 ? "争夺中" : "无人争夺";
        }

        /// <summary>
        /// 邻居未 Spawn 时必须退回 DefaultOwner，禁止读未同步的 NetworkVariable。
        /// </summary>
        public static bool IsNeighborOwnedBy(
            bool neighborExists,
            bool neighborSpawned,
            TeamId liveOwner,
            TeamId defaultOwner,
            TeamId team)
        {
            if (!neighborExists || !TeamIdUtil.IsPlayable(team))
            {
                return false;
            }

            TeamId owner = neighborSpawned ? liveOwner : defaultOwner;
            return owner == team;
        }

        /// <summary>
        /// 易主规则：只有打满 ±1 才变红/蓝；进度回到 0 绝不清成 None。
        /// </summary>
        public static TeamId ResolveOwnerAfterProgress(TeamId currentOwner, float progress)
        {
            if (progress >= 1f)
            {
                return TeamId.Red;
            }

            if (progress <= -1f)
            {
                return TeamId.Blue;
            }

            return currentOwner;
        }

        /// <summary>
        /// 双方都在核心圈内即为拉锯（人数差决定净速率，相等则僵持）。
        /// </summary>
        public static bool IsContested(int redCount, int blueCount) =>
            redCount > 0 && blueCount > 0;

        /// <summary>
        /// 顶栏是否按「上锁纯色」渲染：中立且不可争夺，或后方保护（朝敌邻居已是己方）。
        /// </summary>
        public static bool IsHudLocked(TeamId owner, bool capturable, bool rearGuarded) =>
            rearGuarded || (owner == TeamId.None && !capturable);

        /// <summary>
        /// 按圈内人头计算占领速率（进度/秒，红为正、蓝为负）。
        /// 传入的必须是人头，不要预先乘 captureWeight。1 个人的速度 V = 1 / captureDuration，
        /// 从 0 打到 ±1 的时间就是 captureDuration（场景里是 15 秒）。两人同点且人数相等则差值为 0，进度暂停。
        /// </summary>
        public static float CalculateCaptureRate(int redCount, int blueCount, float captureDuration)
        {
            int red = Mathf.Max(0, redCount);
            int blue = Mathf.Max(0, blueCount);
            if (red == 0 && blue == 0)
            {
                return 0f;
            }

            // 僵持：人数差为 0，进度锁定不动
            int delta = red - blue;
            float baseSpeed = 1f / Mathf.Max(1f, captureDuration);
            return delta * baseSpeed;
        }

        /// <summary>
        /// 将速率落到进度上。无人且 emptyDecayPerSecond&gt;0 时向 0 回落；否则无人保持不动。
        /// </summary>
        public static float ApplyCaptureTick(
            float currentProgress,
            int redCount,
            int blueCount,
            float captureDuration,
            float deltaTime,
            float emptyDecayPerSecond = 0f)
        {
            int red = Mathf.Max(0, redCount);
            int blue = Mathf.Max(0, blueCount);
            if (red == 0 && blue == 0)
            {
                if (emptyDecayPerSecond <= 0f)
                {
                    return Mathf.Clamp(currentProgress, -1f, 1f);
                }

                float decay = emptyDecayPerSecond * Mathf.Max(0f, deltaTime);
                if (currentProgress > 0f)
                {
                    return Mathf.Max(0f, currentProgress - decay);
                }

                if (currentProgress < 0f)
                {
                    return Mathf.Min(0f, currentProgress + decay);
                }

                return 0f;
            }

            float rate = CalculateCaptureRate(red, blue, captureDuration);
            return Mathf.Clamp(currentProgress + rate * deltaTime, -1f, 1f);
        }

        /// <summary>
        /// 进圈/防守：己方占领区（含 HQ）永远可驻守；否则必须已有进攻解锁。
        /// </summary>
        public static bool CanOccupyOrDefend(TeamId team, TeamId owner, bool unlockedForAttack)
        {
            if (!TeamIdUtil.IsPlayable(team))
            {
                return false;
            }

            return owner == team || unlockedForAttack;
        }

        /// <summary>
        /// 顶栏是否按「曾占领」画红蓝无缝条：打满过 ±1，或当前已有归属。
        /// </summary>
        public static bool UsesCapturedHud(TeamId owner, bool hasBeenCaptured) =>
            hasBeenCaptured || owner != TeamId.None;

        /// <summary>
        /// 灰底仅允许：从未占领，且圈内不是双方对峙（单方或无人）。
        /// 双方都在圈内时，即使从未占领也强制红蓝拼接。
        /// </summary>
        public static bool ShowsUncapturedGray(
            TeamId owner,
            bool hasBeenCaptured,
            int redCount,
            int blueCount)
        {
            if (UsesCapturedHud(owner, hasBeenCaptured) || IsContested(redCount, blueCount))
            {
                return false;
            }

            return true;
        }

        /// <summary>进度打满 ±1 时，历史占领标记必须闩上。</summary>
        public static bool LatchHasBeenCaptured(bool current, float progress) =>
            current || progress >= 1f || progress <= -1f;

        /// <summary>
        /// HUD：中立进度 0 不填色；progress&gt;0 红填，progress&lt;0 蓝填。
        /// </summary>
        public static float ToHudRedFill(float progress) => Mathf.Clamp01(progress);

        /// <summary>蓝方从 0 向 -1 推进时的填充（0～1）。</summary>
        public static float ToHudBlueFill(float progress) => Mathf.Clamp01(-progress);

        /// <summary>
        /// 顶栏双色条（未上锁、无人圈）：从未占领则按 Owner 推断是否露灰。
        /// </summary>
        public static void ToHudBarFills(
            TeamId owner,
            float progress,
            out float redFill,
            out float blueFill,
            out bool showGray)
        {
            ToHudBarFills(owner, progress, UsesCapturedHud(owner, false), false, 0, 0, out redFill, out blueFill, out showGray);
        }

        /// <summary>未传入圈内人数时，按无人圈处理。</summary>
        public static void ToHudBarFills(
            TeamId owner,
            float progress,
            bool hasBeenCaptured,
            bool isLocked,
            out float redFill,
            out float blueFill,
            out bool showGray)
        {
            ToHudBarFills(owner, progress, hasBeenCaptured, isLocked, 0, 0, out redFill, out blueFill, out showGray);
        }

        /// <summary>
        /// 顶栏填充：红条自左向右，蓝条自右向左。
        /// 双方都在圈内：按 CaptureProgress 画红蓝无缝条（人头只影响涨速，不改填色）。
        /// 灰底仅在从未占领且进度仍为 0、且单方/无人时出现。
        /// 上锁已占领：所属阵营纯色。
        /// </summary>
        public static void ToHudBarFills(
            TeamId owner,
            float progress,
            bool hasBeenCaptured,
            bool isLocked,
            int redCount,
            int blueCount,
            out float redFill,
            out float blueFill,
            out bool showGray)
        {
            // 上锁且已有归属：纯色 + 锁，不露灰
            if (isLocked && owner == TeamId.Red)
            {
                redFill = 1f;
                blueFill = 0f;
                showGray = false;
                return;
            }

            if (isLocked && owner == TeamId.Blue)
            {
                redFill = 0f;
                blueFill = 1f;
                showGray = false;
                return;
            }

            // 双方对峙：条跟着真实 CaptureProgress，不要用人头比例临时顶替。
            // 1v1 人数相等时进度本来就会冻住；若改画 50/50，死后切回真实进度会像「被清成灰再从头涨」。
            // 人头只影响速率（CalculateCaptureRate），不影响这一格怎么填色。
            if (IsContested(redCount, blueCount))
            {
                showGray = false;
                redFill = ToHudFillAmount(progress);
                blueFill = 1f - redFill;
                return;
            }

            if (UsesCapturedHud(owner, hasBeenCaptured))
            {
                showGray = false;
                redFill = ToHudFillAmount(progress);
                blueFill = 1f - redFill;
                return;
            }

            // 从未打满过，但已经有拉锯进度：继续用红蓝无缝条，避免死后突然变灰底再从 0 涨。
            if (Mathf.Abs(progress) > 0.0001f)
            {
                showGray = false;
                redFill = ToHudFillAmount(progress);
                blueFill = 1f - redFill;
                return;
            }

            // 从未占领且进度仍在 0：灰底 + 两侧向中间填
            showGray = true;
            redFill = ToHudRedFill(progress);
            blueFill = ToHudBlueFill(progress);
        }

        /// <summary>
        /// HUD 进度条映射：-1 蓝满 → fill 0，0 中立 → 0.5，+1 红满 → 1。
        /// </summary>
        public static float ToHudFillAmount(float progress) => (progress + 1f) * 0.5f;
    }
}
