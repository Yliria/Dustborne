using System.Collections.Generic;
using UnityEngine;

namespace Project.Items
{
    /// Project-wide registry of every ItemData asset. The dictionary index by
    /// Id is built lazily on first lookup. Save/load + future recipe systems
    /// resolve item references through GetById to survive prefab moves.
    [CreateAssetMenu(menuName = "Project/Items/Item Database", fileName = "ItemDatabase")]
    public class ItemDatabase : ScriptableObject
    {
        [SerializeField] List<ItemData> allItems = new();

        public IReadOnlyList<ItemData> AllItems => allItems;

        Dictionary<string, ItemData> _byId;

        public ItemData GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            EnsureIndex();
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        public bool Contains(ItemData def)
        {
            if (def == null) return false;
            return allItems.IndexOf(def) >= 0;
        }

        void EnsureIndex()
        {
            if (_byId != null) return;
            _byId = new Dictionary<string, ItemData>(allItems.Count);
            for (int i = 0; i < allItems.Count; i++)
            {
                var item = allItems[i];
                if (item == null || string.IsNullOrEmpty(item.Id)) continue;
                if (_byId.ContainsKey(item.Id))
                {
                    Debug.LogWarning($"[ItemDatabase] Duplicate Id '{item.Id}' on '{item.name}'. Only the first occurrence is indexed.");
                    continue;
                }
                _byId[item.Id] = item;
            }
        }

#if UNITY_EDITOR
        public void EditorReplaceAll(IReadOnlyList<ItemData> items)
        {
            allItems.Clear();
            allItems.AddRange(items);
            _byId = null; // force re-index on next lookup
        }

        void OnValidate()
        {
            _byId = null;
        }
#endif
    }
}
