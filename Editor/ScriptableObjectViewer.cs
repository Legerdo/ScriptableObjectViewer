using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ScriptableObjectViewer
{
    public class ScriptableObjectViewer : EditorWindow
    {
        #region Fields and Variables

        // UI 관련 변수들
        private Vector2 itemSize = new Vector2(350f, 300f);
        private float zoomFactor = 1f;

        // 스크롤 위치 저장 변수들
        private Vector2 scrollPos;
        private Vector2 categoryScrollPos;
        private Vector2 selectedObjectsScrollPos;

        // ScriptableObject 관리 변수들
        private Dictionary<string, List<ScriptableObject>> scriptableObjectsByCategory = new Dictionary<string, List<ScriptableObject>>();
        private List<string> categoryList = new List<string>();
        private Dictionary<string, bool> categoryToggleStates = new Dictionary<string, bool>();

        // 검색 문자열
        private string searchString = "";
        private string categorySearchString = "";

        // 선택된 ScriptableObject 리스트
        public List<ScriptableObject> selectedObjects = new List<ScriptableObject>();

        // 제외된 카테고리 리스트
        private List<string> excludedCategories = new List<string>();

        // 카테고리 관리 객체
        private ScriptableObjectCategoryManager categoryManager;

        // 탭 관리 변수들
        private int selectedTab = 0;
        private readonly string[] tabs = { "Categories", "Excluded Categories" };

        // 패키지 포함 여부
        private bool includePackages = false;

        // 사용자 정의 카테고리
        private HashSet<string> userDefinedCategories = new HashSet<string>();

        // 공통 필드 편집 기능을 위한 변수들
        private bool isEditingCommonFields = false;
        private List<SerializedProperty> commonSerializedProperties = new List<SerializedProperty>();
        private List<SerializedObject> serializedObjects = new List<SerializedObject>();

        // 변경된 필드 경로를 추적하기 위한 변수
        private HashSet<string> changedPropertyPaths = new HashSet<string>();

        // 새 카테고리 생성 관련 변수
        private bool isCreatingNewCategory = false;
        private string newCategoryName = "";

        // 삭제 대기 중인 ScriptableObject 리스트
        private List<ScriptableObject> pendingDeletions = new List<ScriptableObject>();

        // 이름 변경 관련 변수
        private ScriptableObject objectToRename = null;
        private string newObjectName = "";

        // 데이터 저장 키
        private const string SelectedObjectsKey = "SOV_SelectedObjects";
        private const string CategoryToggleStatesKey = "SOV_CategoryToggleStates";

        // 선택된 ScriptableObject을 처리하기 위한 정적 변수
        private static ScriptableObject pendingSelection;

        // 선택된 오브젝트 및 카테고리 상태를 저장하는 클래스
        [System.Serializable]
        private class SelectedObjectsWrapper
        {
            public List<string> selectedGuids;
        }

        [System.Serializable]
        private class CategoryToggleState
        {
            public string categoryName;
            public bool isToggled;
        }

        [System.Serializable]
        private class CategoryToggleStateWrapper
        {
            public List<CategoryToggleState> categoryStates;
        }

        // 패널 크기 조절을 위한 변수들
        private float leftPanelWidth = 300f;
        private float centralPanelWidth = 155f;
        private bool isResizingLeftPanel = false;
        private bool isResizingCentralPanel = false;
        private Rect leftPanelRect;
        private Rect centralPanelRect;
        private Rect leftPanelResizeHandleRect;
        private Rect centralPanelResizeHandleRect;

        // Editor 인스턴스 캐싱을 위한 딕셔너리
        private Dictionary<ScriptableObject, Editor> editorCache = new Dictionary<ScriptableObject, Editor>();

        #endregion

        #region EditorWindow Methods

        [MenuItem("Window/ScriptableObject Viewer")]
        public static void ShowWindow()
        {
            GetWindow<ScriptableObjectViewer>("ScriptableObject Viewer");
        }

        public static void ShowWindow(ScriptableObject so)
        {
            pendingSelection = so;
            ScriptableObjectViewer window = GetWindow<ScriptableObjectViewer>("ScriptableObject Viewer");
            window.Focus();
        }

        private void OnEnable()
        {
            categoryToggleStates = new Dictionary<string, bool>();
            LoadOrCreateCategoryManager();
            excludedCategories = new List<string>(categoryManager.excludedCategories);
            LoadAllScriptableObjects();

            AssetReferenceCache.CacheRefreshed += OnAssetReferenceCacheRefreshed;

            // 데이터 불러오기
            LoadSelectedObjects();
            LoadCategoryToggleStates();

            // Pending Selection 처리
            if (pendingSelection != null)
            {
                SelectScriptableObject(pendingSelection);
                pendingSelection = null;
            }
        }

        private void OnDisable()
        {
            AssetReferenceCache.CacheRefreshed -= OnAssetReferenceCacheRefreshed;

            // 데이터 저장
            SaveSelectedObjects();
            SaveCategoryToggleStates();

            // 캐시된 Editor 인스턴스 정리
            foreach (var editor in editorCache.Values)
            {
                if (editor != null)
                {
                    DestroyImmediate(editor);
                }
            }

            editorCache.Clear();
        }

        private void OnGUI()
        {
            try
            {
                DrawTopToolbar();
                GUILayout.Space(10);

                // 마우스 이벤트 처리
                ProcessEvents(Event.current);

                // 패널 영역 계산
                leftPanelRect = new Rect(0, 40, leftPanelWidth, position.height - 40);
                leftPanelResizeHandleRect = new Rect(leftPanelWidth - 2, 40, 4, position.height - 40);

                centralPanelRect = new Rect(leftPanelWidth, 40, centralPanelWidth, position.height - 40);
                centralPanelResizeHandleRect = new Rect(leftPanelWidth + centralPanelWidth - 2, 40, 4, position.height - 40);

                Rect rightPanelRect = new Rect(leftPanelWidth + centralPanelWidth, 40, position.width - leftPanelWidth - centralPanelWidth, position.height - 40);

                // 왼쪽 패널 그리기
                GUILayout.BeginArea(leftPanelRect);
                DrawTabs();
                GUILayout.EndArea();

                // 왼쪽 패널 리사이즈 핸들 그리기 (선으로 표시)
                EditorGUI.DrawRect(leftPanelResizeHandleRect, Color.gray);

                // 중앙 패널 그리기
                GUILayout.BeginArea(centralPanelRect);
                DrawSearchBar();
                GUILayout.Space(10);
                DrawSelectDeselectButtons();
                GUILayout.Space(10);
                DrawScriptableObjectsList();
                GUILayout.EndArea();

                // 중앙 패널 리사이즈 핸들 그리기 (선으로 표시)
                EditorGUI.DrawRect(centralPanelResizeHandleRect, Color.gray);

                // 오른쪽 패널 그리기
                GUILayout.BeginArea(rightPanelRect);

                if (isEditingCommonFields)
                {
                    DrawCommonFieldsEditor();
                }
                else
                {
                    DrawSelectedObjectsDetails();
                }

                GUILayout.EndArea();

                // 리사이즈 핸들 영역에서 커서 모양 변경
                EditorGUIUtility.AddCursorRect(leftPanelResizeHandleRect, MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(centralPanelResizeHandleRect, MouseCursor.ResizeHorizontal);

                // 새 카테고리 생성 팝업 처리
                if (isCreatingNewCategory)
                {
                    DrawNewCategoryPopup();
                }

                // Repaint 호출하여 UI 업데이트
                if (GUI.changed)
                {
                    Repaint();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"오류 발생: {e.Message}");
            }
        }

        private void ProcessEvents(Event e)
        {
            // 왼쪽 패널 리사이즈 핸들 영역에서의 이벤트 처리
            if (e.type == EventType.MouseDown && leftPanelResizeHandleRect.Contains(e.mousePosition))
            {
                isResizingLeftPanel = true;
            }

            if (isResizingLeftPanel)
            {
                if (e.type == EventType.MouseDrag)
                {
                    leftPanelWidth += e.delta.x;
                    leftPanelWidth = Mathf.Clamp(leftPanelWidth, 100f, position.width - 200f);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    isResizingLeftPanel = false;
                }
            }

            // 중앙 패널 리사이즈 핸들 영역에서의 이벤트 처리
            if (e.type == EventType.MouseDown && centralPanelResizeHandleRect.Contains(e.mousePosition))
            {
                isResizingCentralPanel = true;
            }

            if (isResizingCentralPanel)
            {
                if (e.type == EventType.MouseDrag)
                {
                    centralPanelWidth += e.delta.x;
                    centralPanelWidth = Mathf.Clamp(centralPanelWidth, 100f, position.width - leftPanelWidth - 100f);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    isResizingCentralPanel = false;
                }
            }
        }

        private void SelectScriptableObject(ScriptableObject so)
        {
            // 기존 선택 상태 초기화
            selectedObjects.Clear();
            categoryToggleStates.Clear();

            // 소속 카테고리 찾기
            string category = GetCategoryForScriptableObject(so);

            if (!string.IsNullOrEmpty(category))
            {
                // 카테고리 토글 상태 활성화
                if (categoryToggleStates.ContainsKey(category))
                {
                    categoryToggleStates[category] = true;
                }
                else
                {
                    categoryToggleStates.Add(category, true);
                }

                // selectedObjects에 추가
                if (!selectedObjects.Contains(so))
                {
                    selectedObjects.Add(so);
                }

                // Repaint하여 UI 업데이트
                Repaint();
            }
        }

        private string GetCategoryForScriptableObject(ScriptableObject so)
        {
            // categoryManager를 통해 카테고리 찾기
            var entry = categoryManager.entries.Find(e => e.scriptableObject == so);
            if (entry != null && !string.IsNullOrEmpty(entry.category))
            {
                return entry.category;
            }

            // 카테고리가 지정되지 않았다면 타입 이름 사용
            return so.GetType().Name;
        }

        #endregion

        #region Data Persistence Methods

        // 선택된 오브젝트 및 카테고리 토글 상태를 저장 및 로드하는 메서드들

        private void SaveSelectedObjects()
        {
            // 선택된 ScriptableObject들을 저장
            List<string> selectedGuids = selectedObjects
                .Where(obj => obj != null)
                .Select(obj => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj)))
                .ToList();

            // EditorPrefs에 JSON으로 저장
            EditorPrefs.SetString(SelectedObjectsKey, JsonUtility.ToJson(new SelectedObjectsWrapper { selectedGuids = selectedGuids }));
        }

        private void LoadSelectedObjects()
        {
            // EditorPrefs에서 JSON으로 불러오기
            if (EditorPrefs.HasKey(SelectedObjectsKey))
            {
                string json = EditorPrefs.GetString(SelectedObjectsKey);
                SelectedObjectsWrapper wrapper = JsonUtility.FromJson<SelectedObjectsWrapper>(json);

                selectedObjects = wrapper.selectedGuids
                    .Select(guid => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid)))
                    .Where(obj => obj != null)
                    .ToList();
            }
        }

        private void SaveCategoryToggleStates()
        {
            // 카테고리 토글 상태를 저장
            List<CategoryToggleState> categoryStates = categoryToggleStates.Select(kvp => new CategoryToggleState { categoryName = kvp.Key, isToggled = kvp.Value }).ToList();
            EditorPrefs.SetString(CategoryToggleStatesKey, JsonUtility.ToJson(new CategoryToggleStateWrapper { categoryStates = categoryStates }));
        }

        private void LoadCategoryToggleStates()
        {
            // EditorPrefs에서 카테고리 토글 상태 불러오기
            if (EditorPrefs.HasKey(CategoryToggleStatesKey))
            {
                string json = EditorPrefs.GetString(CategoryToggleStatesKey);
                CategoryToggleStateWrapper wrapper = JsonUtility.FromJson<CategoryToggleStateWrapper>(json);

                foreach (var state in wrapper.categoryStates)
                {
                    categoryToggleStates[state.categoryName] = state.isToggled;
                }
            }
        }

        #endregion

        #region Event Handlers

        // 캐시가 갱신되었을 때 호출되는 이벤트 핸들러
        private void OnAssetReferenceCacheRefreshed()
        {
            LoadAllScriptableObjects();
            Repaint(); // 에디터 창을 다시 그려 변경 사항을 반영
        }

        #endregion

        #region UI Drawing Methods

        // UI를 그리는 메서드들

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh"))
            {
                LoadAllScriptableObjects();
            }

            DrawIncludePackagesToggle();

            // "Edit Common Fields" 버튼을 최소 두 개 이상의 ScriptableObject가 선택되었을 때만 표시
            if (selectedObjects.Count >= 2)
            {
                bool newIsEditingCommonFields = EditorGUILayout.ToggleLeft("Edit Common Fields", isEditingCommonFields, GUILayout.Width(150));
                if (newIsEditingCommonFields != isEditingCommonFields)
                {
                    isEditingCommonFields = newIsEditingCommonFields;
                    if (isEditingCommonFields)
                    {
                        FindCommonFields();
                    }
                    else
                    {
                        // 편집 모드 해제 시 공통 필드 초기화
                        commonSerializedProperties.Clear();
                        serializedObjects.Clear();
                        changedPropertyPaths.Clear();
                    }
                }
            }
            else
            {
                // 선택된 객체가 2개 미만일 때 편집 모드 강제 해제
                if (isEditingCommonFields)
                {
                    isEditingCommonFields = false;
                    commonSerializedProperties.Clear();
                    serializedObjects.Clear();
                    changedPropertyPaths.Clear();
                }
            }

            if (GUILayout.Button("Save"))
            {
                SaveExcludedCategories();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIncludePackagesToggle()
        {
            EditorGUILayout.BeginHorizontal();
            bool newIncludePackages = includePackages;

            // 토글 그리기
            newIncludePackages = EditorGUILayout.Toggle(newIncludePackages, GUILayout.Width(20));

            // 레이블을 버튼으로 만들어 클릭 시 토글
            GUIStyle labelButtonStyle = new GUIStyle(GUI.skin.label);
            labelButtonStyle.normal.textColor = GUI.skin.label.normal.textColor;
            labelButtonStyle.hover.textColor = GUI.skin.label.hover.textColor;

            GUIContent content = new GUIContent("Include Packages");
            float textWidth = labelButtonStyle.CalcSize(content).x;

            Rect labelRect = GUILayoutUtility.GetRect(content, labelButtonStyle, GUILayout.Width(textWidth));

            if (GUI.Button(labelRect, content, labelButtonStyle))
            {
                newIncludePackages = !newIncludePackages;
            }

            EditorGUILayout.EndHorizontal();

            if (newIncludePackages != includePackages)
            {
                includePackages = newIncludePackages;
                LoadAllScriptableObjects();
            }
        }

        private void DrawTabs()
        {
            selectedTab = GUILayout.Toolbar(selectedTab, tabs);
            GUILayout.Space(10);

            if (selectedTab == 0)
            {
                DrawCategoriesTab();
            }
            else if (selectedTab == 1)
            {
                DrawExcludedCategoriesTab();
            }
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search", GUILayout.Width(50));
            string newSearchString = EditorGUILayout.TextField(searchString);
            EditorGUILayout.EndHorizontal();

            if (newSearchString != searchString)
            {
                searchString = newSearchString;
            }
        }

        private void DrawSelectDeselectButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                List<ScriptableObject> displayedSelectScriptableObjects = GetDisplayedScriptableObjects();
                foreach (var so in displayedSelectScriptableObjects)
                {
                    if (so != null && !selectedObjects.Contains(so))
                    {
                        selectedObjects.Add(so);
                    }
                }
            }
            if (GUILayout.Button("Deselect All"))
            {
                List<ScriptableObject> displayedDeselectScriptableObjects = GetDisplayedScriptableObjects();
                foreach (var so in displayedDeselectScriptableObjects)
                {
                    if (so != null && selectedObjects.Contains(so))
                    {
                        selectedObjects.Remove(so);
                    }
                }

                // Deselect All 시 편집 모드 해제
                isEditingCommonFields = false;
                commonSerializedProperties.Clear();
                serializedObjects.Clear();
                changedPropertyPaths.Clear();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawScriptableObjectsList()
        {
            List<ScriptableObject> displayedScriptableObjects = GetDisplayedScriptableObjects();

            if (!string.IsNullOrEmpty(searchString))
            {
                displayedScriptableObjects = displayedScriptableObjects.FindAll(so =>
                    so.name.IndexOf(searchString, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            foreach (var so in displayedScriptableObjects)
            {
                if (so == null) continue;

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button(EditorGUIUtility.IconContent("d_Folder Icon"), GUILayout.Width(20), GUILayout.Height(20)))
                {
                    EditorGUIUtility.PingObject(so);
                }

                bool isSelected = selectedObjects.Contains(so);
                bool newIsSelected = EditorGUILayout.ToggleLeft(so.name, isSelected, GUILayout.Width(200));

                if (newIsSelected != isSelected)
                {
                    if (newIsSelected)
                    {
                        selectedObjects.Add(so);
                    }
                    else
                    {
                        selectedObjects.Remove(so);
                    }

                    if (isEditingCommonFields)
                    {
                        FindCommonFields();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawNewCategoryPopup()
        {
            // 팝업 창의 크기 설정
            float popupWidth = 300;
            float popupHeight = 100;

            // 팝업 창을 중앙에 배치
            Rect popupRect = new Rect((position.width - popupWidth) / 2, (position.height - popupHeight) / 2, popupWidth, popupHeight);
            GUI.Box(popupRect, "Create New Category");

            GUILayout.BeginArea(popupRect);

            GUILayout.Space(20);

            EditorGUILayout.LabelField("Category Name:");
            newCategoryName = EditorGUILayout.TextField(newCategoryName);

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("OK"))
            {
                if (string.IsNullOrEmpty(newCategoryName.Trim()))
                {
                    EditorUtility.DisplayDialog("Error", "카테고리 이름을 입력해주세요.", "OK");
                }
                else if (categoryList.Contains(newCategoryName))
                {
                    EditorUtility.DisplayDialog("Error", "이미 존재하는 카테고리 이름입니다.", "OK");
                }
                else
                {
                    AssignSelectedObjectsToCategory(newCategoryName.Trim());
                    isCreatingNewCategory = false;
                    newCategoryName = "";
                }
            }
            if (GUILayout.Button("Cancel"))
            {
                isCreatingNewCategory = false;
                newCategoryName = "";
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void DrawCommonFieldsEditor()
        {
            if (selectedObjects.Count < 2)
            {
                EditorGUILayout.LabelField("공통 필드를 편집하려면 최소 두 개의 ScriptableObject를 선택하세요.", EditorStyles.wordWrappedLabel);
                return;
            }

            if (commonSerializedProperties.Count == 0)
            {
                EditorGUILayout.LabelField("선택된 오브젝트 간에 공통 필드를 찾을 수 없습니다.", EditorStyles.wordWrappedLabel);
                return;
            }

            EditorGUILayout.LabelField("공통 필드 편집:", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            foreach (var prop in commonSerializedProperties)
            {
                EditorGUI.BeginChangeCheck();
                if (prop.propertyType == SerializedPropertyType.LayerMask)
                {
                    DrawCustomLayerMaskField(prop);
                }
                else
                {
                    EditorGUILayout.PropertyField(prop, true);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    changedPropertyPaths.Add(prop.propertyPath);
                }
            }

            if (changedPropertyPaths.Count > 0)
            {
                if (GUILayout.Button("Apply Changes"))
                {
                    ApplyCommonFieldChanges();
                }
            }
        }

        private void DrawSelectedObjectsDetails()
        {
            if (selectedObjects.Count > 0)
            {
                EditorGUILayout.LabelField("선택된 오브젝트:", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                // 선택된 오브젝트 수가 매우 많을 때 경고 메시지 표시
                if (selectedObjects.Count > 500)
                {
                    EditorGUILayout.HelpBox("선택된 오브젝트가 너무 많습니다. 일부만 표시됩니다.", MessageType.Warning);
                }

                // 줌 컨트롤 추가
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("줌:", GUILayout.Width(30));
                zoomFactor = EditorGUILayout.Slider(zoomFactor, 0.5f, 2f);
                if (GUILayout.Button("리셋", GUILayout.Width(50)))
                {
                    zoomFactor = 1f;
                }
                EditorGUILayout.EndHorizontal();

                // "Create New Category" 버튼 추가
                GUILayout.Space(10);
                EditorGUI.BeginDisabledGroup(selectedObjects.Count < 1);
                if (GUILayout.Button("Create New Category"))
                {
                    isCreatingNewCategory = true;
                    newCategoryName = "";
                }
                EditorGUI.EndDisabledGroup();

                float availableWidth = position.width - leftPanelWidth - centralPanelWidth;
                float availableHeight = position.height - 40;

                float itemWidth = itemSize.x * zoomFactor;
                float itemHeight = itemSize.y * zoomFactor;

                int itemsPerRow = Mathf.Max(1, Mathf.FloorToInt(availableWidth / itemWidth));

                // selectedObjects의 복사본을 만들어 반복
                List<ScriptableObject> selectedObjectsCopy = new List<ScriptableObject>(selectedObjects);

                // 스크롤 영역 시작
                selectedObjectsScrollPos = EditorGUILayout.BeginScrollView(selectedObjectsScrollPos, GUILayout.Height(availableHeight));

                int totalItems = selectedObjectsCopy.Count;
                int totalRows = Mathf.CeilToInt((float)totalItems / itemsPerRow);
                float totalHeightCalc = totalRows * itemHeight;

                // 보이는 영역 계산
                float scrollY = selectedObjectsScrollPos.y;
                float visibleHeight = availableHeight;

                int firstVisibleRow = Mathf.FloorToInt(scrollY / itemHeight);
                int lastVisibleRow = Mathf.Min(totalRows, Mathf.CeilToInt((scrollY + visibleHeight) / itemHeight));

                int firstVisibleIndex = firstVisibleRow * itemsPerRow;
                int lastVisibleIndex = Mathf.Min(totalItems, lastVisibleRow * itemsPerRow);

                // 보이지 않는 위쪽 영역에 대한 공간 확보
                GUILayout.Space(firstVisibleRow * itemHeight);

                for (int i = firstVisibleIndex; i < lastVisibleIndex; i++)
                {
                    ScriptableObject so = selectedObjectsCopy[i];
                    if (so == null) continue;

                    if (i % itemsPerRow == 0)
                    {
                        EditorGUILayout.BeginHorizontal();
                    }

                    EditorGUILayout.BeginVertical("box", GUILayout.Width(itemWidth), GUILayout.Height(itemHeight));

                    EditorGUILayout.LabelField(so.name, EditorStyles.boldLabel);

                    // 버튼들을 가로로 배치하기 위해 BeginHorizontal 사용
                    EditorGUILayout.BeginHorizontal();

                    // Reset 버튼
                    var resetContent = new GUIContent(EditorGUIUtility.IconContent("d_TreeEditor.Refresh").image, "Reset this ScriptableObject");

                    if (CreateButton(resetContent, GUILayout.Width(20), GUILayout.Height(20)))
                    {
                        if (EditorUtility.DisplayDialog("리셋 확인", $"'{so.name}' 오브젝트를 리셋하시겠습니까?", "예", "아니오"))
                        {
                            ResetScriptableObject(so);
                        }
                    }

                    // Ping 버튼
                    var pingContent = new GUIContent(EditorGUIUtility.IconContent("d_FolderOpened Icon").image, "Ping this ScriptableObject in Project");

                    if (CreateButton(pingContent, GUILayout.Width(20), GUILayout.Height(20)))
                    {
                        EditorGUIUtility.PingObject(so);  // 선택된 오브젝트 핑
                    }

                    // 참조 찾기 버튼
                    var referencesContent = new GUIContent(EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow").image, "Show Asset References");

                    if (CreateButton(referencesContent, GUILayout.Width(20), GUILayout.Height(20)))
                    {
                        ShowAssetReferences(so);
                    }

                    // 복사(Copy) 버튼 추가
                    var copyContent = new GUIContent(EditorGUIUtility.IconContent("Toolbar Plus").image, "Copy this ScriptableObject");

                    if (CreateButton(copyContent, GUILayout.Width(20), GUILayout.Height(20)))
                    {
                        if (EditorUtility.DisplayDialog("복사 확인", $"'{so.name}' 오브젝트를 복사하시겠습니까?", "예", "아니오"))
                        {
                            CopyScriptableObject(so);
                        }
                    }

                    // 삭제(Delete) 버튼 추가
                    var deleteContent = new GUIContent(EditorGUIUtility.IconContent("d_TreeEditor.Trash").image, "Delete this ScriptableObject");

                    if (CreateButton(deleteContent, GUILayout.Width(20), GUILayout.Height(20)))
                    {
                        if (EditorUtility.DisplayDialog("삭제 확인", $"'{so.name}' 오브젝트를 삭제하시겠습니까?", "예", "아니오"))
                        {
                            // 삭제 요청을 큐에 추가하고 즉시 리스트에서 제거
                            pendingDeletions.Add(so);
                            selectedObjects.Remove(so);

                            // Editor 인스턴스도 캐시에서 제거하고 해제
                            if (editorCache.TryGetValue(so, out Editor editor))
                            {
                                DestroyImmediate(editor);
                                editorCache.Remove(so);
                            }
                        }
                    }

                    // 이름 변경(Rename) 버튼 추가
                    var renameContent = new GUIContent(EditorGUIUtility.IconContent("UnityEditor.ConsoleWindow").image, "Rename this ScriptableObject");

                    if (CreateButton(renameContent, GUILayout.Width(20), GUILayout.Height(20)))
                    {
                        // Rename 창 열기
                        objectToRename = so;
                        newObjectName = so.name; // 현재 이름으로 초기화
                        RenameWindow.ShowWindow(this, so);
                    }

                    EditorGUILayout.EndHorizontal();

                    try
                    {
                        // ScriptableObjectViewer 창에서 렌더링 중임을 표시
                        GenericScriptableObjectEditor.isInViewer = true;

                        // Editor 인스턴스를 캐시에서 가져오거나 생성
                        if (!editorCache.TryGetValue(so, out Editor cachedEditor) || cachedEditor == null)
                        {
                            cachedEditor = Editor.CreateEditor(so);
                            editorCache[so] = cachedEditor;
                        }

                        if (cachedEditor != null)
                        {
                            cachedEditor.OnInspectorGUI();
                        }
                    }
                    catch (System.Exception e)
                    {
                        EditorGUILayout.HelpBox($"오브젝트 표시 중 오류 발생: {e.Message}", MessageType.Error);
                    }
                    finally
                    {
                        // 렌더링 후 플래그를 해제
                        GenericScriptableObjectEditor.isInViewer = false;
                    }

                    EditorGUILayout.EndVertical();

                    if ((i + 1) % itemsPerRow == 0 || i == lastVisibleIndex - 1)
                    {
                        EditorGUILayout.EndHorizontal();
                    }
                }

                // 보이지 않는 아래쪽 영역에 대한 공간 확보
                int remainingRows = totalRows - lastVisibleRow;
                if (remainingRows > 0)
                {
                    GUILayout.Space(remainingRows * itemHeight);
                }

                EditorGUILayout.EndScrollView();

                // GUI 루프가 끝난 후 삭제 작업을 스케줄
                if (pendingDeletions.Count > 0)
                {
                    List<ScriptableObject> deletions = new List<ScriptableObject>(pendingDeletions);
                    pendingDeletions.Clear();
                    EditorApplication.delayCall += () =>
                    {
                        foreach (var obj in deletions)
                        {
                            DeleteScriptableObject(obj);
                        }
                    };
                }
            }
            else
            {
                EditorGUILayout.LabelField("선택된 오브젝트가 없습니다.", EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawCategoriesTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Category Search", GUILayout.Width(100));
            string newCategorySearchString = EditorGUILayout.TextField(categorySearchString);
            EditorGUILayout.EndHorizontal();

            if (newCategorySearchString != categorySearchString)
            {
                categorySearchString = newCategorySearchString;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                foreach (var categoryName in categoryList)
                {
                    if (!excludedCategories.Contains(categoryName))
                    {
                        categoryToggleStates[categoryName] = true;
                    }
                }
            }
            if (GUILayout.Button("Deselect All"))
            {
                foreach (var categoryName in categoryList)
                {
                    if (!excludedCategories.Contains(categoryName))
                    {
                        categoryToggleStates[categoryName] = false;
                        ToggleCategorySelection(categoryName, false);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            categoryScrollPos = EditorGUILayout.BeginScrollView(categoryScrollPos);

            // 즐겨찾기된 카테고리 먼저 표시
            var favoriteCategoriesToDisplay = categoryList
                .Where(c => categoryManager.favoriteCategories.Contains(c) && !excludedCategories.Contains(c) &&
                            (string.IsNullOrEmpty(categorySearchString) ||
                             c.IndexOf(categorySearchString, System.StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            foreach (var categoryName in favoriteCategoriesToDisplay)
            {
                DrawCategoryItem(categoryName, isFavorite: true);
            }

            // 나머지 카테고리 표시
            var nonFavoriteCategoriesToDisplay = categoryList
                .Where(c => !categoryManager.favoriteCategories.Contains(c) && !excludedCategories.Contains(c) &&
                            (string.IsNullOrEmpty(categorySearchString) ||
                             c.IndexOf(categorySearchString, System.StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            foreach (var categoryName in nonFavoriteCategoriesToDisplay)
            {
                DrawCategoryItem(categoryName, isFavorite: false);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawExcludedCategoriesTab()
        {
            EditorGUILayout.LabelField("Excluded Categories:", EditorStyles.boldLabel);

            categoryScrollPos = EditorGUILayout.BeginScrollView(categoryScrollPos);

            // 사용 가능한 한 번에 여러 항목을 처리하도록 수정하여 성능 향상
            for (int i = 0; i < excludedCategories.Count; i++)
            {
                string categoryName = excludedCategories[i];

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(categoryName);

                if (CreateButton(new GUIContent("Include"), GUILayout.Width(60), GUILayout.Height(20)))
                {
                    excludedCategories.RemoveAt(i);
                    i--;

                    SaveExcludedCategories();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawCategoryItem(string categoryName, bool isFavorite)
        {
            // 카테고리가 선택되었는지 확인
            bool isSelected = categoryToggleStates.ContainsKey(categoryName) && categoryToggleStates[categoryName];

            // 원래의 배경 색상을 저장
            Color originalColor = GUI.backgroundColor;

            // 선택 상태에 따른 배경 색상 설정
            GUI.backgroundColor = GetCategoryBackgroundColor(categoryName, isSelected);

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            DrawFavoriteButton(categoryName, isFavorite);
            DrawRemoveButton(categoryName);
            DrawExcludeButton(categoryName);
            DrawCategoryNameButton(categoryName);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            // 원래의 배경 색상으로 복원
            GUI.backgroundColor = originalColor;
        }

        #endregion

        #region Helper Methods

        // 다양한 보조 메서드들

        private void LoadOrCreateCategoryManager()
        {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObjectCategoryManager");

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                categoryManager = AssetDatabase.LoadAssetAtPath<ScriptableObjectCategoryManager>(path);
            }
            else
            {
                categoryManager = ScriptableObject.CreateInstance<ScriptableObjectCategoryManager>();
                AssetDatabase.CreateAsset(categoryManager, "Assets/ScriptableObjectCategoryManager.asset");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            MonoScript script = MonoScript.FromScriptableObject(categoryManager);
            if (script == null)
            {
                Debug.LogError("ScriptableObjectCategoryManager 스크립트가 올바르게 컴파일되지 않았습니다.");
            }
        }

        private float GetMaxCategoryItemNameWidth()
        {
            float maxWidth = 100;

            List<ScriptableObject> displayedScriptableObjects = GetDisplayedScriptableObjects();

            foreach (var so in displayedScriptableObjects)
            {
                if (so == null) continue;

                float itemWidth = GUI.skin.label.CalcSize(new GUIContent(so.name)).x;

                if (itemWidth > maxWidth)
                {
                    maxWidth = itemWidth;
                }
            }

            return maxWidth;
        }

        private void DrawFavoriteButton(string categoryName, bool isFavorite)
        {
            GUIContent favoriteContent = isFavorite ? new GUIContent("★") : new GUIContent("☆");
            if (CreateButton(favoriteContent, GUILayout.Width(20), GUILayout.Height(20)))
            {
                ToggleFavoriteCategory(categoryName, isFavorite);
            }
        }

        private void ToggleFavoriteCategory(string categoryName, bool isFavorite)
        {
            if (isFavorite)
            {
                categoryManager.favoriteCategories.Remove(categoryName);
            }
            else
            {
                categoryManager.favoriteCategories.Add(categoryName);
            }
            EditorUtility.SetDirty(categoryManager);
            AssetDatabase.SaveAssets();
        }

        private void DrawRemoveButton(string categoryName)
        {
            if (userDefinedCategories.Contains(categoryName))
            {
                if (CreateButton(new GUIContent("Remove"), GUILayout.Width(60), GUILayout.Height(20)))
                {
                    if (EditorUtility.DisplayDialog("Confirm Removal", $"Are you sure you want to remove the category '{categoryName}'?", "Yes", "No"))
                    {
                        RemoveCategory(categoryName);
                    }
                }
            }
        }

        private void DrawExcludeButton(string categoryName)
        {
            if (CreateButton(new GUIContent("Exclude"), GUILayout.Width(60), GUILayout.Height(20)))
            {
                excludedCategories.Add(categoryName);
                categoryToggleStates.Remove(categoryName);
                SaveExcludedCategories();
            }
        }

        private void DrawCategoryNameButton(string categoryName)
        {
            // GUILayout.ExpandWidth을 사용해야 합니다.
            if (CreateButton(new GUIContent(categoryName), GUILayout.ExpandWidth(true), GUILayout.Height(20)))
            {
                ToggleCategorySelectionState(categoryName);
            }
        }

        private Color GetCategoryBackgroundColor(string categoryName, bool isSelected)
        {
            if (isSelected)
            {
                return new Color(0.5f, 0.7f, 1.0f); // 선택된 상태의 배경 색상 (예: 연한 파란색)
            }
            else
            {
                int index = categoryList.IndexOf(categoryName);
                return (index % 2 == 0) ? new Color(0.9f, 0.9f, 0.9f) : new Color(0.8f, 0.8f, 0.8f);
            }
        }

        private void ToggleCategorySelectionState(string categoryName)
        {
            if (categoryToggleStates.ContainsKey(categoryName))
            {
                bool isCurrentlySelected = categoryToggleStates[categoryName];
                categoryToggleStates[categoryName] = !isCurrentlySelected;
                ToggleCategorySelection(categoryName, !isCurrentlySelected);
            }
            else
            {
                categoryToggleStates[categoryName] = true;
                ToggleCategorySelection(categoryName, true);
            }

            if (isEditingCommonFields)
            {
                FindCommonFields();
            }
        }

        private List<ScriptableObject> GetDisplayedScriptableObjects()
        {
            List<ScriptableObject> result = new List<ScriptableObject>();

            foreach (var categoryName in categoryList)
            {
                if (excludedCategories.Contains(categoryName))
                    continue;

                if (categoryToggleStates.TryGetValue(categoryName, out bool isToggled) && isToggled)
                {
                    if (scriptableObjectsByCategory.TryGetValue(categoryName, out List<ScriptableObject> soList))
                    {
                        result.AddRange(soList.Where(so => so != null));
                    }
                }
            }

            return result.Distinct().ToList();
        }

        private void ToggleCategorySelection(string categoryName, bool isSelected)
        {
            if (scriptableObjectsByCategory.TryGetValue(categoryName, out List<ScriptableObject> soList))
            {
                foreach (var so in soList)
                {
                    if (!isSelected && selectedObjects.Contains(so))
                    {
                        selectedObjects.Remove(so);
                    }
                }

                if (isEditingCommonFields)
                {
                    FindCommonFields();
                }
            }
        }

        private void LoadAllScriptableObjects()
        {
            EditorUtility.DisplayProgressBar("Loading ScriptableObjects", "Scanning project for ScriptableObjects...", 0f);

            scriptableObjectsByCategory.Clear();

            selectedObjects.Clear();
            userDefinedCategories.Clear();
            categoryList.Clear();

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");

            int total = guids.Length;
            for (int i = 0; i < guids.Length; i++)
            {
                string guid = guids[i];
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 경로 필터링 최적화
                if (path.Contains("/Editor/") || path.Contains("/Tests/") || path.EndsWith(".cs"))
                    continue;

                ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (so == null || so == categoryManager)
                    continue;

                MonoScript monoScript = MonoScript.FromScriptableObject(so);
                if (monoScript == null)
                    continue;

                string scriptPath = AssetDatabase.GetAssetPath(monoScript);

                if (!includePackages && !scriptPath.StartsWith("Assets/"))
                {
                    continue;
                }

                string category = so.GetType().Name;

                var entry = categoryManager.entries.Find(e => e.scriptableObject == so);
                if (entry != null && !string.IsNullOrEmpty(entry.category))
                {
                    category = entry.category;
                    userDefinedCategories.Add(category);
                }

                if (!scriptableObjectsByCategory.ContainsKey(category))
                {
                    scriptableObjectsByCategory[category] = new List<ScriptableObject>();
                    categoryList.Add(category);
                }

                scriptableObjectsByCategory[category].Add(so);

                // 진행 상태 업데이트
                if (i % 100 == 0)
                {
                    float progress = (float)i / total;
                    EditorUtility.DisplayProgressBar("Loading ScriptableObjects", $"Processing {i}/{total}...", progress);
                }
            }

            // Null 객체 제거 및 카테고리 정리
            foreach (var category in scriptableObjectsByCategory.Keys.ToList())
            {
                scriptableObjectsByCategory[category] = scriptableObjectsByCategory[category].Where(so => so != null).ToList();
                if (scriptableObjectsByCategory[category].Count == 0)
                {
                    scriptableObjectsByCategory.Remove(category);
                    categoryList.Remove(category);
                }
            }

            selectedObjects.RemoveAll(item => item == null);

            // 즐겨찾기된 카테고리를 먼저 정렬
            categoryList = categoryList.OrderByDescending(c => categoryManager.favoriteCategories.Contains(c)).ThenBy(c => c).ToList();

            var validCategoryNames = categoryList.Except(excludedCategories).ToList();
            var keysToRemove = categoryToggleStates.Keys.Except(validCategoryNames).ToList();
            foreach (var key in keysToRemove)
            {
                categoryToggleStates.Remove(key);
            }

            excludedCategories = excludedCategories.Intersect(categoryList).ToList();

            EditorUtility.ClearProgressBar();
        }

        private void AssignSelectedObjectsToCategory(string newCategoryName)
        {
            if (string.IsNullOrEmpty(newCategoryName))
            {
                EditorUtility.DisplayDialog("Error", "Category name cannot be empty.", "OK");
                return;
            }

            foreach (var so in selectedObjects)
            {
                var entry = categoryManager.entries.Find(e => e.scriptableObject == so);
                if (entry != null)
                {
                    entry.category = newCategoryName;
                }
                else
                {
                    categoryManager.entries.Add(new ScriptableObjectCategoryEntry()
                    {
                        scriptableObject = so,
                        category = newCategoryName
                    });
                }
            }

            EditorUtility.SetDirty(categoryManager);
            AssetDatabase.SaveAssets();

            LoadAllScriptableObjects();
        }

        private void SaveExcludedCategories()
        {
            categoryManager.excludedCategories = new List<string>(excludedCategories);
            EditorUtility.SetDirty(categoryManager);
            AssetDatabase.SaveAssets();
            Debug.Log("Excluded categories saved.");
        }

        private void RemoveCategory(string categoryName)
        {
            try
            {
                categoryManager.entries.RemoveAll(e => e.category == categoryName);

                // 즐겨찾기 목록에서도 제거
                if (categoryManager.favoriteCategories.Contains(categoryName))
                {
                    categoryManager.favoriteCategories.Remove(categoryName);
                }

                EditorUtility.SetDirty(categoryManager);
                AssetDatabase.SaveAssets();

                categoryToggleStates.Remove(categoryName);
                excludedCategories.Remove(categoryName);
                LoadAllScriptableObjects();

                Repaint(); // UI 갱신
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to remove category '{categoryName}': {ex.Message}");
            }
        }

        private void FindCommonFields()
        {
            commonSerializedProperties.Clear();
            serializedObjects.Clear();
            changedPropertyPaths.Clear(); // 변경된 필드 경로 초기화

            if (selectedObjects.Count < 2)
            {
                return;
            }

            // 초기화 단계
            SerializedObject firstSerialized = new SerializedObject(selectedObjects[0]);
            serializedObjects.Add(firstSerialized);

            FieldInfo[] firstFields = selectedObjects[0].GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            HashSet<string> commonFieldNames = new HashSet<string>(firstFields.Select(f => f.Name));

            // 공통 필드 이름 찾기
            for (int i = 1; i < selectedObjects.Count; i++)
            {
                var fields = selectedObjects[i].GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var fieldNames = new HashSet<string>(fields.Select(f => f.Name));
                commonFieldNames.IntersectWith(fieldNames);
                serializedObjects.Add(new SerializedObject(selectedObjects[i]));
                if (commonFieldNames.Count == 0)
                    break;
            }

            if (commonFieldNames.Count == 0)
                return;

            // 필드 타입 일치 확인 및 공통 필드 저장
            foreach (var fieldName in commonFieldNames.ToList())
            {
                FieldInfo referenceField = selectedObjects[0].GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (referenceField == null)
                {
                    commonFieldNames.Remove(fieldName);
                    continue;
                }

                bool typeMismatch = false;
                for (int i = 1; i < selectedObjects.Count; i++)
                {
                    var field = selectedObjects[i].GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field == null || field.FieldType != referenceField.FieldType)
                    {
                        typeMismatch = true;
                        break;
                    }
                }

                if (typeMismatch)
                {
                    commonFieldNames.Remove(fieldName);
                }
                else
                {
                    // 공통 필드 추가
                    SerializedProperty commonProp = serializedObjects[0].FindProperty(fieldName);
                    if (commonProp != null)
                    {
                        commonSerializedProperties.Add(commonProp);
                    }
                }
            }

            if (commonSerializedProperties.Count == 0)
            {
                Debug.LogWarning("공통 필드를 찾지 못했습니다.");
            }
        }

        private void ApplyCommonFieldChanges()
        {
            bool anyChanges = false;
            bool anyErrors = false;

            if (serializedObjects.Count == 0)
                return;

            SerializedObject sourceObject = serializedObjects[0];
            sourceObject.ApplyModifiedProperties();

            foreach (string propertyPath in changedPropertyPaths)
            {
                SerializedProperty sourceProp = sourceObject.FindProperty(propertyPath);
                if (sourceProp == null)
                    continue;

                for (int i = 1; i < serializedObjects.Count; i++)
                {
                    SerializedObject targetObject = serializedObjects[i];
                    SerializedProperty targetProp = targetObject.FindProperty(propertyPath);
                    if (targetProp != null && sourceProp != null)
                    {
                        try
                        {
                            CopySerializedPropertyValue(sourceProp, targetProp);
                            targetObject.ApplyModifiedProperties();
                            EditorUtility.SetDirty(targetObject.targetObject);
                            anyChanges = true;
                        }
                        catch (System.Exception e)
                        {
                            Debug.LogError($"필드 '{propertyPath}'를 복사하는 중 오류 발생: {e.Message}");
                            anyErrors = true;
                        }
                    }
                }
            }

            if (anyChanges)
            {
                AssetDatabase.SaveAssets();
            }

            if (anyErrors)
            {
                EditorUtility.DisplayDialog("오류", "일부 필드를 업데이트하는 중 오류가 발생했습니다. 자세한 내용은 콘솔을 확인하세요.", "확인");
            }
            else if (!anyChanges)
            {
                EditorUtility.DisplayDialog("알림", "변경된 사항이 없습니다.", "확인");
            }

            // 변경된 필드 경로 초기화
            changedPropertyPaths.Clear();

            LoadSelectedObjects();
        }

        private void CopySerializedPropertyValue(SerializedProperty source, SerializedProperty dest)
        {
            switch (source.propertyType)
            {
                case SerializedPropertyType.Integer:
                    dest.intValue = source.intValue;
                    break;
                case SerializedPropertyType.Boolean:
                    dest.boolValue = source.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    dest.floatValue = source.floatValue;
                    break;
                case SerializedPropertyType.String:
                    dest.stringValue = source.stringValue;
                    break;
                case SerializedPropertyType.Color:
                    dest.colorValue = source.colorValue;
                    break;
                case SerializedPropertyType.ObjectReference:
                    dest.objectReferenceValue = source.objectReferenceValue;
                    break;
                case SerializedPropertyType.LayerMask:
                    dest.intValue = source.intValue;
                    break;
                case SerializedPropertyType.Vector2:
                    dest.vector2Value = source.vector2Value;
                    break;
                case SerializedPropertyType.Vector3:
                    dest.vector3Value = source.vector3Value;
                    break;
                case SerializedPropertyType.Vector4:
                    dest.vector4Value = source.vector4Value;
                    break;
                case SerializedPropertyType.Rect:
                    dest.rectValue = source.rectValue;
                    break;
                case SerializedPropertyType.ArraySize:
                    dest.arraySize = source.arraySize;
                    break;
                case SerializedPropertyType.Character:
                    dest.intValue = source.intValue;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    dest.animationCurveValue = source.animationCurveValue;
                    break;
                case SerializedPropertyType.Bounds:
                    dest.boundsValue = source.boundsValue;
                    break;
                case SerializedPropertyType.Quaternion:
                    dest.quaternionValue = source.quaternionValue;
                    break;
                default:
                    Debug.LogWarning($"Unsupported property type: {source.propertyType} for property {source.propertyPath}");
                    break;
            }
        }

        /// <summary>
        /// 커스텀 LayerMask 필드를 그립니다.
        /// </summary>
        /// <param name="property">LayerMask 프로퍼티</param>
        private void DrawCustomLayerMaskField(SerializedProperty property)
        {
            // 레이어 이름을 배열으로 가져옵니다.
            string[] layerNames = new string[32];
            for (int i = 0; i < 32; i++)
            {
                layerNames[i] = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(layerNames[i]))
                {
                    layerNames[i] = "Layer " + i;
                }
            }

            // 현재 LayerMask 값을 가져옵니다.
            LayerMask currentMask = property.intValue;

            // LayerMask 필드 그리기
            EditorGUI.BeginChangeCheck();
            int newMask = EditorGUILayout.MaskField(property.displayName, currentMask.value, layerNames);
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = newMask;
            }
        }

        /// <summary>
        /// 특정 ScriptableObject를 참조하는 모든 자산을 찾아 새로운 창에 표시합니다.
        /// </summary>
        /// <param name="target">참조를 찾을 ScriptableObject</param>
        private void ShowAssetReferences(ScriptableObject target)
        {
            // 참조 자산들을 찾습니다.
            List<string> referencingAssetPaths = FindAssetReferences(target);

            if (referencingAssetPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Asset References", $"'{target.name}'을(를) 참조하는 자산이 없습니다.", "OK");
            }
            else
            {
                // 참조 자산들을 표시하는 창을 엽니다.
                ReferenceViewerWindow.ShowWindow(target, referencingAssetPaths);
            }
        }

        private List<string> FindAssetReferences(ScriptableObject target)
        {
            // AssetReferenceCache를 사용하여 참조 자산을 가져옵니다.
            return AssetReferenceCache.GetReferencingAssets(target);
        }

        /// <summary>
        /// 공통 버튼 생성 메서드
        /// </summary>
        /// <param name="content">버튼에 표시할 내용</param>
        /// <param name="options">GUILayoutOption 배열</param>
        /// <returns>버튼이 클릭되었는지 여부</returns>
        private bool CreateButton(GUIContent content, params GUILayoutOption[] options)
        {
            return GUILayout.Button(content, options);
        }

        private void CopyScriptableObject(ScriptableObject original)
        {
            string originalPath = AssetDatabase.GetAssetPath(original);
            if (string.IsNullOrEmpty(originalPath))
            {
                EditorUtility.DisplayDialog("복사 오류", "복사하려는 오브젝트가 에셋에 저장되어 있지 않습니다.", "확인");
                return;
            }

            string directory = System.IO.Path.GetDirectoryName(originalPath);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(originalPath);
            string extension = System.IO.Path.GetExtension(originalPath);

            // 복사된 에셋의 이름을 결정
            string copyName = AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.Combine(directory, fileName + "_Copy" + extension));

            // 현재 선택된 오브젝트들을 임시로 저장
            List<ScriptableObject> previouslySelectedObjects = new List<ScriptableObject>(selectedObjects);

            // 에셋 복사 성공 여부 확인 후 스케줄링
            bool copySuccess = AssetDatabase.CopyAsset(originalPath, copyName);

            if (copySuccess)
            {
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();

                    // 복사된 에셋을 로드
                    ScriptableObject copiedObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(copyName);
                    if (copiedObject != null)
                    {
                        // 기존 선택된 오브젝트들을 복원
                        selectedObjects = new List<ScriptableObject>(previouslySelectedObjects);

                        // 복사된 오브젝트를 선택 리스트에 추가
                        selectedObjects.Add(copiedObject);

                        EditorUtility.DisplayDialog("복사 완료", $"'{original.name}' 오브젝트가 '{System.IO.Path.GetFileName(copyName)}' 이름으로 복사되었습니다.", "확인");
                        Repaint(); // UI 갱신
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("복사 실패", "오브젝트 복사에 실패했습니다.", "확인");
                    }
                };
            }
            else
            {
                EditorUtility.DisplayDialog("복사 실패", "오브젝트 복사에 실패했습니다.", "확인");
            }
        }

        private void DeleteScriptableObject(ScriptableObject target)
        {
            // 현재 선택된 오브젝트들을 임시로 저장
            List<ScriptableObject> previouslySelectedObjects = new List<ScriptableObject>(selectedObjects);

            string targetPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(targetPath))
            {
                EditorUtility.DisplayDialog("삭제 오류", "삭제하려는 오브젝트가 에셋에 저장되어 있지 않습니다.", "확인");
                return;
            }

            bool success = AssetDatabase.DeleteAsset(targetPath);
            if (success)
            {
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();

                    // 기존 선택된 오브젝트들을 복원하되, 삭제된 오브젝트는 제외
                    selectedObjects = previouslySelectedObjects.Where(obj => obj != target).ToList();
                    Repaint(); // UI 갱신
                };
            }
            else
            {
                EditorUtility.DisplayDialog("삭제 실패", "오브젝트 삭제에 실패했습니다.", "확인");
            }
        }

        // 리셋을 처리하는 함수 (이름을 유지하면서 나머지 필드만 리셋)
        private void ResetScriptableObject(ScriptableObject so)
        {
            // ScriptableObject의 이름을 따로 저장
            string originalName = so.name;

            // 리셋할 ScriptableObject의 타입을 가져와서 새로 생성하여 기본값으로 교체
            ScriptableObject newInstance = ScriptableObject.CreateInstance(so.GetType());

            // 기존 데이터 교체 (이름 제외)
            EditorUtility.CopySerialized(newInstance, so);

            // 메모리 해제
            DestroyImmediate(newInstance);

            // 원래 이름을 다시 설정
            so.name = originalName;

            // 변경 사항을 저장
            EditorUtility.SetDirty(so);
            AssetDatabase.SaveAssets();

            LoadSelectedObjects();
        }

        #endregion
    }
}