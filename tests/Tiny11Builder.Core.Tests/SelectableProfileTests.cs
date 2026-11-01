using Tiny11Builder.Core;
using Xunit;

namespace Tiny11Builder.Core.Tests;

public sealed class SelectableProfileTests
{
    private static BuildRecipe Plus(ComponentChoice defender = ComponentChoice.Keep) => new()
    {
        Mode = BuildMode.Custom, Flavor = BuildFlavor.Regular, ShuddhiCorePreset = true,
        SelectedOptions = CleanupCatalog.ForMode(BuildMode.Tiny11),
        WindowsUpdateChoice = ComponentChoice.Remove, DefenderChoice = defender
    };

    [Theory]
    [InlineData(ComponentChoice.Keep)]
    [InlineData(ComponentChoice.Disable)]
    [InlineData(ComponentChoice.Remove)]
    public void Plus_preserves_balanced_actions_requires_update_removal_and_applies_defender_choice(ComponentChoice defender)
    {
        var inventory = Inventory.Concat(SystemComponentSelection.CoreServiceNames.Select(name => Item(ImageItemKind.Service, name))).ToArray();
        var recipe = Plus(defender);
        recipe.ValidateOptions();
        Assert.Equal(BuildMode.Tiny11Core, CleanupCatalog.ModeForRecipe(recipe));
        var compiled = SystemComponentSelection.CompileChoices(recipe, inventory);
        Assert.Equal(BuildFlavor.Regular, compiled.Flavor);
        Assert.False(compiled.ShuddhiCorePreset);
        Assert.All(recipe.SelectedOptions, id => Assert.Contains(id, compiled.SelectedOptions));
        Assert.Contains(compiled.ExpertActions, action => action.Identity == "wuauserv" && action.Mode == ExpertActionMode.Remove);
        Assert.Equal(defender != ComponentChoice.Keep, compiled.ExpertActions.Any(action => action.Identity == "WinDefend"));
        if (defender == ComponentChoice.Keep)
            Assert.DoesNotContain(compiled.ExpertActions, action => action.Identity is "WinDefend" or "WdFilter" or "Sense");
        Assert.All(SystemComponentSelection.CoreServiceNames, name =>
            Assert.Contains(compiled.ExpertActions, action => action.Identity == name && action.Mode == ExpertActionMode.Disable));
        Assert.DoesNotContain(compiled.ExpertActions, action => action.Identity is "BITS" or "TrustedInstaller" or "mpssvc");
    }

    [Fact]
    public void Plus_rejects_update_keep_missing_update_payload_and_silent_preset_changes()
    {
        Assert.Throws<InvalidDataException>(() => (Plus() with { WindowsUpdateChoice = ComponentChoice.Keep }).ValidateOptions());
        Assert.Throws<InvalidDataException>(() => (Plus() with { WindowsUpdateChoice = ComponentChoice.Disable }).ValidateOptions());
        Assert.Throws<InvalidDataException>(() => (Plus() with { SelectedOptions = [] }).ValidateOptions());
        Assert.Throws<InvalidDataException>(() => SystemComponentSelection.CompileChoices(Plus(),
            Inventory.Where(item => item.Identity != "wuauserv").ToArray()));
        Assert.Throws<InvalidDataException>(() => SystemComponentSelection.CompileChoices(Plus(),
            Inventory.Where(item => item.Kind != ImageItemKind.ComponentPayload).ToArray()));
    }

    [Fact]
    public void Plus_skips_absent_optional_services_and_roundtrips_the_preset()
    {
        var profile = Plus();
        var json = System.Text.Json.JsonSerializer.Serialize(profile, JsonDefaults.Options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<BuildRecipe>(json, JsonDefaults.Options)!;
        Assert.True(restored.ShuddhiCorePreset);
        var compiled = SystemComponentSelection.CompileChoices(restored, Inventory);
        Assert.DoesNotContain(compiled.ExpertActions, action => action.Identity == "Spooler");
        Assert.Contains(compiled.ExpertActions, action => action.Identity == "wuauserv");
    }

    [Fact]
    public void Security_controls_are_in_shared_build_settings_not_custom_only()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Deployment", "MainWindow.xaml"));
        var settings = xaml.IndexOf("x:Name=\"SettingsPanel\"", StringComparison.Ordinal);
        var review = xaml.IndexOf("x:Name=\"ReviewPanel\"", StringComparison.Ordinal);
        var defender = xaml.IndexOf("x:Name=\"DefenderChoice\"", StringComparison.Ordinal);
        var update = xaml.IndexOf("x:Name=\"WindowsUpdateChoice\"", StringComparison.Ordinal);
        Assert.True(settings >= 0 && settings < defender && defender < review);
        Assert.True(settings < update && update < review);
    }

    [Fact]
    public void Desktop_choices_roundtrip_and_work_in_serviceable_modes()
    {
        var recipe = new BuildRecipe { Mode = BuildMode.Custom, DefenderChoice = ComponentChoice.Disable, WindowsUpdateChoice = ComponentChoice.Remove };
        recipe.ValidateOptions();
        var json = System.Text.Json.JsonSerializer.Serialize(recipe, JsonDefaults.Options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<BuildRecipe>(json, JsonDefaults.Options)!;
        Assert.Equal(ComponentChoice.Disable, restored.DefenderChoice);
        Assert.Equal(ComponentChoice.Remove, restored.WindowsUpdateChoice);
        (recipe with { Mode = BuildMode.Tiny11, SelectedOptions = CleanupCatalog.ForMode(BuildMode.Tiny11) }).ValidateOptions();
        Assert.Throws<InvalidDataException>(() => (recipe with { Flavor = BuildFlavor.Core, CoreAcknowledged = true }).ValidateOptions());
    }

    [Theory]
    [InlineData(ComponentChoice.Keep, ComponentChoice.Remove)]
    [InlineData(ComponentChoice.Disable, ComponentChoice.Keep)]
    [InlineData(ComponentChoice.Remove, ComponentChoice.Disable)]
    public void Balanced_compiles_independent_choices_without_changing_the_preset(ComponentChoice defender, ComponentChoice update)
    {
        var recipe = new BuildRecipe { Mode = BuildMode.Tiny11, SelectedOptions = CleanupCatalog.ForMode(BuildMode.Tiny11),
            DefenderChoice = defender, WindowsUpdateChoice = update };
        var compiled = SystemComponentSelection.CompileChoices(recipe, Inventory);
        Assert.Equal(BuildMode.Custom, compiled.Mode);
        Assert.Equal(ComponentChoice.Keep, compiled.DefenderChoice);
        Assert.Equal(ComponentChoice.Keep, compiled.WindowsUpdateChoice);
        Assert.All(recipe.SelectedOptions, id => Assert.Contains(id, compiled.SelectedOptions));
        Assert.Equal(defender != ComponentChoice.Keep, compiled.ExpertActions.Any(a => a.Identity == "WinDefend"));
        Assert.Equal(update != ComponentChoice.Keep, compiled.ExpertActions.Any(a => a.Identity == "wuauserv"));
    }

    [Fact]
    public void Both_legacy_and_rebranded_job_names_are_validated_without_accepting_arbitrary_names()
    {
        var id = Guid.NewGuid().ToString("N");
        Assert.True(BuildJob.TryGetId("katharsis-" + id, out var currentId));
        Assert.Equal(id, currentId);
        Assert.True(BuildJob.TryGetId("shuddhi-" + id, out var newId));
        Assert.Equal(id, newId);
        Assert.True(BuildJob.TryGetId("tiny11-" + id, out var oldId));
        Assert.Equal(id, oldId);
        Assert.False(BuildJob.TryGetId("shuddhi-not-a-guid", out _));
        Assert.False(BuildJob.TryGetId("other-" + id, out _));
    }

    [Fact]
    public void Newly_created_jobs_use_katharsis_prefix_and_preserve_the_job_id()
    {
        using var fixture = new BuildFixture();
        using var job = BuildJob.Create(fixture.Root, "inventory");
        Assert.StartsWith("katharsis-", Path.GetFileName(job.Directory));
        Assert.True(BuildJob.TryGetId(Path.GetFileName(job.Directory), out var id));
        var state = System.Text.Json.JsonSerializer.Deserialize<JobState>(File.ReadAllText(Path.Combine(job.Directory, "job.json")), JsonDefaults.Options)!;
        Assert.Equal(id, state.Id);
    }

    private static ImageInventoryItem Item(ImageItemKind kind, string name) => new(kind, name, "Installed", true);
    private static readonly ImageInventoryItem[] Inventory =
    [
        Item(ImageItemKind.Service, "WinDefend"), Item(ImageItemKind.Service, "WdFilter"),
        Item(ImageItemKind.Service, "wuauserv"), Item(ImageItemKind.Service, "UsoSvc"),
        Item(ImageItemKind.Service, "BITS"), Item(ImageItemKind.Service, "TrustedInstaller"),
        Item(ImageItemKind.Service, "Sense"), Item(ImageItemKind.Service, "mpssvc"),
        Item(ImageItemKind.FilePayload, @"Program Files\Windows Defender"),
        Item(ImageItemKind.FilePayload, @"Windows\System32\wuaueng.dll"),
        Item(ImageItemKind.ComponentPayload, "amd64_microsoft-windows-defender-service_31bf3856ad364e35_10.0.26100.1_none_123"),
        Item(ImageItemKind.ComponentPayload, "arm64_microsoft-windows-windowsupdate-client_31bf3856ad364e35_10.0.26100.1_none_123"),
        Item(ImageItemKind.ComponentPayload, "amd64_microsoft-windows-servicingstack_31bf3856ad364e35_10.0.26100.1_none_123"),
        Item(ImageItemKind.ProvisionedApp, "Microsoft.WindowsCalculator_1.0_x64_123"),
        Item(ImageItemKind.Feature, "NetFx3")
    ];

    [Theory]
    [InlineData(ComponentChoice.Keep, ComponentChoice.Keep)]
    [InlineData(ComponentChoice.Keep, ComponentChoice.Disable)]
    [InlineData(ComponentChoice.Keep, ComponentChoice.Remove)]
    [InlineData(ComponentChoice.Disable, ComponentChoice.Keep)]
    [InlineData(ComponentChoice.Disable, ComponentChoice.Disable)]
    [InlineData(ComponentChoice.Disable, ComponentChoice.Remove)]
    [InlineData(ComponentChoice.Remove, ComponentChoice.Keep)]
    [InlineData(ComponentChoice.Remove, ComponentChoice.Disable)]
    [InlineData(ComponentChoice.Remove, ComponentChoice.Remove)]
    public void Independent_modes_never_prune_core_or_change_unselected_services(ComponentChoice defender, ComponentChoice update)
    {
        var recipe = new SelectableProfile { Defender = defender, WindowsUpdate = update, AcceptExperimentalRemoval = true }.Compile(Inventory);
        Assert.Equal(BuildFlavor.Regular, recipe.Flavor);
        Assert.Equal(BuildMode.Custom, recipe.Mode);
        Assert.DoesNotContain(recipe.ExpertActions, action => action.Identity is "BITS" or "TrustedInstaller" or "Sense" or "mpssvc" || action.Identity.Contains("servicingstack"));
        Assert.Equal(defender != ComponentChoice.Keep, recipe.ExpertActions.Any(action => action.Identity == "WinDefend"));
        Assert.Equal(update != ComponentChoice.Keep, recipe.ExpertActions.Any(action => action.Identity == "wuauserv"));
        Assert.Equal(defender == ComponentChoice.Remove, recipe.ExpertActions.Any(action => action.Identity == @"Program Files\Windows Defender"));
        Assert.Equal(update == ComponentChoice.Remove, recipe.ExpertActions.Any(action => action.Identity == @"Windows\System32\wuaueng.dll"));
        if (defender == ComponentChoice.Disable) Assert.All(recipe.ExpertActions.Where(action => action.Identity is "WinDefend" or "WdFilter"), action => Assert.Equal(ExpertActionMode.Disable, action.Mode));
    }

    [Fact]
    public void Current_and_abbreviated_component_families_match_without_generic_update_matching()
    {
        var current = Inventory.Concat(new[] {
            Item(ImageItemKind.ComponentPayload, "amd64_windows-defender-service_31bf_10.0.26100.1_none_abc"),
            Item(ImageItemKind.ComponentPayload, "amd64_microsoft-windows-w..wsupdateclient-core_31bf_10.0.26100.1_none_abc"),
            Item(ImageItemKind.ComponentPayload, "amd64_microsoft-windows-u..te-orchestratorcore_31bf_10.0.26100.1_none_abc"),
            Item(ImageItemKind.ComponentPayload, "amd64_microsoft-windows-update-uus-core_31bf_10.0.26100.1_none_abc"),
            Item(ImageItemKind.ComponentPayload, "amd64_microsoft-windows-deviceupdateagent_31bf_10.0.26100.1_none_abc")
        }).ToArray();
        var recipe = new SelectableProfile { Defender = ComponentChoice.Remove, WindowsUpdate = ComponentChoice.Remove,
            AcceptExperimentalRemoval = true }.Compile(current);
        Assert.Contains(recipe.ExpertActions, action => action.Identity.Contains("amd64_windows-defender-service"));
        Assert.Contains(recipe.ExpertActions, action => action.Identity.Contains("w..wsupdateclient-core"));
        Assert.Contains(recipe.ExpertActions, action => action.Identity.Contains("u..te-orchestratorcore"));
        Assert.Contains(recipe.ExpertActions, action => action.Identity.Contains("update-uus-core"));
        Assert.DoesNotContain(recipe.ExpertActions, action => action.Identity.Contains("deviceupdateagent"));
    }

    [Fact]
    public void Remove_fails_closed_without_consent_or_recognized_payloads()
    {
        Assert.Throws<InvalidDataException>(() => new SelectableProfile { Defender = ComponentChoice.Remove }.Compile(Inventory));
        var profile = new SelectableProfile { Defender = ComponentChoice.Remove, AcceptExperimentalRemoval = true };
        Assert.Throws<InvalidDataException>(() => profile.Compile(Inventory.Where(item => item.Kind != ImageItemKind.ComponentPayload).ToArray()));
        Assert.Throws<InvalidDataException>(() => profile.Compile(Inventory.Where(item => item.Kind != ImageItemKind.FilePayload).ToArray()));
        Assert.Throws<InvalidDataException>(() => profile.Compile(Inventory.Where(item => item.Identity != "WinDefend").ToArray()));
    }

    [Fact]
    public void Manual_actions_cannot_override_keep_or_enable_and_remove_netfx()
    {
        var profile = new SelectableProfile { Recipe = new() { Mode = BuildMode.Custom, ExpertAcknowledged = true,
            ExpertActions = [new(ImageItemKind.Service, "WinDefend", ExpertActionMode.Remove)] } };
        Assert.Throws<InvalidDataException>(() => profile.Compile(Inventory));
        Assert.Throws<InvalidDataException>(() => (profile with { Recipe = profile.Recipe with {
            EnableNetFx3 = true, ExpertActions = [new(ImageItemKind.Feature, "NetFx3", ExpertActionMode.Disable)] } }).Compile(Inventory));
    }

    [Fact]
    public void Exact_app_identity_roundtrips_and_stale_identity_is_refused()
    {
        var profile = new SelectableProfile { Recipe = new() { Mode = BuildMode.Custom, ExpertAcknowledged = true,
            ExpertActions = [new(ImageItemKind.ProvisionedApp, Inventory[^2].Identity, ExpertActionMode.Remove)] } };
        var json = System.Text.Json.JsonSerializer.Serialize(profile, JsonDefaults.Options);
        var restored = System.Text.Json.JsonSerializer.Deserialize<SelectableProfile>(json, JsonDefaults.Options)!;
        Assert.Single(restored.Compile(Inventory).ExpertActions);
        Assert.Throws<InvalidDataException>(() => restored.Compile([]));
    }

    [Fact]
    public void New_policies_are_opt_in_and_not_added_to_upstream_presets()
    {
        Assert.DoesNotContain("selector-disable-defender", CleanupCatalog.ForMode(BuildMode.Tiny11));
        Assert.DoesNotContain("selector-disable-update", CleanupCatalog.ForMode(BuildMode.Tiny11Core));
        Assert.DoesNotContain(SelectableProfile.AllowedOptions, option => option.Id.StartsWith("selector-"));
        Assert.All(PolicyCatalog.ForOption("selector-disable-update"), value => Assert.Equal(PolicyHive.Software, value.Hive));
        Assert.Empty(new SelectableProfile().Compile(Inventory).SelectedOptions);
    }
}
