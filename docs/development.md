# 開発・リリース手順

## 必要な環境

- [.NET 10の対応OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)に含まれるWindows（64bit）
- .NET 10 SDK（`global.json`記載バージョン）

依存パッケージの取得先はリポジトリの`NuGet.Config`で公式nuget.orgに限定しています。

CIでの検証とは別に、使用するPC・音声デバイスでself-contained版の起動と再生を確認してください。

## ビルドとテスト

```powershell
dotnet restore pondex.sln --configfile NuGet.Config --locked-mode
dotnet format pondex.sln --verify-no-changes --no-restore
dotnet build pondex.sln --configuration Release --no-restore
dotnet test pondex.sln --configuration Release --no-build --no-restore
```

詳細ログの `decoder start` はデコーダの開始時刻、準備枠の使用数、待機時間を記録します。緊急停止の `decoder still occupies slot` はキャンセル後も処理が戻っていない枠を示します。同期デコードはドライバやファイルI/Oから戻るまで枠を保持します。長く止まる素材は本番前に読込確認を行い、強制終了が必要な用途では別プロセスでのデコードを検討してください。

詳細ログの `save_timing` と `play_timing` は単調時計で記録し、`id` が要求単位の識別子です。再生の `play requested` には同じ識別子が `trace` として入ります。各項目の単位はミリ秒です。`total_ms` には区間の隙間やスレッドの再開待ちも含まれます。`to_engine`、`prepare`、`ready_to_start` は複数の区間を含みます。別名保存の `file_dialog_ms` は利用者がパスを選ぶ時間です。途中で失敗・中止した場合は、実際に入った区間だけを記録します。

詳細ログの `output_snapshot` は列挙結果やWindows既定の出力が変化したときに記録します。`output_refused` は再生または出力バインドが拒否された理由の確認に使えます。

CIでは`main`へのpushとPull Requestごとに、依存関係の復元、NuGet脆弱性監査、フォーマット検査、Releaseビルド、テストを実行します。監査フィードを取得できない場合や、低重要度以上の既知の脆弱性が検出された場合も失敗します。

## Windows x64配布物

```powershell
dotnet publish src/pondex/pondex.csproj --configuration Release --runtime win-x64 --self-contained true --output publish/win-x64
```

`publish/win-x64/`をフォルダごと対象PCへコピーして`pondex.exe`を起動します。`publish/`はGit管理対象外です。

## GitHub Release

`main`上のリリース対象コミットへ`v0.2`、`v0.2.1`、`vYY.MM.DD`など、2～4区切りの数値バージョンタグを付けてpushします。

```powershell
$tag = "v1.0"
git tag -a $tag -m $tag
git push origin $tag
```

`v1.0-alpha.1`、`v1.0-beta.1`、`v1.0-rc.1`、`v1.0-preview.1`など、ハイフン以降にプレリリース識別子を付けたタグも同じワークフローでビルド・配布できます。`beta`だけに限定していません。番号なしの`v1.0-rc`や、`v1.0-dev.20261106`も使用できます。識別子には英数字と、区切りの`.`・`-`を使用してください（例：`v1.0rc1`ではなく`v1.0-rc.1`）。

これらはすべてGitHub ReleaseのPrereleaseとして作成されます。`v1.0`や`v1.0.1`のようにハイフンのないタグは通常のReleaseです。公開済みタグは付け直さず、修正版ではバージョン番号を上げてください。

Releaseワークフローは次を実行します。

1. タグ形式と、対象コミットが`main`に含まれることを検証
2. NuGet監査、フォーマット、Releaseビルド、全テスト
3. Windows x64 self-contained版の作成とスモークテスト
4. ZIPとSHA-256チェックサムの作成
5. publicリポジトリの場合はGitHub Artifact Attestationを発行（個人所有のprivateリポジトリではGitHub側の制限によりスキップ）
6. GitHub Releaseへの添付

publicリポジトリで発行された来歴はGitHub CLIで検証できます。

```powershell
gh attestation verify pondex-v1.0-win-x64.zip --repo 25c2/pondex
```

ビルド・テストジョブは `contents: read` で実行し、公開ジョブだけが公開・attestationの権限と `release` Environment を使います。GitHubのリポジトリ設定でRequired reviewersを設定すると、公開前の承認ゲートになります。

実行ファイル自体は現在Authenticode署名されていません。署名を導入する場合は、コード署名証明書と秘密鍵をGitHub Actionsの保護されたSecretまたは外部署名サービスで管理してください。
