using UdonSharp;          // 引入 UdonSharp 核心库（必须）
using UnityEngine;         // Unity 基础库（处理 Transform、Vector3 等）
using VRC.SDKBase;        // VRChat SDK 基础库（处理玩家对象、传送 API）
namespace Lgc.TeleportTool
{
    // 继承 UdonSharpBehaviour（替代 MonoBehaviour，适配 Udon 虚拟机）
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TeleportPlayer : UdonSharpBehaviour
{
    // [SerializeField]：允许在 Unity 编辑器中手动赋值目标传送点
    // private Transform targetPosition：存储传送目标的位置和旋转信息
    [SerializeField] private Transform targetPosition;

    // 重写 Udon 的 Interact 方法（VRChat 交互核心方法）
    // 当玩家用手/指针点击挂载该脚本的 GameObject 时触发
    public override void Interact()
    {
        // 安全校验：防止目标传送点未赋值导致空指针错误
        if (targetPosition != null)
        {
            // 调用 VRChat 玩家传送 API：将本地玩家传送到目标位置
            Networking.LocalPlayer.TeleportTo(
                targetPosition.position,       // 目标世界坐标（Vector3）
                targetPosition.rotation,       // 目标旋转角度（Quaternion）
                VRC_SceneDescriptor.SpawnOrientation.Default,  // 保持目标旋转（不重置朝向）
                false                           // 是否保持玩家速度（false=传送后静止）
            );
        }
    }
}
}