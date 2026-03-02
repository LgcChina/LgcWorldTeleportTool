using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// 添加 VPM 规范的命名空间
namespace Lgc.TeleportTool
{
    // 核心传送逻辑脚本，保持功能不变
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)] // 新增：明确同步模式，符合 VRC 最佳实践
    public class TeleportPlayer : UdonSharpBehaviour
    {
        [SerializeField]
        [Tooltip("传送目标位置的Transform")] // 新增：Tooltip 提升易用性
        private Transform targetPosition;

        public override void Interact()
        {
            if (targetPosition != null && Networking.LocalPlayer != null)
            {
                Networking.LocalPlayer.TeleportTo(
                    targetPosition.position,
                    targetPosition.rotation,
                    VRC_SceneDescriptor.SpawnOrientation.Default,
                    false
                );
            }
        }
    }
}