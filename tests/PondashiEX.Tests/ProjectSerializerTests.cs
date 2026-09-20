using System.Text;
using PondashiEX.Project;

namespace PondashiEX.Tests;

public sealed class ProjectSerializerTests
{
    [Fact]
    public async Task SaveLoad_RoundTripsVersionIdsAndReferences()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("show.pondex");
        var soundId = Guid.NewGuid();
        var model = new ProjectModel
        {
            Project = new ProjectMetadata { Name = "文化祭" },
            Settings = new ProjectSettings { SoundListColumns = 3 },
            Sounds =
            [
                new SoundDefinition
                {
                    Id = soundId,
                    Name = "開演ベル",
                    File = "sounds/bell.ogg",
                    Key = "A",
                    Loop = new LoopSettings { Enabled = true, StartSeconds = 1.25 }
                }
            ],
            GroupShortcuts =
            [
                new GroupShortcutDefinition { Group = "SE", Key = "G" }
            ],
            Triggers =
            [
                new TriggerDefinition
                {
                    TargetGroup = "SE",
                    TriggerType = TriggerType.StopwatchElapsed,
                    Parameters = new TriggerParameters { Elapsed = TimeSpan.FromSeconds(3) },
                    Action = TriggerAction.Play
                }
            ]
        };
        var serializer = new ProjectSerializer();

        await serializer.SaveAsync(path, model);
        var loaded = await serializer.LoadAsync(path);

        Assert.Equal(ProjectModel.CurrentFormatVersion, loaded.FormatVersion);
        Assert.Equal("文化祭", loaded.Project.Name);
        Assert.Equal(soundId, Assert.Single(loaded.Sounds).Id);
        Assert.Equal(3, loaded.Settings.SoundListColumns);
        Assert.Equal(1.25, Assert.Single(loaded.Sounds).Loop.StartSeconds);
        var groupShortcut = Assert.Single(loaded.GroupShortcuts);
        Assert.Equal("SE", groupShortcut.Group);
        Assert.Equal("G", groupShortcut.Key);
        Assert.Equal("SE", Assert.Single(loaded.Triggers).TargetGroup);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public async Task Load_IgnoresUnknownJsonFields()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("future.pondex");
        var json = """
            {
              "formatVersion": 1,
              "project": { "name": "将来版", "futureMetadata": 123 },
              "settings": {},
              "sounds": [],
              "triggers": [],
              "futureRoot": { "anything": true }
            }
            """;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var loaded = await new ProjectSerializer().LoadAsync(path);

        Assert.Equal("将来版", loaded.Project.Name);
    }

    [Fact]
    public async Task SaveExisting_KeepsOneGenerationBackupAndNoTempFile()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("safe.pondex");
        var serializer = new ProjectSerializer();
        var model = new ProjectModel { Project = new ProjectMetadata { Name = "旧版" } };
        await serializer.SaveAsync(path, model);

        model.Project.Name = "新版";
        await serializer.SaveAsync(path, model);

        Assert.Equal("新版", (await serializer.LoadAsync(path)).Project.Name);
        Assert.Equal("旧版", (await serializer.LoadAsync(path + ".bak")).Project.Name);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Load_RejectsUnsupportedFormatVersion()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("unsupported.pondex");
        await File.WriteAllTextAsync(
            path,
            """{"formatVersion":999,"project":{},"settings":{},"sounds":[],"triggers":[]}""",
            new UTF8Encoding(false));

        var exception = await Assert.ThrowsAsync<ProjectFormatException>(
            () => new ProjectSerializer().LoadAsync(path));

        Assert.Contains("formatVersion", exception.Message);
    }

    [Fact]
    public async Task Load_RepairsSemanticErrorsWithoutDiscardingHealthySounds()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("partly-invalid.pondex");
        var json = """
            {
              "formatVersion": 1,
              "project": {},
              "settings": { "masterVolume": 4.0 },
              "sounds": [
                null,
                {
                  "id": "00000000-0000-0000-0000-000000000000",
                  "name": "正常部分",
                  "file": "sounds/ok.wav",
                  "volume": 3.0,
                  "pan": -8.0,
                  "pitch": 99.0,
                  "loop": null,
                  "fadeIn": { "enabled": true, "value": -2.0 },
                  "fadeOut": { "enabled": true, "value": 90.0 }
                }
              ],
              "triggers": []
            }
            """;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var loaded = await new ProjectSerializer().LoadAsync(path);
        var sound = Assert.Single(loaded.Sounds);

        Assert.NotEqual(Guid.Empty, sound.Id);
        Assert.Equal("正常部分", sound.Name);
        Assert.Equal(1.0f, sound.Volume);
        Assert.Equal(-1.0f, sound.Pan);
        Assert.Equal(2.0f, sound.Pitch);
        Assert.NotNull(sound.Loop);
        Assert.Equal(0.0f, sound.FadeIn.Value);
        Assert.Equal(60.0f, sound.FadeOut.Value);
        Assert.Equal(1.0f, loaded.Settings.MasterVolume);
    }

    [Fact]
    public async Task Load_SkipsOnlyTypeBrokenSoundAndReportsWarning()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("one-broken-sound.pondex");
        var healthyId = Guid.NewGuid();
        var json = $$"""
            {
              "formatVersion": 1,
              "project": { "name": "部分復旧" },
              "settings": {},
              "sounds": [
                { "id": "{{healthyId}}", "name": "正常", "file": "sounds/ok.wav" },
                { "id": "{{Guid.NewGuid()}}", "name": "破損", "file": "sounds/bad.wav", "volume": "not-a-number" }
              ],
              "triggers": []
            }
            """;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var loaded = await new ProjectSerializer().LoadAsync(path);

        Assert.Equal(healthyId, Assert.Single(loaded.Sounds).Id);
        Assert.Single(loaded.LoadWarnings);
        Assert.Contains("音源[1]", loaded.LoadWarnings[0]);
    }
}
