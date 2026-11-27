using System.Security;
using System.Text;
using pondex.Project;

namespace pondex.Tests;

public sealed class PonDashiImportTests
{
    private const string Example = """
        <?xml version="1.0" encoding="utf-8"?>
        <ProjectSettings xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <windowSize><Width>800</Width><Height>600</Height></windowSize>
          <masterVolume>650</masterVolume><mute>true</mute>
          <allowBackgroundInput>true</allowBackgroundInput><silentWaveLoop>true</silentWaveLoop>
          <soundDeviceGuid>5d4f4014-b0b1-46b0-8433-d0c7ab628688</soundDeviceGuid>
          <formBackColor>5</formBackColor><nanoController>true</nanoController>
          <element>
            <SoundElement>
              <name>開演</name><groupNo>2</groupNo><shortCutKey>26</shortCutKey>
              <filePath>{oldOpening}</filePath><filePathRel>音源\開演 %20 &amp; BGM.wav</filePathRel>
              <volume>79</volume><pan>-35</pan><fadeinTime>0.5</fadeinTime><fadeoutTime>2.75</fadeoutTime>
              <pitch>1.25</pitch><loop>true</loop><loopInSample>110250</loopInSample>
            </SoundElement>
            <SoundElement>
              <name>ベル</name><groupNo>2</groupNo><shortCutKey>27</shortCutKey>
              <filePath>{bell}</filePath><filePathRel>gone\ベル.wav</filePathRel>
              <volume>100</volume><pitch>1</pitch><loop>false</loop>
            </SoundElement>
          </element>
          <evlistInfo>
            <EventTriggerInfo><enable>true</enable><eventName>時刻</eventName><conditionType>0</conditionType><conditionTime>666000000000</conditionTime><commandType>0</commandType><destName>開演</destName></EventTriggerInfo>
            <EventTriggerInfo><enable>false</enable><eventName>SW</eventName><conditionType>1</conditionType><conditionTime>15000000</conditionTime><commandType>0</commandType><destName>ベル</destName></EventTriggerInfo>
            <EventTriggerInfo><enable>true</enable><eventName>終了</eventName><conditionType>2</conditionType><conditionStr>開演</conditionStr><commandType>1</commandType><destName>ベル</destName></EventTriggerInfo>
            <EventTriggerInfo><enable>true</enable><eventName>経過</eventName><conditionType>3</conditionType><conditionStr>ベル</conditionStr><conditionTime>90000000</conditionTime><commandType>2</commandType><destName>開演</destName></EventTriggerInfo>
          </evlistInfo>
        </ProjectSettings>
        """;

    [Fact]
    public async Task Open_MapsVersion17FieldsAndRebasesAudioOnSaveAs()
    {
        using var directory = new TestDirectory();
        var source = directory.GetPath("学校公演.xml");
        var soundDirectory = Directory.CreateDirectory(directory.GetPath("音源")).FullName;
        var opening = Path.Combine(soundDirectory, "開演 %20 & BGM.wav");
        var bell = directory.GetPath("ベル.wav");
        var oldOpening = Path.Combine(Directory.CreateDirectory(directory.GetPath("old")).FullName, "開演.wav");
        await File.WriteAllBytesAsync(oldOpening, [1]);
        await File.WriteAllBytesAsync(opening, [2]);
        await File.WriteAllBytesAsync(bell, [3]);
        var xml = Example.Replace("{oldOpening}", SecurityElement.Escape(oldOpening), StringComparison.Ordinal)
            .Replace("{bell}", SecurityElement.Escape(bell), StringComparison.Ordinal);
        await File.WriteAllTextAsync(source, xml);

        var serializer = new ProjectSerializer();
        using var manager = new ProjectManager(serializer);
        Assert.True(await manager.OpenAsync(source, (candidate, path) =>
            candidate.IsPonDashiImport && candidate.Settings.BackgroundInput && path == source));

        var model = manager.Current;
        Assert.True(manager.RequiresSaveAs);
        Assert.True(manager.IsDirty);
        Assert.Equal("学校公演", model.Project.Name);
        Assert.Equal("#5656A9", model.Project.ThemeColor);
        Assert.Equal(0.65f, model.Settings.MasterVolume, 4);
        Assert.True(model.Settings.MasterMute);
        Assert.True(model.Settings.SilentLoop);
        Assert.True(model.Settings.ExclusiveGroups);
        Assert.True(model.Settings.FadeOutOnStopAll);
        Assert.Null(model.Settings.OutputDevice);
        Assert.Contains(model.LoadWarnings, warning => warning.Contains("nanoKONTROL", StringComparison.Ordinal));
        Assert.Empty(model.MissingFiles);

        var cue = model.Sounds[0];
        Assert.Equal("開演", cue.Name);
        Assert.Equal("Z", cue.Key);
        Assert.Equal("B", cue.Group);
        Assert.Equal("0", model.Sounds[1].Key);
        Assert.Equal(opening, manager.ResolvePlaybackPath(cue));
        Assert.Equal(bell, manager.ResolvePlaybackPath(model.Sounds[1]));
        Assert.Equal(0.79f, cue.Volume, 4);
        Assert.Equal(-0.35f, cue.Pan, 4);
        Assert.Equal(1.25f, cue.Speed);
        Assert.Equal(1f, cue.Pitch);
        Assert.True(cue.Loop.Enabled);
        Assert.Equal(110250, cue.Loop.StartSample);
        Assert.True(cue.FadeIn.Enabled);
        Assert.Equal(0.5f, cue.FadeIn.Value);
        Assert.True(cue.FadeOut.Enabled);
        Assert.Equal(2.75f, cue.FadeOut.Value);

        Assert.Equal(new TimeOnly(18, 30), model.Triggers[0].Parameters.ClockTime);
        Assert.Equal(cue.Id, model.Triggers[0].TargetSoundId);
        Assert.Equal(TimeSpan.FromSeconds(1.5), model.Triggers[1].Parameters.Elapsed);
        Assert.Equal(TriggerType.SoundEnded, model.Triggers[2].TriggerType);
        Assert.Equal(TriggerAction.Stop, model.Triggers[2].Action);
        Assert.Equal(cue.Id, model.Triggers[2].Parameters.SourceSoundId);
        Assert.Equal(TriggerAction.Pause, model.Triggers[3].Action);
        Assert.Equal(TimeSpan.FromSeconds(9), model.Triggers[3].Parameters.Elapsed);
        Assert.All(model.Triggers, item => Assert.False(item.Enabled));

        var nested = Directory.CreateDirectory(directory.GetPath("converted")).FullName;
        var convertedPath = Path.Combine(nested, "学校公演.pondex");
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsAsync(source));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SaveAsAsync(directory.GetPath("new.xml")));
        await manager.SaveAsAsync(convertedPath);
        Assert.Equal(xml, await File.ReadAllTextAsync(source));
        Assert.False(manager.IsDirty);
        Assert.False(manager.RequiresSaveAs);
        var converted = await serializer.LoadAsync(convertedPath);
        Assert.False(converted.IsPonDashiImport);
        Assert.True(converted.Settings.ExclusiveGroups);
        Assert.Equal(opening, ProjectPath.Resolve(convertedPath, converted.Sounds[0].File));
        Assert.Equal(bell, ProjectPath.Resolve(convertedPath, converted.Sounds[1].File));
        Assert.Equal(converted.Sounds[0].Id, converted.Triggers[2].Parameters.SourceSoundId);
        Assert.Empty(converted.LoadWarnings);
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(100, 0.1f)]
    [InlineData(1000, 1f)]
    public async Task Import_ConvertsThousandStepMasterVolume(int original, float expected)
    {
        using var directory = new TestDirectory();
        var source = directory.GetPath("stage");
        await File.WriteAllTextAsync(source, $"""
            <ProjectSettings><masterVolume>{original}</masterVolume><element /></ProjectSettings>
            """);
        var project = await new ProjectSerializer().LoadAsync(source);
        Assert.Equal(expected, project.Settings.MasterVolume, 4);
    }

    [Fact]
    public async Task Import_MissingOrUnsafeAudioCanBeSavedAndAmbiguousEventsAreReported()
    {
        using var directory = new TestDirectory();
        var xml = directory.GetPath("cue.xml");
        await File.WriteAllTextAsync(xml, """
            <ProjectSettings>
              <element>
                <SoundElement><name>同名</name><filePathRel>sounds\missing.wav</filePathRel></SoundElement>
                <SoundElement><name>同名</name><filePathRel>file://remote/share/a.wav</filePathRel><filePath>\\remote\share\b.wav</filePath></SoundElement>
              </element>
              <evlistInfo><EventTriggerInfo><enable>true</enable><eventName>重複</eventName><destName>同名</destName></EventTriggerInfo></evlistInfo>
            </ProjectSettings>
            """);
        using var manager = new ProjectManager(new ProjectSerializer());
        Assert.True(await manager.OpenAsync(xml));
        Assert.Equal(2, manager.Current.MissingFiles.Count);
        Assert.Empty(manager.Current.Sounds[1].File);
        var held = Assert.Single(manager.Current.Triggers);
        Assert.False(held.Enabled);
        Assert.Equal(Guid.Empty, held.TargetSoundId);
        Assert.True(held.TargetGroupOnHold);
        Assert.Contains(manager.Current.LoadWarnings, warning => warning.Contains("重複", StringComparison.Ordinal));

        var destination = directory.GetPath("converted.pondex");
        await manager.SaveAsAsync(destination);
        Assert.True(await manager.OpenAsync(destination));
        Assert.False(manager.RequiresSaveAs);
        Assert.True(Assert.Single(manager.Current.Triggers).TargetGroupOnHold);
        Assert.Equal(2, manager.Current.MissingFiles.Count);
        Assert.Equal(directory.GetPath(Path.Combine("sounds", "missing.wav")), manager.ResolvePlaybackPath(manager.Current.Sounds[0]));
        Assert.Empty(manager.Current.Sounds[1].File);
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("stage.data")]
    public async Task Import_RecognizesUtf16XmlByContentAndKeepsOldProjectWhenDeclined(string name)
    {
        using var directory = new TestDirectory();
        var file = directory.GetPath(name);
        await File.WriteAllTextAsync(file, """
            <?xml version="1.0" encoding="utf-16"?>
            <ProjectSettings><element /></ProjectSettings>
            """, Encoding.Unicode);
        using var manager = new ProjectManager(new ProjectSerializer());
        var previous = manager.Current;
        Assert.False(await manager.OpenAsync(file, (candidate, _) => !candidate.IsPonDashiImport));
        Assert.Same(previous, manager.Current);
        Assert.Null(manager.CurrentPath);
        Assert.True(await manager.OpenAsync(file));
        Assert.True(manager.RequiresSaveAs);
        Assert.Empty(manager.Current.Sounds);
    }

    [Fact]
    public async Task Import_MalformedAndDuplicateFieldsNeverRaiseLevelsOrEnableInput()
    {
        using var directory = new TestDirectory();
        var source = directory.GetPath("show");
        await File.WriteAllTextAsync(source, """
            <ProjectSettings><masterVolume>999999999999999999999</masterVolume>
              <allowBackgroundInput>true</allowBackgroundInput><allowBackgroundInput>false</allowBackgroundInput>
              <mute>maybe</mute><windowLocation><X>10</X><Y>20</Y></windowLocation><newSetting>value</newSetting>
              <element>
                <SoundElement><name>A</name><filePathRel>a.wav</filePathRel><volume>101</volume><pitch>NaN</pitch><pan>-105</pan><fadeinTime>INF</fadeinTime><loop>maybe</loop><loopInSample>-1</loopInSample></SoundElement>
                <SoundElement><name>B</name><filePathRel>b.wav</filePathRel><volume>100</volume><volume>50</volume><fadeoutTime>-4</fadeoutTime></SoundElement>
                <SoundElement><name>C</name><filePathRel>c.wav</filePathRel></SoundElement>
              </element>
              <evlistInfo>
                <EventTriggerInfo><enable>maybe</enable><eventName>Keep</eventName><destName>A</destName></EventTriggerInfo>
                <EventTriggerInfo><eventName>Skip</eventName><destName>B</destName><conditionType>3</conditionType><conditionStr>A</conditionStr><conditionTime>999999999999999999999</conditionTime><commandType>0</commandType></EventTriggerInfo>
              </evlistInfo>
            </ProjectSettings>
            """);
        var model = await new ProjectSerializer().LoadAsync(source);
        Assert.Equal(0, model.Settings.MasterVolume);
        Assert.True(model.Settings.MasterMute);
        Assert.False(model.Settings.BackgroundInput);
        Assert.All(model.Sounds, sound => Assert.Equal(0, sound.Volume));
        Assert.Equal(1, model.Sounds[0].Speed);
        Assert.Equal(-1, model.Sounds[0].Pan);
        Assert.False(model.Sounds[0].Loop.Enabled);
        Assert.False(model.Sounds[0].FadeIn.Enabled);
        Assert.False(model.Sounds[1].FadeOut.Enabled);
        Assert.False(Assert.Single(model.Triggers).Enabled);
        Assert.Contains(model.LoadWarnings, warning => warning.Contains("newSetting", StringComparison.Ordinal));
        Assert.DoesNotContain(model.LoadWarnings, warning => warning.Contains("windowLocation", StringComparison.Ordinal));
        Assert.Contains(model.LoadWarnings, warning => warning.Contains("volume", StringComparison.Ordinal)
            && warning.Contains("重複", StringComparison.Ordinal));
        Assert.Contains(model.LoadWarnings, warning => warning.Contains("Skip", StringComparison.Ordinal)
            && warning.Contains("操作 0", StringComparison.Ordinal) && warning.Contains("対象「B」", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_WarningsAreBoundedForLongUnknownFieldsAndUnsupportedEvents()
    {
        using var directory = new TestDirectory();
        var source = directory.GetPath("show.xml");
        var value = new string('s', 4000);
        var events = $"""
            <EventTriggerInfo><eventName>{value}</eventName><commandType>999</commandType><conditionStr>{value}</conditionStr><destName>{value}</destName></EventTriggerInfo>
            """;
        await File.WriteAllTextAsync(source, $"<ProjectSettings><element/><evlistInfo>{string.Concat(Enumerable.Repeat(events, 60))}</evlistInfo></ProjectSettings>");
        var model = await new ProjectSerializer().LoadAsync(source);
        Assert.Empty(model.Triggers);
        Assert.InRange(model.LoadWarnings.Count, 2, 33);
        Assert.All(model.LoadWarnings, warning => Assert.True(warning.Length < 320));
        Assert.Contains(model.LoadWarnings, warning => warning.Contains("表示上限", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_DecodesFileUriOnceAndUsesExistingAbsolutePathBeforeAbsoluteUriFallback()
    {
        using var directory = new TestDirectory();
        var source = directory.GetPath("show");
        var uriPath = directory.GetPath("日本語 %20 #.wav");
        var preferred = directory.GetPath("absolute.wav");
        await File.WriteAllBytesAsync(uriPath, [1]);
        await File.WriteAllBytesAsync(preferred, [2]);
        var fileUri = new Uri(directory.Path + Path.DirectorySeparatorChar).AbsoluteUri
            + Uri.EscapeDataString(Path.GetFileName(uriPath));
        await File.WriteAllTextAsync(source, $"""
            <ProjectSettings><element>
              <SoundElement><filePathRel>{SecurityElement.Escape(fileUri)}</filePathRel><filePath>{SecurityElement.Escape(preferred)}</filePath></SoundElement>
              <SoundElement><filePathRel>{SecurityElement.Escape(fileUri)}</filePathRel><filePath>missing.wav</filePath></SoundElement>
            </element></ProjectSettings>
            """);
        var model = await new ProjectSerializer().LoadAsync(source);
        Assert.Empty(model.MissingFiles);
        Assert.Equal(preferred, model.Sounds[0].File);
        Assert.Equal(uriPath, model.Sounds[1].File);
    }

    [Fact]
    public async Task Import_FailedOrCancelledSaveKeepsTheProtectedSource()
    {
        using var directory = new TestDirectory();
        var source = directory.GetPath("show");
        const string xml = "<ProjectSettings><element /></ProjectSettings>";
        await File.WriteAllTextAsync(source, xml);
        using var manager = new ProjectManager(new ProjectSerializer());
        Assert.True(await manager.OpenAsync(source));
        var blocked = directory.GetPath("occupied");
        await File.WriteAllTextAsync(blocked, "file");
        await Assert.ThrowsAnyAsync<IOException>(() => manager.SaveAsAsync(Path.Combine(blocked, "show.pondex")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.SaveAsAsync(directory.GetPath("cancelled.pondex"), new CancellationToken(canceled: true)));
        Assert.Equal(xml, await File.ReadAllTextAsync(source));
        Assert.True(manager.IsDirty);
        Assert.True(manager.RequiresSaveAs);
        Assert.True(manager.Current.IsPonDashiImport);
    }

    [Theory]
    [InlineData("<!DOCTYPE ProjectSettings [<!ELEMENT ProjectSettings ANY>]><ProjectSettings><element /></ProjectSettings>")]
    [InlineData("<Unrelated><element /></Unrelated>")]
    [InlineData("<ProjectSettings><element><Unsupported /></element></ProjectSettings>")]
    [InlineData("<ProjectSettings><element /><element /></ProjectSettings>")]
    [InlineData("<ProjectSettings><element>")]
    public async Task Import_RejectsDtdUnrelatedOrMalformedXml(string contents)
    {
        using var directory = new TestDirectory();
        var file = directory.GetPath("unsafe.xml");
        await File.WriteAllTextAsync(file, contents);
        var serializer = new ProjectSerializer();
        await Assert.ThrowsAsync<ProjectFormatException>(() => serializer.LoadAsync(file));
    }
}
