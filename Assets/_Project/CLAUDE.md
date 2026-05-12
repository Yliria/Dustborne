# Dustborne — Fondations + Santé/Sang + Skills + Items + Récolte + Crafting + Poucou chibi

Jeu RTS-like, vue de haut, **une unité** contrôlable par le joueur. Inspirations : Kenshi (pause active, compétences progressives, dégâts par parties du corps), Valheim/Rust (récolte → craft → armement). MVP : zone fixe, hand-crafted.

Sessions livrées :
- **Session 1** — fondations (Unit, OrderQueue, GameTime, Camera, Input, DebugUI).
- **Session 2** — Santé/Sang + Skills + Bridge.
- **Session 3** — Items, Inventaire au poids, Pickup, intégration vitesse/XP, Revive.
- **Session 4** — Dette technique (MPB, drop merge, dédup, extraction PassiveXPHooks) + Récolte (Harvestable, HarvestOrder, 9 noeuds en scène).
- **Session 5** — Crafting (RecipeDefinition + RecipeDatabase + CraftingStation + CraftOrder, hand-craft & workbench, 9 recettes, 1 Workbench en scène, bandage + 5 armes placeholders).
- **Session 5.5** — Poucou chibi visuel (15 segments en primitives, ~0.95m, tête disproportionnée) + body parts étendues à 11 (mains + pieds), cascade Severed anatomique, BodyPartVisual avec MPB tinting.

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
  - `SkillModifiersBridge` — pont event-driven Health ↔ Skills ↔ Inventory (livré)
  - `PassiveXPHooks` — trickle XP (Speed en marchant, Strength sous charge). Séparé du Bridge depuis Session 4 (livré)
  - `Equipment`, `CombatSystem`, ... — Modules 6+
- Composants placeables dans la scène (non sur Unit) :
  - `Harvestable` — Module 3 (livré)
  - `CraftingStation` — Module 4 (livré). Auto-register dans une liste static à OnEnable, désinscription à OnDisable.

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
   - SkillSystem.GainXP(Vitality)  // pour la défense
   - Health.SetVitalityMultiplier + rescale HP/Blood (ratio-preserving)
   - Inventory.SetMaxWeightBonus(skills.GetMaxCarryWeightBonus())
   - agent.speed = base × moveSpeedMult × healthMult × weightMult

   responsabilités du Bridge (event-driven uniquement) :
   - Vitality level up      → rescale max HP / Blood (capacité, pas de heal)
   - Strength level up      → push BonusMaxWeight dans Inventory
   - Speed / Vitality / partState change / WeightChanged → recompute agent.speed
   - Damage taken           → +Vitality XP au défenseur, +Str/Dex XP à l'attaquant

PassiveXPHooks (séparé du Bridge depuis Session 4) tient le tick :
   - Mouvement              → +Speed XP (0.1 / sec)
   - Mouvement + charge     → +Strength XP (0.15 × InverseLerp(0.10, 0.90, ratio) / sec)
                               → 0 si ratio ≤ 10%, lerp linéaire 10→90%, max au-dessus de 90% (overweight inclus)
                               → thresholds réglables dans l'inspector
   - (futur)                → +Hunger, Cold, Posture, etc.
```

`HealthSystem` n'a aucun `using Project.Skills` ni `using Project.Items`. `Inventory` n'a aucun `using Project.Skills` ni `using Project.Health`. `SkillSystem` n'a `using Project.Health` que pour la signature de `GrantAttackerXP(Unit, DamageInfo)` (helper static).

---

## Module 1 — Santé & Sang

### Modèle
- **11 parties** depuis Session 5.5 : `Head`, `Torso`, `Abdomen`, `ArmLeft`, `ArmRight`, `LegLeft`, `LegRight`, `HandLeft`, `HandRight`, `FootLeft`, `FootRight`.
- Chaque partie a des `BaseMaxHP`, un état (`Healthy`/`Wounded`/`Broken`/`Severed`) calculé depuis le ratio HP/Max, et peut **saigner** quand le ratio passe sous `BleedingHPThreshold`.
- **Vitales** : `Head`, `Torso` → HP=0 = mort instantanée.
- **Sécables** : 8 membres (les 4 segments + 4 extrémités). HP=0 = `Severed` (état terminal, ne reçoit plus de damage, ne se heal pas hors Revive).
- **Abdomen** : ni vital ni sécable, peut être `Broken` au max.
- **Cascade anatomique** (Session 5.5) : un parent qui passe Severed force ses children à le devenir aussi. Mapping data-driven dans `BodyPartDefinition.SeveredChildren` :
  - `ArmLeft → [HandLeft]`, `ArmRight → [HandRight]`
  - `LegLeft → [FootLeft]`, `LegRight → [FootRight]`
  - HealthSystem.CascadeSevered récursif natif ; guard `state != Severed` empêche tout cycle.

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
HealthSystem.GetMoveSpeedMultiplier()  // produit multiplicatif sur leg + foot
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
Chaque partie a `MoveSpeedPenaltyIfBroken` et `MoveSpeedPenaltyIfSevered`. Valeurs par défaut :
- Jambes (LegLeft/Right) : Broken → 0.30, Severed → 0.70
- Pieds (FootLeft/Right) : Broken → 0.15, Severed → 0.50
- Tout le reste (head, torso, abdomen, bras, mains) : 0.

Depuis Session 5.5, le calcul est **multiplicatif** et gated sur leg + foot uniquement :

```
mult = 1
pour chaque partie leg/foot : mult *= (1 - penalty)
return max(mult, 0)
```

Ex : `LegLeft.Broken` (0.30) + `FootLeft.Broken` (0.15) = `0.70 × 0.85 = 0.595`. Les deux jambes + deux pieds Severed = `0.30 × 0.30 × 0.50 × 0.50 ≈ 0.0225` — l'unité devient glaciale mais jamais strictement à zéro. Les **mains** ont volontairement zéro pénalité ; elles serviront aux constraintes d'équipement en Module 6+.

### Visual feedback (Session 5.5)
- **Personnage Poucou** : le prefab Unit a un enfant `Visual` contenant 15 segments en primitives Unity arrangés en chibi (tête sphère 0.40m disproportionnée, torse/abdomen cylindres trapus, bras et jambes courts 3 segments chacun). **Hauteur totale ~0.95m**. Pivot du Unit au sol (`NavMeshAgent.baseOffset = 0`).
- **Sizing root** : `NavMeshAgent.radius = 0.25, height = 0.95`. `CapsuleCollider` center `(0, 0.475, 0)`, height `0.95`, radius `0.25` — englobe le Poucou pour clics + physics.
- **`BodyPartVisual`** (Project.Health) sur chaque segment renderable. Listen à `HealthSystem.OnPartStateChanged`, tint via `MaterialPropertyBlock` selon l'état :
  - Healthy → beige peau Poucou (`#EBD79F`)
  - Wounded → jaune teinté
  - Broken → orange teinté
  - Severed → rouge sombre **+ Renderer disabled** (le membre disparaît)
- Plusieurs `BodyPartVisual` peuvent cibler le même `BodyPartId` (typique : upper-arm + forearm tous deux bindés à `ArmLeft`, retintés ensemble par le même event).
- `HealthSystem.Revive()` fire `OnPartStateChanged` pour tous les parts qui transitent → les renderers re-enable et reprennent leur couleur.
- **Pas de Material clone** : un seul `Mat_Poucou.mat` partagé par les 15 segments ; MPB par instance.
- **Pas de hit collider par part** : un `CapsuleCollider` englobant sur la racine du Unit suffit pour clic + pathfinding. Les hit colliders par part arrivent en Module 6 (Combat).

### Menu de rebuild rapide
`Tools → RTS MVP → Rebuild Unit Visual` régénère uniquement la hiérarchie `Visual` du prefab Unit (sans toucher aux components ou aux refs SO). Pratique pour itérer sur les proportions / couleurs du Poucou. Le NavMeshAgent et le CapsuleCollider ne sont **pas** retouchés par ce menu — si on change la hauteur du Poucou, il faut aussi mettre à jour ces deux composants en éditant `BuildUnitPrefab` puis relancer `Build Scene And Prefabs`.

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
| velocity > 0.1 + load assez lourd | Strength | `0.15 × InverseLerp(0.10, 0.90, WeightRatio) / sec` — dead zone <10%, lerp 10→90%, capé plein si ≥90% (overweight inclus) |
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

## Module 3 — Récolte

### Modèle
- **Harvestable** : MonoBehaviour sur les noeuds du monde (arbres, rochers, bancs de poissons). Référence un `HarvestableDefinition` ScriptableObject pour toutes ses stats.
- **HarvestableDefinition** (SO) : porte HP du noeud, vitesse de récolte de base, range d'interaction, outil requis (référence directe vers un `ItemData`), et la table de drops (`HarvestableDrop` : item + min/max qty + chance 0..1).
- **Pas de durabilité d'outil pour le MVP.** L'outil est "vérifié" (must be in inventory), pas consommé.
- **Drops à la fin** (mode "OnDepleted") : quand HP = 0, on roll chaque drop indépendamment et on spawn des WorldItems éparpillés (rayon ~0.6m). Le pipeline pickup existant prend le relais.
- **Pas de respawn** : noeud épuisé reste détruit. Respawn = Module 7 (Monde & Save).

### HarvestOrder
`HarvestOrder : IOrder, ITargetedOrder` :
1. `OnStart` valide : target non-null, non-depleted, Def présent, outil requis dans Inventory (sinon log + Failed).
2. `Tick` : approche jusqu'à `Def.InteractionRange` (XZ), puis stop. À chaque frame in-range :
   - `damage = Def.BaseHarvestSpeed × skills.GetHarvestSpeedMult() × dt`
   - `target.ApplyDamage(damage)`
   - `skills.GainXP(Labour, damage × 0.5)` — pause-safe via `GainXP` normal.
3. Si target depleted → Complete. Drops auto-générés par `Harvestable.OnDepleted` → WorldItem.Spawn(*).
4. `OnEnd` débloque l'agent.

XP Labour est **proportionnel au boulot fait** (au damage), pas au temps écoulé. Plus tu cognes vite, plus tu progresses vite. Un Labour level up rend la récolte visiblement plus rapide via `GetHarvestSpeedMult`.

### Découplage
`HarvestOrder` **ne touche pas à Inventory.Add**. Les drops passent par WorldItems → PickupOrder. Cohérent avec le pipeline existant et préserve la possibilité d'avoir un autre acteur (animal, PNJ) ramasser le drop.

### Pathfinding
Chaque `Harvestable` auto-ajoute un `NavMeshObstacle` avec `carving = true`. La taille de l'obstacle s'aligne sur les bounds du collider racine. La scène re-bake la NavMesh au build pour intégrer les obstacles statiques. Quand un noeud est détruit (depleted), le carving runtime libère l'espace.

### Visual fallback
`HarvestableDefinition.VisualPrefab` est optionnel. Si null, `Harvestable.Awake` instancie un primitive enfant selon le type :
- `Tree` → cylindre marron (3m de haut, radius 0.25m)
- `Rock`/`Ore` → sphère grise (1.2m)
- `FishingSpot` → cube plat cyan (1.5×0.1×1.5) — l'obstacle, lui, est inflated à 1.5×2×1.5 pour bien bloquer la bake.
- `Bush` → sphère verte
- `Other` → cube gris

Le tint runtime utilise `MaterialPropertyBlock`, jamais de Material clone. Quand le setup éditeur crée les noeuds en scène, il pré-injecte le visual via un Material asset partagé par type (`Mat_Harvestable_Tree`, `..._Rock`, `..._FishingSpot`) — pas de clone non plus.

### Préparé pour le futur
- **Multi-tools** : `RequiredTool` → `List<ItemData> AcceptedTools` quand on aura plusieurs paliers d'outil.
- **Drop per tick** : un `HarvestableDefinition.DropMode { OnDepleted, PerTick }` permettra le filet de pêche qui produit en continu.
- **Anims** : `HarvestableInteractAnim` jouable côté Unit (pas bloquant, juste cosmétique).
- **Durabilité d'outil** : décrémenter une durabilité sur l'outil à chaque tick, à intégrer quand `ItemData` aura un champ `Durability` (Module 6 ou plus tard).
- **Respawn** : `HarvestableRespawnerService` mémorise les positions des noeuds détruits + un timer → re-instancie (Module 7).

---

## Module 4 — Crafting

### Modèle
- **`RecipeDefinition`** (SO) porte une liste d'`Inputs` (List<ItemStack>), une liste d'`Outputs`, un `BaseCraftTime`, un éventuel station requise, et un gain d'XP à la complétion. Inputs/outputs référencent des `ItemData` directement (pas par ID).
- **`RecipeDatabase`** (SO) : registre de toutes les recettes, indexé par `Id` pour le save/load et la liste du debug panel. Identique à `ItemDatabase` côté usage.
- **`CraftStationType`** enum : Workbench (MVP) + Forge (réservé).
- **Nullable workaround** : `RecipeDefinition.RequiresStation` (bool) + `StationType` (enum). Propriété `RequiredStation` renvoie `CraftStationType?` pour les call sites. Évite la non-sérialisation des `Nullable<enum>` par Unity.

### Hand-craft vs station
- `RequiredStation == null` → hand-craft. L'unité reste sur place (`agent.isStopped = true` au OnStart). Pas de navigation. Utile pour les outils basiques (hache pierre, bandage).
- `RequiredStation == Workbench` → l'unité walks vers la station la plus proche du type demandé. Si aucune en scène → Failed avec log clair.

### CraftingStation (placeables)
MonoBehaviour sur le prefab Workbench/Forge. Auto-add un `NavMeshObstacle` (carve = true) à `Awake` comme `Harvestable`. Maintient un registre static `ActiveStations` via `OnEnable`/`OnDisable`, exposé via :
- `static CraftingStation FindNearest(Vector3 origin, CraftStationType type)` — sweep linéaire O(n), négligeable au scale prévu.
- `static bool AnyAvailable(CraftStationType type)` — utilisé par le debug panel pour griser les recettes sans station valide.

`InteractionPoint` optionnel (Transform enfant). Si absent, fallback = `position - transform.forward × 1.0f`. `InteractionRange` ~1.5–2 m.

### CraftOrder
Implémente `IOrder` + `ITargetedOrder` (pour la ligne preview shift). Construit avec une `RecipeDefinition` directement — **ne référence jamais `RecipeDatabase`**. Cycle :
1. **OnStart** : check Inventory + Has() pour chaque input → Failed si manque. Résolution station si nécessaire → Failed si aucune trouvée. Hand-craft : `agent.isStopped = true`. Station : `SetDestination(stationInteractionPos)`.
2. **Tick** : approche jusqu'à `InteractionRange` (XZ) si station-craft, puis `agent.isStopped = true` + `LookAt` cosmétique. Incrémente `progressTime += deltaTime × skills.GetCraftSpeedMult()`. Pause-safe natif via `GameTime.DeltaTime`.
3. **Complétion** (quand `progressTime ≥ BaseCraftTime`) : re-check inputs (le joueur a pu vider l'inventaire) → Failed si manque. `Inventory.Remove` chaque input, `Inventory.Add` chaque output (overweight autorisé, cohérent avec pickup). `Skills.GainXP(Recipe.XPGainSkill, Recipe.XPGainAmount)` une seule fois. → Complete.
4. **OnEnd** : `agent.isStopped = false; agent.ResetPath()`.

### Politique de consommation
**Inputs consommés UNIQUEMENT à la complétion** — l'annulation (`Clear()` queue, click sans shift, etc.) est lossless, on n'a rien à rembourser. Re-check au moment du Remove couvre le cas où le joueur a vidé son sac entre temps. XP grant aussi à la fin, jamais par tick — sinon farm d'une recette ultra-courte → XP infinie.

### Items "Weapon" placeholders
6 nouveaux items créés en Session 5 : `bandage`, `spear_stone`, `sword_stone`, `shield_wood`, `bow_basic`, `crossbow_basic`. Le bandage est utilisable (Type=Consumable, stackable). Les 5 armes (Type=Weapon, non-stack) **dorment dans l'inventaire** jusqu'à l'arrivée du module Combat — pas de logique d'équipement encore.

### 9 recettes en MVP
| Id | Type | Inputs | Outputs | Time | XP |
|---|---|---|---|---|---|
| `craft_bandage` | hand | branch×1 | bandage×1 | 1s | +2 Labour |
| `craft_stone_axe` | hand | branch×2, stone×1 | stone_axe×1 | 3s | +5 Labour |
| `craft_stone_pickaxe` | hand | branch×2, stone×1 | stone_pickaxe×1 | 3s | +5 Labour |
| `craft_fishing_rod` | hand | branch×3, stone×1 | fishing_rod×1 | 4s | +6 Labour |
| `craft_spear_stone` | Workbench | wood_log×2, stone_chunk×1 | spear_stone×1 | 6s | +10 Labour |
| `craft_sword_stone` | Workbench | wood_log×3, stone_chunk×2 | sword_stone×1 | 8s | +15 Labour |
| `craft_shield_wood` | Workbench | wood_log×4 | shield_wood×1 | 5s | +10 Labour |
| `craft_bow_basic` | Workbench | wood_log×2, branch×4 | bow_basic×1 | 7s | +12 Labour |
| `craft_crossbow_basic` | Workbench | wood_log×3, stone_chunk×1 | crossbow_basic×1 | 10s | +18 Labour |

### Préparé pour le futur
- **Forge** : nouveau `CraftStationType.Forge`, recettes métal (`iron_ingot`, `iron_sword`...). Setup identique au Workbench, juste un autre prefab + type. Aucune modif du `CraftOrder`.
- **Multi-output stochastique** : ajouter `RecipeDefinition.OutputRoll` pour des drops aléatoires (ex : 70% chance d'un bonus). MVP : outputs déterministes.
- **Recettes débloquées** : `Recipe.UnlockedAt` (Labour level requis). MVP : tout débloqué.
- **UI worldspace station** : double-clic sur Workbench ouvre un panneau de recettes filtrées (post-MVP).
- **Save/Load recettes connues** : sérialisées via `Recipe.Id` (lookup via `RecipeDatabase.GetById`).

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
      BodyPartId.cs             enum (11 values)
      BodyPartState.cs          enum
      DamageType.cs / WeaponCategory.cs
      DamageInfo.cs             struct
      BodyPartDefinition.cs     ScriptableObject (incl. SeveredChildren cascade)
      BodyPartHealth.cs         runtime state
      BloodSystem.cs            runtime state
      HealthSystem.cs           MonoBehaviour (cascade + multiplicative mobility)
      BodyPartVisual.cs         MPB tinter on each renderable segment
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
      PassiveXPHooks.cs         trickle XP (Speed, Strength) — split from Bridge
      Orders/                   Project.Items.Orders
        PickupOrder.cs
    Harvesting/                 Project.Harvesting
      HarvestableType.cs        enum
      HarvestableDrop.cs        serializable
      HarvestableDefinition.cs  ScriptableObject
      Harvestable.cs            MonoBehaviour on world nodes
      Orders/                   Project.Harvesting.Orders
        HarvestOrder.cs
    Crafting/                   Project.Crafting
      CraftStationType.cs       enum
      RecipeDefinition.cs       ScriptableObject (with nullable RequiredStation property)
      RecipeDatabase.cs         ScriptableObject (registry by Id)
      CraftingStation.cs        MonoBehaviour, static ActiveStations registry
      Orders/                   Project.Crafting.Orders
        CraftOrder.cs
    Input/                      Project.PlayerInput
      PlayerInputController.cs  raycast click → PickupOrder | HarvestOrder | MoveOrder
    Debug/                      Project.DebugUI
      GameTimeDebugUI.cs        HUD top-left
      HealthSkillsDebugPanel.cs F1 toggle, paper-doll + Inventory + Harvestables + Crafting + buttons
    UI/                         Project.UI
      FloatingTextService.cs    world-anchored toasts (pickup/error/info/levelup)
      UnitFeedbackToasts.cs     unit-event → toast bridge (SkillSystem.OnLevelUp etc.)
    Editor/                     Project.EditorTools
      MVPSceneSetup.cs          Tools menu builder
  ScriptableObjects/
    BodyParts/                  11 BodyPartDefinition assets (Head, Torso, Abdomen, ArmL/R, LegL/R, HandL/R, FootL/R)
    Skills/
      DefaultXPCurve.asset
    Items/                      16 ItemData assets (10 from S3 + 6 new in S5)
    ItemDatabase.asset
    Harvestables/               3 HarvestableDefinition assets (tree, rock, fishing)
    Recipes/                    9 RecipeDefinition assets
    RecipeDatabase.asset
  Prefabs/
    Unit.prefab                 (Health / Skills / Inventory / Bridge / PassiveXPHooks / UnitFeedbackToasts + Visual stickman child with 15 BodyPartVisual segments)
    OrderMarker.prefab
    OrderMarker_Queued.prefab
    WorldItem_Generic.prefab    fallback world body (cube tinted via MPB)
    Workbench.prefab            crafting station with CraftingStation + NavMeshObstacle
  Scenes/
    MVP.unity                   (5 WorldItems + 9 Harvestables + 1 Workbench placed; navmesh baked w/ all obstacles)
  Art/                          materials générés par le setup (URP Lit + line)
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
| ~~3~~ | ~~Récolte (Harvestable + HarvestOrder + drops via WorldItems)~~ | **livré** |
| ~~4~~ | ~~Crafting (RecipeDefinition + Database + CraftingStation + CraftOrder, hand & workbench)~~ | **livré** |
| 5 | Combat (mêlée, dégâts directionnels, hitbox par partie du corps) | `AttackOrder : IOrder, ITargetedOrder`, `WeaponSystem`, `HealthSystem.ApplyDamage`. Construit le `DamageInfo` avec `Attacker = this.Unit` et `Weapon = ...`. Durabilité d'outil potentiellement intégrée ici. |
| 6 | Equipment (slots d'armes/outils équipés) | `Equipment` component séparé, *pas* une extension d'Inventory. Items équipés sortent de l'inventaire et passent en slot. Module Combat utilisera l'arme équipée. |
| 7 | Monde / Respawn / Save-Load | `HarvestableRespawnerService` re-spawn les noeuds après timer. Sauvegarde via `ItemData.Id` + `Recipe.Id` (lookup `ItemDatabase.GetById` / `RecipeDatabase.GetById`). |
| 8 | IA (monstres, factions) | nouveau composant `AIController` qui pousse des `IOrder` dans une `OrderQueue` exactement comme le joueur. **Réutilise `HealthSystem`, `SkillSystem`, `Inventory` tels quels.** |

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
   - `Assets/_Project/ScriptableObjects/Items/Item_*.asset` (16) + `ItemDatabase.asset`
   - `Assets/_Project/ScriptableObjects/Harvestables/HV_*.asset` (3 : Tree, Rock, FishingSpot)
   - `Assets/_Project/ScriptableObjects/Recipes/Recipe_*.asset` (9) + `RecipeDatabase.asset`
   - `Assets/_Project/Prefabs/Unit.prefab` (Health / Skills / Inventory / Bridge / PassiveXPHooks)
   - `Assets/_Project/Prefabs/OrderMarker*.prefab` + `WorldItem_Generic.prefab` + `Workbench.prefab`
   - `Assets/_Project/Scenes/MVP.unity` (NavMesh bakée autour de tous les obstacles, 5 WorldItems + 9 Harvestables + 1 Workbench placés, GameSystems wired)
5. Vérifie en bas de la console : `[MVPSceneSetup] Built scene at ...`.

### 2. Lancer
1. Ouvre `Assets/_Project/Scenes/MVP.unity`.
2. Play.

### 3. Contrôles
| Input | Effet |
|---|---|
| **Clic gauche** sur un WorldItem | `PickupOrder` (annule la file) |
| **Clic gauche** sur un Harvestable | `HarvestOrder` (annule la file ; échoue si l'outil requis manque) |
| **Clic gauche** sur le sol | `MoveOrder` (annule la file) |
| **Shift + Clic gauche** | Ajoute le même ordre à la file ; ligne preview pendant Shift |
| **Espace** | Pause / reprise |
| **WASD / flèches** | Pan caméra |
| **Molette** | Zoom |
| **F1** | Toggle Health & Skills + Inventory + Harvestables debug panel |

Priorité du raycast clic : **WorldItem > Harvestable > sol**. Tous via `GetComponentInParent<T>` — pas de layer requis.

### 4. Tester Santé/Skills/Inventory/Récolte/Crafting
- **F1** ouvre le panel à droite. Sections : Blood, Body Parts, Skills, Inventory (foldable), Harvestables (foldable), Crafting (foldable), Global Actions.
- Boutons par partie : Damage 10/30/100, Bandage, Heal 50.
- Boutons par skill : +50 XP, +500 XP.
- Inventory : Add 1/Add 10 pour chaque item du Database, Drop 1/Drop All par stack, Clear, Spawn 3 items.
- Harvestables : liste de tous les noeuds en scène avec HP courant, bouton Reset par noeud, bouton "Reset all".
- Crafting : 9 recettes du Database, greyed si inputs/station manquants, toggle "Show only craftable now", header montre `(X/9 available)` + progression live si CraftOrder en cours.
- Globaux : Drain Blood, Restore Blood, KILL, REVIVE, RESET.
- Readouts : tous les modifier getters + `weight speed x` affichés en live.

Cycle de test "from scratch to crossbow" :
1. Add stone_axe via debug → clic sur arbre → wood_log éparpillés → ramasse-les.
2. Add stone_pickaxe (debug ou Craft hand-craft si tu as les inputs) → clic sur rocher → stone_chunk éparpillés → ramasse.
3. Va devant le Workbench (clic Craft Spear Stone) → l'unité s'y rend, 6s de progress → spear_stone produit, +10 XP Labour. Inputs (wood_log×2 + stone_chunk×1) retirés.
4. Add Labour XP via debug → temps de craft visiblement réduit sur la recette suivante.

**Feedback visuel** : `FloatingTextService` (sur GameSystems) affiche des toasts world-anchored **à la position du curseur souris projeté sur le plan sol** :
- Pickup réussi → vert, `+N <Item>` (ex: `+3 Wood Log`).
- Output de craft → vert (idem, un par output).
- Outil manquant (harvest) → rouge, `Tool required: <Item>`.
- Input manquant (craft) → rouge, `Missing: <Item> ×N`.
- Pas de station (craft) → rouge, `No Workbench found`.
- Inputs disparus pendant un craft → rouge, `Inputs lost: <Item> ×N`.
- **Level up de skill** → doré, `LEVEL UP — <Skill> Lv<N>`, durée 2.5s, anchor au-dessus de la tête de l'unité (pas mouse — c'est un évènement de l'unité, pas du clic). Branché via `UnitFeedbackToasts` sur le prefab Unit.

API :
- `SpawnAtMouse / SpawnPickupAtMouse / SpawnErrorAtMouse / SpawnInfoAtMouse` — projection auto curseur → plan y=0 (utilisé par tous les hooks actuels).
- `Spawn / SpawnPickup / SpawnError / SpawnInfo` (avec `Vector3 worldPos`) — pour des futures sources (dégâts à l'emplacement d'un hit, etc.).
- `TryGetMouseGroundPosition(out worldPos)` — helper public exposé si d'autres systèmes veulent le même anchor.

Les toasts utilisent `Time.unscaledTime` — ils continuent d'animer/fader même pendant la pause (UI pure, hors gameplay clock). Rendu via OnGUI (pas de prefab/Canvas/TMP — IMGUI suffit au scale actuel). Si le curseur est hors monde (camera null, raycast manque le plan), le toast est silencieusement skip.

---

## Anti-patterns connus (à ne pas reproduire)

- `Time.deltaTime` dans `Health`, `Skills`, `Items`, `Harvesting`, `Crafting`, `Unit`, `OrderQueue`, ou tout futur gameplay → **utiliser `GameTime.DeltaTime`**.
- Logique métier dans `Unit` → **créer un component dédié**.
- Référence directe entre `HealthSystem`, `SkillSystem`, `Inventory` → **passer par events + bridge**.
- `Resources.Load` → **utiliser ItemDatabase / RecipeDatabase / refs sérialisées**.
- Auto-pickup à proximité → **toujours via PickupOrder**.
- Hardcoder la liste des items / les stats des harvestables / les recettes → **tout via SO**.
- `HarvestOrder` qui ajoute directement à Inventory → **drops passent par WorldItems** (un autre acteur peut les ramasser, save/load les capture).
- `CraftOrder` qui consomme les inputs au démarrage → **consommation à la fin uniquement** (annulation lossless, re-check d'inputs au moment du Remove).
- `CraftOrder` qui référence `RecipeDatabase` → **prend une `RecipeDefinition` directement**, le database sert au debug panel / au save.
- XP de craft par tick → **uniquement à la complétion**, sinon farm de recette ultra-courte.
- Animations bloquantes pendant récolte / craft → **MVP : juste agent.isStopped = true, pas d'anim attendue**.
- Re-bake NavMesh à chaque frame → **uniquement dans MVPSceneSetup**. Le carving runtime de `NavMeshObstacle` gère les destructions.
- Quantité négative dans un stack → **clamp à 0 (suppression du stack)**.
- Drops superposés au même pixel → **offset random dans un cercle**.
- Recréer un Material à chaque tint (WorldItem, Harvestable runtime) → **MaterialPropertyBlock**.
- Recalculer `EffectiveMaxHP` à chaque frame → **uniquement sur level up (via Recompute)**.
- Level up = heal gratos → **préserver les ratios** (politique Vitality).
- Saigner une partie Severed mais avec `BleedRateSevered = 0` → **mettre la valeur dans la SO, pas en dur**.
- Pop-up de level up bloquant → **passif, juste event + UI update**.
- `FindObjectOfType` répété en runtime → **cacher la ref (cf. `CraftingStation.ActiveStations` registry pattern)**.
- UI worldspace pour interagir avec les stations → **post-MVP, le debug panel suffit pour l'instant**.
- Hit colliders par body part → **réservé Module 6 Combat**. Pour l'instant un seul `CapsuleCollider` englobant sur la racine du Unit.
- Cascade Severed inverse (HandLeft severed → ArmLeft severed) → **uniquement parent → child**, jamais l'inverse.
- Modifier `NavMeshAgent.baseOffset` au runtime → **set à 0 une fois pour toutes dans le prefab**, les pieds sur le sol.
- Cloner des Materials pour le Poucou → **un seul `Mat_Poucou.mat` partagé, MPB par renderer**.
- Modifier les proportions du Poucou sans synchroniser le `CapsuleCollider` et le `NavMeshAgent` → **toujours mettre les trois à jour ensemble** (héberger les valeurs dans `BuildUnitPrefab` + `BuildUnitVisual`, le `Rebuild Unit Visual` ne touche pas root collider/agent).

---

## Limites connues / TODO

- **TimeScale (slow-mo)** : `GameTime.TimeScale` est exposé mais `NavMeshAgent` n'utilise pas notre delta time. Pour un vrai slow-mo, il faudra moduler `agent.speed *= GameTime.TimeScale` côté bridge.
- **Pas de Cinemachine** : non installé. La caméra custom suffit.
- **Pas d'asmdef** : tout part dans `Assembly-CSharp`. Si la compile devient lente on découpera. Dépendances actuelles :
  - `Health → Units`
  - `Skills → Health + Items`
  - `Items → Units` (PickupOrder)
  - `Harvesting → Items + Skills + Units`
  - `Crafting → Items + Skills + Units`
  - `PlayerInput → Units + Items + Harvesting`
  - `Debug → Health + Skills + Items + Harvesting + Crafting + PlayerInput + Units`
  - `UI → (no inbound)` — toast service called by gameplay; pure service.
  - Orders (`Items.Orders`, `Harvesting.Orders`, `Crafting.Orders`) → `UI` for toast spawns.
- **Pas de sélection multi-unités** : MVP mono-unité. `PlayerInputController` cherche le 1er Unit de la scène en fallback.
- **WorldItem dropped quantity invisible** : la pile au sol ne montre pas sa qty. La fusion via `Inventory.DropStack` est correcte mais on ne *voit* pas que la pile contient 5+ items. À régler avec un worldspace TMP label.
- **Edit-mode preview des WorldItems** : depuis Session 4, ils apparaissent en gris dans l'éditeur (le tint via MPB se fait au Play). Acceptable, mais si gênant : remettre une création d'asset Material par couleur.
- **Pickup range fixe à 1.5m** dans `PickupOrder` (constante), pas exposée. À aligner avec une éventuelle valeur sur `ItemData` ou `Unit` si on veut moduler par taille de l'unité.
- **Harvestable durabilité d'outil** : pas de consommation. À ajouter quand `ItemData` aura une notion de `Durability`.
- **Harvestable respawn** : aucun pour le MVP. Module 7.
- **Multi-tools acceptés** par Harvestable : un seul outil requis. Quand on aura plusieurs paliers (hache bronze, hache fer), passer à `List<ItemData> AcceptedTools`.
- **Pas d'anim de récolte** : l'unité reste figée pendant qu'elle "frappe". Visuellement médiocre, fonctionnellement correct.
- **Fishing spot collider inflated** : la box collider/obstacle est verticale à 2m pour bloquer la bake, alors que le visuel est plat. Click acceptable, mais sémantiquement c'est un "mur invisible au-dessus de l'eau".
- **Pas d'UI worldspace pour les stations de craft** : tout passe par le debug panel F1. Post-MVP, double-clic sur Workbench ouvrira un panneau de recettes filtrées.
- **Armes craftées dorment dans l'inventaire** : `spear_stone`, `sword_stone`, etc. ne servent à rien tant que Combat (Module 5) et Equipment (Module 6) ne sont pas livrés. Volontaire — on n'a pas voulu attendre.
- **Pas de multi-outputs stochastiques** : chaque recette produit exactement ses Outputs déclarés. À étendre via `RecipeDefinition.OutputRoll` si besoin.
- **Pas d'anim de craft** : l'unité reste figée comme pour Harvest. `LookAt` cosmétique uniquement.
- **Poucou raide** : pas de rigging Mecanim, les bras pendent verticalement, les pieds ne pivotent pas, pas d'idle bobbing. À reprendre avec un mesh rigged si on veut animer. La hiérarchie actuelle ne suit pas la convention humanoid Unity.
- **Severed parts juste disabled** : les membres sectionnés sont invisibles, pas détachés physiquement. Pour un effet "membre qui tombe au sol" il faudrait re-parent + Rigidbody. Module 6+.
- **Pas de hit colliders par part** : Module 6 (Combat). Pour l'instant un seul CapsuleCollider sur la racine, les dégâts sont dispatchés via `DamageInfo.TargetPart` par le caller (debug panel today, AttackOrder demain).
