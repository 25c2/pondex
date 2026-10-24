namespace PondashiEX.UI;

internal sealed class SmoothScrollMotion
{
    private double _position;
    private double _target;

    public int Position => (int)Math.Round(_position, MidpointRounding.AwayFromZero);

    public bool IsAnimating => _position != _target;

    public void Reset(int position, int maximum)
    {
        _position = Math.Clamp(position, 0, Math.Max(0, maximum));
        _target = _position;
    }

    public void AddOffset(double offset, int actualPosition, int maximum)
    {
        Clamp(maximum);
        if (Position != actualPosition)
        {
            Reset(actualPosition, maximum);
        }

        if (!double.IsFinite(offset) || offset == 0)
        {
            return;
        }

        if (Math.Sign(offset) != Math.Sign(_target - _position))
        {
            // Reverse immediately instead of first finishing the old direction's backlog.
            _target = _position;
        }

        _target = Math.Clamp(_target + offset, 0, Math.Max(0, maximum));
    }

    public int Advance(TimeSpan elapsed, int maximum)
    {
        Clamp(maximum);
        if (elapsed > TimeSpan.Zero && IsAnimating)
        {
            var remaining = Math.Exp(-elapsed.TotalSeconds / 0.05);
            _position = _target + (_position - _target) * remaining;
            if (Math.Abs(_target - _position) < 0.1)
            {
                _position = _target;
            }
        }

        return Position;
    }

    private void Clamp(int maximum)
    {
        maximum = Math.Max(0, maximum);
        _position = Math.Clamp(_position, 0, maximum);
        _target = Math.Clamp(_target, 0, maximum);
    }
}
