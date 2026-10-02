using Wukong.Application;
using Wukong.Desktop;

internal static class CompanionEpisodeTests
{
    public static void RealCatalogUsesCompatibleEpisodePreparation()
    {
        var motions = DesktopMotionCatalog.Load(AppContext.BaseDirectory);
        var catalog = DesktopBehaviorCapabilityCatalog.Create(motions.Motions);
        var sit = PetRuntimeState.Default with { CurrentPosture = StablePosture.Sit, CurrentPoseId = "sit.neutral.left_front" };
        var available = DesktopAutonomousEpisodeBindings.AvailableEpisodes(catalog, sit);
        Require(available.Contains(PetEpisodeKind.Exploring), "sitting cannot prepare approved stand-up before exploring");
        var bindings = DesktopAutonomousEpisodeBindings.For(PetEpisodeKind.Exploring)!;
        Require(bindings.Contains(AutonomousDailyCandidateBehaviorIds.SitToStand), "exploring lacks stand preparation");
        Require(bindings.Contains(AutonomousDailyCandidateBehaviorIds.ProneToSit), "exploring lacks side-prone preparation");
        var front = sit with { CurrentPosture = StablePosture.Prone, CurrentPoseId = "prone.awake.front" };
        Require(!DesktopAutonomousEpisodeBindings.AvailableEpisodes(catalog, front).Contains(PetEpisodeKind.Exploring),
            "front prone used an unapproved side-prone rise bridge");
        Require(AutonomousAgentRolloutOptions.ContinuityV1.IsAuthoritative(PetEpisodeKind.Recovering),
            "exhaustion unexpectedly returns control to legacy random behavior");
        var recovering = DesktopAutonomousEpisodeBindings.For(PetEpisodeKind.Recovering)!;
        foreach (var id in recovering)
        {
            var capability = catalog.Find(id);
            if (capability is null) continue;
            Require(capability.Category is BehaviorSemanticCategory.StableIdle or BehaviorSemanticCategory.PostureTransition,
                "recovery includes an energetic/sleep/owner-only sequence");
        }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
