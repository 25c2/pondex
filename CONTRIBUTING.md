# Contributing

PondashiEXへの改善提案・不具合報告・Pull Requestを歓迎します。

## 開発環境

- Windows 11、またはMicrosoftのサポート期間中のWindows 10 Enterprise／LTSC
- .NET 10 SDK（`global.json`記載バージョン）

変更を送る前に、リポジトリルートで次を実行してください。

```powershell
dotnet restore PondashiEX.sln --configfile NuGet.Config --locked-mode
dotnet format PondashiEX.sln --verify-no-changes --no-restore
dotnet build PondashiEX.sln --configuration Release --no-restore
dotnet test PondashiEX.sln --configuration Release --no-build --no-restore
```

## 注意事項

- 「確実に鳴る・止まる」を最優先してください。
- UI、音声処理、キー入力、トリガ、プロジェクト保存の責務分離を維持してください。
- ネットワーク通信、テレメトリ、自動更新をアプリ本体へ追加しないでください。
- 著作権上再配布できない音声、旧版バイナリ、個人の`.pondex`、ログをコミットしないでください。
- 元の「効果音ポンだしくん」のコード、バイナリ、画像、音声、その他の配布物をコピーまたは移植しないでください。機能・仕様の参考に留め、EX側で独立実装してください。
- 新しいランタイム依存を追加する場合は、目的・バージョン・ライセンスを`THIRD_PARTY_NOTICES.md`へ追記してください。
- 依存を更新した場合は`dotnet restore PondashiEX.sln --configfile NuGet.Config --force-evaluate`でlock fileも更新してください。

提出された変更は、このリポジトリと同じAGPL-3.0-onlyの条件で提供されるものとします。
