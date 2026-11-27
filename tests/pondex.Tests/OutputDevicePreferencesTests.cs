using pondex.Audio;
using pondex.Project;
using pondex.Utility;

namespace pondex.Tests;

public sealed class OutputDevicePreferencesTests
{
    private static readonly AudioDeviceInfo ConsoleOutput = new(0, "USB", "legacy-usb") { EndpointId = "console" };
    private static readonly AudioDeviceSnapshot Snapshot = new([AudioDeviceInfo.Default, ConsoleOutput], new(0, "console"));

    [Fact]
    public void FirstRunOffersOnlyAnUnambiguousConcreteLegacyOutput()
    {
        using var directory = new TestDirectory();
        var store = new OutputDevicePreferences(directory.Path);
        var logger = new Logger();
        var preference = store.Load(logger);
        Assert.False(preference.FileExists);
        Assert.Null(preference.SelectionId);
        Assert.Empty(logger.Warnings);
        Assert.Equal(ConsoleOutput, preference.FindLegacyCandidate("legacy-usb", Snapshot)?.Device);
        foreach (var legacyId in new[] { null, "default", "not-connected", "0", "pondashi-output-reselection" })
        {
            Assert.Null(preference.FindLegacyCandidate(legacyId, Snapshot));
        }
        var duplicate = ConsoleOutput with { DeviceNumber = 1, EndpointId = "another" };
        Assert.Null(preference.FindLegacyCandidate("legacy-usb", Snapshot with { Devices = [AudioDeviceInfo.Default, ConsoleOutput, duplicate] }));

        Assert.True(store.TrySave("default", logger));
        preference = store.Load(logger);
        Assert.Equal("default", preference.SelectionId);
        Assert.Null(preference.FindLegacyCandidate("legacy-usb", Snapshot));
        Assert.True(store.TrySave(ConsoleOutput.SelectionId, logger));
        Assert.Equal(ConsoleOutput.SelectionId, store.Load(logger).SelectionId);
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("broken-json")]
    [InlineData("{\"SelectionId\":123}")]
    [InlineData("{\"SelectionId\":\"   \"}")]
    [InlineData("{\"SelectionId\":\"first\",\"SelectionId\":\"second\"}")]
    [InlineData("{\"SelectionId\":\"pondashi-output-reselection\"}")]
    [InlineData("{\"SelectionId\":\"a\\u0000b\"}")]
    public void InvalidLocalOutputCannotOfferAProjectFallback(string json)
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.GetPath("output-device.json"), json);
        var logger = new Logger();
        var preference = new OutputDevicePreferences(directory.Path).Load(logger);
        Assert.True(preference.FileExists);
        Assert.Null(preference.SelectionId);
        Assert.Null(preference.FindLegacyCandidate("legacy-usb", Snapshot));
        Assert.Single(logger.Warnings);
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(9000)]
    public void LongLocalOutputIsRejected(int length)
    {
        using var directory = new TestDirectory();
        File.WriteAllText(directory.GetPath("output-device.json"), $"{{\"SelectionId\":\"{new string('a', length)}\"}}");
        var logger = new Logger();
        Assert.Equal(new OutputDevicePreference(null, true), new OutputDevicePreferences(directory.Path).Load(logger));
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public void WriteErrorReportsFailureAndKeepsExistingData()
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("occupied");
        File.WriteAllText(path, "keep");
        var logger = new Logger();

        Assert.False(new OutputDevicePreferences(path).TrySave("default", logger));
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Single(logger.Warnings);
    }

    [Theory]
    [InlineData("3", null)]
    [InlineData("{\"bad\":true}", null)]
    [InlineData("\"pondashi-output-reselection\"", null)]
    [InlineData("\"default\"", "default")]
    [InlineData("\"legacy-usb\"", "legacy-usb")]
    public async Task LegacyOutputFieldDoesNotDiscardOtherProjectSettings(string jsonId, string? preserved)
    {
        using var directory = new TestDirectory();
        var path = directory.GetPath("show.pondex");
        await File.WriteAllTextAsync(path,
            $$$"""{"formatVersion":{{{ProjectModel.CurrentFormatVersion}}},"settings":{"outputDevice":{{{jsonId}}},"masterVolume":0.25,"extra":123}}""");
        var serializer = new ProjectSerializer();
        var model = await serializer.LoadAsync(path);
        Assert.Equal(preserved, model.Settings.OutputDevice);
        Assert.Equal(0.25f, model.Settings.MasterVolume);
        Assert.Equal(123, model.Settings.ExtensionData!["extra"].GetInt32());
        var copy = directory.GetPath("copy.pondex");
        await serializer.SaveAsync(copy, model);
        if (preserved is null) { Assert.DoesNotContain("\"outputDevice\"", await File.ReadAllTextAsync(copy)); }
        Assert.Equal(preserved, (await serializer.LoadAsync(copy)).Settings.OutputDevice);
    }

    private sealed class Logger : IAppLogger
    {
        internal List<string> Warnings { get; } = [];
        public void Info(string message) { }
        public void Warning(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) { }
    }
}
