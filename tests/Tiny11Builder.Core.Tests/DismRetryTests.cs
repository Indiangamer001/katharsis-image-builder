using Tiny11Builder.Core;
using Xunit;

namespace Tiny11Builder.Core.Tests;

public sealed class DismRetryTests
{
    [Theory]
    [InlineData(87, "Error: 87\n\nAn initialization error occurred.", true)]
    [InlineData(87, "Error: 87\n\nThe get-windowsedition option is unknown.", false)]
    [InlineData(87, "Error: 87\n\nHRESULT=0x80070057", true)]
    [InlineData(50, "An initialization error occurred.", false)]
    public void Only_initialization_failures_are_transient(int exitCode, string output, bool expected) =>
        Assert.Equal(expected, DismClient.IsTransientInitialization(new DismCommandException(exitCode, output)));

    [Fact]
    public async Task Read_only_image_command_retries_after_an_initialization_failure()
    {
        var runner = new SequenceRunner(
            new ProcessResult(87, "An initialization error occurred."),
            new ProcessResult(0, "Image Version: 10.0.26300.9711"));
        var dism = new DismClient("dism.exe", runner);

        var output = await dism.RunAsync(CancellationToken.None, "/Image:C:\\mount", "/Get-Features");

        Assert.Equal(2, runner.Calls);
        Assert.Contains("Image Version", output);
    }

    [Fact]
    public async Task A_mutating_command_is_never_replayed()
    {
        var runner = new SequenceRunner(new ProcessResult(87, "An initialization error occurred."));
        var dism = new DismClient("dism.exe", runner);

        await Assert.ThrowsAsync<DismCommandException>(() =>
            dism.RunAsync(CancellationToken.None, "/Image:C:\\mount", "/Remove-Feature"));

        Assert.Equal(1, runner.Calls);
    }

    private sealed class SequenceRunner : IProcessRunner
    {
        private readonly ProcessResult[] results;
        private int index;

        public SequenceRunner(params ProcessResult[] results) => this.results = results;

        public int Calls { get; private set; }

        public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
            Action<string>? output, CancellationToken cancellationToken, bool finishOnCancellation = true)
        {
            Calls++;
            var result = results[Math.Min(index, results.Length - 1)];
            index++;
            return Task.FromResult(result);
        }
    }
}
