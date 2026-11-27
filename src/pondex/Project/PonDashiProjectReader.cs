using System.Xml;
using System.Xml.Linq;
using pondex.Audio;

namespace pondex.Project;

// Compatibility with the XML fields written by PonDashiKun 1.7.0.
internal static class PonDashiProjectReader
{
    private const int MasterVolumeMaximum = 1000;
    private static readonly HashSet<string> ProjectFields = new(StringComparer.Ordinal)
    {
        "windowSize", "windowLocation", "ctrlLocation", "masterVolume", "mute", "allowBackgroundInput",
        "silentWaveLoop", "soundDeviceGuid", "evlistVisible", "evlistLocation", "evlistSize",
        "evlistInfo", "element", "formBackColor", "nanoController"
    };
    private static readonly HashSet<string> SoundFields = new(StringComparer.Ordinal)
    {
        "windowLocation", "groupNo", "name", "filePath", "filePathRel", "shortCutKey", "volume",
        "pan", "fadeinTime", "fadeoutTime", "pitch", "loop", "minMode", "loopInSample"
    };
    private static readonly HashSet<string> EventFields = new(StringComparer.Ordinal)
    {
        "enable", "eventName", "commandType", "destName", "conditionType", "conditionTime", "conditionStr", "conditionParam"
    };

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
            if (root.Elements("element").Skip(1).Any() || root.Elements("evlistInfo").Skip(1).Any())
            {
                throw new ProjectFormatException("音源またはイベントの一覧が重複しています。元の保存ファイルを確認してください。");
            }
            if (sounds.Elements().Any(item => item.Name != "SoundElement")
                || events?.Elements().Any(item => item.Name != "EventTriggerInfo") == true)
            {
                throw new ProjectFormatException("プロジェクトXMLの音源またはイベントの形式に対応していません。");
            }

            var model = new ProjectModel { IsPonDashiImport = true };
            var warnings = new ImportWarnings(model.LoadWarnings);
            var project = new Fields(root, ProjectFields, "プロジェクト", warnings);
            model.Project.Name = Path.GetFileNameWithoutExtension(projectPath);
            var color = project.Int("formBackColor");
            model.Project.ThemeColor = LegacyColor((int)project.Bounded("formBackColor", color, 0, 9, 0));
            model.Settings.MasterVolume = project.Volume("masterVolume", MasterVolumeMaximum);
            model.Settings.MasterMute = project.Read("mute", XmlConvert.ToBoolean, false, true);
            model.Settings.BackgroundInput = project.Bool("allowBackgroundInput");
            model.Settings.SilentLoop = project.Bool("silentWaveLoop");
            model.Settings.FadeOutOnStopAll = true;
            if (project.Text("soundDeviceGuid") is { Length: > 0 } device
                && !(Guid.TryParse(device, out var id) && id == Guid.Empty))
            {
                warnings.Add("無印の出力指定に代わり、このPCの出力設定を使用します。");
            }
            if (project.Bool("nanoController"))
            {
                warnings.Add("無印のnanoKONTROL設定は移行対象外です。");
            }

            var names = new Dictionary<string, Guid?>(StringComparer.Ordinal);
            foreach (var element in sounds.Elements("SoundElement"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.Sounds.Count == ProjectLimits.MaxSounds)
                {
                    throw new ProjectFormatException($"音源の件数が上限（{ProjectLimits.MaxSounds}件）を超えています。");
                }
                var fields = new Fields(element, SoundFields, $"音源{model.Sounds.Count + 1}", warnings);
                var name = fields.Text("name");
                var sound = ReadSound(fields, projectPath, name, model.Sounds.Count + 1);
                model.Sounds.Add(sound);
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
                var fields = new Fields(element, EventFields, $"イベント{eventNumber}", warnings);
                var wasEnabled = fields.Bool("enable");
                if (ReadEvent(fields, names) is { } trigger)
                {
                    model.Triggers.Add(trigger);
                    if (wasEnabled) { enabledCount++; }
                }
            }
            if (enabledCount > 0)
            {
                warnings.Add($"有効だったイベント{enabledCount}件を無効の状態で読み込みました。イベントタブで条件と対象を確認してから有効にしてください。");
            }
            warnings.Finish();
            return model;
        }
        catch (XmlException exception)
        {
            throw new ProjectFormatException($"プロジェクトXMLを読み込めません（{exception.LineNumber}行目）。DTDや外部参照を含まない無印の保存ファイルを選んでください。", exception);
        }
    }

    private static SoundDefinition ReadSound(Fields fields, string projectPath, string? name, int number)
    {
        var file = ResolveSoundFile(projectPath, fields.Text("filePathRel"), fields.Text("filePath"));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = file.Length == 0 ? $"音源 {number}" : Path.GetFileNameWithoutExtension(file);
        }
        var keyIndex = fields.Int("shortCutKey");
        var groupIndex = fields.Int("groupNo");
        var key = keyIndex switch
        {
            >= 1 and <= 26 => ((char)('A' + keyIndex - 1)).ToString(),
            >= 27 and <= 36 => ((char)('0' + keyIndex - 27)).ToString(),
            _ => string.Empty
        };
        var group = groupIndex is >= 1 and <= 14 ? ((char)('A' + groupIndex - 1)).ToString() : string.Empty;
        if (keyIndex != 0 && key.Length == 0) { fields.Warn("キー割り当てを移行できませんでした。"); }
        if (groupIndex != 0 && group.Length == 0) { fields.Warn("グループを移行できませんでした。"); }
        if (file.Length == 0)
        {
            fields.Warn("音源参照先を移行できませんでした。「欠落音源」で差し替えてください。");
        }
        else if (!AudioFileFormats.IsSupported(file))
        {
            fields.Warn("拡張子はpondexの対応形式に含まれません。");
        }

        var fadeIn = fields.Bounded("fadeinTime", fields.Float("fadeinTime", 0), 0, 60, 0);
        var fadeOut = fields.Bounded("fadeoutTime", fields.Float("fadeoutTime", 0), 0, 60, 0);
        return new SoundDefinition
        {
            Name = fields.Limit(name, ProjectLimits.MaxNameLength, "音源名"),
            File = file,
            Key = key,
            Group = group,
            Volume = fields.Volume("volume", 100),
            Pan = fields.Bounded("pan", fields.Int("pan"), -100, 100, 0) / 100,
            Speed = fields.Bounded("pitch", fields.Float("pitch", 1), 0.5f, 2, 1),
            FadeIn = new FadeSettings { Enabled = fadeIn > 0, Value = fadeIn },
            FadeOut = new FadeSettings { Enabled = fadeOut > 0, Value = fadeOut },
            Loop = new LoopSettings
            {
                Enabled = fields.Bool("loop"),
                StartSample = fields.Read("loopInSample", XmlConvert.ToUInt32, 0U, 0U)
            }
        };
    }

    private static string ResolveSoundFile(string projectPath, string? relativePath, string? absolutePath)
    {
        var relative = ResolveLocalReference(projectPath, relativePath);
        // v1.7 stores an absolute file URI here when MakeRelativeUri crosses volumes.
        if (relative is not null && !Path.IsPathRooted(relativePath)
            && relativePath?.StartsWith("file:", StringComparison.OrdinalIgnoreCase) != true
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

    private static TriggerDefinition? ReadEvent(Fields fields, Dictionary<string, Guid?> names)
    {
        var condition = fields.Read("conditionType", XmlConvert.ToInt32, 0, -1);
        var command = fields.Read("commandType", XmlConvert.ToInt32, 0, -1);
        var ticks = fields.Read("conditionTime", XmlConvert.ToInt64, 0L, -1L);
        var title = fields.Text("eventName") ?? string.Empty;
        var targetName = fields.Text("destName") ?? string.Empty;
        var sourceName = fields.Text("conditionStr") ?? string.Empty;
        var destination = names.GetValueOrDefault(targetName);
        var source = condition is 2 or 3
            ? names.GetValueOrDefault(sourceName) : null;
        var details = $"「{Short(title)}」 条件 {Short(fields.Text("conditionType") ?? "0")}"
            + $"（{Short(sourceName)} / {Short(fields.Text("conditionTime") ?? "0")} ticks）、"
            + $"操作 {Short(fields.Text("commandType") ?? "0")}、対象「{Short(targetName)}」";
        if (command is < 0 or > 2 || condition is < 0 or > 3
            || (condition != 2 && (ticks < 0 || ticks >= TimeSpan.TicksPerDay)))
        {
            fields.Warn($"条件・操作が未対応または時刻が範囲外のため移行できませんでした。{details}");
            return null;
        }

        var held = destination is null || (condition is 2 or 3 && source is null);
        if (held)
        {
            fields.Warn($"参照先の欠落または音源名の重複があるため保留しました。{details}");
        }
        return new TriggerDefinition
        {
            Enabled = false,
            Memo = fields.Limit(title, ProjectLimits.MaxMemoLength, "イベント名"),
            TargetSoundId = destination ?? Guid.Empty,
            TargetGroupOnHold = held,
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

    private static string Short(string value, int maxLength = 48)
    {
        var text = LimitText(value, maxLength);
        return new string(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            + (text.Length != value.Length ? "…" : "");
    }

    private static string LimitText(string value, int maxLength)
    {
        if (value.Length <= maxLength) { return value; }
        return value[..(char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength)];
    }

    private sealed class ImportWarnings(List<string> warnings)
    {
        private const int Limit = 32;
        private int _omitted;

        internal void Add(string warning)
        {
            if (warnings.Count < Limit) { warnings.Add(warning); }
            else { _omitted++; }
        }

        internal void Finish()
        {
            if (_omitted > 0) { warnings.Add($"読み込み結果の表示上限に達しました。ほかに{_omitted}件の確認事項があります。"); }
        }
    }

    private sealed class Fields
    {
        private readonly Dictionary<string, XElement?> _values = new(StringComparer.Ordinal);
        private readonly string _label;
        private readonly ImportWarnings _warnings;

        internal Fields(XElement element, HashSet<string> known, string label, ImportWarnings warnings)
        {
            _label = label;
            _warnings = warnings;
            foreach (var child in element.Elements())
            {
                var name = child.Name.LocalName;
                if (child.Name.Namespace != XNamespace.None || !known.Contains(name))
                {
                    Warn($"未対応の項目「{Short(name)}」を読み飛ばしました。");
                }
                else if (!_values.TryAdd(name, child))
                {
                    if (_values[name] is not null) { Warn($"項目「{name}」が重複しているため使用しませんでした。"); }
                    _values[name] = null;
                }
            }
        }

        internal string? Text(string field) => Read<string?>(field, value => value, null, null);

        internal int Int(string field) => Read(field, XmlConvert.ToInt32, 0, 0);

        internal float Float(string field, float fallback) => Read(field, XmlConvert.ToSingle, fallback, fallback);

        internal bool Bool(string field) => Read(field, XmlConvert.ToBoolean, false, false);

        internal float Volume(string field, int maximum)
        {
            if (!_values.ContainsKey(field)) { Warn($"「{field}」が未指定のため音量を0にしました。"); }
            var parsed = Read(field, XmlConvert.ToInt32, 0, 0);
            if (parsed < 0 || parsed > maximum)
            {
                Warn($"「{field}」が範囲外のため音量を0にしました。");
                return 0;
            }
            return (float)parsed / maximum;
        }

        internal float Bounded(string field, float value, float min, float max, float fallback)
        {
            var result = float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
            if (value != result) { Warn($"「{field}」をpondexの設定範囲に調整しました。"); }
            return result;
        }

        internal string Limit(string value, int length, string field)
        {
            if (value.Length <= length) { return value; }
            Warn($"「{field}」が長すぎるため切り詰めました。");
            return LimitText(value, length);
        }

        internal T Read<T>(string field, Func<string, T> parse, T missing, T invalid)
        {
            if (!_values.TryGetValue(field, out var element)) { return missing; }
            if (element is null) { return invalid; }
            try
            {
                if (!element.HasElements) { return parse(element.Value); }
            }
            catch (Exception exception) when (exception is FormatException or OverflowException) { }
            Warn($"「{field}」の形式が正しくないため使用しませんでした。");
            return invalid;
        }

        internal void Warn(string message) => _warnings.Add($"{_label}: {message}");
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
