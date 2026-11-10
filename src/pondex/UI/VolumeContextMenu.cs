namespace pondex.UI;

internal static class VolumeContextMenu
{
    public static void Attach(NumericUpDown percentage, Control? slider = null)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("100%（標準）に戻す", null, (_, _) =>
        {
            if (percentage.Enabled) { percentage.Value = 100; }
        });
        menu.Opening += (_, args) => args.Cancel = !percentage.Enabled;
        percentage.ContextMenuStrip = menu;
        if (slider is not null) { slider.ContextMenuStrip = menu; }
        percentage.Disposed += (_, _) => menu.Dispose();
    }
}
