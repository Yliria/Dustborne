using UnityEngine;

namespace Project.Health
{
    /// One-segment visual feedback for a BodyPartId. Lives on a renderable
    /// child of the Unit (head sphere, torso cylinder, hand sphere, ...).
    /// On <see cref="HealthSystem.OnPartStateChanged"/> for its target part,
    /// retints the renderer via MaterialPropertyBlock and toggles
    /// Renderer.enabled — Severed parts disappear from the silhouette.
    ///
    /// Multiple BodyPartVisuals can target the same BodyPartId (typical case:
    /// upper-arm + forearm both bound to ArmLeft, both retinted at once).
    /// Hand and foot parts have their own BodyPartId, so cascade severance
    /// retints them via the cascade-fired OnPartStateChanged events too.
    ///
    /// Stays in Project.Health (not Project.UI) because it's part of the
    /// health domain's outward feedback surface — same way a HUD bar is
    /// considered part of its source system.
    [DisallowMultipleComponent]
    public class BodyPartVisual : MonoBehaviour
    {
        [SerializeField] BodyPartId targetPart;
        [SerializeField] Renderer rend;

        [Header("Tints (MPB, no Material clone)")]
        [Tooltip("Base colour of the part when fully healthy. Mixed toward the wounded / broken tints depending on state.")]
        [SerializeField] Color healthyColor = new(0.85f, 0.78f, 0.70f);
        [SerializeField] Color woundedTint = new(1f, 0.85f, 0.40f);
        [SerializeField] Color brokenTint = new(1f, 0.50f, 0.15f);
        [SerializeField] Color severedTint = new(0.25f, 0.05f, 0.05f);

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        HealthSystem _health;
        MaterialPropertyBlock _mpb;

        public BodyPartId TargetPart
        {
            get => targetPart;
            set => targetPart = value;
        }

        public Renderer Renderer
        {
            get => rend;
            set => rend = value;
        }

        void Reset()
        {
            rend = GetComponent<Renderer>();
        }

        void OnEnable()
        {
            // Resolve once per enable so re-parented visuals (debug rebuilds)
            // pick up the HealthSystem that now contains them.
            _health = GetComponentInParent<HealthSystem>();
            if (_health == null)
            {
                Debug.LogWarning($"[BodyPartVisual] {name}: no HealthSystem found in parents — visual will not refresh.");
                return;
            }
            _health.OnPartStateChanged += HandlePartStateChanged;
            Refresh();
        }

        void OnDisable()
        {
            if (_health != null)
            {
                _health.OnPartStateChanged -= HandlePartStateChanged;
                _health = null;
            }
        }

        void HandlePartStateChanged(BodyPartId id, BodyPartState oldState, BodyPartState newState)
        {
            if (id == targetPart) Refresh();
        }

        public void Refresh()
        {
            if (_health == null || rend == null) return;
            var part = _health.GetPart(targetPart);
            if (part == null) return;

            Color c = part.State switch
            {
                BodyPartState.Healthy => healthyColor,
                BodyPartState.Wounded => Color.Lerp(healthyColor, woundedTint, 0.7f),
                BodyPartState.Broken => Color.Lerp(healthyColor, brokenTint, 0.85f),
                BodyPartState.Severed => severedTint,
                _ => healthyColor
            };

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(LegacyColorId, c);
            rend.SetPropertyBlock(_mpb);

            // Severed parts disappear — Renderer disabled, GameObject stays
            // so future Revive() can flip it back on without recreating
            // anything.
            rend.enabled = part.State != BodyPartState.Severed;
        }
    }
}
