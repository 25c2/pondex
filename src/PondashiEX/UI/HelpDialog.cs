namespace PondashiEX.UI;

public sealed class HelpDialog : Form
{
    public HelpDialog()
    {
        Text = "PondashiEX ヘルプ";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(620, 480);
        ClientSize = new Size(760, 620);
        MaximizeBox = true;
        MinimizeBox = false;
        DpiWindowHelper.Attach(this);

        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            Font = UiTheme.CreateFont(10F),
            Text = HelpText,
            DetectUrls = false,
            TabStop = false
        };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 12), BackColor = SystemColors.Window };
        content.Controls.Add(text);
        var closeButton = new Button
        {
            Text = "閉じる",
            Dock = DockStyle.Right,
            Width = 110,
            DialogResult = DialogResult.OK
        };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        footer.Controls.Add(closeButton);
        Controls.Add(content);
        Controls.Add(footer);
        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    private const string HelpText = """
        基本操作

        ・「音源追加」またはドラッグ＆ドロップで音源を追加します。
        ・対応形式はWAV、MP3、Ogg Vorbis、Ogg Opus、FLAC、AIFF、M4A、AAC、WMAです。
          FLAC／M4A／AAC／WMAはWindowsのMedia Foundationで読み込むため、PC側の対応状況によっては再生できない場合があります。
        ・各カードの「詳細」→「ファイル」→「参照…」で、登録を残したまま音声ファイルだけ差し替えられます。
        ・再生ボタン、または設定したキーで再生します。
        ・Shift＋設定キーでその音源を停止します。Ctrl＋Rは全音源を即時停止します。
        ・名前、キー、グループ、メモなどはEnterで確定、Escで変更を取り消せます。
        ・キーには空白を使えません。A、2、F1などのキー名を空白なしで指定してください。

        音源一覧

        ・「設定」→「音源一覧の列数」で1列／2列を選べます。
        ・2列設定でも、ウィンドウ幅が足りない場合は操作不能を防ぐため自動的に1列表示になります。

        音量

        ・音源ごとの音量とMASTER音量は0～100%です。
        ・音量バーはマウスホイール、矢印キー、ドラッグで変更できます。
        ・音源の音量バーを右クリックすると、Fade Outの秒数を使って指定音量まで変化します。

        Pan／Pitch

        ・Panは -100で左、0で中央、+100で右です。Pan欄を右クリックすると0へ戻ります。
        ・Pitchは0.50～2.00倍です。1.00が元の高さです。

        Fade In／Fade Out

        ・Fade Inは、次回の再生開始時に無音から設定音量まで指定秒数で上げます。
        ・Fade Outは、停止ボタン、Shift＋キー、または同じグループの別音源へ切り替えるときに指定秒数で下げてから停止します。
        ・「全停止」は緊急操作なので、Fade Outを待たず即時停止します。
        ・チェックをONにした時点で秒数が0なら、自動的に1.00秒へ設定します。

        ループ開始（秒）

        音源末尾まで再生したあと、ループで戻る位置を秒単位で指定します。0なら先頭へ戻ります。
        旧版プロジェクトにsample値がある場合も、その値を互換用として引き続き読み込めます。

        グループ

        同じグループ名の音源は同時に鳴らさず、新しい音源を再生すると前の音源を停止します。
        前の音源にFade Outが設定されていればクロスフェードします。

        イベントトリガ

        指定時刻、ストップウォッチ経過時間、音源終了、音源経過時間を条件として、
        対象音源の再生・停止・一時停止を自動実行できます。
        ・一覧の「有効」チェックから、その場で有効／無効を切り替えられます。
        ・発火しても設定は無効になりません。同じ条件の間は一度だけ発火し、時計のリセットや音源の再再生で再び待機します。
        ・停止／一時停止は、対象にグループを選ぶと同じグループの全音源へ実行できます。
          再生は候補が一意でないため、対象音源を1つ指定します。

        保存

        ・タイトル末尾の「*」は未保存の変更がある印です。保存後は消えます。
        ・終了時の保存確認は未保存の変更がある場合だけ表示されます。

        動作診断

        「操作」→「動作診断…」から、対応音声形式、音声出力、ループ、キーフック、DPI設定を確認できます。
        短いテスト音が鳴り、結果はLogsフォルダへ保存されます。
        """;
}
