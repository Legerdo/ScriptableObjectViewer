using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace ScriptableObjectViewer
{
    /// <summary>
    /// ScriptableObject의 이름을 변경하기 위한 창
    /// </summary>
    public class RenameWindow : EditorWindow
    {
        private ScriptableObjectViewer parentWindow;
        private ScriptableObject objectToRename;
        private string newName = "";

        public static void ShowWindow(ScriptableObjectViewer parent, ScriptableObject obj)
        {
            RenameWindow window = GetWindow<RenameWindow>("Rename Object");
            window.parentWindow = parent;
            window.objectToRename = obj;
            window.newName = obj.name;
            window.minSize = new Vector2(300, 100);
        }

        private void OnGUI()
        {
            GUILayout.Label($"Rename '{objectToRename.name}'", EditorStyles.boldLabel);
            GUILayout.Space(10);
            newName = EditorGUILayout.TextField("New Name:", newName);

            GUILayout.Space(20);
            EditorGUILayout.BeginHorizontal();

            // 기존에 선택된 오브젝트들을 임시로 저장
            List<ScriptableObject> previouslySelectedObjects = new List<ScriptableObject>(parentWindow.selectedObjects);

            if (GUILayout.Button("OK"))
            {
                if (string.IsNullOrEmpty(newName))
                {
                    EditorUtility.DisplayDialog("이름 변경 실패", "새 이름을 입력해야 합니다.", "확인");
                    EditorGUILayout.EndHorizontal();
                    return;
                }

                if (newName == objectToRename.name)
                {
                    EditorUtility.DisplayDialog("이름 변경 실패", "새 이름이 기존 이름과 동일합니다.", "확인");
                    EditorGUILayout.EndHorizontal();
                    return;
                }

                // 에셋 경로 가져오기
                string assetPath = AssetDatabase.GetAssetPath(objectToRename);
                if (string.IsNullOrEmpty(assetPath))
                {
                    EditorUtility.DisplayDialog("이름 변경 실패", "이 오브젝트는 에셋으로 저장되어 있지 않습니다.", "확인");
                    return;
                }

                string directory = Path.GetDirectoryName(assetPath);
                string extension = Path.GetExtension(assetPath);
                string newAssetPath = Path.Combine(directory, newName + extension);

                // 유니크한 에셋 경로 생성
                newAssetPath = AssetDatabase.GenerateUniqueAssetPath(newAssetPath);

                // 에셋 이름 변경
                string error = AssetDatabase.RenameAsset(assetPath, newName);

                if (string.IsNullOrEmpty(error))
                {
                    // 에셋 데이터 갱신 및 UI 리프레시를 위한 딜레이 콜
                    EditorApplication.delayCall += () =>
                    {
                        AssetDatabase.Refresh();
                    
                        // 기존 선택된 오브젝트를 유지하되, 이름이 변경된 오브젝트만 업데이트
                        parentWindow.selectedObjects = previouslySelectedObjects
                            .Where(obj => obj != objectToRename)  // 이름 변경된 오브젝트는 제외
                            .ToList();

                        // 이름이 변경된 오브젝트 다시 선택 리스트에 추가
                        ScriptableObject renamedObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(newAssetPath);
                        if (renamedObject != null)
                        {
                            parentWindow.selectedObjects.Add(renamedObject);
                        }

                        parentWindow.Repaint();
                    };

                    Close();
                }
                else
                {
                    EditorUtility.DisplayDialog("이름 변경 실패", $"이름 변경 중 오류가 발생했습니다: {error}", "확인");
                }
            }

            if (GUILayout.Button("Cancel"))
            {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
