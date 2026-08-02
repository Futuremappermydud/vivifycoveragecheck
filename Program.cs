try
{
    await VivifyCoverageApp.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to complete Vivify map check: {ex.Message}");
    Environment.ExitCode = 1;
}
