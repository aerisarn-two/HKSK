# Thrown and shot projectiles: how the player and creatures launch them

The third companion of `docs/flight.md` and `docs/swimming.md`: what a projectile is
to the engine, who launches one and from which animation event, how a creature
"throws" when Skyrim has no thrown weapon type, and what that leaves the player.
Evidence: the masters through Mutagen (projectiles, ammunition, weapons, magic
effects, idles, races), the executable (AE layout, addresses as in
`docs/animation-events.md`), and the graphs through HKX2 (`rieklingbehavior`,
`scbehavior`, `atronachflamebehavior`, the player's `1hm_behavior` and `magicbehavior`).

## 1. What a projectile is

A `PROJ` record has a type, and the type is a class: `ArrowProjectile`,
`MissileProjectile`, `GrenadeProjectile` (the record type is *Lobber*),
`BeamProjectile`, `FlameProjectile`, `ConeProjectile`, `BarrierProjectile`, all on
`Projectile`, itself a `TESObjectREFR`. The masters hold:

| type | count | examples |
| --- | --- | --- |
| Missile | 102 | fireballs, firebolts, icicles, spit, catapult stones, ballista bolts |
| Arrow | 38 | every arrow and bolt, the riekling spear, the sphere's bolt |
| Cone | 47 | shouts |
| Flame | 24 | breath, sprays, the centurion's steam |
| Beam | 15 | lightning bolts, absorb beams |
| Lobber | 10 | the rune traps and the oil decal, all with gravity 0 |

The `Lobber` type, the grenade class, is what a thrown weapon would be in Fallout,
and in Skyrim it is used for nothing that flies: the ten records are rune spells
placed on the ground with gravity 0 and speed 1000. What arcs in Skyrim is a
**Missile with gravity**: the chaurus and spider spit (gravity 1, speed 2000, range
4000), the catapult stone (gravity 1, speed 4000), the Dwemer ballista bolt
(gravity 0.8, speed 2800), the ice wraith's shard (gravity 20000, range 768, a
short-range drop), the Solstheim spider bombs (gravity 1 to 4). Every other missile
has gravity 0 and flies straight.

**Gravity is the record's.** `Projectile` virtual `0xB5` (`0x1407e7a00`) returns
the `PROJ` data's gravity field (`+0x84`), or 1.0 when data flag bit 17 is set.
`GrenadeProjectile` does not override it: a lobber falls by its record's gravity
like a missile does, and the two classes differ in their update (`0xAB`,
`0x1407d6ee0` against `0x1407dd640`), the grenade bouncing and arming rather than
impacting. `ArrowProjectile` overrides it (`0x1407c93a0`): the record's gravity
times `fArrowWeakGravity` times the shot's power at `+0x190`, so a weak draw drops
faster; the arrow's own settings are `fArrowGravityBase`, `fArrowGravityMult`
(`0x140418720`), `fArrowMinPower`, `fArrowBowMinTime`, the bounce set, and
`fArrowSpeedMult` and `fArrowFakeMass`, which nothing reads.

**Where it spawns.** The launch takes a node on the actor; the engine's string
table carries one bone name for it, `ProjectileNode` (slot `0x208`, 17 readers),
which only two shipped skeletons have: the chaurus, whose spit leaves it, and the
Dawnguard bellows trap. Every other creature launches from the magic node or the
weapon.

## 2. Who launches, and from which event

One function creates and launches a projectile of any type, `0x1407e46c0`, from a
launch-data block; it calls each class's creator (`0x1407c91c0` arrow, `0x1407dd350`
missile, `0x1407d69b0` grenade, `0x1407cb280` beam, `0x1407d5590` flame,
`0x1407ccb30` cone, `0x1407c9f90` barrier). It has three callers that matter:

- **the weapon**, `0x140286c90`, called by the `ArrowRelease` handler
  (`docs/animation-events.md` §4): the ammunition's projectile, from the equipped
  bow or crossbow, with `fBowNPCSpreadAngle` applied to an NPC's shot. The event
  a clip fires is `arrowRelease`, after `arrowAttach` and `BowDrawn`;
- **the magic caster**, `0x1405bfd60`, called from the caster's fire paths
  (`0x1405bc160`, `0x1405bf630`, `0x1405bfc00`): the magic effect's projectile,
  for an Aimed effect (235 fire-and-forget and 95 concentration effects carry
  one), on `MLh_SpellFire_Event`, `MRh_SpellFire_Event` or `Voice_SpellFire_Event`;
- **the explosion**, `0x140639840`, for an explosion that spawns projectiles.

The fourth caller, `0x1401b6c10`, is not a launch: it is the reference placement
that Papyrus `PlaceAtMe` and the load code use, which creates a projectile
reference when the placed form is a `PROJ`. A fifth site, `0x1406bc9e0`, creates
arrows, beams and missiles without launching, from a player-side virtual
(`0x1407629c0`); it was not read further.

So every projectile in the game leaves an actor on one of four animation events:
`arrowRelease` for a weapon, the three spell-fire events for magic. There is no
"throw" event, no throw action, and no thrown weapon type: the weapon animation
types are hand-to-hand, five melee types, bow, staff and crossbow.

## 3. How a creature throws

**A thrown spear is a bow.** The riekling that throws spears
(`DLC2EncRiekling01Missile`) carries `DLC2crRieklingSpearThrower`, a `WEAP` of
animation type Bow, non-playable, and `DLC2RieklingSpearThrown`, an `AMMO` flagged
non-bolt, whose projectile `DLC2ArrowRieklingSpearProjectile` is an Arrow. The
Dwemer sphere carries `crDwarvenSphereCrossbow` (type Bow, playable) and
`DwarvenSphereBolt01` (non-playable) for `ArrowDwarvenSphereBoltProjectile`. The
Dwemer ballista of Dragonborn is the same pair. The ballista traps of Skyrim.esm
are bow-type weapons too, `TrapDweBallistaWeapon*`, fired by activators through
the reference placement.

The idle tree and the graph then treat the throw as archery. The riekling's idle
`RieklingSpearThrow` sits under `ActionRightAttack` and sends `bowAttackStart`;
its graph answers with `Crossbow_Attack`, a machine of `Crossbow DrawSpear`
(entered on `bowAttackStart`), `Crossbow Drawn` (on `bowDrawn`, holding the drawn
combat idle, turns and locomotion) and `Crossbow Release` (on `attackRelease`),
whose clip `Crossbow_Attack1` fires `arrowRelease` at 0.77 s and `bowRelease` at
1.20 s and exits with `arrowDetach`. The sphere's `Ranged_Draw` clip fires
`bowDrawn` and its `Crossbow_Attack_State` the same way. The engine sees a bow
being drawn and released: `ArrowAttach` attaches the spear model to the biped as
`Arrow%d`, `BowDrawn` sets the bow-drawn attack state, `ArrowRelease` launches the
ammunition's projectile and moves the state to released. The combat AI drives it
as archery too, with `CombatBehaviorTreeRanged` (`Ranged Attack Idle`, `Ranged
Attack`, `Ranged Attack Selector`, `Ranged Attack Repeat`) and the ranged movement
tree, and the riekling's combat style `DLC2csRieklingMissile01` is a ranged style.

**A spat missile is a spell.** The chaurus, the frostbite spider and the Solstheim
spiders throw through magic: `crChaurusSpitFFAimed` and `crSpiderSpitFFAimed` are
Aimed fire-and-forget effects whose projectiles are gravity-1 missiles, cast by the
magic tree and fired on a spell-fire event from the creature's cast clip. The
flame atronach's fire is the same (`Mag_Con_Fthrow_Loop` enters with
`MLh_SpellFire_Event`), the dragon's breath is a Flame projectile on
`Voice_SpellFire_Event`, the shouts are Cones. The names carry `Throw` in the
atronach's states and in the player's `AttackThrow*` first-person spell clips, but
the engine has no such event; they are cast animations.

**Attack data spells are not projectiles.** The `ATKD` spell on a race's attack,
`crDragonTail`, `crSprigganPoisonClaw`, the disease bites, is applied on the hit,
not launched.

## 4. What the AI does with a projectile

The ranged combat tree fires by chance and hold time (`fCombatRangedAttackChance*`,
`fCombatRangedAttackHoldTime*`, `fCombatRangedAttackMaximumHoldTime`) and aims
through `CombatProjectileAimController` (`0x1418c59a8`). Its solve (`0x1407f7c40`)
iterates up to `iCombatAimMaxIterations` times until the change is under
`fCombatAimDeltaThreshold`, keeps the aim point `fCombatAimProjectileGroundMinRadius`
off the ground, and adds `fCombatAimProjectileRandomOffset`; the target is
re-aimed every `fCombatAimProjectileUpdateTime`, and the shot gets
`fCombatRangedAimVariance` (`0x1405557e0`). For a projectile that falls, the ranged
context raises the shot by `fCombatRangedProjectileFiringArc` (`0x140556390`,
`0x140555f30`, `0x14081f170`), which is how a chaurus lobs its spit and a riekling
its spear. All of it is global; a creature's own contribution is its projectile's
gravity and speed, its combat style's ranged block, and the reach the idle tree
keys on.

## 5. The player

The player launches on the same four events: `arrowRelease` from a bow or crossbow,
the three spell fires from the hands and the voice. A thrown weapon for the player
is therefore the riekling's pattern with the flags turned: a Bow-type `WEAP` whose
draw, hold and release clips are a throw, and an Arrow-type `AMMO` whose projectile
is the thrown thing, with `fArrowWeakGravity` and the draw power deciding the arc.
The riekling's ammunition is already playable; its weapon is not, and the player's
graph would need a bow variant whose `BowDraw`, `BowDrawn` and release clips look
like a throw, since the shipped `Bow_AttackState` is a bow. Everything below the
graph, attach, draw state, release, spread, aim assist, sticks in targets and
recovery of the ammunition, comes for free from the arrow path. A projectile that
should arc more than a weak arrow gets its gravity from its own `PROJ` record.

A spell that throws is nothing new: an Aimed effect with a gravity missile, cast
from any hand, is the chaurus's spit in the player's hand.

**Telekinesis** is the one true throw of an object the player did not own. The
effect (`TelekinesisEffect`, archetype Telekinesis, concentration, self) holds a
reference on a spring (`fMagicTelekinesisSpring*`, `fMagicTelekinesisMaxForce`) at
`fMagicTelekinesisBaseDistance`, moves it by `fMagicTelekinesisMove*`, and on
release throws it by `fMagicTelekinesisThrow`, `ThrowAccelerate` and `ThrowMax`
(`0x1405d7100`), doubled by `fMagicTelekinesisDualCastThrowMult`, with damage from
`fMagicTelekinesisDamageBase` and `Mult`. The thrown reference is not a projectile:
it is the object itself under Havok, and it hits by physics. That is the only
route to throwing an arbitrary object, and it is a spell, not a weapon.

What is missing for a thrown-weapon player: nothing in the engine, only clips and
records. What cannot be had: a throw that is neither a bow nor a spell. The engine's
grenade class exists and works, and no record uses it in flight; a Lobber `PROJ`
with gravity and speed, launched as ammunition, would arc and bounce, but the
ammunition path attaches and launches whatever projectile the `AMMO` names, so a
lobber on a bow-type weapon is the untested but plausible route to a bouncing
throw.

## 6. How this was measured

Projectiles, ammunition, weapons, effects, idles, races and settings were read from
the five masters with Mutagen. The graphs were dumped with HKX2. The executable was
searched for the projectile class vtables and their constructors' callers, the
projectile-own virtuals beyond `TESObjectREFR` (slots `0x9B` to `0xBF`, compared per
class), the readers of the arrow, aim, ranged and telekinesis settings by their
setting records, and the `ProjectileNode` name-table slot; the functions found
were read with `index.py dump`. The player-side thrown-weapon route is reasoning
from the riekling's records and the arrow handlers, not a run of the game.
