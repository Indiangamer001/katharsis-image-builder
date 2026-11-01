using System.Text.Json;
using Tiny11Builder.Core;

// JSON results go to stdout; progress goes to stderr so PowerShell can parse results.
try
{
    if (args.Length == 0) throw new ArgumentException("Commands: catalog, tools, inspect <source>, inventory <profile>, plan <profile> <inventory>, build <profile>.");
    var engine = new BuildEngine(new ProcessRunner(), new MediaSourceFactory(), new OfflineRegistry(), new ImageOperations(), new BuildHost());
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); Console.Error.WriteLine("Cancelling at the next safe image-operation boundary..."); };
    var progress = new ImmediateProgress(value => Console.Error.WriteLine($"[{value.Stage}] {value.Message}"));
    void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonDefaults.Options));
    SelectableProfile ReadProfile(bool allowUnacknowledged = false)
    {
        if (args.Length < 2) throw new ArgumentException("A profile path is required.");
        var strictJson = new JsonSerializerOptions(JsonDefaults.Options)
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        var profile = JsonSerializer.Deserialize<SelectableProfile>(File.ReadAllText(args[1]), strictJson)
            ?? throw new InvalidDataException("Empty profile.");
        (allowUnacknowledged ? profile with { AcceptExperimentalRemoval = true } : profile).Validate();
        return profile;
    }
    BuildRecipe ResolveTools(BuildRecipe recipe) => recipe with
    {
        DismPath = string.IsNullOrWhiteSpace(recipe.DismPath) ? ToolLocator.FindDism(Console.Error.WriteLine) : recipe.DismPath,
        OscdimgPath = string.IsNullOrWhiteSpace(recipe.OscdimgPath) ? ToolLocator.FindOscdimg() : recipe.OscdimgPath
    };
    switch (args[0])
    {
        case "catalog": Print(SelectableProfile.AllowedOptions); break;
        case "validate": Print(ReadProfile()); break;
        case "read": Print(ReadProfile(allowUnacknowledged: true)); break;
        case "tools": Print(new { DismPath = ToolLocator.FindDism(), OscdimgPath = ToolLocator.FindOscdimg(), Administrator = ToolLocator.IsAdministrator }); break;
        case "inspect":
            if (args.Length != 2) throw new ArgumentException("inspect requires a source ISO or media folder.");
            Print(await engine.InspectAsync(args[1], ToolLocator.FindDism(), cancellation.Token)); break;
        case "inventory":
        {
            var profile = ReadProfile();
            var recipe = ResolveTools(profile.Recipe) with { ExpertActions = [], SelectedOptions = [] };
            Print(await engine.InventoryAsync(recipe, progress, cancellation.Token)); break;
        }
        case "plan":
        {
            if (args.Length != 3) throw new ArgumentException("plan requires a profile and inventory JSON.");
            var profile = ReadProfile();
            var inventory = JsonSerializer.Deserialize<ImageInventoryItem[]>(File.ReadAllText(args[2]), JsonDefaults.Options)
                ?? throw new InvalidDataException("Empty inventory.");
            Print(profile.Compile(inventory)); break;
        }
        case "build":
        {
            var profile = ReadProfile();
            profile = profile with { Recipe = ResolveTools(profile.Recipe) };
            if (string.IsNullOrWhiteSpace(profile.Recipe.OscdimgPath))
                throw new FileNotFoundException("Install Windows ADK Deployment Tools (oscdimg.exe), or set Recipe.OscdimgPath in the profile.");
            // Always refresh the inventory. A preview file is never trusted to authorize a different image.
            var inventory = await engine.InventoryAsync(profile.Recipe with { ExpertActions = [], SelectedOptions = [] }, progress, cancellation.Token);
            var recipe = profile.Compile(inventory);
            Console.Error.WriteLine($"Defender: {profile.Defender}; Windows Update: {profile.WindowsUpdate}. Exact actions: {recipe.ExpertActions.Length}.");
            if (profile.Defender != ComponentChoice.Keep || profile.WindowsUpdate != ComponentChoice.Keep)
                Console.Error.WriteLine("Offline changes are not proof of runtime state. Validate the installed image in a VM; Windows repair/upgrade may restore components.");
            Print(await engine.BuildAsync(recipe, progress, cancellation.Token)); break;
        }
        default: throw new ArgumentException("Unknown command: " + args[0]);
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}

sealed class ImmediateProgress(Action<BuildProgress> report) : IProgress<BuildProgress>
{
    public void Report(BuildProgress value) => report(value);
}
