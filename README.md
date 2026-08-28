# Who Cooked This Meal?

RimWorld 1.6 mod that records the player-colony colonist or colony mech who made a meal and the maker's Cooking level at production time. The information appears below the vanilla ingredient line in the meal inspect panel.

## Build

Set `RimWorldDir` to the RimWorld installation directory, then run:

```powershell
dotnet build .\Source\WhoCookThisMeal.csproj -p:RimWorldDir="C:\Path\To\RimWorld"
```

The DLL is written to `Assemblies/WhoCookThisMeal.dll`. Copy or build the complete mod folder into RimWorld's `Mods` directory.

The current workspace does not contain the game's managed assemblies, so compilation cannot be completed here until `RimWorldDir` points to an installed copy of the game.
