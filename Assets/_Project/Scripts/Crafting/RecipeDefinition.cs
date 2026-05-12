using System.Collections.Generic;
using Project.Items;
using Project.Skills;
using UnityEngine;

namespace Project.Crafting
{
    /// One recipe: a list of inputs consumed, a list of outputs produced,
    /// a craft duration, an optional station requirement, and an XP grant
    /// applied at completion. Hand-authored via "Create > Project/Crafting/
    /// Recipe Definition" or seeded by MVPSceneSetup. Owned by the
    /// RecipeDatabase asset for lookup.
    [CreateAssetMenu(menuName = "Project/Crafting/Recipe Definition", fileName = "Recipe_New")]
    public class RecipeDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable string identifier used by save/load and the debug panel. Lowercase + underscores.")]
        public string Id = "craft_new";
        public string DisplayName = "New Recipe";
        public Sprite Icon;

        [Header("Ingredients")]
        public List<ItemStack> Inputs = new();
        public List<ItemStack> Outputs = new();

        [Header("Pace")]
        [Min(0.05f)] public float BaseCraftTime = 3f;

        [Header("Station")]
        [Tooltip("If true, this recipe needs a station of StationType nearby. If false, the unit hand-crafts on the spot.")]
        public bool RequiresStation = false;
        [Tooltip("Only consulted when RequiresStation is true.")]
        public CraftStationType StationType = CraftStationType.Workbench;

        [Header("XP grant on completion")]
        public SkillType XPGainSkill = SkillType.Labour;
        [Min(0f)] public float XPGainAmount = 5f;

        /// Returns the required station type as a nullable, so callers can
        /// branch with a clean `if (def.RequiredStation == null)` check.
        public CraftStationType? RequiredStation => RequiresStation ? StationType : (CraftStationType?)null;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (BaseCraftTime < 0.05f) BaseCraftTime = 0.05f;
            if (XPGainAmount < 0f) XPGainAmount = 0f;
            for (int i = 0; i < Inputs.Count; i++)
            {
                if (Inputs[i] != null && Inputs[i].Quantity < 1) Inputs[i].Quantity = 1;
            }
            for (int i = 0; i < Outputs.Count; i++)
            {
                if (Outputs[i] != null && Outputs[i].Quantity < 1) Outputs[i].Quantity = 1;
            }
        }
#endif
    }
}
