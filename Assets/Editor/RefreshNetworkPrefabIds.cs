#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 打开工程时刷新 Player 预制体的 NetworkObject Hash，并把它写进默认网络预制体列表。
/// Hash 为 0 时 NGO 无法生成角色。
/// </summary>
[InitializeOnLoad]
public static class RefreshNetworkPrefabIds
{
    const string PrefabPath = "Assets/Player/Player.prefab";
    const string BulletPrefabPath = "Assets/Player/Bullet.prefab";
    const string ListPath = "Assets/DefaultNetworkPrefabs.asset";

    static RefreshNetworkPrefabIds()
    {
        EditorApplication.delayCall += Refresh;
    }

    static void Refresh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            return;
        }

        NetworkObject networkObject = prefab.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            SerializedObject serializedNetworkObject = new SerializedObject(networkObject);
            SerializedProperty hashProperty = serializedNetworkObject.FindProperty("GlobalObjectIdHash");
            if (hashProperty != null && hashProperty.uintValue == 0)
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ListPath);
        if (list != null)
        {
            bool dirty = false;
            dirty |= EnsurePrefabInList(list, prefab);
            GameObject bulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPrefabPath);
            dirty |= EnsurePrefabInList(list, bulletPrefab);
            if (dirty)
            {
                EditorUtility.SetDirty(list);
                AssetDatabase.SaveAssets();
            }
        }
    }

    static bool EnsurePrefabInList(NetworkPrefabsList list, GameObject prefab)
    {
        if (prefab == null || list.Contains(prefab))
        {
            return false;
        }

        list.Add(new NetworkPrefab { Prefab = prefab });
        return true;
    }
}
#endif
