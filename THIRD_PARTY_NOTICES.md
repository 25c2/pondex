# Third-party notices

PondashiEX の配布物には、以下の第三者製ソフトウェアが含まれます。

| Package | Version | Purpose | License | Project |
|---|---:|---|---|---|
| NAudio / NAudio.Core / NAudio.WinMM / NAudio.Wasapi / NAudio.Asio / NAudio.Midi / NAudio.WinForms | 2.4.0 | 音声ミキサー・出力・WAV処理 | MIT | https://github.com/naudio/NAudio |
| NAudio.Vorbis.Latest | 1.6.0 | NAudio向けOgg Vorbisブリッジ | MIT | https://github.com/i2van/NAudio.Vorbis.Latest |
| NVorbis | 0.10.5 | Managed Ogg Vorbisデコーダ | MIT | https://github.com/NVorbis/NVorbis |
| NLayer / NLayer.NAudioSupport | 2.0.1 | Managed MP3デコーダ・NAudioブリッジ | MIT | https://github.com/naudio/NLayer |
| System.Memory | 4.6.3 | 依存ライブラリ | MIT | https://github.com/dotnet/maintenance-packages |
| System.ValueTuple | 4.5.0 | 依存ライブラリ | MIT | https://dot.net/ |
| .NET Runtime / Windows Desktop Runtime | 8.0.31 | self-contained実行環境 | MITおよび同梱notice記載の各ライセンス | https://github.com/dotnet/runtime |

テスト専用依存（Microsoft.NET.Test.Sdk、xUnit、xunit.runner.visualstudio）は最終アプリ配布物には含めません。

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

主な著作者は、NAudioがMark Heathおよびcontributors、NAudio.Vorbis.LatestがAndrew Ward・Ivan Ivon、NVorbisがAndrew Ward、NLayerがMark Heath・Andrew Ward、System系が.NET Foundationおよびcontributorsです。

配布物には、この文書に加えて `DOTNET_LICENSE.txt` と `DOTNET_THIRD_PARTY_NOTICES.txt` を同梱します。
