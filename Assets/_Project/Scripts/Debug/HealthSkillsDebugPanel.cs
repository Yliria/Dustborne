using System;
using Project.Health;
using Project.Items;
using Project.Skills;
using Project.Units;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Project.DebugUI
{
    /// IMGUI panel toggled with F1. Right-side overlay showing:
    ///   - Blood bar
    ///   - 7 body parts with state-coloured background + bleeding icon + per-part action buttons
    ///   - 5 skills with level + XP bar + XP gain buttons
    ///   - Inventory: weight bar, stack list with drop buttons, add-test-item buttons, clear / spawn debug actions
    ///   - Global "Drain blood", "Kill", "Revive", "Reset" actions
    /// All actions go through public APIs — no shortcut into private state.
    [DisallowMultipleComponent]
    public class HealthSkillsDebugPanel : MonoBehaviour
    {
        [SerializeField] Unit watchedUnit;
        [SerializeField] ItemDatabase itemDatabase;
        [SerializeField] bool startVisible = false;
        [SerializeField] float panelWidth = 540f;

        HealthSystem _health;
        SkillSystem _skills;
        Inventory _inventory;

        bool _visible;
        Vector2 _scroll;
        bool _inventoryFolded;

        Texture2D _whiteTex;
        GUIStyle _header;
        GUIStyle _row;
        GUIStyle _smallRich;
        GUIStyle _bigRich;
        bool _stylesInit;

        static readonly Color StateHealthy = new(0.22f, 0.55f, 0.25f);
        static readonly Color StateWounded = new(0.78f, 0.62f, 0.15f);
        static readonly Color StateBroken = new(0.85f, 0.45f, 0.12f);
        static readonly Color StateSevered = new(0.75f, 0.18f, 0.18f);
        static readonly Color BloodColor = new(0.78f, 0.10f, 0.12f);
        static readonly Color XPColor = new(0.30f, 0.55f, 0.90f);
        static readonly Color BarBg = new(0.12f, 0.12f, 0.12f);
        static readonly Color WeightLight = new(0.30f, 0.65f, 0.30f);
        static readonly Color WeightFull = new(0.85f, 0.70f, 0.15f);
        static readonly Color WeightOver = new(0.85f, 0.20f, 0.20f);

        void Awake()
        {
            if (watchedUnit == null) watchedUnit = FindFirstObjectByType<Unit>();
            Resolve();
            _visible = startVisible;
        }

        void Resolve()
        {
            if (watchedUnit == null) return;
            _health = watchedUnit.GetComponent<HealthSystem>();
            _skills = watchedUnit.GetComponent<SkillSystem>();
            _inventory = watchedUnit.GetComponent<Inventory>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) _visible = !_visible;
        }

        void EnsureStyles()
        {
            if (_stylesInit) return;
            _whiteTex = Texture2D.whiteTexture;
            _header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14, richText = true };
            _row = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            _smallRich = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
            _bigRich = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true };
            _stylesInit = true;
        }

        void OnGUI()
        {
            if (!_visible) return;

            if (_health == null || _skills == null)
            {
                Resolve();
                if (_health == null || _skills == null) return;
            }

            EnsureStyles();

            float h = Mathf.Min(Screen.height - 20f, 900f);
            var rect = new Rect(Screen.width - panelWidth - 10f, 10f, panelWidth, h);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f));

            GUILayout.Label($"<b>HEALTH &amp; SKILLS</b>  <i>(F1 to toggle)</i>  unit: {watchedUnit.name}", _bigRich);
            if (_health.IsDead) GUILayout.Label("<color=#ff5050><b>** DEAD **</b></color>", _bigRich);
            GUILayout.Space(4);

            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawBlood();
            GUILayout.Space(8);
            DrawParts();
            GUILayout.Space(8);
            DrawSkills();
            GUILayout.Space(8);
            DrawInventory();
            GUILayout.Space(8);
            DrawGlobalActions();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---- Sections ----

        void DrawBlood()
        {
            var blood = _health.Blood;
            GUILayout.Label($"<b>BLOOD</b>  {blood.CurrentBlood:0.0} / {blood.EffectiveMaxBlood:0.0}", _header);
            DrawBar(blood.Ratio, BloodColor, 14f);
        }

        void DrawParts()
        {
            GUILayout.Label("<b>BODY PARTS</b>", _header);
            foreach (var p in _health.Parts)
            {
                DrawPartRow(p);
            }
        }

        void DrawPartRow(BodyPartHealth p)
        {
            if (p == null || p.Def == null) return;

            Color bg = p.State switch
            {
                BodyPartState.Healthy => StateHealthy,
                BodyPartState.Wounded => StateWounded,
                BodyPartState.Broken => StateBroken,
                BodyPartState.Severed => StateSevered,
                _ => Color.gray
            };

            var rect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.Height(46f));
            DrawSolid(rect, new Color(bg.r, bg.g, bg.b, 0.35f));
            DrawOutline(rect, bg);

            var top = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 20f);
            string bleed = p.IsBleeding ? "  <color=#ff7070>* bleeding *</color>" : "";
            string bandage = p.IsBandaged ? "  <color=#aacaff>(bandaged)</color>" : "";
            GUI.Label(top, $"<b>{p.Def.DisplayName}</b>  {p.CurrentHP:0.0} / {p.EffectiveMaxHP:0.0}  — <b>{p.State}</b>{bleed}{bandage}", _row);

            var btnRow = new Rect(rect.x + 6f, rect.y + 22f, rect.width - 12f, 22f);
            const float bw = 70f;
            const float gap = 4f;
            float x = btnRow.x;
            if (GUI.Button(new Rect(x, btnRow.y, bw, btnRow.height), "Damage 10")) Damage(p.Def.Id, 10f);
            x += bw + gap;
            if (GUI.Button(new Rect(x, btnRow.y, bw, btnRow.height), "Damage 30")) Damage(p.Def.Id, 30f);
            x += bw + gap;
            if (GUI.Button(new Rect(x, btnRow.y, bw, btnRow.height), "Damage 100")) Damage(p.Def.Id, 100f);
            x += bw + gap;
            if (GUI.Button(new Rect(x, btnRow.y, bw, btnRow.height), "Bandage")) _health.Bandage(p.Def.Id);
            x += bw + gap;
            if (GUI.Button(new Rect(x, btnRow.y, bw, btnRow.height), "Heal 50")) _health.Heal(p.Def.Id, 50f);
        }

        void DrawSkills()
        {
            GUILayout.Label("<b>SKILLS</b>", _header);
            foreach (SkillType t in Enum.GetValues(typeof(SkillType)))
            {
                DrawSkillRow(t);
            }
        }

        void DrawSkillRow(SkillType type)
        {
            var s = _skills.Get(type);
            if (s == null) return;

            float xpForNext = _skills.GetXPToNext(type);
            float ratio = xpForNext > 0f ? Mathf.Clamp01(s.XPCurrent / xpForNext) : 0f;

            var rect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.Height(40f));

            var labelRect = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, 16f);
            GUI.Label(labelRect, $"<b>{type}</b>  L{s.LevelInt}  ({s.Level:0.00})  —  XP {s.XPCurrent:0.0} / {xpForNext:0.0}", _row);

            var barRect = new Rect(rect.x + 4f, rect.y + 18f, rect.width - 156f, 8f);
            DrawBarAt(barRect, ratio, XPColor);

            var btnRow = new Rect(rect.x + rect.width - 150f, rect.y + 16f, 70f, 22f);
            if (GUI.Button(btnRow, "+50 XP")) _skills.GainXPIgnoringPause(type, 50f);
            var btn2 = new Rect(btnRow.x + 75f, btnRow.y, 70f, 22f);
            if (GUI.Button(btn2, "+500 XP")) _skills.GainXPIgnoringPause(type, 500f);
        }

        // ---- Inventory ----

        void DrawInventory()
        {
            if (_inventory == null)
            {
                GUILayout.Label("<b>INVENTORY</b>  <i>(no Inventory on unit)</i>", _header);
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_inventoryFolded ? "▶" : "▼", GUILayout.Width(22f), GUILayout.Height(20f)))
                _inventoryFolded = !_inventoryFolded;

            float ratio = _inventory.WeightRatio;
            Color barColor = _inventory.IsOverweight ? WeightOver : ratio > 0.75f ? WeightFull : WeightLight;
            string over = _inventory.IsOverweight ? "  <color=#ff4040><b>OVERWEIGHT</b></color>" : "";
            GUILayout.Label($"<b>INVENTORY</b>  {_inventory.CurrentWeight:0.0} / {_inventory.EffectiveMaxWeight:0.0} kg  ({ratio * 100f:0}%){over}", _header);
            GUILayout.EndHorizontal();

            DrawBar(Mathf.Clamp01(ratio), barColor, 14f);

            if (_inventoryFolded) return;

            GUILayout.Space(4);
            DrawStacks();
            GUILayout.Space(4);
            DrawAddItemRows();
            GUILayout.Space(4);
            DrawInventoryActions();
        }

        void DrawStacks()
        {
            if (_inventory.Stacks.Count == 0)
            {
                GUILayout.Label("<i>empty</i>", _smallRich);
                return;
            }

            // Copy indices because Drop mutates the list.
            for (int i = 0; i < _inventory.Stacks.Count; i++)
            {
                var s = _inventory.Stacks[i];
                if (s == null || s.Def == null) continue;

                var rect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.Height(22f));

                var label = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 180f, 18f);
                GUI.Label(label, $"{s.Def.DisplayName} x{s.Quantity}  ({s.TotalWeight:0.0} kg)", _row);

                var d1 = new Rect(rect.x + rect.width - 172f, rect.y, 80f, 20f);
                if (GUI.Button(d1, "Drop 1")) { _inventory.DropStack(i, 1); return; }
                var dAll = new Rect(rect.x + rect.width - 88f, rect.y, 84f, 20f);
                if (GUI.Button(dAll, "Drop All")) { _inventory.DropStack(i, s.Quantity); return; }
            }
        }

        void DrawAddItemRows()
        {
            if (itemDatabase == null || itemDatabase.AllItems.Count == 0)
            {
                GUILayout.Label("<i>no ItemDatabase assigned</i>", _smallRich);
                return;
            }
            GUILayout.Label("<b>Add test items</b>", _smallRich);
            for (int i = 0; i < itemDatabase.AllItems.Count; i++)
            {
                var item = itemDatabase.AllItems[i];
                if (item == null) continue;
                var rect = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.Height(20f));
                var label = new Rect(rect.x + 4f, rect.y + 1f, rect.width - 160f, 18f);
                GUI.Label(label, $"{item.DisplayName}  <i>({item.Weight:0.0} kg, stack {item.MaxStackSize})</i>", _smallRich);
                var b1 = new Rect(rect.x + rect.width - 156f, rect.y, 70f, 18f);
                if (GUI.Button(b1, "Add 1")) _inventory.Add(item, 1);
                var b10 = new Rect(rect.x + rect.width - 82f, rect.y, 78f, 18f);
                if (GUI.Button(b10, "Add 10")) _inventory.Add(item, 10);
            }
        }

        void DrawInventoryActions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear inventory", GUILayout.Height(22f))) _inventory.Clear();
            if (GUILayout.Button("Spawn 3 items near unit", GUILayout.Height(22f))) SpawnDebugItemsNearUnit();
            GUILayout.EndHorizontal();
        }

        void SpawnDebugItemsNearUnit()
        {
            if (itemDatabase == null || itemDatabase.AllItems.Count == 0)
            {
                Debug.LogWarning("[HealthSkillsDebugPanel] No ItemDatabase to spawn from.");
                return;
            }
            Vector3 origin = watchedUnit.transform.position;
            origin.y = 0.15f;
            for (int i = 0; i < 3; i++)
            {
                var def = itemDatabase.AllItems[UnityEngine.Random.Range(0, itemDatabase.AllItems.Count)];
                if (def == null) continue;
                Vector2 r = UnityEngine.Random.insideUnitCircle * 2.5f;
                var pos = origin + new Vector3(r.x, 0f, r.y);
                int qty = def.Stackable ? UnityEngine.Random.Range(1, Mathf.Min(5, def.MaxStackSize) + 1) : 1;
                WorldItem.Spawn(def, qty, pos);
            }
        }

        // ---- Global ----

        void DrawGlobalActions()
        {
            GUILayout.Label("<b>GLOBAL DEBUG ACTIONS</b>", _header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Drain Blood 20", GUILayout.Height(24f))) _health.Blood.Drain(20f);
            if (GUILayout.Button("Restore Blood 20", GUILayout.Height(24f))) _health.Blood.Restore(20f);
            if (GUILayout.Button("KILL", GUILayout.Height(24f))) KillUnit();
            if (GUILayout.Button("REVIVE", GUILayout.Height(24f))) ReviveUnit();
            if (GUILayout.Button("RESET", GUILayout.Height(24f))) ResetUnit();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>MODIFIER READOUTS</b>", _header);
            GUILayout.Label($"melee damage x{_skills.GetMeleeDamageMult():0.000}   vitality HP x{_skills.GetVitalityHPMultiplier():0.000}   move speed x{_skills.GetMoveSpeedMult():0.000}", _smallRich);
            GUILayout.Label($"attack speed x{_skills.GetAttackSpeedMult():0.000}   accuracy x{_skills.GetAccuracyMult():0.000}   dodge {_skills.GetDodgeChance() * 100f:0.0}%", _smallRich);
            GUILayout.Label($"harvest x{_skills.GetHarvestSpeedMult():0.000}   craft x{_skills.GetCraftSpeedMult():0.000}   carry+{_skills.GetMaxCarryWeightBonus():0.0} kg", _smallRich);
            GUILayout.Label($"health.GetMoveSpeedMultiplier = {_health.GetMoveSpeedMultiplier():0.000}", _smallRich);

            var bridge = watchedUnit.GetComponent<SkillModifiersBridge>();
            if (bridge != null)
            {
                GUILayout.Label($"weight speed x{bridge.GetWeightSpeedMultiplier():0.000}", _smallRich);
            }
        }

        // ---- Helpers ----

        void Damage(BodyPartId id, float amount)
        {
            _health.ApplyDamage(new DamageInfo
            {
                Amount = amount,
                Type = DamageType.Blunt,
                TargetPart = id,
                Attacker = null,
                Weapon = WeaponCategory.Unarmed
            });
        }

        void KillUnit()
        {
            _health.Blood.Drain(_health.Blood.EffectiveMaxBlood + 1f);
        }

        void ReviveUnit()
        {
            _health.Revive();
        }

        void ResetUnit()
        {
            _skills.ResetAllSkills();

            // Push the L1 multiplier into Health & weight bonus into Inventory.
            var bridge = watchedUnit.GetComponent<SkillModifiersBridge>();
            if (bridge != null) bridge.ApplyVitalityMultiplier(preserveRatios: false);

            if (_inventory != null)
            {
                _inventory.Clear();
                _inventory.SetMaxWeightBonus(_skills.GetMaxCarryWeightBonus());
            }

            // Auto-revive on RESET — testing-friendly.
            if (_health.IsDead) _health.Revive();
        }

        void DrawBar(float ratio01, Color fill, float height)
        {
            var r = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            DrawBarAt(r, ratio01, fill);
        }

        void DrawBarAt(Rect r, float ratio01, Color fill)
        {
            DrawSolid(r, BarBg);
            var fillRect = new Rect(r.x, r.y, r.width * Mathf.Clamp01(ratio01), r.height);
            DrawSolid(fillRect, fill);
        }

        void DrawSolid(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _whiteTex);
            GUI.color = prev;
        }

        void DrawOutline(Rect r, Color c)
        {
            DrawSolid(new Rect(r.x, r.y, r.width, 1f), c);
            DrawSolid(new Rect(r.x, r.y + r.height - 1f, r.width, 1f), c);
            DrawSolid(new Rect(r.x, r.y, 1f, r.height), c);
            DrawSolid(new Rect(r.x + r.width - 1f, r.y, 1f, r.height), c);
        }
    }
}
