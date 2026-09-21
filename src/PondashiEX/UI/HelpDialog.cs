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
        ・「詳細」の「この音源を削除…」では、音声ファイルを残したままプロジェクトから登録を削除します。最後のグループ音源を削除した場合、そのグループ専用設定も確認後に削除されます。
        ・再生ボタン、または設定したキーで再生します。
        ・同じキーを複数音源へ設定すると、1回のキー操作でまとめて同時再生できます。同じグループ内でも同時再生されます。
        ・Shift＋設定キーでその音源を停止します。Ctrl＋Rは全音源を即時停止します。
        ・名前、キー、グループ、メモなどはEnterで確定、Escで変更を取り消せます。
        ・キーには空白を使えません。A、2、F1などのキー名を空白なしで指定してください。解釈できない値は編集終了時に解除されます。

        音源一覧

        ・「設定」→「音源一覧の列数」で1～8列を選べます。
        ・「設定」→「画面全体を暗い配色にする」で、ライト／ダーク表示を切り替えられます。設定はプロジェクトへ保存されます。
        ・指定列数がウィンドウ幅に収まらない場合は、操作不能を防ぐため自動的に列数を減らします。
        ・音源カード左上の「↕ 音源名」をドラッグすると、カードを好きな順番へ並べ替えられます。複数列ではドロップした行と左右位置に合わせて移動します。

        音量

        ・音源ごとの音量とMASTER音量は0～100%です。
        ・音量バーはマウスホイール、矢印キー、ドラッグで変更でき、横の数値欄へ直接入力することもできます。
        ・音源の音量バーを右クリックすると、Fade Outの秒数を使って指定音量まで変化します。

        Pan／Pitch

        ・Panは -100で左、0で中央、+100で右です。Pan欄を右クリックすると0へ戻ります。
        ・Pitchは0.50～2.00倍です。1.00が元の高さです。

        Fade In／Fade Out

        ・Fade Inは、次回の再生開始時に無音から設定音量まで指定秒数で上げます。
        ・「設定」の「通常の停止操作でFade Outを使う」がONなら、停止ボタン、Shift＋キー、グループ停止、停止トリガで、指定秒数をかけて下げてから停止します。
        ・停止時フェードをOFFにすると、通常の停止操作も即時停止になります。
        ・「全停止でも音源ごとのFade Outを使う」がONなら、全停止でもFade Out設定のある音源はフェード後に停止します。OFFなら即時停止します。
        ・全停止時フェードは通常停止時フェードとは別に設定できます。Ctrl＋R、操作メニュー、常設ボタンの全停止すべてに適用されます。
        ・チェックをONにした時点で秒数が0なら、自動的に1.00秒へ設定します。

        ループ開始（秒）

        初回は0秒から再生します。音源末尾まで再生したあと、ループで戻る位置を秒単位で指定します。
        0、または音源の長さ以上の値なら先頭へ戻ります。
        旧版プロジェクトにsample値がある場合も、その値を互換用として引き続き読み込めます。

        グループ

        グループは音源の分類と一括操作に使用します。同じグループ内の音源も同時再生できます。
        ・「操作」→「グループショートカット…」で、グループ専用のキーを複数登録できます。
        ・登録キーでグループ内の全音源を同時再生し、Shift＋登録キーでまとめて停止します。
        ・「操作」→「ミュート」は、上部のミュートチェックと同じ状態を切り替えます。

        イベントトリガ

        指定時刻、ストップウォッチ経過時間、音源終了、音源経過時間を条件として、
        対象音源の再生・停止・一時停止を自動実行できます。
        ・一覧の「有効」チェックから、その場で有効／無効を切り替えられます。
        ・発火しても設定は無効になりません。同じ条件の間は一度だけ発火し、時計のリセットや音源の再再生で再び待機します。
        ・時刻条件はアプリ起動後に指定時刻を通過したとき発火します。起動前の時刻を遡って発火せず、日をまたぐ0時指定にも対応します。
        ・対象にグループを選ぶと、同じグループの全音源へ再生・停止・一時停止を実行できます。
        ・グループ対象の再生では、グループ内の全音源を同時再生します。

        保存

        ・タイトル末尾の「*」は未保存の変更がある印です。保存後は消えます。
        ・終了時の保存確認は未保存の変更がある場合だけ表示されます。

        動作診断

        「操作」→「動作診断…」から、対応音声形式、音声出力、ループ、キーフック、DPI設定を確認できます。
        短いテスト音が鳴り、結果はLogsフォルダへ保存されます。
        """;
}
