using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ScriptableObjectViewer
{
    // AssetReferenceCache를 통해 참조 캐싱 관리
    [InitializeOnLoad]
    public static class AssetReferenceCache
    {
        // ScriptableObject의 GUID를 키로 하고, 참조하는 자산의 경로 리스트를 값으로 하는 딕셔너리
        private static Dictionary<string, List<string>> referenceCache = new Dictionary<string, List<string>>();

        // 캐시 갱신 시 호출되는 이벤트
        public static event Action CacheRefreshed;

        // 정적 생성자에서 초기화
        static AssetReferenceCache()
        {
            RefreshCache();
            // 에셋 변경 이벤트 구독
            EditorApplication.projectChanged += RefreshCache;
        }

        // 캐시를 갱신하는 메서드
        public static void RefreshCache()
        {
            referenceCache.Clear();
            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            int total = allAssetPaths.Length;
            for (int i = 0; i < allAssetPaths.Length; i++)
            {
                string assetPath = allAssetPaths[i];
                // 경로 필터링 최적화
                if (assetPath.Contains("/Editor/") || assetPath.Contains("/Tests/") || assetPath.EndsWith(".cs"))
                    continue;

                string[] dependencies = AssetDatabase.GetDependencies(assetPath, false);
                foreach (var dependency in dependencies)
                {
                    if (referenceCache.ContainsKey(dependency))
                    {
                        referenceCache[dependency].Add(assetPath);
                    }
                    else
                    {
                        referenceCache[dependency] = new List<string> { assetPath };
                    }
                }

                // 진행 상태 업데이트
                if (i % 500 == 0)
                {
                    float progress = (float)i / total;
                    EditorUtility.DisplayProgressBar("Building Reference Cache", $"Processing {i}/{total} assets...", progress);
                }
            }

            EditorUtility.ClearProgressBar();

            // 캐시 갱신 완료 후 이벤트 발생
            CacheRefreshed?.Invoke();
        }

        // 특정 ScriptableObject를 참조하는 자산 경로 리스트 반환
        public static List<string> GetReferencingAssets(ScriptableObject target)
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(assetPath))
                return new List<string>();

            if (referenceCache.TryGetValue(assetPath, out List<string> referencingAssets))
            {
                return referencingAssets.Distinct().ToList();
            }

            return new List<string>();
        }
    }
}
