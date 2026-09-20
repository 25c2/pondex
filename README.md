# 効果音ポン出しくんEX

[![CI](https://github.com/25c2/pond/actions/workflows/ci.yml/badge.svg)](https://github.com/25c2/pond/actions/workflows/ci.yml)

BGM／効果音再生用、オフライン動作のWindows向けポン出しアプリです。

> [!IMPORTANT]
> 本ソフトは、[効果音ポンだしくん](https://www.vector.co.jp/soft/winnt/art/se499320.html)を個人的に使いやすいよう、仕様と操作感を参考にして再実装した独立したソフトウェアです。元の効果音ポンだしくんのソースコード、バイナリ、画像、音声、その他の配布物は含まれておらず、それらに実行時依存もしません。また、元ソフトの公式アップデートや公式後継版ではありません。

## 主な機能

- WAV／MP3／Ogg Vorbis／Ogg Opus／FLAC／AIFF／M4A／AAC／WMAの再生と複数音源の同時ミックス
- 音量、左右バランス、Pitch、Loop、Fade In／Fade Out
- Group単位の排他再生とクロスフェード
- キーアサイン、バックグラウンド入力、常設の「全停止」ボタン
- 現在時刻、ストップウォッチ、音源終了、音源経過時間によるイベントトリガ
- 人が読める形式のプロジェクトファイル（`.pondex`）
- 出力デバイスの選択、切断検出、Windows既定出力へのフォールバック
- ネットワーク接続、オンライン認証、テレメトリ不要

詳しい要求仕様は[`docs/specification.md`](docs/specification.md)を参照してください。

## 動作環境

- Windows 10以降（64bit）
- 一般的なWindows音声出力デバイス

`win-x64`のself-contained配布物を使用する場合、.NET Runtimeの追加インストールは不要です。ソースからビルドする場合は.NET 8 SDKが必要です。

## 基本的な使い方

1. `PondashiEX.exe`を起動します。
2. 「音源を追加…」またはファイルのドラッグ＆ドロップで音声を追加します。
3. 音源カードでキー、音量、Group、Loop、Fadeなどを設定します。「詳細」の「参照…」から登録済み音源のファイルだけを差し替えられます。
4. 必要に応じて「設定」で出力デバイス、バックグラウンド入力、音源一覧の1列／2列表示を選びます。
5. `.pondex`ファイルとしてプロジェクトを保存します。

通常のキー入力で音源を再生し、`Shift + 割り当てキー`で停止します。`Ctrl+R`または常設の「全停止」は、Fade設定にかかわらずすべての音源を即時停止します。

音量バーは通常操作で即時変更されます。`Fade Out`を有効にして音量バー上を右クリックすると、指定位置の音量まで設定秒数でフェードします。同じGroupの別音源を再生した場合、旧音源にFade Out設定があればクロスフェードになります。

`Fade In`は次回の再生開始時、`Fade Out`は停止・`Shift + キー`・同じGroupの切り替え時に動作します。「全停止」は緊急操作のため即時停止します。Fadeを有効にしたとき秒数が0なら、1.00秒が自動設定されます。

名前、キー、Group、メモなどの入力中は、`Enter`で確定、`Esc`で入力開始時の値へ戻して編集を終了できます。カード内の余白をクリックして確定することもできます。キー欄の空白は使用できないため、自動的に取り除かれます。

Panは`-100`が左、`0`が中央、`+100`が右です。Pan欄の右クリックで中央へ戻せます。ループ開始位置は秒単位で指定します。

イベントトリガは発火後も有効設定を維持し、同じ成立状態での連続発火だけを抑止します。ストップウォッチのリセットや音源の再再生で再び待機します。一覧のチェックボックスから有効／無効を直接変更でき、停止・一時停止ではGroup全体も対象にできます。

画面上部の「ヘルプ」→「使い方…」では、対応形式、Fade、ループ、Pan、グループ、イベントトリガなどの説明を確認できます。

## プロジェクトと音声ファイル

プロジェクトを保存すると、同じフォルダまたは配下にある音声は原則として相対パスで記録されます。別PCへ移すときは、`.pondex`と音声ファイルの位置関係を保ったままフォルダごとコピーしてください。

保存時には一時ファイルを検証してから置き換え、既存ファイルがある場合は`.pondex.bak`を作成します。旧「効果音ポンだしくん」の独自プロジェクト形式を直接読み込む機能はありません。

## 動作診断

GUIの「操作」→「動作診断…」では、WAV／MP3／Ogg Vorbis／Ogg Opus、Pitch＋Loop、Fade In／Fade Out、無音ループ、バックグラウンド入力フック、DPIモードを実機確認します。短いテスト音が鳴ります。

GUIを開かずに実行する場合は次のコマンドを使用します。

```powershell
PondashiEX.exe --audio-self-test
```

長時間運転診断では、無音出力を維持しながらmanaged MP3／Ogg Vorbis／Ogg Opusの生成・破棄を繰り返し、出力sample数、反復回数、メモリ、ハンドル数を記録します。次は120分実行する例です。

```powershell
PondashiEX.exe --soak-test-minutes=120
```

診断結果と通常ログは、書き込み可能なら実行フォルダの`Logs/`へ保存します。実行フォルダが読み取り専用の場合、`%LOCALAPPDATA%\PondashiEX\Logs`へ保存します。

本番利用前には、実際に使用するPC・音声デバイス・音源で動作診断と通し稽古を行ってください。

## ビルドとテスト

```powershell
dotnet restore PondashiEX.sln --configfile NuGet.Config --locked-mode
dotnet format PondashiEX.sln --verify-no-changes --no-restore
dotnet build PondashiEX.sln --configuration Release --no-restore
dotnet test PondashiEX.sln --configuration Release --no-build --no-restore
```

GitHub Actionsでも、`main`へのpushとPull Requestごとに依存関係の復元・脆弱性確認・フォーマット検査・Releaseビルド・テストを実行します。

## Windows x64配布物の作成

```powershell
dotnet publish src/PondashiEX/PondashiEX.csproj --configuration Release --runtime win-x64 --self-contained true --output publish/win-x64
```

`publish/win-x64/`をフォルダごと対象PCへコピーし、`PondashiEX.exe`を起動します。`publish/`はGit管理対象外です。配布するときはフォルダ全体をZIPにまとめてください。

## GitHub Releaseの作成

`main`上のリリース対象コミットへ`v0.2`、`v0.2.1`、`vYY.MM.DD`など、2～4区切りの数値バージョンタグを付けてpushすると、GitHub ActionsがReleaseビルドとテストを行い、Windows x64 self-contained版のZIPとSHA-256チェックサムをGitHub Releasesへ自動公開します。

```powershell
$tag = "v0.2.1"
git tag -a $tag -m $tag
git push origin $tag
```

`v0.3-beta.1`のようにハイフンを含むバージョンはPrereleaseとして公開されます。タグの付け直しは履歴と公開済みReleaseの不一致を招くため、修正版では番号を上げて新しいタグを作成してください。

## プライバシー

アプリケーション自身はネットワーク通信、自動更新、オンライン認証、利用状況の送信を行いません。プロジェクト、最近使ったファイルの一覧、ログはローカルにだけ保存されます。

## コントリビューション

開発時の注意事項と確認コマンドは[`CONTRIBUTING.md`](CONTRIBUTING.md)を参照してください。

## ライセンス

効果音ポン出しくんEXは[`AGPL-3.0-only`](LICENSE)で公開します。配布物に含まれる第三者ライブラリの著作権・ライセンス表記は[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)を参照してください。
