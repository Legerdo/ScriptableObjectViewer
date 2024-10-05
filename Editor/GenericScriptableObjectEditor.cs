using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Reflection;
using System.Collections.Generic;

namespace ScriptableObjectViewer
{
    // 버튼을 숨길 ScriptableObject들을 위한 인터페이스
    public interface IHideViewerButton
    {
        // 이 인터페이스는 마커 역할만 하므로 메서드가 필요 없습니다.
    }

    [CustomEditor(typeof(ScriptableObject), true)]
    public class GenericScriptableObjectEditor : Editor
    {
        // ScriptableObjectViewer 내에서 렌더링 중인지 여부를 나타내는 정적 플래그
        public static bool isInViewer = false;

        public override void OnInspectorGUI()
        {
            // 기본 인스펙터 그리기
            DrawDefaultInspector();

            // ScriptableObjectViewer 창 내에서 렌더링 중이지 않을 때만 버튼 표시
            if (!isInViewer && !(target is IHideViewerButton))
            {
                if (GUILayout.Button("ScriptableObject Viewer 열기"))
                {
                    // ScriptableObjectViewer 창 열기 및 선택된 ScriptableObject 전달
                    ScriptableObjectViewer.ShowWindow((ScriptableObject)target);
                }
            }

            // 빈 공간 추가
            GUILayout.Space(10);

            // 현재 플레이 모드일 때만 참조 개수 표시
            if (Application.isPlaying)
            {
                int referenceCount = GetSceneReferenceCount((ScriptableObject)target);
                GUILayout.Label($"현재 씬에서 이 ScriptableObject를 참조하는 개수: {referenceCount}");
            }
            else
            {
                GUILayout.Label("참조 개수는 플레이 모드에서만 표시됩니다.");
            }
        }

        /// <summary>
        /// 현재 활성 씬에서 특정 ScriptableObject를 참조하는 개수를 계산합니다.
        /// </summary>
        /// <param name="targetObject">참조를 찾을 대상 ScriptableObject</param>
        /// <returns>참조 개수</returns>
        private int GetSceneReferenceCount(ScriptableObject targetObject)
        {
            int count = 0;

            // 현재 활성 씬의 모든 루트 게임 오브젝트 가져오기
            GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();

            foreach (GameObject root in rootObjects)
            {
                // 모든 자식 게임 오브젝트 순회
                MonoBehaviour[] monoBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour mono in monoBehaviours)
                {
                    if (mono == null)
                        continue; // 비활성화된 스크립트 또는 삭제된 스크립트

                    SerializedObject serializedObject = new SerializedObject(mono);
                    SerializedProperty prop = serializedObject.GetIterator();

                    while (prop.NextVisible(true))
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            if (prop.objectReferenceValue == targetObject)
                            {
                                count++;
                                break; // 하나의 스크립트에서 여러 참조는 하나로 카운트
                            }
                        }
                        // Handle arrays and lists
                        else if (prop.propertyType == SerializedPropertyType.ArraySize)
                        {
                            // Move to the first element of the array
                            if (prop.name.EndsWith(".Array"))
                            {
                                // The iterator will handle moving into the array elements
                                continue;
                            }
                        }
                    }
                }
            }

            return count;
        }
    }
}
