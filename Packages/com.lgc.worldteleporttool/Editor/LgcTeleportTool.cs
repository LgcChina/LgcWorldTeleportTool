#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Lgc.TeleportTool.Editor
{
    /// <summary>
    /// LGC传送工具 - 版本：1.0.5 | 作者：豆包
    /// 功能：快速创建/编辑/删除场景内的传送体，支持二次确认删除+Undo撤回
    /// 新增：所有创建/删除操作接入Unity Undo系统，支持Ctrl+Z撤销
    /// </summary>
    public class LgcWorldTeleportTool : EditorWindow
    {
        // 基础配置
        private const string VERSION = "1.0.0";
        private const string AUTHOR = "豆包";
        private string teleportBaseName = "传送点";
        private static int teleportCounter = 1;

        // 工具模式
        private enum ToolMode { Idle, Creating, Editing }
        private ToolMode currentMode = ToolMode.Idle;

        // 调整目标
        private enum AdjustPart { Button, Target }
        private AdjustPart currentAdjust = AdjustPart.Button;

        // 当前操作的传送体
        private GameObject currentRoot;
        private GameObject currentBtn;
        private GameObject currentTarget;

        // 二次确认状态
        private bool isConfirmingDeleteCurrent = false;
        private float deleteCurrentConfirmTime = 0f;
        private GameObject confirmDeleteListItem = null;
        private float deleteListItemConfirmTime = 0f;
        private const float CONFIRM_TIMEOUT = 2f;

        private Vector2 scrollPos;

        [MenuItem("LGC/LGC_世界传送点创建工具")]
        public static void ShowWindow()
        {
            GetWindow<LgcWorldTeleportTool>($"LGC_世界用传送点工具 v{VERSION}");
            RefreshCounter();
        }

        private void OnGUI()
        {
            // 1. 顶部标题（居中）
            DrawCenteredLabel($"LGC 传送工具 | v{VERSION} | 作者：{AUTHOR}", EditorStyles.boldLabel);
            GUILayout.Space(6);

            // 2. 基础设置区
            DrawBaseSettings();

            // 3. 操作区标题（居中）
            GUILayout.Space(8);
            DrawCenteredLabel("—————— 操作 ——————", EditorStyles.miniLabel);
            GUILayout.Space(4);

            // 4. 核心操作按钮
            DrawOperationButtons();

            // 5. 列表区标题（居中）
            GUILayout.Space(10);
            DrawCenteredLabel("—————— 场景传送体 ——————", EditorStyles.miniLabel);
            GUILayout.Space(4);

            // 6. 传送体列表
            DrawTeleportList();

            // 7. 检测确认超时
            CheckConfirmTimeouts();
        }

        #region 基础UI方法
        /// <summary>
        /// 绘制居中Label
        /// </summary>
        private void DrawCenteredLabel(string text, GUIStyle style)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, style);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制带渐变的确认按钮（适配列表窄按钮）
        /// </summary>
        private bool DrawConfirmButton(string normalText, bool isConfirming, float confirmStartTime, float height, float width = 0, bool isListItem = false)
        {
            bool isClicked = false;
            Color originalColor = GUI.backgroundColor;
            string displayText = normalText;

            if (isConfirming)
            {
                float progress = Mathf.Clamp01((Time.realtimeSinceStartup - confirmStartTime) / CONFIRM_TIMEOUT);
                GUI.backgroundColor = Color.Lerp(Color.red, new Color(0.8f, 0.8f, 0.8f), progress);
                int remaining = Mathf.Max(0, Mathf.CeilToInt(CONFIRM_TIMEOUT - (Time.realtimeSinceStartup - confirmStartTime)));

                if (isListItem)
                {
                    displayText = remaining.ToString(); // 列表项纯数字
                }
                else
                {
                    displayText = $"再次点击删除 ({remaining}秒)";
                }
            }
            else
            {
                GUI.backgroundColor = new Color(0.8f, 0.8f, 0.8f);
            }

            if (width > 0)
            {
                if (GUILayout.Button(displayText, GUILayout.Width(width), GUILayout.Height(height)))
                    isClicked = true;
            }
            else
            {
                if (GUILayout.Button(displayText, GUILayout.Height(height)))
                    isClicked = true;
            }

            GUI.backgroundColor = originalColor;
            return isClicked;
        }
        #endregion

        #region 功能绘制
        private void DrawBaseSettings()
        {
            if (currentMode == ToolMode.Idle || currentMode == ToolMode.Creating)
            {
                teleportBaseName = EditorGUILayout.TextField("传送点名称", teleportBaseName);
                EditorGUILayout.LabelField("下一个编号", teleportCounter.ToString());
            }
            else
            {
                EditorGUILayout.LabelField("当前编辑：", currentRoot?.name ?? "无");
            }
        }

        private void DrawOperationButtons()
        {
            // 1. 创建传送体
            using (new EditorGUI.DisabledScope(currentMode != ToolMode.Idle))
            {
                if (GUILayout.Button("创建传送体", GUILayout.Height(28)))
                {
                    CreateNewTeleport();
                    ResetAllConfirmStates();
                }
            }
            GUILayout.Space(4);

            // 2. 调整按钮
            using (new EditorGUI.DisabledScope(currentMode == ToolMode.Idle))
            {
                Color c = GUI.backgroundColor;
                if (currentAdjust == AdjustPart.Button) GUI.backgroundColor = Color.green;
                if (GUILayout.Button("调整按钮", GUILayout.Height(28)))
                {
                    currentAdjust = AdjustPart.Button;
                    Selection.activeGameObject = currentBtn;
                    ResetAllConfirmStates();
                }
                GUI.backgroundColor = c;
            }
            GUILayout.Space(4);

            // 3. 调整目标
            using (new EditorGUI.DisabledScope(currentMode == ToolMode.Idle))
            {
                Color c = GUI.backgroundColor;
                if (currentAdjust == AdjustPart.Target) GUI.backgroundColor = Color.green;
                if (GUILayout.Button("调整目标", GUILayout.Height(28)))
                {
                    currentAdjust = AdjustPart.Target;
                    Selection.activeGameObject = currentTarget;
                    ResetAllConfirmStates();
                }
                GUI.backgroundColor = c;
            }
            GUILayout.Space(4);

            // 4. 完成/退出
            using (new EditorGUI.DisabledScope(currentMode == ToolMode.Idle))
            {
                string btnText = currentMode == ToolMode.Creating ? "完成创建" : "退出编辑";
                if (GUILayout.Button(btnText, GUILayout.Height(28)))
                {
                    FinishEditMode();
                    ResetAllConfirmStates();
                }
            }
            GUILayout.Space(4);

            // 5. 删除当前传送体（支持Undo）
            using (new EditorGUI.DisabledScope(currentMode == ToolMode.Idle))
            {
                bool btnClicked = DrawConfirmButton(
                    "删除当前传送体",
                    isConfirmingDeleteCurrent,
                    deleteCurrentConfirmTime,
                    28f);

                if (btnClicked)
                {
                    if (!isConfirmingDeleteCurrent)
                    {
                        isConfirmingDeleteCurrent = true;
                        deleteCurrentConfirmTime = Time.realtimeSinceStartup;
                    }
                    else
                    {
                        // 执行删除 + 注册Undo
                        if (currentRoot != null)
                        {
                            Undo.DestroyObjectImmediate(currentRoot); // 支持撤销的删除
                            currentRoot = null;
                            currentBtn = null;
                            currentTarget = null;
                            currentMode = ToolMode.Idle;
                            RefreshCounter();
                        }
                        isConfirmingDeleteCurrent = false;
                    }
                }
            }
        }

        private void DrawTeleportList()
        {
            List<GameObject> teleportList = GetAllTeleportGroups();
            if (teleportList.Count == 0)
            {
                EditorGUILayout.HelpBox("场景中暂无传送体", MessageType.Info);
                return;
            }

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(140));
            foreach (var teleportObj in teleportList)
            {
                using (new GUILayout.HorizontalScope())
                {
                    // 选中编辑
                    if (GUILayout.Button(teleportObj.name, GUILayout.Height(24)))
                    {
                        SelectTeleportForEdit(teleportObj);
                        ResetAllConfirmStates();
                    }

                    // 列表项删除按钮（支持Undo）
                    bool isItemConfirming = confirmDeleteListItem == teleportObj;
                    bool btnClicked = DrawConfirmButton(
                        "X",
                        isItemConfirming,
                        deleteListItemConfirmTime,
                        24f,
                        40f,
                        true);

                    if (btnClicked)
                    {
                        if (!isItemConfirming)
                        {
                            confirmDeleteListItem = teleportObj;
                            deleteListItemConfirmTime = Time.realtimeSinceStartup;
                        }
                        else
                        {
                            // 执行删除 + 注册Undo
                            if (teleportObj != null)
                            {
                                Undo.DestroyObjectImmediate(teleportObj); // 支持撤销的删除
                                if (teleportObj == currentRoot)
                                {
                                    currentRoot = null;
                                    currentBtn = null;
                                    currentTarget = null;
                                    currentMode = ToolMode.Idle;
                                }
                                RefreshCounter();
                            }
                            confirmDeleteListItem = null;
                        }
                    }
                }
                GUILayout.Space(2);
            }
            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region 核心逻辑（全量支持Undo）
        /// <summary>
        /// 创建新传送体（注册Undo，支持撤销）
        /// </summary>
        private void CreateNewTeleport()
        {
            // 1. 创建根物体 + 注册Undo
            currentRoot = new GameObject($"{teleportBaseName}({teleportCounter})");
            Undo.RegisterCreatedObjectUndo(currentRoot, $"Create Teleport Group: {currentRoot.name}");

            // 2. 创建按钮物体 + 注册Undo
            currentBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(currentBtn, $"Create Teleport Button: {currentBtn.name}");
            currentBtn.name = "传送按钮";
            currentBtn.transform.SetParent(currentRoot.transform);
            currentBtn.transform.localScale = Vector3.one * 0.1f;
            currentBtn.GetComponent<BoxCollider>().isTrigger = true;

            // 3. 创建目标物体 + 注册Undo
            currentTarget = new GameObject("传送目标");
            Undo.RegisterCreatedObjectUndo(currentTarget, $"Create Teleport Target: {currentTarget.name}");
            currentTarget.transform.SetParent(currentRoot.transform);
            currentTarget.transform.position = currentBtn.transform.position + new Vector3(0, 0, 2);

            currentMode = ToolMode.Creating;
            currentAdjust = AdjustPart.Button;
            Selection.activeGameObject = currentBtn;
        }

        /// <summary>
        /// 选中传送体编辑
        /// </summary>
        private void SelectTeleportForEdit(GameObject teleportObj)
        {
            currentRoot = teleportObj;
            currentBtn = teleportObj.transform.Find("传送按钮")?.gameObject;
            currentTarget = teleportObj.transform.Find("传送目标")?.gameObject;

            if (currentBtn != null && currentTarget != null)
            {
                currentMode = ToolMode.Editing;
                currentAdjust = AdjustPart.Button;
                Selection.activeGameObject = currentBtn;
            }
            else
            {
                currentMode = ToolMode.Idle;
                currentRoot = null;
            }
        }

        /// <summary>
        /// 完成/退出编辑（为脚本赋值注册Undo）
        /// </summary>
        private void FinishEditMode()
        {
            if (currentMode == ToolMode.Creating && currentBtn != null && currentTarget != null)
            {
                // 添加传送脚本 + 注册Undo
                var teleportScript = currentBtn.GetComponent<Lgc.TeleportTool.TeleportPlayer>();
                if (teleportScript == null)
                {
                    teleportScript = currentBtn.AddComponent<Lgc.TeleportTool.TeleportPlayer>();
                    Undo.RegisterCreatedObjectUndo(teleportScript, $"Add Teleport Script to {currentBtn.name}");
                }

                // 修改脚本属性 + 注册Undo
                Undo.RecordObject(teleportScript, $"Set Target for {currentBtn.name}");
                SerializedObject so = new SerializedObject(teleportScript);
                so.FindProperty("targetPosition").objectReferenceValue = currentTarget.transform;
                so.ApplyModifiedProperties();

                teleportCounter++;
            }

            currentMode = ToolMode.Idle;
            currentRoot = null;
            currentBtn = null;
            currentTarget = null;
        }

        /// <summary>
        /// 获取所有传送体
        /// </summary>
        private List<GameObject> GetAllTeleportGroups()
        {
            List<GameObject> list = new List<GameObject>();
            foreach (GameObject obj in Object.FindObjectsOfType<GameObject>())
            {
                if (IsValidTeleportGroup(obj))
                    list.Add(obj);
            }
            return list;
        }

        /// <summary>
        /// 验证是否为有效传送体
        /// </summary>
        private bool IsValidTeleportGroup(GameObject obj)
        {
            if (obj == null) return false;
            bool hasBtn = obj.transform.Find("传送按钮") != null;
            bool hasTarget = obj.transform.Find("传送目标") != null;
            if (hasBtn)
                hasBtn = obj.transform.Find("传送按钮").GetComponent<Lgc.TeleportTool.TeleportPlayer>() != null;
            return hasBtn && hasTarget;
        }

        /// <summary>
        /// 检测确认超时，自动取消
        /// </summary>
        private void CheckConfirmTimeouts()
        {
            if (isConfirmingDeleteCurrent && (Time.realtimeSinceStartup - deleteCurrentConfirmTime) >= CONFIRM_TIMEOUT)
            {
                isConfirmingDeleteCurrent = false;
            }

            if (confirmDeleteListItem != null && (Time.realtimeSinceStartup - deleteListItemConfirmTime) >= CONFIRM_TIMEOUT)
            {
                confirmDeleteListItem = null;
            }
        }

        /// <summary>
        /// 重置所有确认状态
        /// </summary>
        private void ResetAllConfirmStates()
        {
            isConfirmingDeleteCurrent = false;
            confirmDeleteListItem = null;
        }

        /// <summary>
        /// 刷新编号
        /// </summary>
        private static void RefreshCounter()
        {
            teleportCounter = 1;
            while (GameObject.Find($"传送点({teleportCounter})") != null)
                teleportCounter++;
        }
        #endregion

        // 实时刷新面板
        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
#endif