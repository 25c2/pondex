namespace PondashiEX.UI;

internal static class DpiWindowHelper
{
    public static void Attach(Form form)
    {
        form.Shown += (_, _) => FitToWorkingArea(form);
        form.DpiChanged += (_, _) => form.BeginInvoke(() => FitToWorkingArea(form));
    }

    public static void FitToWorkingArea(Form form)
    {
        var workingArea = Screen.FromControl(form).WorkingArea;
        form.Bounds = CalculateFittedBounds(form.Bounds, workingArea);
    }

    internal static Rectangle CalculateFittedBounds(Rectangle desired, Rectangle workingArea)
    {
        var width = Math.Min(desired.Width, workingArea.Width);
        var height = Math.Min(desired.Height, workingArea.Height);
        var maximumX = workingArea.Right - width;
        var maximumY = workingArea.Bottom - height;
        var x = Math.Clamp(desired.X, workingArea.Left, maximumX);
        var y = Math.Clamp(desired.Y, workingArea.Top, maximumY);
        return new Rectangle(x, y, width, height);
    }
}
