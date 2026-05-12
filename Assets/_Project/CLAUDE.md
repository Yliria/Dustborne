# Dustborne — Fondations + Santé/Sang + Skills + Items/Inventaire

Jeu RTS-like, vue de haut, **une unité** contrôlable par le joueur. Inspirations : Kenshi (pause active, compétences progressives, dégâts par parties du corps), Valheim/Rust (récolte → craft → armement). MVP : zone fixe, hand-crafted.

Sessions livrées :
- **Session 1** — fondations (Unit, OrderQueue, GameTime, Camera, Input, DebugUI).
- **Session 2** — Santé/Sang + Skills + Bridge.
- **Session 3** — Items, Inventaire au poids, Pickup, intégration vitesse/XP, Revive.

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
  - `Inventory` — Module 2 (livré)
  - `SkillModifiersBridge` — pont Health ↔ Skills ↔ Inventory (livré)
  - `Equipment`, `CombatSystem`, ... — Modules 5+

### 5. Couplage event-based (Health ↔ Skills ↔ Inventory)
`HealthSystem`, `SkillSystem` et `Inventory` **ne se référencent pas directement entre eux**. Tout le cross-domain wiring passe par `SkillModifiersBridge`. Schéma :

```
                         ┌─────────────────────────┐
                         │  SkillModifiersBridge   │
                         │  (le seul qui connaît   │
                         │   les trois côtés)      │
                         └─┬───────┬───────┬───────┘
                           │ events│ events│ events
       ┌───────────────────┘       │       └────────────────┐
       ▼                           ▼                        ▼
   HealthSystem               SkillSystem               Inventory
   - OnDamageTaken            - OnLevelUp               - OnWeightChanged
   - OnPartStateChanged                                 
                                                       
   le bridge appelle (push) :
   - SkillSystem.GainXP(Speed | Strength | Vitality)
   - Health.SetVitalityMultiplier + rescale HP/Blood (ratio-preserving)
   - Inventory.SetMaxWeightBonus(skills.GetMaxCarryWeightBonus())
   - agent.speed = base × moveSpeedMult × healthMult × weightMult

   responsabilités :
   - Vitality level up      → rescale max HP / Blood (capacité, pas de heal)
   - Strength level up      → push BonusMaxWeight dans Inventory
   - Speed / Vitality / partState change / WeightChanged → recompute agent.speed
   - Damage taken           → +Vitality XP au défenseur, +Str/Dex XP à l'attaquant
   - Mouvement              → +Speed XP (trickle)
   - Mouvement + overweight → +Strength XP (trickle)
```

`HealthSystem` n'a aucun `using Project.Skills` ni `using Project.Items`. `Inventory` n'a aucun `using Project.Skills` ni `using Project.Health`. `SkillSystem` n'a `using Project.Health` que pour la signature de `GrantAttackerXP(Unit, DamageInfo)` (helper static).

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
| velocity > 0.1 + overweight | Strength | `0.15 / sec` |
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

## Module 2 — Items & Inventaire au poids

### Modèle
- **Pas de slots, pas de grille.** Le seul critère est le poids. `BaseMaxWeight` (défaut 30 kg sur le prefab Unit) + `BonusMaxWeight` (poussé par le Bridge sur level-up Strength).
- **Stockage** : `List<ItemStack>` (pas un Dict). Un stack = `(ItemData, Quantity)`. Stockage en liste pour préparer les items non-stackables (futures armes avec durabilité unique).
- **Stacking** : géré par `ItemData.Stackable` + `MaxStackSize`. `Add()` merge dans un stack existant si possible (jusqu'à `MaxStackSize`), sinon crée un nouveau stack. Pour items non-stackables : un stack `Quantity=1` par unité ajoutée.
- **Overweight autorisé** : pas de refus à l'`Add` (return = qty effectivement ajoutée, toujours = qty demandée en MVP). Le coût est sur la vitesse + XP Strength.

### Identification (data-driven)
- Chaque item = un `ItemData` ScriptableObject (asset). Référence directe par object reference dans les stacks.
- `ItemDatabase` SO contient la liste de tous les `ItemData` du projet, indexé par `Id` (string stable). Utilisé par : le debug panel (boutons "Add"), le save/load futur (résoudre `Id → ItemData`).
- **Pas de `Resources.Load`.** Tout passe par références sérialisées ou via `ItemDatabase.GetById(id)`.

### WorldItem & Pickup
- `WorldItem` : MonoBehaviour sur un GameObject "loot pile" en monde, porte `Def` + `Quantity`.
- `WorldItem.Spawn(def, qty, position)` (static) : instancie le `def.WorldPrefab` si défini, sinon le **prefab générique** (cube 0.3m teinté avec `def.FallbackColor`). Le prefab générique est exposé via `WorldItemService` (singleton scène posé sur GameSystems).
- **Pickup explicite** (Kenshi-style, pas d'auto-pickup) : clic sur un WorldItem → `PickupOrder` dans la queue. L'unité navigue jusqu'à lui, l'absorbe (`Inventory.Add` + `Destroy(worldItem)`).
- `PickupOrder` implémente `IOrder` + `ITargetedOrder`, donc apparaît dans la ligne preview (shift) comme les MoveOrders.

### Pipeline de vitesse (étendu)
`SkillModifiersBridge.RecomputeMoveSpeed()` :

```
agent.speed = baseAgentSpeed
            × skills.GetMoveSpeedMult()
            × health.GetMoveSpeedMultiplier()
            × bridge.GetWeightSpeedMultiplier()
```

`GetWeightSpeedMultiplier()` :
- ratio ≤ 0.75 → **1.0** (pas de pénalité)
- 0.75 < ratio ≤ 1.0 → lerp linéaire de 1.0 → 0.7 (mult à pleine charge)
- ratio > 1.0 → `0.7 - (ratio - 1.0) × 0.4`, clamp min `0.15`

Tous les seuils/pentes/floor sont sérialisés dans le bridge → tunables sans recompile.

### Hook XP overweight
Pendant que `agent.velocity > 0.1` ET `inventory.IsOverweight` ET `GameTime.DeltaTime > 0` :
- `skills.GainXP(Strength, 0.15f × dt)`

Pause-safe via `GameTime.DeltaTime`. Pas de gain à l'arrêt.

### Revive
`HealthSystem.Revive()` : reset HP toutes parties (Severed inclus pour MVP — testing-friendly), reset bandages/bleeding, reset blood au max, débloque l'agent. Conserve le multiplicateur Vitality courant (capacité préservée si on a leveled up). Fire `OnRevived`.
Le bouton REVIVE du debug panel l'appelle. Le bouton RESET aussi (auto-revive après reset des skills).

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
      SkillModifiersBridge.cs   pont Health <-> Skills <-> Inventory <-> Agent
    Items/                      Project.Items
      ItemType.cs               enum
      ItemData.cs               ScriptableObject
      ItemStack.cs              serializable runtime
      ItemDatabase.cs           ScriptableObject (registry by Id)
      Inventory.cs              MonoBehaviour
      WorldItem.cs              MonoBehaviour on loot piles
      WorldItemService.cs       generic-prefab bootstrap
      Orders/                   Project.Items.Orders
        PickupOrder.cs
    Input/                      Project.PlayerInput
      PlayerInputController.cs  raycast click → PickupOrder or MoveOrder
    Debug/                      Project.DebugUI
      GameTimeDebugUI.cs        HUD top-left
      HealthSkillsDebugPanel.cs F1 toggle, paper-doll + Inventory + buttons
    Editor/                     Project.EditorTools
      MVPSceneSetup.cs          Tools menu builder
  ScriptableObjects/
    BodyParts/                  7 BodyPartDefinition assets
    Skills/
      DefaultXPCurve.asset
    Items/                      10 ItemData assets (test set)
    ItemDatabase.asset
  Prefabs/
    Unit.prefab                 (Health/Skills/Inventory/Bridge wired)
    OrderMarker.prefab
    OrderMarker_Queued.prefab
    WorldItem_Generic.prefab    fallback world body (cube tinted at spawn)
  Scenes/
    MVP.unity                   (5 WorldItems spawned at fixed positions)
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
- **`HealthSystem`, `SkillSystem`, `Inventory` mutuellement orthogonaux.** Couplage uniquement via `SkillModifiersBridge` + events.
- **`DamageInfo` toujours par valeur**, pas alloué. Pas de tableau, pas de boxing.
- **Tuning data dans des ScriptableObjects** (`BodyPartDefinition`, `XPCurve`, `ItemData`, `ItemDatabase`). Pas de valeurs hardcodées dans les MonoBehaviours pour ce qui doit varier par item/unité/setup.
- **Pas de `Resources.Load`.** Tout passe par ScriptableObject references ou via `ItemDatabase.GetById(string)`.
- **Pas d'auto-pickup.** Le pickup est explicite (clic → `PickupOrder`). Kenshi-style.

---

## Modules à venir

| # | Module | Branche sur |
|---|---|---|
| ~~1~~ | ~~Santé & Sang~~ | **livré** |
| ~~1.5~~ | ~~Skills (XP par usage)~~ | **livré** |
| ~~2~~ | ~~Items & Inventaire au poids + Pickup~~ | **livré** |
| 3 | Récolte (Harvestable, ressources, outils) | `HarvestOrder : IOrder, ITargetedOrder`, `Harvestable` MonoBehaviour. Appelle `inventory.Add(def, qty)` puis `skills.GainXP(Labour, X)` à la fin du tick. |
| 4 | Crafting (recettes, stations, output) | `CraftingStation` MonoBehaviour, `CraftOrder : IOrder`. Consomme via `inventory.Remove`, produit via `inventory.Add`. Vitesse modulée par `GetCraftSpeedMult`. |
| 5 | Combat (mêlée, dégâts directionnels, hitbox par partie du corps) | `AttackOrder : IOrder, ITargetedOrder`, `WeaponSystem`, `HealthSystem.ApplyDamage`. Construit le `DamageInfo` avec `Attacker = this.Unit` et `Weapon = ...`. |
| 6 | Equipment (slots d'armes/outils équipés) | `Equipment` component séparé, *pas* une extension d'Inventory. Items équipés sortent de l'inventaire et passent en slot. |
| 7 | IA (monstres, factions) | nouveau composant `AIController` qui pousse des `IOrder` dans une `OrderQueue` exactement comme le joueur. **Réutilise `HealthSystem`, `SkillSystem`, `Inventory` tels quels.** |
| 8 | Save/Load | sérialiser via `ItemData.Id` (lookup `ItemDatabase.GetById`) pour résister aux refactors. |

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
   - `Assets/_Project/ScriptableObjects/Items/Item_*.asset` (10)
   - `Assets/_Project/ScriptableObjects/ItemDatabase.asset`
   - `Assets/_Project/Prefabs/Unit.prefab` (Health/Skills/Inventory/Bridge câblés)
   - `Assets/_Project/Prefabs/OrderMarker*.prefab`
   - `Assets/_Project/Prefabs/WorldItem_Generic.prefab`
   - `Assets/_Project/Scenes/MVP.unity` (NavMesh bakée, 5 WorldItems placés, GameSystems = `GameTimeService` + `WorldItemService` + Debug panels)
5. Vérifie en bas de la console : `[MVPSceneSetup] Built scene at ...`.

### 2. Lancer
1. Ouvre `Assets/_Project/Scenes/MVP.unity`.
2. Play.

### 3. Contrôles
| Input | Effet |
|---|---|
| **Clic gauche** sur un WorldItem | `PickupOrder` (annule la file) |
| **Clic gauche** sur le sol | `MoveOrder` (annule la file) |
| **Shift + Clic gauche** | Ajoute le même ordre à la file ; ligne preview pendant Shift |
| **Espace** | Pause / reprise |
| **WASD / flèches** | Pan caméra |
| **Molette** | Zoom |
| **F1** | Toggle Health & Skills + Inventory debug panel |

### 4. Tester Santé/Skills/Inventory
- **F1** ouvre le panel à droite. Sections : Blood, Body Parts, Skills, Inventory (foldable), Global Actions.
- Boutons par partie : Damage 10/30/100, Bandage, Heal 50.
- Boutons par skill : +50 XP, +500 XP.
- Inventory : Add 1/Add 10 pour chaque item du Database, Drop 1/Drop All par stack, Clear, Spawn 3 items.
- Globaux : Drain Blood, Restore Blood, KILL, REVIVE, RESET.
- Readouts : tous les modifier getters + `weight speed x` affichés en live.

---

## Anti-patterns connus (à ne pas reproduire)

- `Time.deltaTime` dans `Health`, `Skills`, `Items`, `Unit`, `OrderQueue`, ou tout futur gameplay → **utiliser `GameTime.DeltaTime`**.
- Logique métier dans `Unit` → **créer un component dédié**.
- Référence directe entre `HealthSystem`, `SkillSystem`, `Inventory` → **passer par events + bridge**.
- `Resources.Load` → **utiliser ItemDatabase / refs sérialisées**.
- Auto-pickup à proximité → **toujours via PickupOrder**.
- Hardcoder la liste des items en code → **tout via `ItemDatabase` SO**.
- Quantité négative dans un stack → **clamp à 0 (suppression du stack)**.
- WorldItem qui re-spawn automatiquement → **drops uniques en MVP**.
- Recalculer `EffectiveMaxHP` à chaque frame → **uniquement sur level up (via Recompute)**.
- Level up = heal gratos → **préserver les ratios** (politique Vitality).
- Saigner une partie Severed mais avec `BleedRateSevered = 0` → **mettre la valeur dans la SO, pas en dur**.
- Pop-up de level up bloquant → **passif, juste event + UI update**.
- `FindObjectOfType` répété en runtime → **cacher la ref**.

---

## Limites connues / TODO

- **TimeScale (slow-mo)** : `GameTime.TimeScale` est exposé mais `NavMeshAgent` n'utilise pas notre delta time. Pour un vrai slow-mo, il faudra moduler `agent.speed *= GameTime.TimeScale` côté bridge.
- **Pas de Cinemachine** : non installé. La caméra custom suffit.
- **Pas d'asmdef** : tout part dans `Assembly-CSharp`. Si la compilation devient longue, on découpera. Dépendances actuelles : `Health → Units` ; `Skills → Health + Units` ; `Items → Units` (PickupOrder + Inventory.GetComponent) ; `Skills → Items` (Bridge utilise Inventory) ; `PlayerInput → Units + Items + Items.Orders` ; `Debug → Health + Skills + Items + Units`.
- **Pas de sélection multi-unités** : MVP mono-unité. `PlayerInputController` cherche le 1er Unit de la scène en fallback.
- **Material per-instance pour WorldItem générique** : chaque spawn alloue un Material (légère GC + bloat scène si beaucoup d'items). À optimiser via `MaterialPropertyBlock` si ça devient problématique.
- **WorldItem doublonnent au sol** : si on drop 2× le même item, on a 2 cubes superposés. Acceptable pour MVP. Possible amélioration : merge spatial des piles de même `ItemData` à proximité.
- **Revive après mort par dégât vital (Head/Torso à 0)** : Revive remplit les HP donc fonctionne — mais sémantiquement on "ressuscite avec une tête neuve". Si on veut un système de cicatrices/handicaps persistants, ajouter un flag à `BodyPartHealth`.
