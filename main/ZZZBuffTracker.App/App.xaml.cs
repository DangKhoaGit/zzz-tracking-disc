using System.IO;
using System.Windows;
using ZZZBuffTracker.Application;
using ZZZBuffTracker.Detection;
using ZZZBuffTracker.Infrastructure;

namespace ZZZBuffTracker.App;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var evaluationArgument = Array.IndexOf(e.Args, "--evaluate-replay");
        if (evaluationArgument >= 0 && evaluationArgument + 2 < e.Args.Length)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[evaluationArgument + 2]);
            try
            {
                var character = evaluationArgument + 3 < e.Args.Length ? e.Args[evaluationArgument + 3] : "demo-character";
                var result = await ReplayEvaluator.EvaluateAsync(Path.GetFullPath(e.Args[evaluationArgument + 1]), character);
                await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(result, TemplateFiles.JsonOptions));
                Shutdown(result.FalsePositives + result.FalseNegatives == 0 && result.LabelledFrames > 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(new { Error = ex.ToString() }, TemplateFiles.JsonOptions));
                Shutdown(1);
            }
            return;
        }
        var smokeArgument = Array.IndexOf(e.Args, "--capture-smoke");
        if (smokeArgument >= 0 && smokeArgument + 1 < e.Args.Length)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await CaptureSmoke.RunAsync(Path.GetFullPath(e.Args[smokeArgument + 1])));
            return;
        }
        // Composition root: explicit constructor injection, no service locator.
        var clock = new MonotonicClock();
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZZZBuffTracker");
        var dataArgument = Array.IndexOf(e.Args, "--data-dir");
        if (dataArgument >= 0 && dataArgument + 1 < e.Args.Length) dataDirectory = Path.GetFullPath(e.Args[dataArgument + 1]);
        var logPath = Path.Combine(dataDirectory, "logs", "tracking.jsonl");
        var log = new FileTrackingLog(logPath);
        var source = new CaptureEventSource(clock, log);
        var profilePath = Path.Combine(dataDirectory, "profiles.json");
        var repository = new JsonProfileRepository(profilePath, log);
        var profiles = new ProfileService(repository);
        var settingsService = new OverlaySettingsService(new ProfileOverlaySettingsRepository(profiles));
        var settings = new Domain.OverlaySettings();
        string? settingsError = null;
        try
        {
            await profiles.ReloadAsync();
            var legacyOverlay = Path.Combine(dataDirectory, "overlay.json");
            if (!File.Exists(profilePath) && File.Exists(legacyOverlay))
            {
                await profiles.SaveOverlayAsync(await new JsonOverlaySettingsRepository(legacyOverlay).LoadAsync());
                log.Write("Information", "Migrated legacy overlay.json into profiles.json; original preserved.", Guid.Empty);
            }
            settings = await settingsService.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        { settingsError = "Không nạp được profile; hãy kiểm tra file/backup rồi Nạp lại trong Presets. " + ex.Message; }
        var overlay = new OverlayViewModel(settings, settingsService);
        if (settingsError is not null) overlay.SetMessage(settingsError);
        var service = new TrackingService(source, new BuffStateStore(), clock, log, repository);
        var profileViewModel = new ProfileViewModel(profiles, service, overlay);
        await profileViewModel.InitializeAsync();
        MainWindow = new MainWindow(new DashboardViewModel(service, source, logPath, overlay, profileViewModel), source, clock);
        MainWindow.Show();
    }
}
