using PondashiEX.Project;

namespace PondashiEX.Audio;

public interface IFadePolicy
{
    TimeSpan? GetFadeOutDuration(FadeSettings settings);
}

public sealed class SecondsFadePolicy : IFadePolicy
{
    public TimeSpan? GetFadeOutDuration(FadeSettings settings)
    {
        // 旧版1.7.0の配布バイナリでは秒からDateTime ticksへ換算していた。
        // EXでは本番中の誤操作を避けるため、正の値を最大60秒に制限する。
        if (!settings.Enabled || settings.Value <= 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Clamp(settings.Value, 0.001f, 60.0f));
    }
}
