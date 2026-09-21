using UnityEngine;

namespace Weapon
{
    public class HitEffectSpawner : MonoBehaviour
    {
        public static HitEffectSpawner Instance { get; private set; }

        [Header("占位特效与贴图")]
        public GameObject defaultDecalPrefab;
        [Tooltip("弹孔大小（米）。Quad 本身是 1x1，0.03 约等于 3 厘米。")]
        [SerializeField] float decalScale = 0.03f;
        public ParticleSystem fleshHitParticle;

        [Header("占位音效")]
        public AudioSource audioSource;
        public AudioClip fleshHitSound;
        public AudioClip helmetPingSound;
        public AudioClip fireSound;

        static Material _visibleDecalMaterial;
        static AudioClip _beepClip;
        static bool _loggedAudioDiag;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureAudioSource();
            PreloadClips();
        }

        void PreloadClips()
        {
            PreloadClip(fireSound);
            PreloadClip(fleshHitSound);
            PreloadClip(helmetPingSound);
        }

        static void PreloadClip(AudioClip clip)
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
            }
        }

        /// <summary>
        /// 单机场景若漏挂或物体未激活，开枪时再找一次 / 补一个，避免 Instance 一直为 null。
        /// </summary>
        public static HitEffectSpawner EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            Instance = FindFirstObjectByType<HitEffectSpawner>(FindObjectsInactive.Include);
            if (Instance != null)
            {
                if (!Instance.gameObject.activeInHierarchy)
                {
                    Instance.gameObject.SetActive(true);
                }

                Instance.EnsureAudioSource();
                return Instance;
            }

            var host = new GameObject("HitEffectManager");
            Instance = host.AddComponent<HitEffectSpawner>();
            Instance.EnsureAudioSource();
            Debug.LogWarning("[HitEffectSpawner] 场景中没有生成器，已自动创建 HitEffectManager。请补弹孔 Prefab 与音效。");
            return Instance;
        }

        void EnsureAudioSource()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            audioSource.playOnAwake = false;
            audioSource.mute = false;
            audioSource.volume = Mathf.Max(audioSource.volume, 1f);
            audioSource.spatialBlend = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 核心生成接口：根据命中信息播放音效与特效。
        /// </summary>
        public void SpawnHitEffect(RaycastHit hit, bool isHeadshot = false)
        {
            Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
            SpawnHitEffectAt(hit.point, normal, isHeadshot);
        }

        /// <summary>
        /// 不依赖 RaycastHit / Collider：供 ClientRpc 广播弹孔坐标与法线。
        /// </summary>
        public void SpawnHitEffectAt(Vector3 point, Vector3 normal, bool isHeadshot)
        {
            if (normal.sqrMagnitude < 0.0001f)
            {
                normal = Vector3.up;
            }
            else
            {
                normal = normal.normalized;
            }

            Debug.Log($"[HitEffectSpawner] 正在生成击中特效！命中点: {point}, 爆头: {isHeadshot}");
            EnsureAudioSource();
            SpawnDecalAt(point, normal);
            SpawnParticleAt(point, normal);
        }

        public void PlayFireSound(AudioSource preferredSource = null, AudioClip overrideClip = null)
        {
            AudioClip clip = overrideClip != null ? overrideClip : fireSound;
            if (clip == null)
            {
                clip = fleshHitSound;
            }

            PlayClipNow(clip, preferredSource);
        }

        void SpawnDecalAt(Vector3 point, Vector3 normal)
        {
            if (defaultDecalPrefab == null)
            {
                Debug.LogWarning("[HitEffectSpawner] 弹孔 Prefab 未赋值，跳过弹孔。");
                return;
            }

            GameObject decal = Instantiate(
                defaultDecalPrefab,
                point + normal * 0.01f,
                Quaternion.LookRotation(-normal));

            if (decal == null)
            {
                return;
            }

            MakeDecalVisible(decal);

            Collider[] colliders = decal.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Destroy(colliders[i]);
            }

            decal.transform.SetParent(null, true);
            // 尺寸由本脚本统一决定：预制体上的缩放会在每次生成时被这里覆盖。
            decal.transform.localScale = Vector3.one * Mathf.Max(0.001f, decalScale);
            Destroy(decal, 8f);
        }

        static void MakeDecalVisible(GameObject decal)
        {
            if (_visibleDecalMaterial == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                _visibleDecalMaterial = new Material(shader)
                {
                    color = new Color(0.05f, 0.05f, 0.05f, 1f)
                };
            }

            Renderer[] renderers = decal.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = _visibleDecalMaterial;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].enabled = true;
            }
        }

        void SpawnParticleAt(Vector3 point, Vector3 normal)
        {
            if (fleshHitParticle == null)
            {
                return;
            }

            ParticleSystem particle = Instantiate(fleshHitParticle, point, Quaternion.LookRotation(normal));
            if (particle == null)
            {
                return;
            }

            particle.Play();
            float lifetime = particle.main.duration + particle.main.startLifetime.constantMax;
            Destroy(particle.gameObject, Mathf.Max(0.1f, lifetime));
        }

        void PlayHitSound(bool isHeadshot)
        {
            AudioClip clipToPlay = isHeadshot ? helmetPingSound : fleshHitSound;
            PlayClipNow(clipToPlay, null);
        }

        void PlayClipNow(AudioClip clip, AudioSource preferredSource)
        {
            UnmuteEditorAudio();
            AudioListener.pause = false;
            AudioListener.volume = 1f;

            AudioClip playbackClip = ResolvePlaybackClip(clip);
            if (playbackClip == null)
            {
                Debug.LogWarning("[HitEffectSpawner] 没有可播放的音效（文件、Inspector Clip、占位蜂鸣都失败）。");
                return;
            }

            AudioSource source = preferredSource != null ? preferredSource : GetEarAudioSource();
            ConfigureSource(source);

            if (!_loggedAudioDiag)
            {
                _loggedAudioDiag = true;
                LogAudioDiagnostics(clip, playbackClip, source);
            }

            source.PlayOneShot(playbackClip, 1f);
            Debug.Log($"[GunShot] 播放音效: {playbackClip.name}, 采样={playbackClip.samples}, 声源: {source.gameObject.name}");
        }

        static void ConfigureSource(AudioSource source)
        {
            source.enabled = true;
            source.mute = false;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
            source.loop = false;
            source.ignoreListenerPause = true;
            source.bypassEffects = true;
            source.bypassListenerEffects = true;
            source.bypassReverbZones = true;
            source.outputAudioMixerGroup = null;
        }

        static AudioClip ResolvePlaybackClip(AudioClip inspectorClip)
        {
            if (inspectorClip != null)
            {
                if (inspectorClip.loadState != AudioDataLoadState.Loaded)
                {
                    inspectorClip.LoadAudioData();
                }

                if (inspectorClip.samples > 0)
                {
                    return inspectorClip;
                }
            }

            if (_beepClip == null)
            {
                _beepClip = CreateBeepClip();
            }

            return _beepClip;
        }

        static AudioClip CreateBeepClip()
        {
            const int frequency = 44100;
            const float duration = 0.12f;
            const float toneHz = 880f;
            int frames = Mathf.CeilToInt(frequency * duration);
            float[] samples = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float t = i / (float)frequency;
                float envelope = 1f - t / duration;
                samples[i] = Mathf.Sin(2f * Mathf.PI * toneHz * t) * envelope * 0.6f;
            }

            AudioClip clip = AudioClip.Create("gun_beep", frames, 1, frequency, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static void UnmuteEditorAudio()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.audioMasterMute)
            {
                UnityEditor.EditorUtility.audioMasterMute = false;
                Debug.LogWarning("[GunShot] 检测到 Game 窗口 Mute Audio，已自动取消静音。");
            }
#endif
        }

        static void LogAudioDiagnostics(AudioClip inspectorClip, AudioClip playbackClip, AudioSource source)
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int enabledCount = 0;
            string enabledName = "无";
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].enabled)
                {
                    enabledCount++;
                    enabledName = listeners[i].gameObject.name;
                }
            }

            AudioConfiguration config = AudioSettings.GetConfiguration();
            string inspectorInfo = inspectorClip != null
                ? $"{inspectorClip.name} samples={inspectorClip.samples} load={inspectorClip.loadState}"
                : "null";

#if UNITY_EDITOR
            bool editorMute = UnityEditor.EditorUtility.audioMasterMute;
#else
            bool editorMute = false;
#endif

            Debug.Log(
                $"[GunShot] 诊断: InspectorClip=({inspectorInfo}), 实际播放={playbackClip.name}/{playbackClip.samples}, " +
                $"Listener启用数={enabledCount}({enabledName}), source.mute={source.mute}, " +
                $"AudioListener.volume={AudioListener.volume}, pause={AudioListener.pause}, " +
                $"editorMute={editorMute}, speaker={config.speakerMode}, sampleRate={config.sampleRate}");
        }

        AudioSource GetEarAudioSource()
        {
            AudioListener listener = FindEnabledListener();
            Transform host = listener != null ? listener.transform : transform;
            AudioSource source = host.GetComponent<AudioSource>();
            if (source == null)
            {
                source = host.gameObject.AddComponent<AudioSource>();
            }

            return source;
        }

        static AudioListener FindEnabledListener()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < listeners.Length; i++)
            {
                if (listeners[i] != null && listeners[i].enabled)
                {
                    return listeners[i];
                }
            }

            return null;
        }
    }
}
