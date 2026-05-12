using System.Collections.Generic;
using UnityEngine;

namespace Project.Crafting
{
    /// Project-wide registry of every RecipeDefinition. The Id -> recipe
    /// index is built lazily on first lookup so a freshly seeded database
    /// is immediately queryable without an explicit rebuild call. Used by
    /// the debug panel (recipe list) and, later, save/load.
    [CreateAssetMenu(menuName = "Project/Crafting/Recipe Database", fileName = "RecipeDatabase")]
    public class RecipeDatabase : ScriptableObject
    {
        [SerializeField] List<RecipeDefinition> allRecipes = new();

        public IReadOnlyList<RecipeDefinition> AllRecipes => allRecipes;

        Dictionary<string, RecipeDefinition> _byId;

        public RecipeDefinition GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureIndex();
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        void EnsureIndex()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, RecipeDefinition>(allRecipes.Count);
            for (int i = 0; i < allRecipes.Count; i++)
            {
                var r = allRecipes[i];
                if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                if (_byId.ContainsKey(r.Id))
                {
                    Debug.LogWarning($"[RecipeDatabase] Duplicate Id '{r.Id}' on '{r.name}'. Only the first occurrence is indexed.");
                    continue;
                }
                _byId[r.Id] = r;
            }
        }

#if UNITY_EDITOR
        public void EditorReplaceAll(IReadOnlyList<RecipeDefinition> recipes)
        {
            allRecipes.Clear();
            allRecipes.AddRange(recipes);
            _byId = null;
        }

        void OnValidate()
        {
            _byId = null;
        }
#endif
    }
}
