#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace Lgc.TeleportTool
{
    /// <summary>
    /// 功能：快速创建/编辑/删除场景内的传送体，支持二次确认删除+Undo撤回
    /// 新增：实时控制当前按钮的网格显示，隐藏时直接移除MeshFilter和MeshRenderer，显示时自动添加
    /// 新增：新建传送体默认放置在当前场景视图中心前方5米处，避免从原点创建的不便
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

        // 当前按钮网格显示状态（用于实时控制）
        private bool showButtonMesh = true;

        private Vector2 scrollPos;

        [MenuItem("LGC/LGC_世界传送点创建工具")]
        public static void ShowWindow()
        {
            GetWindow<LgcWorldTeleportTool>($"LGC_世界用传送点工具 v{VERSION}");
            RefreshCounter();
        }

        private void OnGUI()
        {
            DrawCenteredLabel($"LGC 传送工具 | v{VERSION} | 作者：{AUTHOR}", EditorStyles.boldLabel);
            GUILayout.Space(6);

            DrawBaseSettings();

            GUILayout.Space(8);
            DrawCenteredLabel("—————— 操作 ——————", EditorStyles.miniLabel);
            GUILayout.Space(4);

            DrawOperationButtons();

            GUILayout.Space(10);
            DrawCenteredLabel("—————— 场景传送体 ——————", EditorStyles.miniLabel);
            GUILayout.Space(4);

            DrawTeleportList();

            CheckConfirmTimeouts();
        }

        #region 基础UI方法
        private void DrawCenteredLabel(string text, GUIStyle style)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, style);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

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
                    displayText = remaining.ToString();
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

            // 当前按钮网格显示实时控制（仅编辑模式下可用）
            using (new EditorGUI.DisabledScope(currentMode == ToolMode.Idle || currentBtn == null))
            {
                EditorGUI.BeginChangeCheck();
                bool newShowState = EditorGUILayout.Toggle("显示按钮网格", showButtonMesh);
                if (EditorGUI.EndChangeCheck())
                {
                    showButtonMesh = newShowState;
                    ApplyButtonMeshVisibility(showButtonMesh);
                }
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

            // 5. 删除当前传送体
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
                        if (currentRoot != null)
                        {
                            Undo.DestroyObjectImmediate(currentRoot);
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
                    if (GUILayout.Button(teleportObj.name, GUILayout.Height(24)))
                    {
                        SelectTeleportForEdit(teleportObj);
                        ResetAllConfirmStates();
                    }

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
                            if (teleportObj != null)
                            {
                                Undo.DestroyObjectImmediate(teleportObj);
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

        #region 核心逻辑
        /// <summary>
        /// 获取当前场景视图中心前方一定距离的位置
        /// </summary>
        private Vector3 GetSceneViewCenterPosition(float distance = 1f)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                Camera cam = sceneView.camera;
                return cam.transform.position + cam.transform.forward * distance;
            }
            return Vector3.zero; // 回退到原点
        }

        private void CreateNewTeleport()
        {
            Vector3 spawnPos = GetSceneViewCenterPosition();

            currentRoot = new GameObject($"{teleportBaseName}({teleportCounter})");
            Undo.RegisterCreatedObjectUndo(currentRoot, $"Create Teleport Group: {currentRoot.name}");

            currentBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(currentBtn, $"Create Teleport Button: {currentBtn.name}");
            currentBtn.name = "传送按钮";
            currentBtn.transform.SetParent(currentRoot.transform);
            currentBtn.transform.position = spawnPos;
            currentBtn.transform.localScale = Vector3.one * 0.1f;
            currentBtn.GetComponent<BoxCollider>().isTrigger = true;

            currentTarget = new GameObject("传送目标");
            Undo.RegisterCreatedObjectUndo(currentTarget, $"Create Teleport Target: {currentTarget.name}");
            currentTarget.transform.SetParent(currentRoot.transform);
            currentTarget.transform.position = spawnPos + new Vector3(0, 0, 2);

            currentMode = ToolMode.Creating;
            currentAdjust = AdjustPart.Button;
            Selection.activeGameObject = currentBtn;

            UpdateButtonMeshVisibilityState();
        }

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
                UpdateButtonMeshVisibilityState();
            }
            else
            {
                currentMode = ToolMode.Idle;
                currentRoot = null;
            }
        }

        /// <summary>
        /// 更新“显示按钮网格”复选框的状态，使其与当前按钮的实际MeshRenderer存在状态同步
        /// </summary>
        private void UpdateButtonMeshVisibilityState()
        {
            if (currentBtn == null)
            {
                showButtonMesh = true;
                return;
            }
            // 如果存在MeshRenderer，则认为网格可见
            showButtonMesh = (currentBtn.GetComponent<MeshRenderer>() != null);
        }

        /// <summary>
        /// 应用网格显示状态：根据showButtonMesh添加或移除MeshFilter和MeshRenderer
        /// </summary>
        private void ApplyButtonMeshVisibility(bool visible)
        {
            if (currentBtn == null) return;

            if (visible)
            {
                // 需要显示网格：确保MeshFilter和MeshRenderer存在
                MeshFilter mf = currentBtn.GetComponent<MeshFilter>();
                if (mf == null)
                {
                    mf = currentBtn.AddComponent<MeshFilter>();
                    mf.sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    Undo.RegisterCreatedObjectUndo(mf, "Add MeshFilter");
                }

                MeshRenderer mr = currentBtn.GetComponent<MeshRenderer>();
                if (mr == null)
                {
                    mr = currentBtn.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
                    Undo.RegisterCreatedObjectUndo(mr, "Add MeshRenderer");
                }
            }
            else
            {
                // 隐藏网格：移除MeshFilter和MeshRenderer组件
                RemoveMeshComponents(currentBtn);
            }

            // 更新复选框状态
            showButtonMesh = visible;
        }

        /// <summary>
        /// 移除按钮上的MeshFilter和MeshRenderer组件，并注册Undo
        /// </summary>
        private void RemoveMeshComponents(GameObject btn)
        {
            MeshFilter mf = btn.GetComponent<MeshFilter>();
            if (mf != null)
            {
                Undo.DestroyObjectImmediate(mf);
            }

            MeshRenderer mr = btn.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Undo.DestroyObjectImmediate(mr);
            }
        }

        private void FinishEditMode()
        {
            if (currentMode == ToolMode.Creating && currentBtn != null && currentTarget != null)
            {
                var teleportScript = currentBtn.GetComponent<TeleportPlayer>();
                if (teleportScript == null)
                {
                    teleportScript = currentBtn.AddComponent<TeleportPlayer>();
                    Undo.RegisterCreatedObjectUndo(teleportScript, $"Add Teleport Script to {currentBtn.name}");
                }

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

        private bool IsValidTeleportGroup(GameObject obj)
        {
            if (obj == null) return false;
            Transform btnTransform = obj.transform.Find("传送按钮");
            if (btnTransform == null) return false;
            Transform targetTransform = obj.transform.Find("传送目标");
            if (targetTransform == null) return false;
            return btnTransform.GetComponent<TeleportPlayer>() != null;
        }

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

        private void ResetAllConfirmStates()
        {
            isConfirmingDeleteCurrent = false;
            confirmDeleteListItem = null;
        }

        private static void RefreshCounter()
        {
            teleportCounter = 1;
            while (GameObject.Find($"传送点({teleportCounter})") != null)
                teleportCounter++;
        }
        #endregion

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
#endif
