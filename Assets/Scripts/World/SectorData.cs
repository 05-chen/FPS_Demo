using System;
using Core;
using UnityEngine;

namespace World
{
    [Serializable]
    public class SectorData
    {
        [Tooltip("据点唯一标识符")]
        public string pointId = "Alpha";

        [Tooltip("UI显示名称")]
        public string pointName = "据点 Alpha";

        [Tooltip("初始默认归属阵营")]
        public TeamId defaultTeam = TeamId.None;

        [Tooltip("从无人占领到完全占领所需的净时间(秒)")]
        public float captureDuration = 15f;

        [Tooltip("圈内无人时进度每秒向 0 回落；0 表示保持不动")]
        public float emptyDecayPerSecond;

        [Tooltip("是否曾被彻底占领（打满 ±1）。一旦为 true，HUD 永久不再露灰底。与圈内人数无关。")]
        public bool HasBeenCaptured;
    }
}