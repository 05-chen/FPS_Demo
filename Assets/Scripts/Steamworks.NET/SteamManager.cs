// The SteamManager is designed to work with Steamworks.NET
// This file is released into the public domain.
// Where that dedication is not recognized you are granted a perpetual,
// irrevocable license to copy and modify this file as you see fit.
//
// Version: 1.0.13

#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using System.IO;
using Steamworks;
#endif

//
// The SteamManager provides a base implementation of Steamworks.NET on which you can build upon.
// It handles the basics of starting up and shutting down the SteamAPI for use.
//
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class SteamManager : MonoBehaviour {
#if !DISABLESTEAMWORKS
	protected static bool s_EverInitialized = false;

	protected static SteamManager s_instance;
	protected static SteamManager Instance {
		get {
			return s_instance;
		}
	}

	protected bool m_bInitialized = false;
	public static bool Initialized {
		get {
			// 不要走 Instance 的自动创建：退出 Play 时其它脚本的 OnDestroy
			// 一访问 Initialized 就会再 new 一个 SteamManager，从而二次 Init。
			return s_instance != null && s_instance.m_bInitialized;
		}
	}

	protected SteamAPIWarningMessageHook_t m_SteamAPIWarningMessageHook;

	[AOT.MonoPInvokeCallback(typeof(SteamAPIWarningMessageHook_t))]
	protected static void SteamAPIDebugTextHook(int nSeverity, System.Text.StringBuilder pchDebugText) {
		Debug.LogWarning(pchDebugText);
	}

#if UNITY_2019_3_OR_NEWER
	// In case of disabled Domain Reload, reset static members before entering Play Mode.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void InitOnPlayMode()
	{
		s_EverInitialized = false;
		s_instance = null;
	}
#endif

	protected virtual void Awake() {
		// Only one instance of SteamManager at a time!
		if (s_instance != null) {
			Destroy(gameObject);
			return;
		}

		if (s_EverInitialized) {
			// 退出 Play / 切场景时可能误生成第二个 SteamManager。丢掉即可，不要抛异常。
			Destroy(gameObject);
			return;
		}

		s_instance = this;

		// We want our SteamManager Instance to persist across scenes.
		DontDestroyOnLoad(gameObject);

		if (!Packsize.Test()) {
			Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.", this);
		}

		if (!DllCheck.Test()) {
			Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.", this);
		}

		EnsureSteamAppIdFile();

		try {
			// 正式包：没通过 Steam 启动时，会让 Steam 重新拉起游戏。
			// 编辑器里不要调用：返回 true 时会 Quit，会把整个 Unity 关掉。
#if !UNITY_EDITOR
			if (SteamAPI.RestartAppIfNecessary((AppId_t)480)) {
				Debug.Log("[Steamworks.NET] Shutting down because RestartAppIfNecessary returned true. Steam will restart the application.");

				Application.Quit();
				return;
			}
#endif
		}
		catch (System.DllNotFoundException e) { // We catch this exception here, as it will be the first occurrence of it.
			Debug.LogError("[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.\n" + e, this);

			Application.Quit();
			return;
		}

		string steamError;
		ESteamAPIInitResult initResult = SteamAPI.InitEx(out steamError);
		m_bInitialized = initResult == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
		if (!m_bInitialized) {
			Debug.LogError(BuildInitFailMessage(initResult, steamError), this);
			return;
		}

		s_EverInitialized = true;
		SteamNetworkingUtils.InitRelayNetworkAccess();
	}

	const string TestAppId = "480";

	static void EnsureSteamAppIdFile() {
		TryWriteAppId(Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt"));

		DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
		if (projectRoot != null) {
			TryWriteAppId(Path.Combine(projectRoot.FullName, "steam_appid.txt"));
		}
	}

	static void TryWriteAppId(string path) {
		try {
			if (File.Exists(path) && File.ReadAllText(path).Trim() == TestAppId) {
				return;
			}

			File.WriteAllText(path, TestAppId);
			Debug.Log("[Steamworks.NET] 已写入 steam_appid.txt -> " + path);
		}
		catch (System.Exception e) {
			Debug.LogWarning("[Steamworks.NET] 无法写入 " + path + " : " + e.Message);
		}
	}

	static string BuildInitFailMessage(ESteamAPIInitResult result, string steamError) {
		bool steamRunning = System.Diagnostics.Process.GetProcessesByName("steam").Length > 0
			|| System.Diagnostics.Process.GetProcessesByName("Steam").Length > 0;

		string message = "[Steamworks.NET] SteamAPI_Init 失败。\n"
			+ "原因: " + result + "\n"
			+ "Steam 原文: " + steamError + "\n"
			+ "当前工作目录: " + Directory.GetCurrentDirectory() + "\n"
			+ "Steam 进程: " + (steamRunning ? "已检测到" : "未检测到（请先打开并登录 Steam）") + "\n\n";

		if (result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient || !steamRunning) {
			message += "请先打开 Steam 客户端并登录，再回到 Unity 点 Play。\n"
				+ "注意：Unity 不要用管理员身份运行，Steam 也不要用管理员身份，两边权限必须一致。";
		}
		else if (result == ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch) {
			message += "Steam 客户端版本过旧，请在 Steam 里更新后再试。";
		}
		else {
			message += "当前用的是测试 AppID 480（Spacewar）。请在浏览器打开 steam://install/480 ，让 Steam 把这个测试游戏加进库，再重试。";
		}

		return message;
	}

	// This should only ever get called on first load and after an Assembly reload, You should never Disable the Steamworks Manager yourself.
	protected virtual void OnEnable() {
		if (s_instance == null) {
			s_instance = this;
		}

		if (!m_bInitialized) {
			return;
		}

		if (m_SteamAPIWarningMessageHook == null) {
			// Set up our callback to receive warning messages from Steam.
			// You must launch with "-debug_steamapi" in the launch args to receive warnings.
			m_SteamAPIWarningMessageHook = new SteamAPIWarningMessageHook_t(SteamAPIDebugTextHook);
			SteamClient.SetWarningMessageHook(m_SteamAPIWarningMessageHook);
		}
	}

	// OnApplicationQuit gets called too early to shutdown the SteamAPI.
	// Because the SteamManager should be persistent and never disabled or destroyed we can shutdown the SteamAPI here.
	// Thus it is not recommended to perform any Steamworks work in other OnDestroy functions as the order of execution can not be garenteed upon Shutdown. Prefer OnDisable().
	protected virtual void OnDestroy() {
		if (s_instance != this) {
			return;
		}

		s_instance = null;
		s_EverInitialized = false;

		if (!m_bInitialized) {
			return;
		}

		SteamAPI.Shutdown();
		m_bInitialized = false;
	}

	protected virtual void Update() {
		if (!m_bInitialized) {
			return;
		}

		// Run Steam client callbacks
		SteamAPI.RunCallbacks();
	}
#else
	public static bool Initialized {
		get {
			return false;
		}
	}
#endif // !DISABLESTEAMWORKS
}
