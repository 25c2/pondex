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
            Settings = new ProjectSettings
            {
                SoundListColumns = 3,
                FadeOutOnStop = false,
                FadeOutOnStopAll = true,
                DarkMode = true
            },
            Sounds =
            [
                new SoundDefinition
                {
                    Id = soundId,
                    Name = "開演ベル",
                    File = "sounds/bell.ogg",
                    Key = "A",
                    Speed = 1.25F,
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
        Assert.False(loaded.Settings.FadeOutOnStop);
        Assert.True(loaded.Settings.FadeOutOnStopAll);
        Assert.True(loaded.Settings.DarkMode);
        Assert.Equal(1.25, Assert.Single(loaded.Sounds).Loop.StartSeconds);
        Assert.Equal(1.25F, Assert.Single(loaded.Sounds).Speed);
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
        Assert.True(loaded.Settings.FadeOutOnStop);
        Assert.False(loaded.Settings.FadeOutOnStopAll);
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
    public async Task Save_BackupRotationFailureKeepsCommittedProjectAndBothRecoveryCopies()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("rotation.pondex");
        var serializer = new ProjectSerializer();
        using var manager = new ProjectManager(serializer);
        manager.Current.Project.Name = "one";
        await manager.SaveAsAsync(path);
        manager.Current.Project.Name = "two";
        manager.MarkDirty();
        await manager.SaveAsync();
        manager.Current.Project.Name = "three";
        manager.MarkDirty();
        using (File.Open(path + ".bak", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await manager.SaveAsync();
            Assert.False(manager.IsDirty);
            Assert.Equal("three", (await serializer.LoadAsync(path)).Project.Name);
            Assert.NotNull(manager.LastSaveResult?.BackupWarning);
        }
        Assert.Equal("one", (await serializer.LoadAsync(path + ".bak")).Project.Name);
        Assert.Equal("two", (await serializer.LoadAsync(manager.LastSaveResult!.RecoveryBackupPath!)).Project.Name);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task Save_CancelledBeforeCommitPreservesOriginalAndRemovesOwnedTempFile()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("cancel.pondex");
        var serializer = new ProjectSerializer();
        var model = new ProjectModel();
        model.Project.Name = "original";
        await serializer.SaveAsync(path, model);
        model.Project.Name = "changed";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serializer.SaveAsync(path, model, cancellation.Token));
        Assert.Equal("original", (await serializer.LoadAsync(path)).Project.Name);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task Save_ReplacementFailurePreservesOriginalAndExistingBackup()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("locked.pondex");
        var serializer = new ProjectSerializer();
        var model = new ProjectModel();
        model.Project.Name = "first";
        await serializer.SaveAsync(path, model);
        model.Project.Name = "second";
        await serializer.SaveAsync(path, model);
        model.Project.Name = "third";
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => serializer.SaveAsync(path, model));
        }
        Assert.Equal("second", (await serializer.LoadAsync(path)).Project.Name);
        Assert.Equal("first", (await serializer.LoadAsync(path + ".bak")).Project.Name);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
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

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999")]
    public async Task Load_InvalidVersionTypeProducesProjectFormatException(string value)
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("invalid-version.pondex");
        await File.WriteAllTextAsync(path, "{\"formatVersion\":" + value + "}");
        var error = await Assert.ThrowsAsync<ProjectFormatException>(() => new ProjectSerializer().LoadAsync(path));
        Assert.Contains("整数", error.Message);
    }

    [Fact]
    public async Task Save_DoesNotTouchPreexistingFixedTempFile()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("safe.pondex");
        await File.WriteAllTextAsync(path + ".tmp", "別のデータ");
        await new ProjectSerializer().SaveAsync(path, new ProjectModel());
        Assert.Equal("別のデータ", await File.ReadAllTextAsync(path + ".tmp"));
        Assert.Single(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task Save_HardLinkedFixedTempCannotOverwriteAnotherFile()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("show.pondex");
        var unrelated = directory.GetPath("unrelated.txt");
        await File.WriteAllTextAsync(unrelated, "保存対象外");
        Assert.True(TestLinks.CreateHardLink(path + ".tmp", unrelated, IntPtr.Zero));
        await new ProjectSerializer().SaveAsync(path, new ProjectModel());
        Assert.Equal("保存対象外", await File.ReadAllTextAsync(unrelated));
    }

    [Fact]
    public async Task Save_HardLinkedBackupAndDestinationDoNotModifyOtherNames()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("show.pondex");
        var unrelated = directory.GetPath("unrelated.txt");
        var backupTarget = directory.GetPath("backup-target.txt");
        await File.WriteAllTextAsync(unrelated, "元データ");
        await File.WriteAllTextAsync(backupTarget, "バックアップ対象外");
        Assert.True(TestLinks.CreateHardLink(path, unrelated, IntPtr.Zero));
        Assert.True(TestLinks.CreateHardLink(path + ".bak", backupTarget, IntPtr.Zero));
        await new ProjectSerializer().SaveAsync(path, new ProjectModel());
        Assert.Equal("元データ", await File.ReadAllTextAsync(unrelated));
        Assert.Equal("バックアップ対象外", await File.ReadAllTextAsync(backupTarget));
        Assert.Equal("元データ", await File.ReadAllTextAsync(path + ".bak"));
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

    [Fact]
    public async Task Load_RejectsFileLargerThanLimitBeforeParsing()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("huge.pondex");
        await using (var stream = File.Create(path))
        {
            stream.SetLength(ProjectLimits.MaxFileBytes + 1);
        }

        var exception = await Assert.ThrowsAsync<ProjectFormatException>(
            () => new ProjectSerializer().LoadAsync(path));

        Assert.Contains("大きすぎ", exception.Message);
    }

    [Fact]
    public async Task Load_RejectsTooManySounds()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("too-many.pondex");
        var sounds = string.Join(',', Enumerable.Repeat("{}", ProjectLimits.MaxSounds + 1));
        var json = $"{{\"formatVersion\":1,\"project\":{{}},\"settings\":{{}},\"sounds\":[{sounds}],\"triggers\":[]}}";
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var exception = await Assert.ThrowsAsync<ProjectFormatException>(
            () => new ProjectSerializer().LoadAsync(path));

        Assert.Contains("件数", exception.Message);
    }

    [Fact]
    public async Task Load_RejectsRemoteAudioPath()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("remote.pondex");
        var json = """
            {
              "formatVersion": 1,
              "project": {},
              "settings": {},
              "sounds": [{ "name": "危険", "file": "\\\\attacker\\share\\sound.wav" }],
              "triggers": []
            }
            """;
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var exception = await Assert.ThrowsAsync<ProjectFormatException>(
            () => new ProjectSerializer().LoadAsync(path));

        Assert.Contains("UNC", exception.Message);
    }

    [Fact]
    public async Task Load_RejectsOverlongProjectStrings()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("long-name.pondex");
        var name = new string('x', ProjectLimits.MaxNameLength + 1);
        var json = $"{{\"formatVersion\":1,\"project\":{{\"name\":\"{name}\"}},\"settings\":{{}},\"sounds\":[],\"triggers\":[]}}";
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false));

        var exception = await Assert.ThrowsAsync<ProjectFormatException>(
            () => new ProjectSerializer().LoadAsync(path));

        Assert.Contains("長すぎ", exception.Message);
    }
}
