using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ScriptableObjectViewer
{
    [CreateAssetMenu(menuName = "ScriptableObject Category Manager")]
    public class ScriptableObjectCategoryManager : ScriptableObject, IHideViewerButton
    {
        public List<ScriptableObjectCategoryEntry> entries = new List<ScriptableObjectCategoryEntry>();
        public List<string> excludedCategories = new List<string>();

        public List<string> favoriteCategories = new List<string>();
    }
}