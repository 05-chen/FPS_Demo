namespace World
{
    /// <summary>
    /// 统一的占领测试 Pass/Fail 控制台报告。
    /// </summary>
    public static class SectorTestReport
    {
        public static void Suite(string name) =>
            GameLog.Info("SectorTest", "======== " + name + " ========");

        public static void Pass(string caseName) =>
            GameLog.Info("SectorTest", "[PASS] " + caseName);

        public static void Fail(string caseName, string reason)
        {
            GameLog.Error("SectorTest", "[FAIL] " + caseName + " | " + reason);
        }

        public static bool Check(string caseName, bool condition, string failReason)
        {
            if (condition)
            {
                Pass(caseName);
                return true;
            }

            Fail(caseName, failReason);
            return false;
        }

        public static void Summary(string suiteName, int passed, int failed)
        {
            if (failed == 0)
            {
                GameLog.Info("SectorTest", "[PASS] " + suiteName + " 全部通过 (" + passed + "/" + passed + ")");
                return;
            }

            GameLog.Error(
                "SectorTest",
                "[FAIL] " + suiteName + " 失败 " + failed + " 项，通过 " + passed + " 项");
        }
    }
}
