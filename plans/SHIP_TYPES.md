# Ship types

Ship types have:
- One engine layout
- Set of initial turrets
- Max hp
- Weapon slots
- Graphics definition
- Model name
- Description

At game start, player must select one ship to fly during the game.


# Implementation phases:

## Phase 1 Ship record

Starting ships:
- Scout: balanced engines, 8 hp, side shotguns, green thin triangle
- Fighter: Pursuit engines, 10 hp, machinegun, medium blue triangle
- Heavy: new slow engines, 20 hp, side shotguns and machinegun, large fat red triangle

## Phase 2 Weapon specific upgrades

Add weapon slots to the ships: heavy gets 3, scout 1, fighter 2

Weapons get their own upgrade stats
- auto targeting range for weapons which do autotargeting 15 %
- range (how long shots live) 15 %

New weapon rail gun that shoots a fast powerful shot forward
- no auto targeting
- long cooldown 1,5 sec
- more kick back then other weapons
- slightly bright blue bullet (1,5 x size of machine gun bullet)

New weapon twin chain gun
- two side turrets firing straight forward only
- low scatter, fast bullet speed, fast fire rate, 
- no auto targeting, fires constantly
- upgrades: rate of fire, range, bullet speed

New weapon acid bubble spray
- high scatter
- high rate of fire
- low range
- one turret fires forward
- large green globs as bullets (2x size of machine gun bullet)
- no autotargeting, fires constantly

Point defence turret
- Like a chain gun, but firing arc is 270 degrees pointing backwards
- slower projectile speed and fire rate than machine gun
- has autotargeting

# Phase 3 minor upgrade vs new weapon

Upgrades will now be selected at each level as follows:

Levels 1-4 minor upgrade
- Hp upgrade 2 points
- 3 random weapon upgrades for any weapon carried by player ship currently
- 1 random engine upgrade

Level 5 new weapon
- Hp upgrade 5 points
- 2 Random weapons

Weapons already carried may appear too. They will upgrade damage of the duplicated weapon and won't occupy a new slot. 
New weapons will occupy a weapon slot on the ship.

After level 5, the same upgrade cycle will repeat, so four minor upgrades, then weapon upgrade and so on.

## Phase 4 Engine upgrades

Engine upgrades:
- Forward acceleration 10%
- Faster turning 10%
- Faster side thrust 10%
- Faster backwards thrust 10%

## Phase 5 Upgrades defined with weapon types

WeaponTypes should include definition of what upgrades they can get.

Each possible upgrade should have:
- name
- list of upgrade changes consisting of: what stat can change and by how much and is it additive or multiplicative

Weapon type can have many different minor upgrade options and one major upgrade option which is applied if player chooses an existing weapon type
in new weapon upgrade.

When one of these weapon types is equipped on the player ship, it's upgrades are available to be randomly selected as one upgrade choice during minor level up.