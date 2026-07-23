namespace Spacevors.Domain.Components;

public enum EngineLayout { Balanced, Maneuverable, Pursuit }

public enum Loadout { Forward, Broadside }

public record GameChoice(EngineLayout Engine, Loadout Weapon);
