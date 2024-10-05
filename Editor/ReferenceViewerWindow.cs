using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;  // 중복 제거를 위해 추가

namespace ScriptableObjectViewer
{
    public class ReferenceViewerWindow : EditorWindow
    {
        private ScriptableObject target;
        private List<string> referencingAssetPaths;
        private Vector2 scrollPos;

        public static void ShowWindow(ScriptableObject target, List<string> referencingAssetPaths)
        {
            ReferenceViewerWindow window = GetWindow<ReferenceViewerWindow>($"'{target.name}' 참조 보기");
            window.target = target;

            // 중복 제거: Distinct()를 사용하여 중복 경로 제거
            window.referencingAssetPaths = referencingAssetPaths.Distinct().ToList();
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField($"'{target.name}'을(를) 참조하는 자산들:", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (referencingAssetPaths == null || referencingAssetPaths.Count == 0)
            {
                EditorGUILayout.LabelField("참조하는 자산이 없습니다.");
                return;
            }

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            foreach (string path in referencingAssetPaths)
            {
                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(obj, typeof(UnityEngine.Object), false);
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(obj);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.LabelField($"{path} (로딩 실패)");
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
