# 効果音ポン出しくんEX

[![CI](https://github.com/25c2/pond/actions/workflows/ci.yml/badge.svg)](https://github.com/25c2/pond/actions/workflows/ci.yml)

BGM／効果音をすばやく再生する、Windows向けのオフライン・ポン出しアプリです。舞台、配信、イベントなどで複数の音源を扱う用途を想定しています。

> [!IMPORTANT]
> 本ソフトは、[効果音ポンだしくん](https://www.vector.co.jp/soft/winnt/art/se499320.html)を個人的に使いやすいよう、仕様と操作感を参考にして再実装した独立したソフトウェアです。元ソフトのコードや配布物は含まれておらず、公式アップデートまたは公式後継版でもありません。

## 主な機能

- WAV、MP3、Ogg Vorbis／Opus、FLAC、AIFF、M4A、AAC、WMAの再生と同時ミックス
- 音量、Pan、Pitch、Loop、Fade In／Fade Out
- 音源単体およびGroup単位のキーボード操作
- 時刻、ストップウォッチ、音源終了／経過時間によるイベントトリガ
- 音源の並べ替え、1～8列表示、ライト／ダークモード
- JSON形式のプロジェクト保存と出力デバイス切替

## 動作環境

- Windows 10以降（64bit）
- 一般的なWindows音声出力デバイス

[GitHub Releases](https://github.com/25c2/pond/releases)の`win-x64`版はself-containedなので、.NET Runtimeを別途インストールする必要はありません。現在の実行ファイルはコード署名されていないため、SmartScreenが表示された場合はダウンロード元と同梱のSHA-256チェックサムを確認してください。

## はじめかた

1. `PondashiEX.exe`を起動します。
2. 「音源を追加…」またはドラッグ＆ドロップで音声を登録します。
3. 音源カードでキー、音量、Loop、Fadeなどを設定します。
4. 必要に応じて`.pondex`として保存します。

詳しい操作方法とプロジェクトの安全上の注意は[使い方](docs/usage.md)を参照してください。

## 開発

ソースからのビルド、テスト、配布物の作成、GitHub Releaseについては[開発・リリース手順](docs/development.md)を参照してください。コントリビューション時の注意事項は[CONTRIBUTING.md](CONTRIBUTING.md)にあります。

## プライバシー

アプリ自身はネットワーク通信、自動更新、オンライン認証、テレメトリ送信を行いません。プロジェクト、最近使ったファイルの一覧、ログはローカルに保存されます。

## ライセンス

本ソフトは[AGPL-3.0-only](LICENSE)で公開します。第三者ライブラリの表記は[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)を参照してください。
