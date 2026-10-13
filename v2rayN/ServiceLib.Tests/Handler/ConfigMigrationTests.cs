namespace ServiceLib.Tests.Handler;

// StartupPath mutates the process-wide V2RAYN_LOCAL_APPLICATION_DATA_V2 env var;
// [NotInParallel] keeps every test in this class (and its env restore in finally)
// isolated from parallel tests that also resolve data paths.
[NotInParallel]
public class ConfigMigrationTests
{
    [Test]
    public async Task StartupPath_WithLocalAppDataEnv_EndsWithV2rayMini()
    {
        var previous = Environment.GetEnvironmentVariable(Global.LocalAppData);
        try
        {
            Environment.SetEnvironmentVariable(Global.LocalAppData, "1");
            await Utils.StartupPath().Should().EndWith("v2rayMini");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Global.LocalAppData, previous);
        }
    }

    [Test]
    public async Task Migrate_WhenTargetExists_DoesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cmt-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        var candidate = Path.Combine(root, "candidate");
        try
        {
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "existing.txt"), "keep");
            Directory.CreateDirectory(candidate);
            await File.WriteAllTextAsync(Path.Combine(candidate, "guiNDB.db"), "legacy");

            var result = ConfigMigrationHandler.Migrate(target, [candidate]);

            await result.Should().BeFalse();
            var kept = await File.ReadAllTextAsync(Path.Combine(target, "existing.txt"));
            await kept.Should().BeEqualTo("keep");
            await File.Exists(Path.Combine(target, "guiNDB.db")).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task Migrate_WhenTargetMissing_CopiesFirstNonEmptyCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cmt-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        var empty = Path.Combine(root, "empty");
        var legacy = Path.Combine(root, "legacy");
        try
        {
            Directory.CreateDirectory(empty);
            Directory.CreateDirectory(legacy);
            await File.WriteAllTextAsync(Path.Combine(legacy, "guiNDB.db"), "legacy-db");

            var result = ConfigMigrationHandler.Migrate(target, [empty, legacy]);

            await result.Should().BeTrue();
            await File.Exists(Path.Combine(target, "guiNDB.db")).Should().BeTrue();
            var copied = await File.ReadAllTextAsync(Path.Combine(target, "guiNDB.db"));
            await copied.Should().BeEqualTo("legacy-db");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Test]
    public async Task Migrate_WhenNoCandidatesExist_ReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cmt-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target");
        try
        {
            var result = ConfigMigrationHandler.Migrate(target, Array.Empty<string>());

            await result.Should().BeFalse();
            await Directory.Exists(target).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
