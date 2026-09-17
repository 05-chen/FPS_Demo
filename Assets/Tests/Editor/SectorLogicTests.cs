using Core;
using NUnit.Framework;
using UnityEngine;
using World;

/// <summary>
/// 白盒：占领规则与 Trigger 过滤的 Editor 单测。
/// 路径：Assets/Tests/Editor。Window → General → Test Runner → EditMode 运行。
/// </summary>
public class SectorLogicTests
{
    [OneTimeSetUp]
    public void SuiteStart()
    {
        SectorTestReport.Suite("白盒-SectorCaptureRules / TriggerFilter");
    }

    [OneTimeTearDown]
    public void SuiteEnd()
    {
        GameLog.Info("SectorTest", "白盒套件结束（细节见各 [PASS]/[FAIL] 与 Test Runner）");
    }

    static void Expect(string caseName, bool condition, string failReason = "")
    {
        if (!SectorTestReport.Check(caseName, condition, failReason))
        {
            Assert.Fail(caseName + " | " + failReason);
        }
    }

    [Test]
    public void OpeningChain_RedAttacksB_BlueAttacksD_CLocked()
    {
        // B 的左邻是 A=红，右邻是 C=中立。不能把「A 有人占领」写成蓝方的 leftOwnedByTeam=true。
        Expect(
            "开局红方可攻 B（邻接 A）",
            SectorCaptureRules.IsActiveFrontlineFor(TeamId.Red, TeamId.None, TeamId.Red, TeamId.None));
        Expect(
            "开局蓝方不可攻 B",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.None, TeamId.Red, TeamId.None));
        Expect(
            "开局蓝方可攻 D（邻接 E）",
            SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.None, TeamId.None, TeamId.Blue));
        Expect(
            "开局红方不可攻 D",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Red, TeamId.None, TeamId.None, TeamId.Blue));
        Expect(
            "开局 C 对红锁定（B/D 均未连上）",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Red, TeamId.None, TeamId.None, TeamId.None));
        Expect(
            "开局 C 对蓝锁定",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.None, TeamId.None, TeamId.None));
    }

    [Test]
    public void AfterRedTakesB_CUnlocksForRed_StillLockedForBlue()
    {
        Expect(
            "红占 B 后可攻 C",
            SectorCaptureRules.IsActiveFrontlineFor(TeamId.Red, TeamId.None, TeamId.Red, TeamId.None));
        Expect(
            "红占 B 后蓝仍不能越级打 C",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.None, TeamId.Red, TeamId.None));
    }

    [Test]
    public void SweepAndTimeoutOutcomeRules()
    {
        Expect("红占 E 即推平胜", MatchOutcomeRules.EvaluateSweep(TeamId.Red, TeamId.Red) == TeamId.Red);
        Expect("蓝占 A 即推平胜", MatchOutcomeRules.EvaluateSweep(TeamId.Blue, TeamId.Blue) == TeamId.Blue);
        Expect("尚未推平", MatchOutcomeRules.EvaluateSweep(TeamId.Red, TeamId.Blue) == TeamId.None);
        Expect("限时红占领更多", MatchOutcomeRules.EvaluateTimeout(3, 2) == TeamId.Red);
        Expect("限时蓝占领更多", MatchOutcomeRules.EvaluateTimeout(1, 4) == TeamId.Blue);
        Expect("限时相等平局", MatchOutcomeRules.EvaluateTimeout(2, 2) == TeamId.None);
    }

    [Test]
    public void CaptureTrendLabel_ContestedWhenTied()
    {
        Expect("红多", SectorCaptureRules.CaptureTrendLabel(3, 0).Contains("红"));
        Expect("蓝多", SectorCaptureRules.CaptureTrendLabel(0, 3).Contains("蓝"));
        Expect("相等争夺", SectorCaptureRules.CaptureTrendLabel(3, 3) == "争夺中");
    }

    [Test]
    public void Frontline_B_Unlocks_After_A_Owned_By_Red()
    {
        bool redOpen = SectorCaptureRules.IsActiveFrontlineFor(
            TeamId.Red, TeamId.None, TeamId.Red, TeamId.None);
        bool blueOpen = SectorCaptureRules.IsActiveFrontlineFor(
            TeamId.Blue, TeamId.None, TeamId.Red, TeamId.None);
        Expect("A 被红方占领后 B 对红方解锁", redOpen);
        Expect("A 被红方占领后 B 对蓝方仍锁定", !blueOpen, "蓝方不应因红方邻居而解锁");
    }

    [Test]
    public void Frontline_SelfOwned_AlwaysActive()
    {
        Expect(
            "无己方邻居时即使本区已属红也不可攻",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Red, TeamId.Red, TeamId.None, TeamId.None));
        Expect(
            "None 阵营永远不是前线",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.None, TeamId.Red, TeamId.Red, TeamId.Blue));
    }

    [Test]
    public void A_Protected_Until_B_Leaves_Red()
    {
        Expect(
            "B 属红时蓝不可攻 A",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.Red, TeamId.None, TeamId.Red));
        Expect(
            "B 属蓝时蓝可攻 A",
            SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.Red, TeamId.None, TeamId.Blue));
        Expect(
            "B 中立时蓝仍不可攻 A（必须先占 B）",
            !SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.Red, TeamId.None, TeamId.None));
    }

    [Test]
    public void C_BothTeams_When_B_Red_And_D_Blue()
    {
        Expect(
            "红占 B、蓝占 D 时红可攻 C",
            SectorCaptureRules.IsActiveFrontlineFor(TeamId.Red, TeamId.None, TeamId.Red, TeamId.Blue));
        Expect(
            "红占 B、蓝占 D 时蓝可攻 C",
            SectorCaptureRules.IsActiveFrontlineFor(TeamId.Blue, TeamId.None, TeamId.Red, TeamId.Blue));
    }

    [Test]
    public void NeutralHudFill_StartsAtZero()
    {
        Expect("中立红填为 0", Mathf.Abs(SectorCaptureRules.ToHudRedFill(0f)) < 0.0001f);
        Expect("中立蓝填为 0", Mathf.Abs(SectorCaptureRules.ToHudBlueFill(0f)) < 0.0001f);
        Expect("+1 仅红满", Mathf.Abs(SectorCaptureRules.ToHudRedFill(1f) - 1f) < 0.0001f &&
                           Mathf.Abs(SectorCaptureRules.ToHudBlueFill(1f)) < 0.0001f);
        Expect("-1 仅蓝满", Mathf.Abs(SectorCaptureRules.ToHudBlueFill(-1f) - 1f) < 0.0001f &&
                           Mathf.Abs(SectorCaptureRules.ToHudRedFill(-1f)) < 0.0001f);
    }

    [Test]
    public void CaptureRate_EmptySingleAndContested()
    {
        const float duration = 10f;
        Expect("无人速率为 0", Mathf.Abs(SectorCaptureRules.CalculateCaptureRate(0, 0, duration)) < 0.0001f);
        Expect("单红 1 人 = +V", Mathf.Abs(SectorCaptureRules.CalculateCaptureRate(1, 0, duration) - 0.1f) < 0.0001f);
        Expect("单红 3 人线性叠加", Mathf.Abs(SectorCaptureRules.CalculateCaptureRate(3, 0, duration) - 0.3f) < 0.0001f);
        Expect("单蓝 2 人 = -2V", Mathf.Abs(SectorCaptureRules.CalculateCaptureRate(0, 2, duration) + 0.2f) < 0.0001f);
        Expect("拉锯人数差 2 红压", Mathf.Abs(SectorCaptureRules.CalculateCaptureRate(3, 1, duration) - 0.2f) < 0.0001f);
        Expect("拉锯人数相等僵持", Mathf.Abs(SectorCaptureRules.CalculateCaptureRate(2, 2, duration)) < 0.0001f);
        float stayed = SectorCaptureRules.ApplyCaptureTick(0.4f, 0, 0, duration, 1f, 0f);
        Expect("无人且无衰减时进度不动", Mathf.Abs(stayed - 0.4f) < 0.0001f);
        float decayed = SectorCaptureRules.ApplyCaptureTick(0.4f, 0, 0, duration, 1f, 0.1f);
        Expect("无人且有衰减时向 0 回落", Mathf.Abs(decayed - 0.3f) < 0.0001f);
    }

    [Test]
    public void OwnedHudFills_FollowsHasBeenCaptured_NotOccupants()
    {
        SectorCaptureRules.ToHudBarFills(TeamId.Red, 1f, true, false, out float redFull, out float blueEmpty, out bool grayAtRed);
        Expect("红占满不露灰", !grayAtRed && Mathf.Abs(redFull - 1f) < 0.0001f && Mathf.Abs(blueEmpty) < 0.0001f);

        SectorCaptureRules.ToHudBarFills(TeamId.None, 0.5f, false, false, out float virginRed, out float virginBlue, out bool virginGray);
        Expect("从未占领单方推进露灰（红+灰）", virginGray && Mathf.Abs(virginRed - 0.5f) < 0.0001f && Mathf.Abs(virginBlue) < 0.0001f);

        SectorCaptureRules.ToHudBarFills(TeamId.None, 0f, false, false, 1, 1, out float cRed, out float cBlue, out bool cGray);
        Expect("开局中立双方入场强制红蓝拼接不露灰", !cGray && Mathf.Abs(cRed + cBlue - 1f) < 0.0001f && Mathf.Abs(cRed - 0.5f) < 0.0001f);

        Expect(
            "灰底仅单方/无人且从未占领",
            SectorCaptureRules.ShowsUncapturedGray(TeamId.None, false, 1, 0) &&
            !SectorCaptureRules.ShowsUncapturedGray(TeamId.None, false, 1, 1));

        SectorCaptureRules.ToHudBarFills(TeamId.Red, 0.5f, true, false, out float tugRed, out float tugBlue, out bool tugGray);
        Expect("已占领过即使无人也红蓝相加为 1", !tugGray && Mathf.Abs(tugRed + tugBlue - 1f) < 0.0001f);

        SectorCaptureRules.ToHudBarFills(TeamId.None, 0.2f, true, false, out float emptyRed, out float emptyBlue, out bool emptyGray);
        Expect("HasBeenCaptured 后清空圈内仍不露灰", !emptyGray && Mathf.Abs(emptyRed + emptyBlue - 1f) < 0.0001f);

        SectorCaptureRules.ToHudBarFills(TeamId.Blue, -0.7f, true, true, out float lockRed, out float lockBlue, out bool lockGray);
        Expect("上锁蓝区纯蓝不露灰", !lockGray && Mathf.Abs(lockRed) < 0.0001f && Mathf.Abs(lockBlue - 1f) < 0.0001f);

        SectorCaptureRules.ToHudBarFills(TeamId.None, 0.5f, false, true, out float nRed, out float nBlue, out bool nGray);
        Expect("上锁且从未占领只露灰", nGray && Mathf.Abs(nRed) < 0.0001f && Mathf.Abs(nBlue) < 0.0001f);

        Expect("打满 +1 闩上标记", SectorCaptureRules.LatchHasBeenCaptured(false, 1f));
        Expect("未打满不闩", !SectorCaptureRules.LatchHasBeenCaptured(false, 0.95f));
        Expect("已闩后不清回", SectorCaptureRules.LatchHasBeenCaptured(true, 0f));
    }

    [Test]
    public void OccupyOrDefend_AllowsOwnerEvenIfAttackLocked()
    {
        Expect(
            "红方可在己方 A 防守，即使进攻链未解锁",
            SectorCaptureRules.CanOccupyOrDefend(TeamId.Red, TeamId.Red, false));
        Expect(
            "蓝方攻 A 必须进攻解锁",
            !SectorCaptureRules.CanOccupyOrDefend(TeamId.Blue, TeamId.Red, false) &&
            SectorCaptureRules.CanOccupyOrDefend(TeamId.Blue, TeamId.Red, true));
        Expect(
            "中立区未解锁不可进圈",
            !SectorCaptureRules.CanOccupyOrDefend(TeamId.Red, TeamId.None, false));
    }

    [Test]
    public void NeighborUnspawned_FallsBackToDefaultOwner()
    {
        bool unspawnedUsesDefault = SectorCaptureRules.IsNeighborOwnedBy(
            true, false, TeamId.None, TeamId.Red, TeamId.Red);
        bool spawnedUsesLive = SectorCaptureRules.IsNeighborOwnedBy(
            true, true, TeamId.None, TeamId.Red, TeamId.Red);
        bool missingNeighbor = SectorCaptureRules.IsNeighborOwnedBy(
            false, false, TeamId.Red, TeamId.Red, TeamId.Red);

        Expect("邻居未 Spawn 时用 DefaultOwner=Red", unspawnedUsesDefault);
        Expect("邻居已 Spawn 时用 live Owner=None，不算红方占领", !spawnedUsesLive);
        Expect("邻居引用缺失时安全返回 false", !missingNeighbor);
    }

    [Test]
    public void OwnerDoesNotBecomeNone_AtZeroProgress()
    {
        Expect("进度 0 时红方归属保持 Red", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.Red, 0f) == TeamId.Red);
        Expect("进度 0.05 时不洗成 None", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.Red, 0.05f) == TeamId.Red);
        Expect("进度 0 时蓝方归属保持 Blue", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.Blue, 0f) == TeamId.Blue);
        Expect("中立据点进度 0 仍是 None（本来就是）", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.None, 0f) == TeamId.None);
    }

    [Test]
    public void OwnerFlips_OnlyAtFullCapture()
    {
        Expect("+1 变为 Red", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.None, 1f) == TeamId.Red);
        Expect("-1 变为 Blue", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.None, -1f) == TeamId.Blue);
        Expect("蓝方打满 +1 易主为红", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.Blue, 1f) == TeamId.Red);
        Expect("红方打满 -1 易主为蓝", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.Red, -1f) == TeamId.Blue);
        Expect("未满进度不换边", SectorCaptureRules.ResolveOwnerAfterProgress(TeamId.Red, 0.99f) == TeamId.Red);
    }

    [Test]
    public void HudFillMapping_MinusOne_Zero_PlusOne()
    {
        Expect("progress=-1 → fill=0", Mathf.Abs(SectorCaptureRules.ToHudFillAmount(-1f) - 0f) < 0.0001f);
        Expect("progress=0 → fill=0.5", Mathf.Abs(SectorCaptureRules.ToHudFillAmount(0f) - 0.5f) < 0.0001f);
        Expect("progress=1 → fill=1", Mathf.Abs(SectorCaptureRules.ToHudFillAmount(1f) - 1f) < 0.0001f);
    }

    [Test]
    public void TriggerFilter_RejectsGunMeshCollider()
    {
        GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gun.name = "UT_GunMesh";
        try
        {
            bool accepted = SectorTriggerFilter.TryGetPlayerClientId(gun.GetComponent<Collider>(), out _);
            Expect("枪模 BoxCollider 不得计入人头", !accepted);
        }
        finally
        {
            Object.DestroyImmediate(gun);
        }
    }

    [Test]
    public void TriggerFilter_AcceptsCharacterControllerDummy()
    {
        GameObject dummy = CreateEditModeDummy("UT_Dummy", 9301);
        CharacterController controller = dummy.GetComponent<CharacterController>();
        try
        {
            bool accepted = SectorTriggerFilter.TryGetPlayerClientId(controller, out ulong clientId);
            Expect("根节点 CharacterController + Player 标签被接受", accepted && clientId == 9301, "accepted=" + accepted + " id=" + clientId);
        }
        finally
        {
            SafeDestroyEditModeObject(dummy);
        }
    }

    [Test]
    public void BlackBox_OnTriggerEnter_GunIgnored_BodyCounted()
    {
        GameObject zoneGo = new GameObject("UT_Zone");
        zoneGo.AddComponent<SphereCollider>().isTrigger = true;
        StrongpointArea area = zoneGo.AddComponent<StrongpointArea>();

        GameObject gun = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gun.name = "UT_GunInZone";

        // EditMode 不用 CharacterController：引擎会在激活/销毁时刷 ShouldRunBehaviour，Test Runner 会判失败。
        GameObject dummy = new GameObject("UT_BodyInZone");
        dummy.tag = "Player";
        CapsuleCollider body = dummy.AddComponent<CapsuleCollider>();
        dummy.AddComponent<SectorTestDummy>().AssignClientId(9401);

        try
        {
            area.SendMessage("OnTriggerEnter", gun.GetComponent<Collider>(), SendMessageOptions.DontRequireReceiver);
            Expect("枪模 OnTriggerEnter 后圈内人数仍为 0", area.DebugOccupantCount == 0, "实际=" + area.DebugOccupantCount);

            area.SendMessage("OnTriggerEnter", body, SendMessageOptions.DontRequireReceiver);
            Expect("身体 Collider OnTriggerEnter 后圈内人数为 1", area.DebugOccupantCount == 1, "实际=" + area.DebugOccupantCount);

            area.SendMessage("OnTriggerExit", body, SendMessageOptions.DontRequireReceiver);
            Expect("身体 Collider OnTriggerExit 后圈内人数为 0", area.DebugOccupantCount == 0, "实际=" + area.DebugOccupantCount);

            area.SendMessage("OnTriggerEnter", body, SendMessageOptions.DontRequireReceiver);
            Expect("再次进入后圈内人数为 1", area.DebugOccupantCount == 1, "实际=" + area.DebugOccupantCount);

            dummy.SetActive(false);
            body = null;
            area.CleanInvalidEntries();
            Object.DestroyImmediate(dummy);
            dummy = null;
            area.CleanInvalidEntries();
            Expect("Dummy Destroy 后圈内人数为 0", area.DebugOccupantCount == 0, "实际=" + area.DebugOccupantCount);
        }
        finally
        {
            SafeDestroyEditModeObject(gun);
            SafeDestroyEditModeObject(dummy);
            SafeDestroyEditModeObject(zoneGo);
        }
    }

    /// <summary>CharacterController 全程保持未激活，避免 EditMode ShouldRunBehaviour。</summary>
    static GameObject CreateEditModeDummy(string name, ulong clientId)
    {
        GameObject dummy = new GameObject(name);
        dummy.SetActive(false);
        dummy.tag = "Player";
        CharacterController body = dummy.AddComponent<CharacterController>();
        body.enabled = false;
        dummy.AddComponent<SectorTestDummy>().AssignClientId(clientId);
        return dummy;
    }

    /// <summary>先拆掉 CharacterController，再 Destroy 物体。</summary>
    static void SafeDestroyEditModeObject(GameObject go)
    {
        if (go == null)
        {
            return;
        }

        go.SetActive(false);
        CharacterController controller = go.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
            Object.DestroyImmediate(controller);
        }

        Object.DestroyImmediate(go);
    }
}
