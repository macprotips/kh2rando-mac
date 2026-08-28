namespace Kh2RandoMac.Core;

/// <summary>Whether a switch can go ahead, and why not when it cannot.</summary>
public enum BottleSwitchOutcome
{
    /// <summary>Nothing is set up yet, so there is no game to install into.</summary>
    NeedsGameFolder,

    /// <summary>Something is running in the target bottle; setting it up needs it idle.</summary>
    TargetBusy,

    /// <summary>Good to go, once the user has read what it involves.</summary>
    Ready,
}

/// <summary>The decision, plus the words for it and what to actually set up.</summary>
public record BottleSwitchPlan(BottleSwitchOutcome Outcome, string Title, string Message, GameInstall? Install);

/// <summary>
/// Everything the decision depends on, gathered by the caller. Passing facts in rather
/// than reading the machine here is what lets the rules be tested: a switch runs a full
/// setup against a bottle, which is not something to find out about in production.
/// </summary>
public record BottleSwitchFacts(
    string? CurrentBottle,
    string CurrentLauncher,
    string? GameDir,
    bool GameDirUsable,
    bool TargetIsRunning,
    string TargetBusyDescription,
    GameInstall? DetectedInTarget,
    bool DetectionFailed,
    bool LeavingWasSetUp);

/// <summary>Working out what moving the game to another bottle would mean.</summary>
public static class BottleSwitch
{
    public static BottleSwitchPlan Plan(Bottle target, BottleSwitchFacts facts)
    {
        if (facts.GameDir == null || !facts.GameDirUsable)
            return new BottleSwitchPlan(BottleSwitchOutcome.NeedsGameFolder, "No game folder yet",
                "Run Setup first, so the app knows which copy of the game to install into.", null);

        // Checked before asking, as everywhere else: a refusal after the dialog reads as
        // the button having done nothing.
        if (facts.TargetIsRunning)
            return new BottleSwitchPlan(BottleSwitchOutcome.TargetBusy, "Something is using that bottle",
                $"Setting '{target.Name}' up needs it to itself.\n\n" +
                $"{facts.TargetBusyDescription}, then choose it again.", null);

        var message =
            $"'{target.Name}' needs the mod loader, the DLL overrides and the runtimes " +
            "the tracker and Re:Fined use. That is a few minutes, and it runs now. " +
            "Quit the game and Steam first.";

        // Setting the loader up in a bottle that cannot see the game leaves someone
        // configured to a bottle that will not launch, with nothing on screen saying so.
        // Detection failing is not the same as knowing it cannot: say nothing then.
        if (facts.DetectedInTarget == null && !facts.DetectionFailed)
            message += $"\n\nNote that '{target.Name}' does not have this copy of the game in " +
                "its library, so it will not be able to launch it until you add it there.";

        if (facts.LeavingWasSetUp)
            message += $"\n\n'{facts.CurrentBottle}' keeps the changes this app made to it. Reset " +
                $"only ever acts on the bottle in use, so if you want '{facts.CurrentBottle}' back " +
                "to stock, cancel and run Reset first.";

        // Take the store from detection, which knows which one the copy belongs to in
        // that bottle; carrying the old setting over would happily call an Epic copy
        // Steam and configure LuaBackend and the launch route for the wrong one. The
        // bottle is always the one being switched to, never whatever the detected
        // record names: setting the loader up somewhere the user did not choose is a
        // worse failure than getting the store wrong.
        var launcher = facts.DetectedInTarget?.Launcher ?? facts.CurrentLauncher;
        var install = new GameInstall(target, facts.GameDir, launcher);

        return new BottleSwitchPlan(BottleSwitchOutcome.Ready, $"Use bottle '{target.Name}'", message, install);
    }
}
