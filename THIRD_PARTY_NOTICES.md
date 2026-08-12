# Third-Party Notices

LiveSubtitle includes or links to the following runtime components. Copyright remains with each component's authors.

| Component | Version | License | Project |
| --- | --- | --- | --- |
| .NET Windows Desktop Runtime | 10.0.10 | MIT and component-specific notices | https://github.com/dotnet/runtime |
| NAudio | 2.3.0 | MIT | https://github.com/naudio/NAudio |
| Whisper.net | 1.9.1 | MIT | https://github.com/sandrohanea/whisper.net |
| whisper.cpp native runtimes | via Whisper.net 1.9.1 | MIT | https://github.com/ggml-org/whisper.cpp |
| Microsoft.Data.Sqlite | 10.0.10 | MIT | https://learn.microsoft.com/dotnet/standard/data/sqlite/ |
| SQLitePCLRaw / e_sqlite3 | 2.1.12 | Apache-2.0; SQLite public-domain code | https://github.com/ericsink/SQLitePCL.raw |
| Silero VAD model | 6.2.0 | MIT | https://github.com/snakers4/silero-vad |

The self-contained publish includes the .NET runtime's generated `ThirdPartyNotices.txt`; that file is the authoritative detailed notice set for framework binaries.

Whisper speech-recognition models are not bundled. They are downloaded only on user request and remain subject to the notices published with the upstream OpenAI Whisper and whisper.cpp model distributions.

License texts:

- MIT: https://opensource.org/license/mit/
- Apache License 2.0: https://www.apache.org/licenses/LICENSE-2.0
