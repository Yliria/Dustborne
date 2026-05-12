# Projet RTS — Fondations + Santé/Sang + Skills

Jeu RTS-like, vue de haut, **une unité** contrôlable par le joueur. Inspirations : Kenshi (pause active, compétences progressives, dégâts par parties du corps), Valheim/Rust (récolte → craft → armement). MVP : zone fixe, hand-crafted.

Sessions livrées :
- **Session 1** — fondations (Unit, OrderQueue, GameTime, Camera, Input, DebugUI).
- **Session 2** — Santé/Sang + Skills + Bridge.

Tous les modules suivants se branchent dessus sans modifier ces fondations.

---

## Architecture (non négociable)

### 1. Pause active
- **Espace** fige le gameplay (unité, ordres, NavMeshAgent, bleeding, regen, XP), mais **caméra et UI restent réactives**.
- `Time.timeScale` reste à **1.0 en permanence**. La pause est gérée par `GameTime`, pas par Unity.
- Conséquence : la caméra peut continuer à panner / zoomer pendant la pause, le joueur peut continuer à donner des ordres et à inspecter le debug panel.

### 2. Time decoupling
- **Aucun code gameplay n'utilise `Time.deltaTime` directement.** Tout passe par `Project.Core.GameTime.DeltaTime`.
- `GameTime.DeltaTime` renvoie `0` quand la partie est en pause, sinon `Time.deltaTime * TimeScale`.
- `TimeScale` est prévu pour du slow-mo (ex. visée focalisée). Aujourd'hui = 1.0.
- **Exception unique** : caméra et UI (panneaux IMGUI inclus) utilisent `Time.unscaledDeltaTime` ou rien (OnGUI tourne sur les events Repaint/Layout).

### 3. Order queue
- L'`Unit` ne sait pas se déplacer / attaquer / récolter elle-même. Elle exécute une file d'`IOrder` que `OrderQueue` fait avancer chaque frame.
- Pattern :
  ```
  unit.IssueOrder(new MoveOrder(point, markerPrefab), append: shiftPressed)
  ```
- **Clic** = `EnqueueAndClear` (annule la file en cours, démarre le nouvel ordre).
- **Shift+Clic** = `Enqueue` (ajoute à la fin).
- Les futurs ordres (`AttackOrder`, `HarvestOrder`, `InteractOrder`, ...) implémentent simplement `IOrder` — aucun changement requis dans `OrderQueue` ni dans `Unit`.

### 4. Composition stricte
- `Unit` est un **hub léger** : il porte des références (`Agent`, `Orders`), relaye le tick aux composants, gère la pause sur le NavMeshAgent. **Aucune logique métier.**
- Les modules s'attachent comme composants frères :
  - `HealthSystem` — Module 1 (livré)
  - `SkillSystem` — Module 1.5 (livré)
  - `SkillModifiersBridge` — pont entre les deux (livré)
  - `Inventory` — Module 2 (à venir)
  - `Equipment`, `CombatSystem`, ... — Modules 5+

### 5. Couplage event-based (Health ↔ Skills)
`HealthSystem` et `SkillSystem` **ne se référencent pas directement**. Tout le cross-domain wiring passe par `SkillModifiersBridge`. Schéma :

```
                    ┌─────────────────────────┐
                    │  SkillModifiersBridge   │
                    │  (le seul qui connaît   │
                    │   les deux côtés)       │
                    └──┬─────────────────┬────┘
                       │ events          │ events
       ┌───────────────┴──────────┐   ┌──┴────────────────┐
       │                          │   │                   │
   subscribes to:             subscribes to:          calls back into:
   - HealthSystem.            - SkillSystem.          - SkillSystem.GainXP
     OnDamageTaken              OnLevelUp             - Health.SetVitalityMultiplier
   - HealthSystem.                                    - Health.Blood.SetVitality...
     OnPartStateChanged                               - agent.speed
   - SkillSystem.OnLevelUp
   - NavMeshAgent.velocity (poll)

   responsabilités :
   - Vitality level up → rescale max HP / Blood en préservant les ratios
   - Speed level up   → recompute agent.speed
   - Part state change (jambe cassée/sectionnée) → recompute agent.speed
   - Damage taken     → +Vitality XP au défenseur, +Str/Dex XP à l'attaquant
   - Mouvement        → +Speed XP (trickle)
```

`HealthSystem` n'a aucun `using Project.Skills`. `SkillSystem` n'a aucun `using Project.Health` sauf pour la signature de `GrantAttackerXP(Unit, DamageInfo)` qui est un helper static utilitaire (pas une dépendance d'instance).

---

## Module 1 — Santé & Sang

### Modèle
- **7 parties** : `Head`, `Torso`, `Abdomen`, `ArmLeft`, `ArmRight`, `LegLeft`, `LegRight`.
- Chaque partie a des `BaseMaxHP`, un état (`Healthy`/`Wounded`/`Broken`/`Severed`) calculé depuis le ratio HP/Max, et peut **saigner** quand le ratio passe sous `BleedingHPThreshold`.
- **Vitales** : `Head`, `Torso` → HP=0 = mort instantanée.
- **Sécables** : 4 membres → HP=0 = `Severed` (état terminal, ne reçoit plus de damage, ne se heal pas).
- **Abdomen** : ni vital ni sécable, peut être `Broken` au max.

### Pool de sang
- `BloodSystem` : pool global (100 base × Vitality multiplier).
- Drainé chaque frame par la somme des `CurrentBleedRate` des parties qui saignent.
- À 0 → `OnBloodDepleted` → `HealthSystem.SetDead()`.

### Bandages
- `Bandage(BodyPartId)` met `IsBandaged = true` et stoppe immédiatement le saignement.
- Le flag se reset automatiquement quand la partie revient à `Healthy` (régen complète).
- Toute nouvelle damage **clear le flag bandage** (`NotifyDamageIncoming`) → la blessure peut re-saigner.

### API
```
HealthSystem.ApplyDamage(DamageInfo)
HealthSystem.Heal(BodyPartId, float)
HealthSystem.Bandage(BodyPartId)
HealthSystem.GetMoveSpeedMultiplier()  // 1 - somme des pénalités jambes
HealthSystem.GetPart(BodyPartId)
HealthSystem.IsDead

events : OnDamageTaken(DamageInfo), OnPartStateChanged(id, old, new), OnDeath
```

### DamageInfo
Struct value-type passée par valeur pour éviter les allocations. Champs :
- `Amount` (float)
- `Type` (Blunt | Slash | Pierce)
- `TargetPart` (BodyPartId)
- `Attacker` (Unit ou null pour les damages environnementales)
- `Weapon` (Unarmed | Melee | MeleeFast | Ranged)

Helper `DamageInfo.Environmental(amount, type, part)` pour les sources sans attaquant.

### Régénération
- Chaque partie **non-sectionnée** regen `0.1 HP/sec × RecoveryMultiplier × GameTime.DeltaTime`.
- `RecoveryMultiplier` est exposé publiquement sur `HealthSystem` — futurs consommables (trousse de soin) le bumpent temporairement.
- Severed = définitif, pas de regen (cohérence Kenshi-like).

### Penalty mouvement
Chaque partie a `MoveSpeedPenaltyIfBroken` et `MoveSpeedPenaltyIfSevered`. Les jambes par défaut :
- Broken → -0.3 (×0.7 vitesse)
- Severed → -0.7 (×0.3 vitesse)

Les pénalités sont **additives** et clampées à `[0, 1]`. Sectionner les deux jambes (2 × 0.7 = 1.4 → clamp 0) = unité immobile.

---

## Module 1.5 — Skills

### Modèle
5 skills, niveaux de 1 à 100 (float pour progression visible) :
- **Strength** : dégâts mêlée, poids portable
- **Vitality** : HP/Blood max, gain XP sur damage subi
- **Speed** : vitesse de mouvement, partiellement vitesse d'attaque
- **Dexterity** : vitesse d'attaque, précision, esquive
- **Labour** : récolte, craft

### XP & courbes
`XPCurve` (ScriptableObject, unique pour tous les skills) :
- `XPRequiredPerLevel(L)` : XP requis pour passer de L à L+1. Pré-calé : L1≈50, L25≈1000, L50≈5000, L75≈15000, L100≈50000.
- `GainMultiplierByLevel(L)` : multiplicateur appliqué aux gains. 1.0 jusqu'à L20, décroît jusqu'à 0.1 à L100 (diminishing returns).

`SkillSystem.GainXP(type, baseAmount)` :
- No-op si pause.
- Multiplie `baseAmount` par `GainMultiplier(level)`.
- Roule à travers plusieurs level-ups si gros gain.
- Fire `OnXPGained` puis `OnLevelUp` si l'entier change.

Variante `GainXPIgnoringPause()` pour les boutons debug.

### Hooks XP automatiques (dans le Bridge)
| Source | Skill | Formule |
|---|---|---|
| `OnDamageTaken` | Vitality | `info.Amount × 1.0` |
| `OnDamageTaken` + Melee | Strength (attaquant) | `info.Amount × 0.5` |
| `OnDamageTaken` + MeleeFast | Dexterity (att.) | `info.Amount × 0.5` |
| `OnDamageTaken` + MeleeFast | Strength (att.) | `info.Amount × 0.2` |
| `OnDamageTaken` + Ranged | Dexterity (att.) | `info.Amount × 0.6` |
| `OnDamageTaken` + Unarmed | Strength (att.) | `info.Amount × 0.3` |
| velocity > 0.1 (Update) | Speed | `0.1 / sec` |
| (futur HarvestOrder) | Labour | `appel manuel skills.GainXP(Labour, X)` |
| (futur CraftOrder) | Labour | `idem` |

### Modifier getters (formules exposées en `const`)
- `GetMeleeDamageMult` = 1 + (Str-1)×0.01
- `GetMaxCarryWeightBonus` = (Str-1)×2  (kg)
- `GetVitalityHPMultiplier` = 1 + (Vit-1)×0.015
- `GetMoveSpeedMult` = 1 + (Spd-1)×0.005
- `GetAttackSpeedMult` = 1 + (Spd-1)×0.003 + (Dex-1)×0.005
- `GetAccuracyMult` = 1 + (Dex-1)×0.01
- `GetDodgeChance` = (Dex-1)×0.003, clamp 0.5
- `GetHarvestSpeedMult` = 1 + (Lab-1)×0.01
- `GetCraftSpeedMult` = 1 + (Lab-1)×0.008

### Level up Vitality (politique)
**Pas de heal gratos**. Le bridge :
1. Snapshot les ratios `CurrentHP / EffectiveMaxHP` de chaque partie + `CurrentBlood / EffectiveMaxBlood`.
2. Push le nouveau multiplicateur Vitality dans `HealthSystem` + `BloodSystem`.
3. Recalcule `CurrentHP = ratio × new EffectiveMaxHP` (idem blood).

Conséquence : monter Vitality = +1.5% de capacité par niveau, sans soigner. La compétence sert à *survivre plus*, pas à *se soigner gratos*.

---

## Structure des dossiers

```
Assets/_Project/
  Scripts/
    Core/                       Project.Core
      GameTime.cs               static facade
      GameTimeService.cs        scene-bound singleton
    Camera/                     Project.CameraRig
      RTSCameraController.cs    WASD/edge pan, zoom, optional Q/E
    Units/                      Project.Units
      Unit.cs                   composition hub
      OrderQueue.cs             FIFO of IOrder
      IOrder.cs / ITargetedOrder.cs / OrderStatus.cs
      OrderPathRenderer.cs      LineRenderer preview (shift only)
      Orders/                   Project.Units.Orders
        MoveOrder.cs
    Health/                     Project.Health
      BodyPartId.cs             enum
      BodyPartState.cs          enum
      DamageType.cs / WeaponCategory.cs
      DamageInfo.cs             struct
      BodyPartDefinition.cs     ScriptableObject
      BodyPartHealth.cs         runtime state
      BloodSystem.cs            runtime state
      HealthSystem.cs           MonoBehaviour
    Skills/                     Project.Skills
      SkillType.cs              enum
      SkillData.cs              runtime state
      XPCurve.cs                ScriptableObject
      SkillSystem.cs            MonoBehaviour
      SkillModifiersBridge.cs   pont Health <-> Skills <-> Agent
    Input/                      Project.PlayerInput
      PlayerInputController.cs  raycast click → Unit.IssueOrder
    Debug/                      Project.DebugUI
      GameTimeDebugUI.cs        HUD top-left
      HealthSkillsDebugPanel.cs F1 toggle, paper-doll + buttons
    Editor/                     Project.EditorTools
      MVPSceneSetup.cs          Tools menu builder
  ScriptableObjects/
    BodyParts/                  7 BodyPartDefinition assets
    Skills/
      DefaultXPCurve.asset
  Prefabs/
    Unit.prefab                 (now wired with Health/Skills/Bridge)
    OrderMarker.prefab
    OrderMarker_Queued.prefab
  Scenes/
    MVP.unity
  Art/                          materials générés par le setup
  Settings/                     réservé (URP assets, etc.)
```

---

## Conventions de code

- **Namespaces** : `Project.<area>`. `Project.Camera` est évité parce qu'il collisionnerait avec `UnityEngine.Camera` — on utilise `Project.CameraRig`. Idem `Project.PlayerInput` (pas `Project.Input`), `Project.DebugUI` (pas `Project.Debug`).
- **`Time.deltaTime` interdit dans le gameplay.** Si tu écris du code dans `Units/`, `Orders/`, `Health/`, `Skills/`, etc. → utilise `GameTime.DeltaTime`.
- **`Time.unscaledDeltaTime` autorisé uniquement** dans `Camera/`, `Debug/`, et les couches UI.
- **Pas de `FindObjectOfType` en runtime répété.** Cache les références (auto-fetch en `Awake`, ou drag-drop en inspecteur).
- **Pas de logique dans `Unit`.** Si tu te sens obligé d'y mettre quelque chose, c'est qu'il manque un component.
- **`HealthSystem` ne référence jamais `SkillSystem`** et inversement. Couplage uniquement via `SkillModifiersBridge` + events.
- **`DamageInfo` toujours par valeur**, pas alloué. Pas de tableau, pas de boxing.
- **Tuning data dans des ScriptableObjects** (`BodyPartDefinition`, `XPCurve`). Pas de valeurs hardcodées dans les MonoBehaviours pour ce qui doit varier par unité/setup.

---

## Modules à venir

| # | Module | Branche sur |
|---|---|---|
| ~~1~~ | ~~Santé & Sang~~ | **livré** |
| ~~1.5~~ | ~~Skills (XP par usage)~~ | **livré** |
| 2 | Inventaire poids (slots + masse, encombrement → vitesse) | `Inventory` component, lit `SkillSystem.GetMaxCarryWeightBonus()` |
| 3 | Récolte (Harvestable, ressources, outils) | `HarvestOrder : IOrder, ITargetedOrder`, `Harvestable` MonoBehaviour. Appelle `skills.GainXP(Labour, X)` sur tick. |
| 4 | Crafting (recettes, stations, output) | `CraftingStation` MonoBehaviour, `CraftOrder : IOrder`. Vitesse modulée par `GetCraftSpeedMult`. |
| 5 | Combat (mêlée, dégâts directionnels, hitbox par partie du corps) | `AttackOrder : IOrder, ITargetedOrder`, `WeaponSystem`, `HealthSystem.ApplyDamage`. Construit le `DamageInfo` avec `Attacker = this.Unit` et `Weapon = ...`. |
| 6 | IA (monstres, factions) | nouveau composant `AIController` qui pousse des `IOrder` dans une `OrderQueue` exactement comme le joueur. **Réutilise `HealthSystem` et `SkillSystem` tels quels.** |

Conséquence : `Unit` accepte tous ces composants sans modification, et `IOrder` accueille tous ces nouveaux types d'ordre sans changement d'interface.

---

## Comment lancer la MVP

### 1. Première fois (génération scène + prefabs + SO)
1. Ouvre le projet dans Unity 6 (6000.4.2f1 ou compatible).
2. Attends la compilation des scripts (regarder la barre de progression en bas à droite).
3. Menu **Tools → RTS MVP → Build Scene And Prefabs**.
4. Crée/met à jour :
   - `Assets/_Project/ScriptableObjects/BodyParts/BodyPart_*.asset` (7)
   - `Assets/_Project/ScriptableObjects/Skills/DefaultXPCurve.asset`
   - `Assets/_Project/Prefabs/Unit.prefab` (avec Health/Skills/Bridge câblés)
   - `Assets/_Project/Prefabs/OrderMarker*.prefab`
   - `Assets/_Project/Scenes/MVP.unity` (avec NavMesh bakée et `HealthSkillsDebugPanel` sur GameSystems)
5. Vérifie en bas de la console : `[MVPSceneSetup] Built scene at ...`.

### 2. Lancer
1. Ouvre `Assets/_Project/Scenes/MVP.unity`.
2. Play.

### 3. Contrôles
| Input | Effet |
|---|---|
| **Clic gauche** sur le sol | `MoveOrder` (annule la file) |
| **Shift + Clic gauche** | Ajoute un `MoveOrder` à la file ; ligne preview pendant Shift |
| **Espace** | Pause / reprise |
| **WASD / flèches** | Pan caméra |
| **Molette** | Zoom |
| **F1** | Toggle Health & Skills debug panel |

### 4. Tester Santé/Skills
- **F1** ouvre le panel à droite. Tu vois 7 parties, blood bar, 5 skills.
- Boutons par partie : Damage 10/30/100, Bandage, Heal 50.
- Boutons par skill : +50 XP, +500 XP.
- Globaux : Drain Blood 20, Restore Blood 20, KILL, RESET.
- Readouts en bas : tous les modifier getters affichés en live.

---

## Anti-patterns connus (à ne pas reproduire)

- `Time.deltaTime` dans `Health`, `Skills`, `Unit`, `OrderQueue`, ou tout futur gameplay → **utiliser `GameTime.DeltaTime`**.
- Logique métier dans `Unit` → **créer un component dédié**.
- Référence directe `HealthSystem → SkillSystem` ou inverse → **passer par events + bridge**.
- Recalculer `EffectiveMaxHP` à chaque frame → **uniquement sur level up (via Recompute)**.
- Level up = heal gratos → **préserver les ratios** (voir politique Vitality).
- Saigner une partie Severed mais avec `BleedRateSevered = 0` → **mettre la valeur dans la SO, pas en dur**.
- Pop-up de level up bloquant → **passif, juste event + UI update**.
- `FindObjectOfType` répété en runtime → **cacher la ref**.

---

## Limites connues / TODO

- **TimeScale (slow-mo)** : `GameTime.TimeScale` est exposé mais `NavMeshAgent` n'utilise pas notre delta time. Pour un vrai slow-mo, il faudra moduler `agent.speed *= GameTime.TimeScale` côté `Unit.Update` ou via le bridge.
- **Reset après mort** : le bouton RESET du debug panel ne ressuscite pas (pas de `Revive()` method sur `HealthSystem`). Rebuild la scène ou ajoute la méthode si besoin.
- **Pas de Cinemachine** : non installé. La caméra custom suffit.
- **Pas d'asmdef** : tout part dans `Assembly-CSharp`. Si la compilation devient longue, on découpera.
- **Pas de sélection multi-unités** : MVP mono-unité.
- **DamageInfo.Attacker == Unit** : on a une dépendance `Project.Health → Project.Units`. Si on isole Health en asmdef plus tard, il faudra référencer l'assembly Units.
