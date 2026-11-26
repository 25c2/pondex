using System.Xml;
using System.Xml.Linq;
using pondex.Audio;

namespace pondex.Project;

// Compatibility with the XML fields written by PonDashiKun 1.7.0.
internal static class PonDashiProjectReader
{
    private const int MasterVolumeMaximum = 1000;

    internal static bool LooksLikeXml(ReadOnlySpan<byte> payload)
    {
        if (payload.StartsWith(new byte[] { 0xff, 0xfe }) || payload.StartsWith(new byte[] { 0xfe, 0xff })
            || payload.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { return true; }
        foreach (var value in payload)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') { continue; }
            return value == (byte)'<' || payload.StartsWith(new byte[] { 0, (byte)'<' })
                || payload.StartsWith(new byte[] { 0, 0, 0, (byte)'<' });
        }
        return false;
    }

    internal static ProjectModel Read(byte[] bytes, string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = ProjectLimits.MaxFileBytes,
                IgnoreComments = true
            };
            using (var validator = XmlReader.Create(stream, settings))
            {
                while (validator.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (validator.Depth > 16) { throw new ProjectFormatException("プロジェクトXMLの階層が深すぎます。"); }
                }
            }
            stream.Position = 0;
            using var reader = XmlReader.Create(stream, settings);
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "ProjectSettings" || root.Element("element") is not { } sounds)
            {
                throw new ProjectFormatException("無印ポン出しくんのプロジェクトXMLではありません。");
            }

            var events = root.Element("evlistInfo");
            if (sounds.Elements().Any(item => item.Name != "SoundElement")
                || events?.Elements().Any(item => item.Name != "EventTriggerInfo") == true)
            {
                throw new ProjectFormatException("プロジェクトXMLの音源またはイベントの形式に対応していません。");
            }

            var model = new ProjectModel { IsPonDashiImport = true };
            model.Project.Name = Path.GetFileNameWithoutExtension(projectPath);
            model.Project.ThemeColor = LegacyColor((int?)root.Element("formBackColor") ?? 0);
            model.Settings.MasterVolume = Bounded(model, (int?)root.Element("masterVolume") ?? MasterVolumeMaximum,
                0, MasterVolumeMaximum, MasterVolumeMaximum, "マスター音量") / MasterVolumeMaximum;
            model.Settings.MasterMute = (bool?)root.Element("mute") ?? false;
            model.Settings.BackgroundInput = (bool?)root.Element("allowBackgroundInput") ?? false;
            model.Settings.SilentLoop = (bool?)root.Element("silentWaveLoop") ?? false;
            model.Settings.FadeOutOnStopAll = true;
            if (root.Element("soundDeviceGuid") is { Value.Length: > 0 } device
                && !(Guid.TryParse(device.Value, out var id) && id == Guid.Empty))
            {
                // A DirectSound GUID cannot identify a pondex output endpoint.
                model.Settings.OutputDevice = "pondashi-output-reselection";
                model.LoadWarnings.Add("無印の出力先は引き継げません。「設定」でこのPCの出力先を選んでください。");
            }
            if ((bool?)root.Element("nanoController") == true)
            {
                model.LoadWarnings.Add("無印のnanoKONTROL設定は移行対象外です。");
            }

            var names = new Dictionary<string, Guid?>(StringComparer.Ordinal);
            foreach (var element in sounds.Elements("SoundElement"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.Sounds.Count == ProjectLimits.MaxSounds)
                {
                    throw new ProjectFormatException($"音源の件数が上限（{ProjectLimits.MaxSounds}件）を超えています。");
                }
                var sound = ReadSound(element, projectPath, model);
                model.Sounds.Add(sound);
                var name = (string?)element.Element("name");
                if (!string.IsNullOrEmpty(name) && !names.TryAdd(name, sound.Id)) { names[name] = null; }
                model.Settings.ExclusiveGroups |= sound.Group.Length > 0;
            }

            var enabledCount = 0;
            var eventNumber = 0;
            foreach (var element in events?.Elements("EventTriggerInfo") ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++eventNumber > ProjectLimits.MaxTriggers)
                {
                    throw new ProjectFormatException($"イベントの件数が上限（{ProjectLimits.MaxTriggers}件）を超えています。");
                }
                if (ReadEvent(element, names) is { } trigger)
                {
                    model.Triggers.Add(trigger);
                    if ((bool?)element.Element("enable") == true) { enabledCount++; }
                }
                else
                {
                    var name = (string?)element.Element("eventName");
                    model.LoadWarnings.Add($"イベント{eventNumber}「{name}」は条件・操作が未対応か、音源名から対象を特定できないため移行できませんでした。");
                }
            }
            if (enabledCount > 0)
            {
                model.LoadWarnings.Add($"有効だったイベント{enabledCount}件を無効の状態で読み込みました。イベントタブで条件と対象を確認してから有効にしてください。");
            }
            return model;
        }
        catch (XmlException exception)
        {
            throw new ProjectFormatException($"プロジェクトXMLを読み込めません（{exception.LineNumber}行目）。DTDや外部参照を含まない無印の保存ファイルを選んでください。", exception);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new ProjectFormatException("プロジェクトXMLの数値かチェック項目の形式が正しくありません。", exception);
        }
    }

    private static SoundDefinition ReadSound(XElement element, string projectPath, ProjectModel model)
    {
        var file = ResolveSoundFile(projectPath, (string?)element.Element("filePathRel"), (string?)element.Element("filePath"));
        var name = (string?)element.Element("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = file.Length == 0 ? $"音源 {model.Sounds.Count + 1}" : Path.GetFileNameWithoutExtension(file);
        }
        var keyIndex = (int?)element.Element("shortCutKey") ?? 0;
        var groupIndex = (int?)element.Element("groupNo") ?? 0;
        var key = keyIndex switch
        {
            >= 1 and <= 26 => ((char)('A' + keyIndex - 1)).ToString(),
            >= 27 and <= 36 => ((char)('0' + keyIndex - 27)).ToString(),
            _ => string.Empty
        };
        var group = groupIndex is >= 1 and <= 14 ? ((char)('A' + groupIndex - 1)).ToString() : string.Empty;
        if (keyIndex != 0 && key.Length == 0) { model.LoadWarnings.Add($"「{name}」のキー割り当てを移行できませんでした。"); }
        if (groupIndex != 0 && group.Length == 0) { model.LoadWarnings.Add($"「{name}」のグループを移行できませんでした。"); }
        if (file.Length == 0)
        {
            model.LoadWarnings.Add($"「{name}」の音源参照先を移行できませんでした。「欠落音源」で差し替えてください。");
        }
        else if (!AudioFileFormats.IsSupported(file))
        {
            model.LoadWarnings.Add($"「{name}」の拡張子はpondexの対応形式に含まれません。");
        }

        var fadeIn = Bounded(model, (float?)element.Element("fadeinTime") ?? 0, 0, 60, 0, $"「{name}」のFade In");
        var fadeOut = Bounded(model, (float?)element.Element("fadeoutTime") ?? 0, 0, 60, 0, $"「{name}」のFade Out");
        return new SoundDefinition
        {
            Name = name,
            File = file,
            Key = key,
            Group = group,
            Volume = Bounded(model, (int?)element.Element("volume") ?? 100, 0, 100, 100, $"「{name}」の音量") / 100,
            Pan = Bounded(model, (int?)element.Element("pan") ?? 0, -100, 100, 0, $"「{name}」のPan") / 100,
            Speed = Bounded(model, (float?)element.Element("pitch") ?? 1, 0.5f, 2, 1, $"「{name}」の再生速度"),
            FadeIn = new FadeSettings { Enabled = fadeIn > 0, Value = fadeIn },
            FadeOut = new FadeSettings { Enabled = fadeOut > 0, Value = fadeOut },
            Loop = new LoopSettings
            {
                Enabled = (bool?)element.Element("loop") ?? false,
                StartSample = (uint?)element.Element("loopInSample") ?? 0
            }
        };
    }

    private static string ResolveSoundFile(string projectPath, string? relativePath, string? absolutePath)
    {
        var relative = ResolveLocalReference(projectPath, relativePath);
        if (relative is not null && relativePath?.StartsWith("file:", StringComparison.OrdinalIgnoreCase) != true
            && File.Exists(relative))
        {
            return relative;
        }
        var absolute = ResolveLocalReference(projectPath, absolutePath);
        return absolute is not null && File.Exists(absolute) ? absolute : relative ?? absolute ?? string.Empty;
    }

    private static string? ResolveLocalReference(string projectPath, string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > ProjectLimits.MaxPathLength) { return null; }
        if (reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(reference.Replace('\\', '/'), UriKind.Absolute, out var uri)
                || !uri.IsFile || uri.Host.Length > 0) { return null; }
            reference = uri.LocalPath;
        }
        reference = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var colon = reference.IndexOf(':');
        if ((colon >= 0 && (colon != 1 || reference.AsSpan(2).Contains(':')))
            || (colon == 1 && !OperatingSystem.IsWindows())
            || (Path.IsPathRooted(reference) && !Path.IsPathFullyQualified(reference))) { return null; }
        try
        {
            var resolved = ProjectPath.Resolve(projectPath, reference);
            return resolved.Length <= ProjectLimits.MaxPathLength ? resolved : null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static TriggerDefinition? ReadEvent(XElement element, Dictionary<string, Guid?> names)
    {
        var condition = (int?)element.Element("conditionType") ?? 0;
        var command = (int?)element.Element("commandType") ?? 0;
        var ticks = (long?)element.Element("conditionTime") ?? 0;
        var destination = names.GetValueOrDefault((string?)element.Element("destName") ?? string.Empty);
        var source = condition is 2 or 3
            ? names.GetValueOrDefault((string?)element.Element("conditionStr") ?? string.Empty) : null;
        if (destination is null || command is < 0 or > 2 || condition is < 0 or > 3
            || (condition is 2 or 3 && source is null)
            || (condition != 2 && (ticks < 0 || ticks >= TimeSpan.TicksPerDay)))
        {
            return null;
        }

        return new TriggerDefinition
        {
            Enabled = false,
            Memo = (string?)element.Element("eventName") ?? string.Empty,
            TargetSoundId = destination.Value,
            Action = command switch { 0 => TriggerAction.Play, 1 => TriggerAction.Stop, _ => TriggerAction.Pause },
            TriggerType = condition switch
            {
                0 => TriggerType.ClockTime,
                1 => TriggerType.StopwatchElapsed,
                2 => TriggerType.SoundEnded,
                _ => TriggerType.SoundElapsed
            },
            Parameters = new TriggerParameters
            {
                ClockTime = condition == 0 ? new TimeOnly(ticks) : null,
                Elapsed = condition is 1 or 3 ? TimeSpan.FromTicks(ticks) : null,
                SourceSoundId = source
            }
        };
    }

    private static float Bounded(ProjectModel model, float value, float min, float max, float fallback, string label)
    {
        var result = float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        if (value != result) { model.LoadWarnings.Add($"{label}をpondexの設定範囲に調整しました。"); }
        return result;
    }

    private static string LegacyColor(int color) => color switch
    {
        1 => "#B8BC4B",
        2 => "#BC6E29",
        3 => "#B56D6B",
        4 => "#B579B3",
        5 => "#5656A9",
        6 => "#57A8A9",
        7 => "#559F54",
        8 => "#444444",
        9 => "#F0E9D3",
        _ => "#202020"
    };
}
