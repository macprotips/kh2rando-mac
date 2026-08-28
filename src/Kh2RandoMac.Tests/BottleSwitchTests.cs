using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class BottleSwitchTests
{
    private static readonly Bottle Target = new() { Name = "KH2", Root = "/bottles/KH2" };
    private static readonly Bottle Other = new() { Name = "Steam", Root = "/bottles/Steam" };
    private const string GameDir = "/Volumes/Games/KINGDOM HEARTS -HD 1.5+2.5 ReMIX-";

    private static BottleSwitchFacts Facts(
        bool gameDirUsable = true,
        bool targetIsRunning = false,
        GameInstall? detected = null,
        bool detectionFailed = false,
        bool leavingWasSetUp = false,
        string? gameDir = GameDir,
        string currentLauncher = "Steam") =>
        new("Steam", currentLauncher, gameDir, gameDirUsable, targetIsRunning,
            "Quit the game and Steam in CrossOver", detected, detectionFailed, leavingWasSetUp);

    [Fact]
    public void RefusesBeforeSetupHasFoundAGame()
    {
        var plan = BottleSwitch.Plan(Target, Facts(gameDir: null, gameDirUsable: false));
        Assert.Equal(BottleSwitchOutcome.NeedsGameFolder, plan.Outcome);
        Assert.Null(plan.Install);
    }

    [Fact]
    public void RefusesWhenTheGameFolderIsNotReachable()
    {
        // Drive unplugged: the folder is remembered but nothing can be installed into it.
        var plan = BottleSwitch.Plan(Target, Facts(gameDirUsable: false));
        Assert.Equal(BottleSwitchOutcome.NeedsGameFolder, plan.Outcome);
    }

    [Fact]
    public void RefusesUpFrontWhenSomethingIsUsingTheTargetBottle()
    {
        var plan = BottleSwitch.Plan(Target, Facts(targetIsRunning: true));
        Assert.Equal(BottleSwitchOutcome.TargetBusy, plan.Outcome);
        Assert.Null(plan.Install);
        Assert.Contains("Quit the game and Steam", plan.Message);
    }

    [Fact]
    public void BusyCheckComesBeforeAnythingElse()
    {
        // A busy bottle with no game folder still reports the game folder first, since
        // that is the one the user can act on without quitting anything.
        var plan = BottleSwitch.Plan(Target, Facts(gameDirUsable: false, targetIsRunning: true));
        Assert.Equal(BottleSwitchOutcome.NeedsGameFolder, plan.Outcome);
    }

    [Fact]
    public void WarnsWhenTheTargetBottleCannotSeeTheGame()
    {
        var plan = BottleSwitch.Plan(Target, Facts(detected: null));
        Assert.Equal(BottleSwitchOutcome.Ready, plan.Outcome);
        Assert.Contains("does not have this copy of the game", plan.Message);
    }

    [Fact]
    public void SaysNothingAboutReachabilityWhenDetectionCouldNotRun()
    {
        // Not knowing is not the same as knowing it cannot; do not invent a warning.
        var plan = BottleSwitch.Plan(Target, Facts(detected: null, detectionFailed: true));
        Assert.DoesNotContain("does not have this copy", plan.Message);
    }

    [Fact]
    public void WarnsOnlyWhenTheBottleBeingLeftWasActuallySetUp()
    {
        Assert.DoesNotContain("keeps the changes", BottleSwitch.Plan(Target, Facts()).Message);

        var plan = BottleSwitch.Plan(Target, Facts(leavingWasSetUp: true));
        Assert.Contains("'Steam' keeps the changes", plan.Message);
        Assert.Contains("cancel and run Reset first", plan.Message);
    }

    [Fact]
    public void TakesTheStoreFromTheBottleBeingSwitchedTo()
    {
        // The target holds an Epic copy while the config still says Steam.
        var epic = new GameInstall(Target, GameDir, "EGS");
        var plan = BottleSwitch.Plan(Target, Facts(detected: epic, currentLauncher: "Steam"));

        Assert.Equal("EGS", plan.Install!.Launcher);
        Assert.Equal(Target.Name, plan.Install.Bottle.Name);
    }

    [Fact]
    public void KeepsTheCurrentStoreWhenTheTargetKnowsNothingAboutTheGame()
    {
        var plan = BottleSwitch.Plan(Target, Facts(detected: null, currentLauncher: "EGS"));
        Assert.Equal("EGS", plan.Install!.Launcher);
        Assert.Equal(GameDir, plan.Install.GameDirMac);
        Assert.Equal(Target.Name, plan.Install.Bottle.Name);
    }

    [Fact]
    public void AlwaysInstallsIntoTheBottleBeingSwitchedTo()
    {
        // A detected record naming another bottle must not redirect the install. The
        // caller filters by bottle today, so this guards the rule rather than a bug.
        var mismatched = new GameInstall(Other, GameDir, "EGS");
        var plan = BottleSwitch.Plan(Target, Facts(detected: mismatched));

        Assert.Equal(Target.Name, plan.Install!.Bottle.Name);
        Assert.Equal("EGS", plan.Install.Launcher);
    }
}
