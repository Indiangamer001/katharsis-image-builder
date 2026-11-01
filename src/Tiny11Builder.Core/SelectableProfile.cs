using System.Text.RegularExpressions;

namespace Tiny11Builder.Core;

public enum ComponentChoice { Keep, Disable, Remove }

// The PowerShell profile stays portable: service and payload identities are resolved
// from the selected image, never copied from a different Windows release.
public sealed record SelectableProfile
{
    public int Version { get; init; } = 1;
    public BuildRecipe Recipe { get; init; } = new() { Mode = BuildMode.Custom };
    public ComponentChoice Defender { get; init; }
    public ComponentChoice WindowsUpdate { get; init; }
    public bool AcceptExperimentalRemoval { get; init; }

    public void Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Defender) || !Enum.IsDefined(WindowsUpdate) || Recipe is null)
            throw new InvalidDataException("Unsupported selector profile or component choice.");
        Recipe.ValidateOptions();
        if (Recipe.Flavor != BuildFlavor.Regular || Recipe.Mode != BuildMode.Custom)
            throw new InvalidDataException("Selector profiles require Custom mode without Core pruning.");
        if ((Defender == ComponentChoice.Remove || WindowsUpdate == ComponentChoice.Remove) && !AcceptExperimentalRemoval)
            throw new InvalidDataException("Physical removal requires AcceptExperimentalRemoval. Servicing and boot may break.");
        // Keep must not be overridden by a hidden broad group or hand-written raw action.
        if (Recipe.SelectedOptions.Any(id => !AllowedOptions.Any(option => option.Id == id)))
            throw new InvalidDataException("This group is not supported by the selector. Use individual apps/features instead.");
        if (Recipe.ExpertActions.Any(action => action.Kind is not
                (ImageItemKind.ProvisionedApp or ImageItemKind.Feature or ImageItemKind.Capability) ||
                SystemComponentSelection.IsManaged(action)))
            throw new InvalidDataException("Use the dedicated Defender / Windows Update choices for managed components. Raw service/file/package actions are not accepted.");
        if (Recipe.EnableNetFx3 && Recipe.ExpertActions.Any(action =>
                action.Kind == ImageItemKind.Feature && action.Identity.Equals("NetFx3", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Cannot enable .NET 3.5 and also disable/remove NetFx3.");
    }

    public static IReadOnlyList<CleanupOption> AllowedOptions => CleanupCatalog.Options.Where(option =>
        (option.Kind is OptionKind.App or OptionKind.Feature or OptionKind.Capability or OptionKind.Policy ||
         option.Id is "edge-files" or "onedrive-files") &&
        !option.CustomOnly && option.Id != "cleanup").ToArray();

    public BuildRecipe Compile(IReadOnlyList<ImageInventoryItem> inventory)
    {
        Validate();
        ImageInventory.ValidateActions(Recipe.ExpertActions, inventory);
        var actions = Recipe.ExpertActions.ToList();
        actions.AddRange(SystemComponentSelection.Resolve("Defender", Defender, inventory));
        actions.AddRange(SystemComponentSelection.Resolve("WindowsUpdate", WindowsUpdate, inventory));
        var options = Recipe.SelectedOptions.ToList();
        if (Defender != ComponentChoice.Keep) options.Add("selector-disable-defender");
        if (WindowsUpdate != ComponentChoice.Keep) options.Add("selector-disable-update");
        var compiled = Recipe with
        {
            ExpertActions = actions.Distinct().ToArray(),
            SelectedOptions = options.ToArray(),
            ExpertAcknowledged = actions.Count > 0 || Recipe.ExpertAcknowledged
        };
        compiled.ValidateOptions();
        return compiled;
    }
}

public static class SystemComponentSelection
{
    public static BuildRecipe CompileChoices(BuildRecipe recipe, IReadOnlyList<ImageInventoryItem> inventory)
    {
        recipe.ValidateOptions();
        if (!recipe.ShuddhiCorePreset && recipe.DefenderChoice == ComponentChoice.Keep && recipe.WindowsUpdateChoice == ComponentChoice.Keep) return recipe;
        if (recipe.ExpertActions.Any(IsManaged))
            throw new InvalidDataException("Use the dedicated Defender and Windows Update controls instead of selecting their individual entries as well.");
        var actions = recipe.ExpertActions.Concat(Resolve("Defender", recipe.DefenderChoice, inventory))
            .Concat(Resolve("WindowsUpdate", recipe.WindowsUpdateChoice, inventory))
            .Concat(recipe.ShuddhiCorePreset ? ResolveCoreServices(inventory) : []).ToArray();
        var groups = recipe.SelectedOptions.ToList();
        if (recipe.DefenderChoice != ComponentChoice.Keep) groups.Add("selector-disable-defender");
        if (recipe.WindowsUpdateChoice != ComponentChoice.Keep) groups.Add("selector-disable-update");
        var compiled = recipe with { Mode = BuildMode.Custom, ExpertActions = actions, SelectedOptions = groups.ToArray(),
            ExpertAcknowledged = true, DefenderChoice = ComponentChoice.Keep, WindowsUpdateChoice = ComponentChoice.Keep, ShuddhiCorePreset = false };
        compiled.ValidateOptions();
        return compiled;
    }
    public static readonly string[] CoreServiceNames =
    ["BthAvctpSvc", "Ccx01000", "diagsvc", "IsoSessionCore", "MapsBroker", "PcaSvc", "RemoteRegistry", "Spooler", "ssh-agent", "WMPNetworkSvc", "dmwappushservice"];
    public static IReadOnlyList<ExpertAction> ResolveCoreServices(IReadOnlyList<ImageInventoryItem> inventory) =>
        inventory.Where(item => item.Kind == ImageItemKind.Service && CoreServiceNames.Contains(item.Identity, StringComparer.OrdinalIgnoreCase))
            .Select(item => new ExpertAction(item.Kind, item.Identity, ExpertActionMode.Disable)).ToArray();
    private static readonly string[] DefenderServices = ["WinDefend", "WdNisSvc", "WdNisDrv", "WdFilter", "WdBoot"];
    private static readonly string[] UpdateServices = ["wuauserv", "UsoSvc", "WaaSMedicSvc"];
    public static readonly string[] PayloadFolders =
    [
        @"Program Files\Windows Defender", @"Program Files (x86)\Windows Defender",
        @"ProgramData\Microsoft\Windows Defender", @"Windows\System32\Tasks\Microsoft\Windows\Windows Defender",
        @"Windows\System32\Tasks\Microsoft\Windows\WindowsUpdate",
        @"Windows\System32\Tasks\Microsoft\Windows\UpdateOrchestrator",
        @"Windows\System32\Tasks\Microsoft\Windows\WaaSMedic", @"Windows\UUS"
    ];
    private static readonly HashSet<string> DefenderFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        @"Windows\System32\drivers\WdBoot.sys", @"Windows\System32\drivers\WdFilter.sys",
        @"Windows\System32\drivers\WdNisDrv.sys"
    };
    private static readonly HashSet<string> UpdateFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        @"Windows\System32\wuaueng.dll", @"Windows\System32\wuauclt.exe",
        @"Windows\System32\UsoClient.exe", @"Windows\System32\usocoreworker.exe",
        @"Windows\System32\usosvc.dll", @"Windows\System32\WaaSMedicSvc.dll",
        @"Windows\System32\WaaSMedicAgent.exe"
    };

    public static bool IsManaged(ExpertAction action) =>
        Regex.IsMatch(action.Identity, @"defender|sechealth|securityhealth|windowsupdate|updateorchestrator|waasmedic", RegexOptions.IgnoreCase) ||
        new[] { "Defender", "WindowsUpdate" }.Any(component => Matches(component, action.Kind, action.Identity));

    private static bool Matches(string component, ImageItemKind kind, string identity)
    {
        var defender = component == "Defender";
        if (kind == ImageItemKind.Service)
            return (defender ? DefenderServices : UpdateServices).Contains(identity, StringComparer.OrdinalIgnoreCase);
        if (kind == ImageItemKind.FilePayload)
            return (defender ? DefenderFiles : UpdateFiles).Contains(identity) || PayloadFolders.Where(path =>
                defender ? path.Contains("Defender", StringComparison.Ordinal) :
                !path.Contains("Defender", StringComparison.Ordinal)).Contains(identity, StringComparer.OrdinalIgnoreCase);
        // Match whole component families, not arbitrary occurrences of "update".
        // Servicing stack, BITS, MSI, recovery, firewall and Windows Security UI stay intact.
        if (kind == ImageItemKind.ComponentPayload)
            return Regex.IsMatch(identity, defender ?
                @"^(amd64|arm64|x86|wow64)_(?:microsoft-)?windows-(?:defender|windefend)(?:[-_.])" :
                @"^(amd64|arm64|x86|wow64)_microsoft-windows-(?:windowsupdate|updateorchestrator|waasmedic|update-(?:orchestratorapi|uus-core|uus-desktop|uus-stable|usocoreps)|w\.\.wsupdateclient-core|w\.\.owsupdateclient-aux|u\.\.te-orchestratorcore|u\.\.-orchestratordocked)(?:[-_.])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return false;
    }

    public static IReadOnlyList<ExpertAction> Resolve(string component, ComponentChoice choice,
        IReadOnlyList<ImageInventoryItem> inventory)
    {
        if (component is not ("Defender" or "WindowsUpdate") || !Enum.IsDefined(choice))
            throw new ArgumentException("Unknown component or choice.");
        if (choice == ComponentChoice.Keep) return [];
        var services = inventory.Where(item => item.Kind == ImageItemKind.Service && Matches(component, item.Kind, item.Identity)).ToArray();
        var required = component == "Defender" ? "WinDefend" : "wuauserv";
        if (!services.Any(item => item.Identity.Equals(required, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Cannot identify {required} in this image. Use Keep for an already-modified image; no disable/removal is claimed.");
        var mode = choice == ComponentChoice.Disable ? ExpertActionMode.Disable : ExpertActionMode.Remove;
        var actions = services.Select(item => new ExpertAction(item.Kind, item.Identity, mode)).ToList();
        if (choice == ComponentChoice.Remove)
        {
            var payloads = inventory.Where(item => item.Kind != ImageItemKind.Service && Matches(component, item.Kind, item.Identity)).ToArray();
            // An unknown build is refused, never downgraded to registry-only disablement.
            if (!payloads.Any(item => item.Kind == ImageItemKind.FilePayload) ||
                !payloads.Any(item => item.Kind == ImageItemKind.ComponentPayload))
                throw new InvalidDataException($"Cannot identify both deployed files and component-store payloads for {component}. Physical removal is unsupported for this image.");
            actions.AddRange(payloads.Select(item => new ExpertAction(item.Kind, item.Identity, ExpertActionMode.Remove)));
        }
        return actions;
    }
}
